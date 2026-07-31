using System;
using System.Collections.Generic;
using System.Text;
using BattleMatch;
using Cards;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Keyboard navigation + speech for the in-run character sheet (<c>CharacterWindowUI</c>) — the
/// window that opens from a side-portrait click on the map, in town, in combat, and on the
/// rewards/loot screens. Tab/Shift+Tab cycle the window's tabs (Deck/Level/Items/Stats/Perks out
/// of combat; Draw pile/Discard/Vanished/Items/Stats/Perks in combat; Cards cast/Stats for an
/// enemy), Up/Down walk the rows of the current tab, 1–4 switch party member, Enter activates
/// (the Level tab's trait choice), Ctrl+Up/Down drill into the focused card, Escape closes.
///
/// Rows are rebuilt from game data (hero.Cards, BattleCards piles, SubClassData traits), never
/// from the spawned CardItems — DeckWindowUI's combat pile view spawns one card per frame in a
/// coroutine, so the UI can't be trusted to be complete when we read. The draw pile is sorted
/// exactly like the game sorts it (the real draw order is hidden from sighted players too).
///
/// Open/close/tab-change announcements all come from a postfix on <c>CharacterWindowUI.Show</c> /
/// a guarded <c>Hide</c> postfix, so mouse opens (portrait clicks, combat right-clicks, pile
/// icons) speak identically to keyboard ones. Level-up outcomes are announced from a postfix on
/// <c>Hero.LevelUp(string)</c>, which also covers mouse picks and multiplayer echoes (the
/// HeroLevelUpMP RPC runs it on every client). The Perks tab is virtual — Enter on it opens the
/// PerkTree overlay, whose own context (re-prioritised above this one) takes over.
/// </summary>
internal static class CharWindowScreenManager
{
    private sealed class Row
    {
        public Func<string> Read;
        public Action Activate;           // null = read-only row
        public CardRealtimeData Card;     // Alt+T full detail + Ctrl drill
        public int? LiveCost;             // combat: cost after modifiers, for card speech
        public Func<string> Detail;       // Alt+T for non-card rows (traits, stat breakdowns)
        public int LevelIndex = -1;       // level rows: game level index 1-4 (row = level i+1)
    }

    private static CharacterWindowUI _window;
    private static bool _open;
    private static string _element = "";         // resolved tab key ("perks" = virtual tab)
    private static int _heroIndex = -1;
    private static bool _isNpc;
    private static int _rowIndex = -1;
    private static readonly List<Row> _rows = new List<Row>();
    private static int _levelCol;                // 0 = choice A, 1 = choice B on level rows
    private static int _openedFrame = -1;

    // Upgraded-cards popup sub-mode (mid-run random-upgrade events reuse this window with all
    // tab buttons hidden; the FinishRun scene's unlocked-cards popup is NOT ours).
    private static bool _popupMode;
    private static readonly List<string> _popupCards = new List<string>();
    private static int _popupIndex;

    // Ctrl+Up/Down drill-in, mirroring the rewards/loot screens.
    private static bool _drill;
    private static readonly List<string> _drillLines = new List<string>();
    private static int _drillIndex;

    /// <summary>Live "the sheet is open" check, so a mouse close can never strand the state.</summary>
    public static bool IsOpen
        => _open && _window != null && _window.IsActive();

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Per-frame tick from the poller: drops state when the window went away without
    /// Hide (scene change).</summary>
    public static void Tick()
    {
        if (_open && (_window == null || !_window.IsActive()))
            ResetState();
    }

    private static void ResetState()
    {
        _open = false;
        _element = "";
        _heroIndex = -1;
        _isNpc = false;
        _rowIndex = -1;
        _rows.Clear();
        _levelCol = 0;
        _openedFrame = -1;
        _popupMode = false;
        _popupCards.Clear();
        _popupIndex = 0;
        ExitDrillSilently();
    }

    /// <summary>CharacterWindowUI.Show landed (keyboard, mouse, or a teammate's echo).</summary>
    public static void OnShown(CharacterWindowUI window, string rawElement, bool isHero)
    {
        // The end-of-run unlocked-cards popup belongs to FinishRunScreenManager.
        if (FinishRunManager.Instance != null)
            return;
        if (window == null || !window.IsActive())
            return; // Show bailed early (missing hero/NPC data)

        _window = window;

        // "perks" never becomes the window's own tab — it opens the PerkTree overlay over the
        // sheet, and the tree's context/manager announce it. Keep our state on the tab beneath.
        if (rawElement == "perks")
            return;

        // Show resolves an empty element internally (last tab, or "level" when a level-up is
        // pending); the resolved value lands in the private activeTab field.
        string element = rawElement;
        if (string.IsNullOrEmpty(element))
            element = Traverse.Create(window).Field<string>("activeTab").Value;

        if (element == "unlockedCards")
        {
            // Mid-run upgraded-cards popup: the ShowUpgradedCards postfix (which has the card
            // list) announces and fills the sub-mode; here just mark the window open.
            _open = true;
            _popupMode = true;
            _openedFrame = Time.frameCount;
            return;
        }

        bool wasOpen = _open && !_popupMode;
        bool heroChanged = _isNpc != !isHero
            || (isHero && _heroIndex != window.heroIndex)
            || (!isHero && _heroIndex != window.npcIndex);
        bool tabChanged = _element != element;
        int keepRow = _rowIndex;

        _open = true;
        _popupMode = false;
        _isNpc = !isHero;
        _heroIndex = isHero ? window.heroIndex : window.npcIndex;
        _element = element;
        _rowIndex = -1;
        _levelCol = 0;
        ExitDrillSilently();
        BuildRows();

        if (!wasOpen)
        {
            _openedFrame = Time.frameCount;
            SpeechManager.Speak(ArrivalText());
        }
        else if (heroChanged)
        {
            SpeechManager.Speak(SubjectName() + " — " + TabName(_element) + " tab. " + RowCountHint());
        }
        else if (tabChanged)
        {
            SpeechManager.Speak(TabName(_element) + " tab. " + RowCountHint());
        }
        else if (keepRow >= 0 && _rows.Count > 0)
        {
            // Same hero, same tab: a silent refresh (the game re-shows on several paths,
            // e.g. clicking the already-open hero's portrait) — keep the reading position.
            _rowIndex = Math.Min(keepRow, _rows.Count - 1);
        }
    }

    public static void OnHidden()
    {
        if (!_open)
            return;
        bool popup = _popupMode; // ResetState clears it
        ResetState();
        SpeechManager.Speak(popup ? "Upgraded cards closed." : "Character sheet closed.");
    }

    /// <summary>The mid-run "cards upgraded" popup (random-upgrade events). FinishRun's
    /// unlocked-cards popup takes a different method and is handled by that screen.</summary>
    public static void OnUpgradedCardsShown(List<string> upgradedCards)
    {
        if (FinishRunManager.Instance != null || _window == null || !_window.IsActive())
            return;
        _open = true;
        _popupMode = true;
        _popupCards.Clear();
        if (upgradedCards != null)
        {
            foreach (var id in upgradedCards)
            {
                if (!string.IsNullOrEmpty(id))
                    _popupCards.Add(id);
            }
        }
        _popupIndex = 0;
        ExitDrillSilently();
        var sb = new StringBuilder();
        sb.Append("Cards upgraded — ").Append(_popupCards.Count)
          .Append(_popupCards.Count == 1 ? " card. " : " cards. ");
        var first = PopupCardData(0);
        if (first != null)
            sb.Append(CardSpeech.BriefLine(first)).Append(". ");
        sb.Append("Left and right to review, Alt T for detail, Escape to close.");
        SpeechManager.Speak(sb.ToString());
    }

    /// <summary>Spoken when the perk tree closes while the sheet is still open beneath it.</summary>
    public static string ReturnAnnouncement()
        => "Back to character sheet — " + TabName(_element) + " tab.";

    // ------------------------------------------------------------------ opening (map/town/combat)

    /// <summary>Party-strip Enter on the map/town: open the sheet for the focused slot through
    /// the game's own portrait-click path (keeps the sidebar active-state consistent).</summary>
    public static bool OpenHeroSheet(int slot)
    {
        var team = AtOManager.Instance != null ? AtOManager.Instance.team : null;
        var hero = team != null ? team.GetHero(slot) : null;
        if (hero == null || hero.HeroData == null)
            return false;
        var go = GameObject.Find("/SideCharacters/OverCharacter" + slot);
        var oc = go != null ? go.GetComponent<OverCharacter>() : null;
        if (oc == null)
            return false;
        oc.Clicked();
        return true;
    }

    /// <summary>Combat Alt+C: open the sheet for the character under review focus (heroes get
    /// the full sheet, enemies their stats view), falling back to the active hero. Uses the same
    /// "stats" landing tab as the game's own right-click.</summary>
    public static void OpenFromCombat()
    {
        var m = MatchManager.Instance;
        if (m == null)
            return;
        var t = CombatNavigator.FocusedTransform;
        var ci = t != null
            ? (t.GetComponent<CharacterItem>() ?? t.GetComponentInParent<CharacterItem>())
            : null;
        if (ci != null)
        {
            var hero = Traverse.Create(ci).Field<Hero>("_hero").Value;
            if (hero != null)
            {
                m.ShowCharacterWindow("stats", isHero: true, hero.HeroIndex);
                return;
            }
            var npc = Traverse.Create(ci).Field<NPC>("_npc").Value;
            if (npc != null)
            {
                m.ShowCharacterWindow("stats", isHero: false, npc.NPCIndex);
                return;
            }
        }
        int active = m.GetHeroActive();
        if (active > -1)
        {
            m.ShowCharacterWindow("stats", isHero: true, active);
            return;
        }
        SpeechManager.Speak("No character focused.");
    }

    // ------------------------------------------------------------------ subject helpers

    private static Hero CurrentHero()
    {
        if (_isNpc)
            return null;
        var team = AtOManager.Instance != null ? AtOManager.Instance.team : null;
        return team != null ? team.GetHero(_heroIndex) : null;
    }

    private static NPC CurrentNpc()
        => _window != null ? _window.currentCharacter as NPC : null;

    private static SubClassData SubClass(Hero hero)
        => hero != null && hero.HeroData != null ? hero.HeroData.HeroSubClass : null;

    private static string SubjectName()
    {
        if (_isNpc)
        {
            var npc = CurrentNpc();
            return npc != null ? AccessibleMenuBase.StripRichText(npc.SourceName) : "Enemy";
        }
        var hero = CurrentHero();
        return hero != null ? AccessibleMenuBase.StripRichText(hero.SourceName) : "Hero";
    }

    // ------------------------------------------------------------------ tabs

    private static List<string> AvailableTabs()
    {
        var tabs = new List<string>();
        if (_isNpc)
        {
            tabs.Add("combatdiscard"); // "Cards cast" view
            tabs.Add("stats");
            return tabs;
        }
        if (MatchManager.Instance != null)
        {
            tabs.Add("combatdeck");
            tabs.Add("combatdiscard");
            tabs.Add("combatvanish");
        }
        else
        {
            tabs.Add("deck");
            tabs.Add("level");
        }
        tabs.Add("items");
        tabs.Add("stats");
        if (GameManager.Instance == null || !GameManager.Instance.IsObeliskChallenge())
            tabs.Add("perks");
        return tabs;
    }

    /// <summary>The current tab normalised to its key in <see cref="AvailableTabs"/>.</summary>
    private static string TabKey()
        => _element == "deckreward" ? "deck" : _element;

    private static string TabName(string key)
    {
        switch (key)
        {
            case "deck":
            case "deckreward": return "Deck";
            case "level": return "Level";
            case "items": return "Items";
            case "stats": return "Stats";
            case "perks": return "Perks";
            case "combatdeck": return "Draw pile";
            case "combatdiscard": return _isNpc ? "Cards cast" : "Discard pile";
            case "combatvanish": return "Vanished cards";
            case "unlockedCards": return "Upgraded cards";
            default: return key;
        }
    }

    public static void CycleTab(bool backwards)
    {
        if (!IsOpen)
            return;
        if (_popupMode)
        {
            SpeechManager.Speak("Left and right to review the upgraded cards, Escape to close.");
            return;
        }
        var tabs = AvailableTabs();
        int start = tabs.IndexOf(TabKey());
        if (start < 0)
            start = 0;
        int next = (start + (backwards ? tabs.Count - 1 : 1)) % tabs.Count;
        SwitchToTab(tabs[next]);
    }

    private static void SwitchToTab(string key)
    {
        if (key == "perks")
        {
            // Virtual tab: no game UI change until Enter opens the tree.
            _element = "perks";
            _rowIndex = -1;
            ExitDrillSilently();
            BuildRows();
            HeroSpeech.PlayButtonFocusSound();
            SpeechManager.Speak("Perks tab. " + (_rows.Count > 0 ? _rows[0].Read() : ""));
            return;
        }
        HeroSpeech.PlayButtonFocusSound();
        ShowTab(key); // the Show postfix announces the change
    }

    /// <summary>Route a tab change through the scene's own wrapper, exactly like the game's
    /// side buttons do (keeps sideCharacters state consistent everywhere the sheet exists).</summary>
    private static void ShowTab(string element)
        => ShowTab(element, _isNpc ? _window.npcIndex : _heroIndex, !_isNpc);

    /// <summary>The scene-appropriate ShowCharacterWindow wrapper (each scene manager keeps its
    /// own extra state in sync), with a direct Show fallback for scene-less callers.</summary>
    private static void ShowTab(string element, int index, bool isHero)
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.ShowCharacterWindow(element, isHero, index);
        else if (MapManager.Instance != null)
            MapManager.Instance.ShowCharacterWindow(element, index);
        else if (TownManager.Instance != null)
            TownManager.Instance.ShowCharacterWindow(element, index);
        else if (RewardsManager.Instance != null)
            RewardsManager.Instance.ShowCharacterWindow(element, isHero, index);
        else if (LootManager.Instance != null)
            LootManager.Instance.ShowCharacterWindow(element, isHero, index);
        else if (_window != null)
            _window.Show(element, index, isHero);
    }

    /// <summary>1–4: switch to that party member (same as the game's shoulder-cycle, which
    /// clicks the side portrait). Works from the enemy view too.</summary>
    public static void SelectHeroSlot(int slot)
    {
        if (!IsOpen || _popupMode)
            return;
        var team = AtOManager.Instance != null ? AtOManager.Instance.team : null;
        var hero = team != null ? team.GetHero(slot) : null;
        if (hero == null || hero.HeroData == null)
        {
            SpeechManager.Speak("Slot " + (slot + 1) + " empty");
            return;
        }
        if (!_isNpc && slot == _heroIndex)
        {
            SpeechManager.Speak(SubjectName() + " — " + TabName(_element) + " tab.");
            return;
        }
        var go = GameObject.Find("/SideCharacters/OverCharacter" + slot);
        var oc = go != null ? go.GetComponent<OverCharacter>() : null;
        if (oc != null)
            oc.Clicked(); // SetActive path re-opens the sheet for that hero; Show postfix speaks
        else
            ShowTabForHero(slot);
    }

    private static void ShowTabForHero(int slot)
        => ShowTab(_isNpc || _element == "perks" ? "" : _element, slot, isHero: true);

    // ------------------------------------------------------------------ announcements

    private static string ArrivalText()
    {
        var sb = new StringBuilder();
        sb.Append("Character sheet — ");
        if (_isNpc)
        {
            sb.Append(SubjectName()).Append(". ");
        }
        else
        {
            var hero = CurrentHero();
            if (hero != null)
            {
                sb.Append(AccessibleMenuBase.StripRichText(hero.SourceName));
                sb.Append(", level ").Append(hero.Level);
                if (hero.Level < 5 && Globals.Instance != null)
                    sb.Append(", ").Append(hero.Experience).Append(" of ")
                      .Append(Globals.Instance.GetExperienceByLevel(hero.Level))
                      .Append(" experience");
                sb.Append(", ").Append(hero.HpCurrent).Append(" of ").Append(hero.Hp)
                  .Append(" health. ");
                if (hero.IsReadyForLevelUp())
                    sb.Append("Ready to level up. ");
            }
        }
        sb.Append(TabName(_element)).Append(" tab. ").Append(RowCountHint());
        sb.Append(" Tab switches tabs, 1 to 4 switch character, Escape closes.");
        return sb.ToString();
    }

    private static string RowCountHint()
    {
        if (_rows.Count == 0)
            return "Nothing to show.";
        if (_rows.Count == 1)
            return _rows[0].Read();
        return _rows.Count + " rows. Up and down to read.";
    }

    // ------------------------------------------------------------------ row building

    private static void BuildRows()
    {
        _rows.Clear();
        if (_window == null)
            return;
        switch (TabKey())
        {
            case "deck":
            case "combatdeck":
            case "combatdiscard":
            case "combatvanish":
                BuildDeckRows();
                break;
            case "level":
                BuildLevelRows();
                break;
            case "items":
                BuildItemsRows();
                break;
            case "stats":
                BuildStatsRows();
                break;
            case "perks":
                BuildPerksRow();
                break;
        }
    }

    private static void BuildRowsIfEmpty()
    {
        if (_rows.Count == 0)
            BuildRows();
    }

    // ----------------------------------------------- deck / pile tabs

    /// <summary>The cards for the current deck-family tab, straight from game data — mirroring
    /// DeckWindowUI's own sources and ordering (draw pile sorted, discard newest-first).</summary>
    private static void CollectDeckCards(List<string> main, List<string> injuries, out string title)
    {
        title = "";
        if (_isNpc)
        {
            var npc = CurrentNpc();
            if (npc == null || MatchManager.Instance == null)
                return;
            var casted = MatchManager.Instance.GetNPCCardsCastedList(npc.InternalId);
            if (casted != null)
            {
                for (int i = casted.Count - 1; i >= 0; i--)
                    main.Add(casted[i]);
            }
            title = "Cards cast by " + SubjectName();
            return;
        }

        var hero = CurrentHero();
        if (hero == null)
            return;
        switch (TabKey())
        {
            case "deck":
            {
                if (hero.Cards != null)
                {
                    foreach (var id in hero.Cards)
                    {
                        var cd = Globals.Instance.GetCardData(id, instantiate: false);
                        if (cd == null)
                            continue;
                        if (cd.CardClass == Enums.CardClass.Injury || cd.CardClass == Enums.CardClass.Boon)
                            injuries.Add(cd.Id);
                        else
                            main.Add(cd.Id);
                    }
                }
                main.Sort();
                injuries.Sort();
                title = SubjectName() + "'s deck";
                break;
            }
            case "combatdeck":
            {
                foreach (var id in hero.BattleCards.GetPile())
                    main.Add(id);
                main.Sort(); // the game sorts too — the real draw order stays hidden
                title = "Draw pile";
                break;
            }
            case "combatdiscard":
            {
                var pile = hero.BattleCards.GetPile(DeckType.DeckDiscard);
                for (int i = pile.Count - 1; i >= 0; i--)
                    main.Add(pile.GetCardAtIndex(i)); // newest first, like the game
                title = "Discard pile";
                break;
            }
            case "combatvanish":
            {
                foreach (var id in hero.BattleCards.GetPile(DeckType.DeckVanish))
                    main.Add(id);
                title = "Vanished cards";
                break;
            }
        }
    }

    private static CardRealtimeData ResolveCard(string id, Hero hero, out int? liveCost)
    {
        liveCost = null;
        if (string.IsNullOrEmpty(id))
            return null;
        if (MatchManager.Instance != null)
        {
            var cd = MatchManager.Instance.GetCardData(id, createInDict: false);
            if (cd != null && hero != null)
                liveCost = hero.GetCardFinalCost(cd);
            return cd;
        }
        return Globals.Instance.GetCardData(id, instantiate: false);
    }

    /// <summary>Average energy cost over the playable cards, mirroring DeckEnergy, plus the
    /// per-cost histogram for the overview.</summary>
    private static string EnergySummary(List<string> cards, Hero hero, bool histogram)
    {
        int count = 0;
        float total = 0f;
        var buckets = new SortedDictionary<int, int>();
        foreach (var id in cards)
        {
            var cd = ResolveCard(id, hero, out int? liveCost);
            if (cd == null || !cd.Playable)
                continue;
            int cost = liveCost ?? cd.EnergyCost;
            total += cost;
            count++;
            int bucket = cost > 5 ? 5 : cost;
            buckets[bucket] = buckets.TryGetValue(bucket, out int n) ? n + 1 : 1;
        }
        if (count == 0)
            return "";
        var sb = new StringBuilder();
        sb.Append("Average energy cost ").Append((total / count).ToString("0.00")).Append('.');
        if (histogram)
        {
            foreach (var pair in buckets)
            {
                sb.Append(' ').Append(pair.Value)
                  .Append(pair.Key == 5 ? " cost five or more," : " cost " + pair.Key + ",");
            }
            sb.Length -= 1; // trailing comma
            sb.Append('.');
        }
        return sb.ToString();
    }

    private static void BuildDeckRows()
    {
        var hero = CurrentHero();
        var main = new List<string>();
        var injuries = new List<string>();
        CollectDeckCards(main, injuries, out string title);

        string energy = _isNpc ? "" : EnergySummary(main, hero, histogram: false);
        _rows.Add(new Row
        {
            Read = () => title + " — " + main.Count + (main.Count == 1 ? " card." : " cards.")
                + (energy != "" ? " " + energy : ""),
        });
        AddCardRows(main, hero);

        if (injuries.Count > 0)
        {
            _rows.Add(new Row
            {
                Read = () => "Injuries and boons — " + injuries.Count
                    + (injuries.Count == 1 ? " card." : " cards."),
            });
            AddCardRows(injuries, hero);
        }
    }

    private static void AddCardRows(List<string> ids, Hero hero)
    {
        // Resolve first so ids that fail to resolve don't leave gaps in the "N of M" numbering.
        var resolved = new List<KeyValuePair<CardRealtimeData, int?>>();
        foreach (var id in ids)
        {
            var cd = ResolveCard(id, hero, out int? liveCost);
            if (cd != null)
                resolved.Add(new KeyValuePair<CardRealtimeData, int?>(cd, liveCost));
        }
        int total = resolved.Count;
        for (int i = 0; i < resolved.Count; i++)
        {
            var captured = resolved[i].Key;
            var capturedCost = resolved[i].Value;
            int position = i + 1;
            _rows.Add(new Row
            {
                Read = () => CardSpeech.BriefLine(captured, capturedCost)
                    + ", " + position + " of " + total,
                Card = captured,
                LiveCost = capturedCost,
            });
        }
    }

    // ----------------------------------------------- level tab

    private static TraitData TraitOption(SubClassData scd, int level, int col)
    {
        if (scd == null)
            return null;
        switch (level)
        {
            case 0: return scd.Trait0;
            case 1: return col == 0 ? scd.Trait1A : scd.Trait1B;
            case 2: return col == 0 ? scd.Trait2A : scd.Trait2B;
            case 3: return col == 0 ? scd.Trait3A : scd.Trait3B;
            case 4: return col == 0 ? scd.Trait4A : scd.Trait4B;
            default: return null;
        }
    }

    /// <summary>The hero-adjusted trait data (some heroes rename/retune traits).</summary>
    private static TraitData Adjusted(TraitData td, Hero hero)
    {
        if (td == null)
            return null;
        var adjusted = Globals.Instance.GetTraitData(td.Id, hero);
        return adjusted != null ? adjusted : td;
    }

    private static string TraitName(TraitData td)
    {
        if (td == null)
            return "";
        if (td.TraitCard != null)
        {
            var cd = Globals.Instance.GetCardData(td.TraitCard.Id, instantiate: false);
            if (cd != null)
                return AccessibleMenuBase.StripRichText(cd.CardName);
        }
        return AccessibleMenuBase.StripRichText(td.TraitName);
    }

    private static string TraitDescription(TraitData td)
    {
        if (td == null)
            return "";
        if (td.TraitCard == null)
            return CardSpeech.CleanFlat(td.Description);
        string cardName = TraitName(td);
        string format = Texts.Instance != null ? Texts.Instance.GetText("traitAddCard") : "";
        return string.IsNullOrEmpty(format)
            ? "Adds " + cardName + " to the deck"
            : CardSpeech.CleanFlat(string.Format(format, cardName));
    }

    /// <summary>The card a card-granting trait would add, at the tier the game would grant it
    /// (account perk tier, or the Obelisk-madness tiers) — same rules as Hero.LevelUp.</summary>
    private static CardRealtimeData TraitGrantedCard(TraitData td, Hero hero)
    {
        if (td == null)
            return null;
        if (td.TraitCardForAllHeroes != null)
            return Globals.Instance.GetCardData(td.TraitCardForAllHeroes.Id, instantiate: false);
        if (td.TraitCard == null)
            return null;
        int tier = PlayerManager.Instance.GetCharacterTier("", "trait",
            hero != null ? hero.PerkRank : 0);
        if (GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge())
        {
            if (GameManager.Instance.IsWeeklyChallenge())
                tier = 2;
            else
            {
                int madness = AtOManager.Instance != null ? AtOManager.Instance.GetObeliskMadness() : 0;
                if (madness >= 8)
                    tier = 2;
                else if (madness >= 5)
                    tier = 1;
            }
        }
        var baseCard = Globals.Instance.GetCardData(td.TraitCard.Id, instantiate: false);
        if (baseCard == null || tier == 0)
            return baseCard;
        string upgraded = tier == 1 ? baseCard.UpgradesTo1 : baseCard.UpgradesTo2;
        if (string.IsNullOrEmpty(upgraded))
            return baseCard;
        var upgradedCard = Globals.Instance.GetCardData(upgraded, instantiate: false);
        return upgradedCard != null ? upgradedCard : baseCard;
    }

    private static void BuildLevelRows()
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        if (hero == null || scd == null)
            return;

        _rows.Add(new Row { Read = () => LevelHeaderText(hero) });

        // Level 1: the innate trait.
        _rows.Add(new Row
        {
            Read = () =>
            {
                var td = Adjusted(TraitOption(scd, 0, 0), hero);
                return "Level 1 — innate trait " + TraitName(td) + ": " + TraitDescription(td);
            },
            Detail = () => OptionDetail(0, 0),
            Activate = () => SpeechManager.Speak("The innate trait comes with the hero."),
            LevelIndex = 0,
        });

        for (int i = 1; i <= 4; i++)
        {
            int level = i;
            _rows.Add(new Row
            {
                Read = () => LevelRowText(level),
                Detail = () => OptionDetail(level, _levelCol),
                Activate = () => ActivateLevelRow(level),
                LevelIndex = level,
            });
        }
    }

    private static string LevelHeaderText(Hero hero)
    {
        var sb = new StringBuilder();
        sb.Append("Level ").Append(hero.Level).Append('.');
        if (hero.Level >= 5)
        {
            sb.Append(" Maximum level reached.");
            return sb.ToString();
        }
        sb.Append(' ').Append(hero.Experience).Append(" of ")
          .Append(Globals.Instance.GetExperienceByLevel(hero.Level)).Append(" experience.");
        if (hero.IsReadyForLevelUp())
            sb.Append(MpSpeech.LocalOwns(hero)
                ? " Ready to level up — go to the level " + (hero.Level + 1) + " row."
                : " Ready to level up — only " + MpSpeech.OwnerNick(hero) + " can choose.");
        return sb.ToString();
    }

    /// <summary>The trait the hero picked at this level, or null.</summary>
    private static TraitData ChosenTrait(Hero hero, int level)
    {
        if (hero == null || hero.Traits == null || level >= hero.Traits.Length)
            return null;
        string id = hero.Traits[level];
        return string.IsNullOrEmpty(id) ? null : Globals.Instance.GetTraitData(id, hero);
    }

    private static string LevelRowText(int level)
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        if (hero == null || scd == null)
            return "";
        var sb = new StringBuilder();
        sb.Append("Level ").Append(level + 1);
        if (scd.MaxHp != null && level < scd.MaxHp.Length)
            sb.Append(", plus ").Append(scd.MaxHp[level]).Append(" max health");
        var chosen = ChosenTrait(hero, level);
        if (chosen != null)
        {
            sb.Append(" — chose ").Append(TraitName(chosen))
              .Append(". Left and right review both options.");
        }
        else if (level == hero.Level && hero.IsReadyForLevelUp())
        {
            var a = Adjusted(TraitOption(scd, level, 0), hero);
            var b = Adjusted(TraitOption(scd, level, 1), hero);
            if (MpSpeech.LocalOwns(hero))
                sb.Append(" — choose ").Append(TraitName(a)).Append(" or ").Append(TraitName(b))
                  .Append(". Left and right compare, Enter takes the focused choice.");
            else
                sb.Append(" — level up pending, only ").Append(MpSpeech.OwnerNick(hero)).Append(" can choose.");
        }
        else if (level == hero.Level)
        {
            sb.Append(" — needs ")
              .Append(Globals.Instance.GetExperienceByLevel(hero.Level) - hero.Experience)
              .Append(" more experience.");
        }
        else
        {
            sb.Append(" — not yet reached.");
        }
        return sb.ToString();
    }

    /// <summary>Left/right on a level row: the focused option, with its state.</summary>
    private static string OptionText(int level, int col)
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        if (hero == null || scd == null)
            return "";
        if (level == 0)
        {
            var innate = Adjusted(TraitOption(scd, 0, 0), hero);
            return "Innate trait " + TraitName(innate) + ": " + TraitDescription(innate);
        }
        var td = Adjusted(TraitOption(scd, level, col), hero);
        if (td == null)
            return "No option here.";
        var sb = new StringBuilder();
        sb.Append(col == 0 ? "First choice — " : "Second choice — ");
        sb.Append(TraitName(td)).Append(": ").Append(TraitDescription(td));
        var chosen = ChosenTrait(hero, level);
        if (chosen != null)
            sb.Append(chosen.Id == td.Id ? " Chosen." : " Not taken.");
        else if (level == hero.Level && hero.IsReadyForLevelUp() && MpSpeech.LocalOwns(hero))
            sb.Append(" Press Enter to take this trait.");
        return sb.ToString();
    }

    /// <summary>Alt+T on a level row: full trait text plus the granted card at its real tier.</summary>
    private static string OptionDetail(int level, int col)
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        var td = Adjusted(TraitOption(scd, level, col), hero);
        if (td == null)
            return "";
        var sb = new StringBuilder();
        sb.Append(TraitName(td)).Append(": ").Append(TraitDescription(td));
        var card = TraitGrantedCard(td, hero);
        if (card != null)
        {
            sb.Append(' ');
            if (td.TraitCardForAllHeroes != null)
                sb.Append("Adds this card to every hero's deck: ");
            sb.Append(CardSpeech.FullDetail(card));
        }
        return sb.ToString();
    }

    private static void ActivateLevelRow(int level)
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        if (hero == null || scd == null)
            return;
        var chosen = ChosenTrait(hero, level);
        if (chosen != null)
        {
            SpeechManager.Speak("Already chose " + TraitName(chosen) + " at this level.");
            return;
        }
        if (level > hero.Level)
        {
            SpeechManager.Speak("Locked — " + SubjectName() + " must reach level " + level + " first.");
            return;
        }
        if (!hero.IsReadyForLevelUp())
        {
            SpeechManager.Speak("Not enough experience — needs "
                + (Globals.Instance.GetExperienceByLevel(hero.Level) - hero.Experience)
                + " more.");
            return;
        }
        if (!MpSpeech.LocalOwns(hero))
        {
            SpeechManager.Speak("Only " + MpSpeech.OwnerNick(hero) + " can choose this trait.");
            return;
        }
        // Same scene rule as the game's own TraitLevel click: map or town only.
        if (TownManager.Instance == null && MapManager.Instance == null)
        {
            SpeechManager.Speak("Leveling up is only possible on the map or in town.");
            return;
        }
        var td = Adjusted(TraitOption(scd, level, _levelCol), hero);
        if (td == null)
            return;
        // Mirror TraitLevel.OnMouseUp: direct model call in SP, RPC in MP. The Hero.LevelUp
        // postfix announces the outcome (it also covers mouse picks and teammate echoes).
        if (GameManager.Instance != null && GameManager.Instance.IsMultiplayer())
            AtOManager.Instance.HeroLevelUpMP(_heroIndex, td.Id);
        else
            hero.LevelUp(td.Id);
        BuildRows();
    }

    /// <summary>Hero.LevelUp landed and actually raised the level — refresh our rows so the
    /// focused row now reads as chosen.</summary>
    public static void OnHeroLeveledUp(Hero hero)
    {
        if (!IsOpen || _isNpc || hero == null || hero.HeroIndex != _heroIndex)
            return;
        BuildRows();
    }

    // ----------------------------------------------- items tab

    private static void BuildItemsRows()
    {
        var hero = CurrentHero();
        if (hero == null)
            return;
        AddItemRow("Weapon", hero.Weapon);
        AddItemRow("Armor", hero.Armor);
        AddItemRow("Jewelry", hero.Jewelry);
        AddItemRow("Accessory", hero.Accesory);
        AddItemRow("Pet", hero.Pet);
    }

    private static void AddItemRow(string slot, string cardId)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            _rows.Add(new Row { Read = () => slot + ": empty." });
            return;
        }
        var cd = Globals.Instance.GetCardData(cardId, instantiate: false);
        if (cd == null)
        {
            _rows.Add(new Row { Read = () => slot + ": unknown item." });
            return;
        }
        var captured = cd;
        _rows.Add(new Row
        {
            Read = () => slot + ": " + CardSpeech.BriefLine(captured),
            Card = captured,
        });
    }

    // ----------------------------------------------- stats tab

    /// <summary>"--" and empty TMP values become "none" in speech.</summary>
    private static string StatValue(TMP_Text tmp)
    {
        if (tmp == null)
            return "none";
        string s = AccessibleMenuBase.StripRichText(tmp.text).Trim();
        return s == "" || s == "--" ? "none" : s;
    }

    private static string PopText(PopupText pop)
        => pop != null ? CardSpeech.CleanFlat(pop.text) : "";

    private static void BuildStatsRows()
    {
        var character = _window.currentCharacter;
        var sw = _window.statsWindow;
        if (character == null || sw == null)
            return;

        var hero = character as Hero;
        _rows.Add(new Row
        {
            Read = () => hero != null
                ? SubjectName() + ", level " + hero.Level + "."
                : SubjectName() + ".",
        });
        _rows.Add(new Row
        {
            Read = () => "Health " + character.HpCurrent + " of " + character.Hp + ".",
        });
        if (character.IsHero)
        {
            _rows.Add(new Row
            {
                Read = () =>
                {
                    int energy = MatchManager.Instance != null
                        ? character.EnergyCurrent : character.Energy;
                    return "Energy " + energy + ", plus " + character.EnergyTurn + " per turn.";
                },
            });
        }
        _rows.Add(new Row
        {
            Read = () =>
            {
                int[] speed = character.GetSpeed();
                return "Speed " + speed[0] + " of " + speed[1] + ".";
            },
        });
        if (character.IsHero)
        {
            _rows.Add(new Row
            {
                Read = () => "Draws " + character.GetDrawCardsTurnForDisplayInDeck()
                    + " cards per turn.",
            });
        }

        // Global modifiers — read from the TMP fields DoStats just filled; the popup texts give
        // Alt+T the same itemised source breakdown a sighted player's hover shows.
        _rows.Add(new Row
        {
            Read = () => "Damage done bonus: " + StatValue(sw.globalDamageDonePercent) + ".",
            Detail = () => DetailOrNone(PopText(sw.globalDamageDonePop)),
        });
        _rows.Add(new Row
        {
            Read = () => "Healing done: " + StatValue(sw.globalHealingDonePercent)
                + ", flat bonus " + StatValue(sw.globalHealingDoneFlat) + ".",
            Detail = () => DetailOrNone(JoinDetails(
                PopText(sw.globalHealingDonePercentPop), PopText(sw.globalHealingDoneFlatPop))),
        });
        _rows.Add(new Row
        {
            Read = () => "Healing received: " + StatValue(sw.globalHealingTakenPercent)
                + ", flat bonus " + StatValue(sw.globalHealingTakenFlat) + ".",
            Detail = () => DetailOrNone(JoinDetails(
                PopText(sw.globalHealingTakenPercentPop), PopText(sw.globalHealingTakenFlatPop))),
        });

        // The nine damage-type rows, from the arrays DoStats populated (private — Traverse).
        var tw = Traverse.Create(sw);
        var resistText = tw.Field<TMP_Text[]>("resistanceText").Value;
        var ddText = tw.Field<TMP_Text[]>("ddText").Value;
        var dtText = tw.Field<TMP_Text[]>("dtText").Value;
        var resistPop = tw.Field<PopupText[]>("resistancePop").Value;
        var ddPop = tw.Field<PopupText[]>("damagedonePop").Value;
        var dtPop = tw.Field<PopupText[]>("damagetakenPop").Value;
        string[] typeKeys =
            { "slash", "blunt", "piercing", "fire", "cold", "lightning", "holy", "shadow", "mind" };
        for (int i = 0; i < typeKeys.Length; i++)
        {
            if (resistText == null || i >= resistText.Length)
                break;
            int index = i;
            string typeName = DamageTypeName(typeKeys[i]);
            _rows.Add(new Row
            {
                Read = () => typeName
                    + ": resistance " + StatValue(resistText[index])
                    + ", damage bonus " + StatValue(ddText != null ? ddText[index] : null)
                    + ", extra damage taken " + StatValue(dtText != null ? dtText[index] : null)
                    + ".",
                Detail = () => DetailOrNone(JoinDetails(
                    Section("Resistance", PopText(resistPop != null ? resistPop[index] : null)),
                    Section("Damage done", PopText(ddPop != null ? ddPop[index] : null)),
                    Section("Damage taken", PopText(dtPop != null ? dtPop[index] : null)))),
            });
        }

        BuildAuraRows(character);
    }

    private static string DamageTypeName(string key)
    {
        string name = Texts.Instance != null ? Texts.Instance.GetText(key) : "";
        return string.IsNullOrEmpty(name)
            ? Functions.UppercaseFirst(key)
            : Functions.UppercaseFirst(AccessibleMenuBase.StripRichText(name));
    }

    private static string Section(string label, string body)
        => string.IsNullOrEmpty(body) ? "" : label + ": " + body;

    private static string JoinDetails(params string[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p))
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(p.Trim());
            if (!p.Trim().EndsWith("."))
                sb.Append('.');
        }
        return sb.ToString();
    }

    private static string DetailOrNone(string detail)
        => string.IsNullOrEmpty(detail) ? "No modifiers." : detail;

    private static void BuildAuraRows(Character character)
    {
        // Active effects — one row per aura/curse, like the icon strip.
        bool any = false;
        if (character.AuraCurseList != null)
        {
            foreach (var aura in character.AuraCurseList)
            {
                if (aura == null || aura.ACData == null)
                    continue;
                if (aura.ACData.Id == "stealthbonus" || aura.ACData.Id == "furnace")
                    continue; // the game hides these in the stats view too
                var captured = aura;
                any = true;
                _rows.Add(new Row
                {
                    Read = () =>
                    {
                        int charges = captured.GetCharges();
                        return "Effect: " + CardSpeech.AuraName(captured.ACData) + ", " + charges
                            + (charges == 1 ? " charge." : " charges.");
                    },
                    Detail = () => CardSpeech.AuraName(captured.ACData) + ": "
                        + CardSpeech.CleanFlat(captured.ACData.Description),
                });
            }
        }
        if (!any)
            _rows.Add(new Row { Read = () => "No active effects." });

        // Immunities (innate + item-granted, deduplicated — same merge as DoStats).
        var immune = new List<string>();
        if (character.AuracurseImmune != null)
        {
            foreach (var id in character.AuracurseImmune)
            {
                if (!immune.Contains(id))
                    immune.Add(id);
            }
        }
        var itemImmune = character.AuraCurseImmunitiesByItemsList();
        if (itemImmune != null)
        {
            foreach (var id in itemImmune)
            {
                if (!immune.Contains(id))
                    immune.Add(id);
            }
        }
        _rows.Add(new Row
        {
            Read = () =>
            {
                if (immune.Count == 0)
                    return "No immunities.";
                var names = new List<string>();
                foreach (var id in immune)
                {
                    string n = CardSpeech.AuraName(Globals.Instance.GetAuraCurseData(id));
                    names.Add(n.Length > 0 ? n : id);
                }
                return "Immune to: " + string.Join(", ", names.ToArray()) + ".";
            },
        });

        // Charge modifiers ("your Bleed applies +1"), merged the way DoStats merges them.
        _rows.Add(new Row { Read = () => ChargeModifierText(character) });
    }

    private static string ChargeModifierText(Character character)
    {
        var merged = new Dictionary<string, int>();
        MergeCharges(merged, character.GetItemAuraCurseModifiers(),
            accumulateNegatives: true, createNegatives: true);
        MergeCharges(merged, character.GetTraitAuraCurseModifiers(),
            accumulateNegatives: false, createNegatives: false);
        Dictionary<string, int> baseDict = character.HeroData != null
            && character.HeroData.HeroSubClass != null
            ? Perk.GetAuraCurseBonusDict(character.HeroData.HeroSubClass.Id)
            : character.AuraCurseModification;
        MergeCharges(merged, baseDict, accumulateNegatives: false, createNegatives: false);
        // Auras accumulate onto existing keys regardless of sign, but the game never creates
        // a key from a purely negative aura modifier (StatsWindowUI.DoStats).
        MergeCharges(merged, character.GetAurasAuraCurseModifiers(),
            accumulateNegatives: true, createNegatives: false);

        var parts = new List<string>();
        foreach (var pair in merged)
        {
            if (pair.Value == 0)
                continue;
            string name = CardSpeech.AuraName(Globals.Instance.GetAuraCurseData(pair.Key));
            if (name.Length == 0)
                name = pair.Key;
            parts.Add(name + (pair.Value > 0 ? " plus " + pair.Value : " minus " + -pair.Value));
        }
        return parts.Count == 0
            ? "No charge bonuses."
            : "Charge bonuses: " + string.Join(", ", parts.ToArray()) + ".";
    }

    private static void MergeCharges(Dictionary<string, int> into,
        Dictionary<string, int> from, bool accumulateNegatives, bool createNegatives)
    {
        if (from == null)
            return;
        foreach (var pair in from)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;
            if (into.ContainsKey(pair.Key))
            {
                if (accumulateNegatives || pair.Value > 0)
                    into[pair.Key] += pair.Value;
            }
            else if (createNegatives || pair.Value > 0)
            {
                into.Add(pair.Key, pair.Value);
            }
        }
    }

    // ----------------------------------------------- perks (virtual) tab

    private static void BuildPerksRow()
    {
        var hero = CurrentHero();
        var scd = SubClass(hero);
        if (scd == null)
            return;
        string id = scd.Id;
        _rows.Add(new Row
        {
            Read = () =>
            {
                int points = PlayerManager.Instance.GetPerkPointsAvailable(id);
                return "Perk tree — " + points + (points == 1 ? " point" : " points")
                    + " available. Press Enter to open.";
            },
            Activate = () => ShowTab("perks"),
        });
    }

    // ------------------------------------------------------------------ input handlers

    public static void MoveVertical(int delta)
    {
        if (!IsOpen)
            return;
        if (_popupMode)
        {
            ExitDrillSilently();
            MovePopup(delta); // be forgiving: up/down also walk the popup cards
            return;
        }
        ExitDrillSilently(); // plain arrows leave an active drill and resume row navigation
        BuildRowsIfEmpty();
        if (_rows.Count == 0)
        {
            SpeechManager.Speak("Nothing to show on this tab.");
            return;
        }
        int next = _rowIndex < 0
            ? (delta > 0 ? 0 : _rows.Count - 1)
            : Mathf.Clamp(_rowIndex + delta, 0, _rows.Count - 1);
        _rowIndex = next;
        _levelCol = 0;
        if (_rows[next].Card != null)
            HeroSpeech.PlayCardFocusSound();
        else
            HeroSpeech.PlayButtonFocusSound();
        SpeechManager.Speak(_rows[next].Read());
    }

    public static void MoveHorizontal(int delta)
    {
        if (!IsOpen)
            return;
        if (_popupMode)
        {
            ExitDrillSilently();
            MovePopup(delta);
            return;
        }
        ExitDrillSilently();
        BuildRowsIfEmpty();
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
        {
            SpeechManager.Speak("Up and down to read this tab first.");
            return;
        }
        var row = _rows[_rowIndex];
        if (TabKey() == "level" && row.LevelIndex > 0)
        {
            _levelCol = Mathf.Clamp(_levelCol + delta, 0, 1);
            HeroSpeech.PlayButtonFocusSound();
            SpeechManager.Speak(OptionText(row.LevelIndex, _levelCol));
            return;
        }
        // Elsewhere left/right just re-read the focused row.
        SpeechManager.Speak(row.Read());
    }

    private static void MovePopup(int delta)
    {
        if (_popupCards.Count == 0)
        {
            SpeechManager.Speak("Nothing to review.");
            return;
        }
        _popupIndex = Mathf.Clamp(_popupIndex + delta, 0, _popupCards.Count - 1);
        var cd = PopupCardData(_popupIndex);
        HeroSpeech.PlayCardFocusSound();
        SpeechManager.Speak(cd != null
            ? CardSpeech.BriefLine(cd) + ", " + (_popupIndex + 1) + " of " + _popupCards.Count
            : "Unknown card");
    }

    private static CardRealtimeData PopupCardData(int index)
        => index >= 0 && index < _popupCards.Count
            ? Globals.Instance.GetCardData(_popupCards[index], instantiate: false)
            : null;

    public static void Activate()
    {
        if (!IsOpen)
            return;
        // The Enter that opened the sheet also arrives as a Confirm — ignore it.
        if (Time.frameCount == _openedFrame)
            return;
        if (_popupMode)
        {
            _window.Hide(); // done reviewing — the Hide postfix announces
            return;
        }
        BuildRowsIfEmpty();
        // A single-row tab (Perks) announces its row on arrival — Enter should just work.
        if (_rowIndex < 0 && _rows.Count == 1)
            _rowIndex = 0;
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
        {
            SpeechManager.Speak("Up and down to read this tab first.");
            return;
        }
        var row = _rows[_rowIndex];
        if (row.Activate == null)
        {
            SpeechManager.Speak(row.Read());
            return;
        }
        row.Activate();
    }

    /// <summary>Escape: exit an active drill first; otherwise close the sheet.</summary>
    public static bool Cancel()
    {
        if (!IsOpen)
            return false;
        if (TryDrillExit())
            return true;
        _window.Hide(); // the Hide postfix announces
        return true;
    }

    // ------------------------------------------------------------------ drill-in (Ctrl+Up/Down)

    private static CardRealtimeData FocusedCard(out int? liveCost)
    {
        liveCost = null;
        if (_popupMode)
            return PopupCardData(_popupIndex);
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
            return null;
        var row = _rows[_rowIndex];
        if (row.Card != null)
        {
            liveCost = row.LiveCost;
            return row.Card;
        }
        // Level rows drill into the granted card of the focused option.
        if (TabKey() == "level" && row.LevelIndex >= 0)
        {
            var hero = CurrentHero();
            return TraitGrantedCard(
                Adjusted(TraitOption(SubClass(hero), row.LevelIndex, _levelCol), hero), hero);
        }
        return null;
    }

    public static void DrillNext(int dir)
    {
        if (!IsOpen)
            return;
        if (!_drill)
        {
            var card = FocusedCard(out int? liveCost);
            if (card == null)
                return; // nothing to drill on this row
            _drillLines.Clear();
            _drillLines.AddRange(CardSpeech.DetailLines(card, liveCost));
            _drill = true;
            _drillIndex = 0;
            SpeechManager.Speak(_drillLines.Count > 0 ? _drillLines[0] : "No description");
            return;
        }
        if (_drillLines.Count > 0)
        {
            _drillIndex = Mathf.Clamp(_drillIndex + dir, 0, _drillLines.Count - 1);
            SpeechManager.Speak(_drillLines[_drillIndex]);
        }
    }

    public static bool TryDrillExit()
    {
        if (!_drill)
            return false;
        ExitDrillSilently();
        if (_popupMode)
        {
            var cd = PopupCardData(_popupIndex);
            if (cd != null)
                SpeechManager.Speak(CardSpeech.BriefLine(cd));
        }
        else if (_rowIndex >= 0 && _rowIndex < _rows.Count)
        {
            SpeechManager.Speak(_rows[_rowIndex].Read());
        }
        return true;
    }

    private static void ExitDrillSilently()
    {
        _drill = false;
        _drillLines.Clear();
        _drillIndex = 0;
    }

    // ------------------------------------------------------------------ review keys

    /// <summary>Alt+T: full detail of the focused row (card text, trait + granted card,
    /// per-source stat breakdown).</summary>
    public static void SpeakRowDetail()
    {
        if (!IsOpen)
            return;
        if (_popupMode)
        {
            var cd = PopupCardData(_popupIndex);
            SpeechManager.Speak(cd != null ? CardSpeech.FullDetail(cd) : "Nothing to review.");
            return;
        }
        BuildRowsIfEmpty();
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
        {
            SpeechManager.Speak("Up and down to read this tab first.");
            return;
        }
        var row = _rows[_rowIndex];
        if (row.Card != null)
            SpeechManager.Speak(CardSpeech.FullDetail(row.Card, row.LiveCost));
        else if (row.Detail != null)
            SpeechManager.Speak(row.Detail());
        else
            SpeechManager.Speak(row.Read());
    }

    /// <summary>Alt+I: the sheet headline — subject, level/XP/HP, tab, and for deck tabs the
    /// energy histogram.</summary>
    public static void SpeakOverview()
    {
        if (!IsOpen)
            return;
        if (_popupMode)
        {
            SpeechManager.Speak("Cards upgraded — " + _popupCards.Count
                + (_popupCards.Count == 1 ? " card." : " cards.")
                + " Left and right to review, Escape to close.");
            return;
        }
        BuildRowsIfEmpty();
        var sb = new StringBuilder();
        sb.Append("Character sheet — ").Append(SubjectName());
        var hero = CurrentHero();
        if (hero != null)
        {
            sb.Append(", level ").Append(hero.Level);
            if (hero.Level < 5)
                sb.Append(", ").Append(hero.Experience).Append(" of ")
                  .Append(Globals.Instance.GetExperienceByLevel(hero.Level)).Append(" experience");
            sb.Append(", ").Append(hero.HpCurrent).Append(" of ").Append(hero.Hp).Append(" health");
            if (hero.IsReadyForLevelUp())
                sb.Append(", ready to level up");
        }
        sb.Append(". ").Append(TabName(_element)).Append(" tab, ")
          .Append(_rows.Count).Append(_rows.Count == 1 ? " row." : " rows.");
        string key = TabKey();
        if (key == "deck" || key == "combatdeck" || key == "combatdiscard" || key == "combatvanish")
        {
            var main = new List<string>();
            var injuries = new List<string>();
            CollectDeckCards(main, injuries, out _);
            string energy = _isNpc ? "" : EnergySummary(main, hero, histogram: true);
            if (energy != "")
                sb.Append(' ').Append(energy);
        }
        SpeechManager.Speak(sb.ToString());
    }
}

// ======================= announcement patches =======================

/// <summary>Every path into the sheet lands here — portrait clicks, combat right-clicks, pile
/// icons, our keyboard paths, and tab switches. The manager sorts out open vs tab-change vs
/// hero-change and skips the FinishRun scene (its unlocked-cards popup has its own manager).</summary>
[HarmonyPatch(typeof(CharacterWindowUI), nameof(CharacterWindowUI.Show))]
internal static class CharWindowShowAnnouncePatch
{
    static void Postfix(CharacterWindowUI __instance, string _element, bool isHero)
        => CharWindowScreenManager.OnShown(__instance, _element, isHero);
}

/// <summary>The sheet closed (Escape, the exit button, or the hero died under us). The prefix
/// snapshots the open state — Hide is a no-op when the window is already closed.</summary>
[HarmonyPatch(typeof(CharacterWindowUI), nameof(CharacterWindowUI.Hide))]
internal static class CharWindowHideAnnouncePatch
{
    static void Prefix(CharacterWindowUI __instance, out bool __state)
        => __state = __instance.IsActive();

    static void Postfix(bool __state)
    {
        if (__state)
            CharWindowScreenManager.OnHidden();
    }
}

/// <summary>The mid-run "cards upgraded" popup (random-upgrade events). The FinishRun scene's
/// unlocked-cards popup uses ShowUnlockedCards instead and keeps its existing handling.</summary>
[HarmonyPatch(typeof(CharacterWindowUI), nameof(CharacterWindowUI.ShowUpgradedCards))]
internal static class CharWindowUpgradedCardsPatch
{
    static void Postfix(List<string> upgradedCards)
        => CharWindowScreenManager.OnUpgradedCardsShown(upgradedCards);
}

/// <summary>A hero leveled up — ours by keyboard, anyone's by mouse, or a teammate's via the
/// HeroLevelUpMP RPC (which runs Hero.LevelUp on every client). The prefix snapshots the level:
/// the game calls this even when the trait assignment fails, so only a real level change
/// speaks. Queued so a multiplayer echo never stomps whatever is being read.</summary>
[HarmonyPatch(typeof(Hero), nameof(Hero.LevelUp), typeof(string))]
internal static class HeroLevelUpAnnouncePatch
{
    static void Prefix(Hero __instance, out int __state)
        => __state = __instance.Level;

    static void Postfix(Hero __instance, string traitId, int __state)
    {
        if (__instance == null || __instance.Level == __state)
            return;
        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(__instance.SourceName));
        sb.Append(" leveled up to ").Append(__instance.Level);
        var td = Globals.Instance.GetTraitData(traitId, __instance);
        string traitName = "";
        if (td != null)
        {
            if (td.TraitCard != null)
            {
                var cd = Globals.Instance.GetCardData(td.TraitCard.Id, instantiate: false);
                traitName = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : "";
            }
            if (traitName == "")
                traitName = AccessibleMenuBase.StripRichText(td.TraitName);
        }
        if (traitName != "")
            sb.Append(" — ").Append(traitName);
        sb.Append(". Max health now ").Append(__instance.Hp).Append('.');
        if (td != null && td.TraitCard != null)
            sb.Append(" Card added to deck.");
        else if (td != null && td.TraitCardForAllHeroes != null)
            sb.Append(" Card added to every hero's deck.");
        SpeechManager.SpeakQueued(sb.ToString());
        CharWindowScreenManager.OnHeroLeveledUp(__instance);
    }
}
