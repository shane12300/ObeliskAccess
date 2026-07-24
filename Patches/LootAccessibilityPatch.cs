using System.Collections.Generic;
using System.Text;
using Cards;
using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// Accessible navigation and speech for the item-loot scene (<c>LootManager</c>) — shown after
/// boss/chest fights when a map event's close data carries a loot table, and on Obelisk-challenge
/// boss/chest/draft nodes. One shared pool, heroes pick in turn: the screen is modelled as a
/// single loot row (item cards, then the gold pile, then the Restart button) plus a Tab party
/// region for reviewing each hero's equipped items. Enter takes the focused thing for the hero
/// whose turn it is; turn changes are announced from a per-frame poll because the game advances
/// turns inside coroutines (with network sync in multiplayer).
/// </summary>
internal static class LootScreenManager
{
    private enum Region { Items, Party }
    private enum ItemKind { Card, Gold, Restart }

    private struct ItemEntry
    {
        public ItemKind Kind;
        public CardItem Card; // only for Kind == Card
    }

    private static Region _region = Region.Items;
    private static int _item = -1; // -1 sentinel: nothing focused yet
    private static int _hero;

    // Display slots (0..3) that hold a living hero, in on-screen order.
    private static readonly List<int> _slots = new List<int>();

    private static bool _wasOpen;
    private static bool _ready;
    private static bool _announcedArrival;
    private static bool _announcedFinish;
    private static int _lastTurnSlot = -1;
    private static bool _windowWasOpen;

    // Who took which loot card, for re-reads of taken items (keyed by lootId).
    private static readonly Dictionary<string, string> _takenBy = new Dictionary<string, string>();

    // Ctrl+Up/Down card drill (same shape as RewardsScreenManager's).
    private static bool _drill;
    private static readonly List<string> _cardLines = new List<string>();
    private static int _cardLineIndex;

    // ======================= lifecycle =======================

    /// <summary>
    /// Per-frame tick from the poller, outside the active-context gate: the loot cards animate in
    /// one at a time and multiplayer waits on network sync, so readiness and turn detection must
    /// keep running while an alert or tutorial popup owns input.
    /// </summary>
    public static void Tick()
    {
        var lm = LootManager.Instance;
        if (lm == null)
        {
            if (_wasOpen)
                ResetState();
            return;
        }
        _wasOpen = true;

        // The deck/character window makes the context yield (it isn't accessible yet), so a
        // silent open would strand the user with dead keys. Announce both edges — the window can
        // still open via a real mouse click even now that the stale-cursor Enter click is
        // suppressed.
        bool windowOpen = lm.characterWindowUI != null && lm.characterWindowUI.IsActive();
        if (windowOpen != _windowWasOpen)
        {
            _windowWasOpen = windowOpen;
            if (windowOpen)
                SpeechManager.Speak("Character window opened. Not accessible yet — press Escape to close.");
            else
                SpeechManager.Speak("Character window closed. Back on loot.");
        }

        if (!_ready)
        {
            // ActivateCharacter(0) runs only after the item-spawn coroutine finishes, so a valid
            // active slot doubles as the "all cards are on screen" signal.
            if (ActiveSlot(lm) < 0)
                return;

            _slots.Clear();
            var order = CharacterOrder(lm);
            if (order == null)
                return;
            for (int i = 0; i < 4 && i < order.Count; i++)
            {
                if (HeroAt(lm, i) != null)
                    _slots.Add(i);
            }
            if (_slots.Count == 0)
                return;

            _ready = true;
            if (!_announcedArrival)
            {
                _announcedArrival = true;
                int slot = ActiveSlot(lm);
                _lastTurnSlot = slot >= 0 && slot < 4 ? slot : -1;
                // Queued: a first-visit tutorial popup announcement may still be speaking.
                SpeechManager.SpeakQueued(BuildOverview(lm));
            }
            return;
        }

        if (!_announcedFinish && Traverse.Create(lm).Field<bool>("finishLoot").Value)
        {
            _announcedFinish = true;
            SpeechManager.SpeakQueued("All heroes have chosen. Leaving loot.");
            return;
        }

        // Turn watch: the active slot moves inside coroutines (pick advance, ChangeCharacter,
        // multiplayer sync), so polling is the one mechanism that catches every path.
        int active = ActiveSlot(lm);
        if (active >= 0 && active < 4 && active != _lastTurnSlot)
        {
            _lastTurnSlot = active;
            AnnounceTurn(lm, active);
        }
    }

    private static void ResetState()
    {
        _wasOpen = false;
        _ready = false;
        _announcedArrival = false;
        _announcedFinish = false;
        _lastTurnSlot = -1;
        _windowWasOpen = false;
        _slots.Clear();
        _takenBy.Clear();
        _region = Region.Items;
        _item = -1;
        _hero = 0;
        ExitDrillSilently();
    }

    // ======================= navigation =======================

    /// <summary>
    /// Move within the current region. Both axes traverse the same list (the loot row is
    /// horizontal on screen, the party column vertical, but one-dimensional either way).
    /// </summary>
    public static void MoveFocus(int delta)
    {
        if (!EnsureReady())
            return;
        ExitDrillSilently();

        if (_region == Region.Party)
        {
            if (_slots.Count == 0)
                return;
            _hero = Wrap(_hero + delta, _slots.Count);
            PlayFocusSound(ItemKind.Restart); // button hover sound
            SpeechManager.Speak(DescribeHero(LootManager.Instance, _hero));
            return;
        }

        var items = BuildItems(LootManager.Instance);
        if (items.Count == 0)
            return;
        if (_item < 0)
            _item = delta > 0 ? 0 : items.Count - 1;
        else
            _item = Wrap(_item + delta, items.Count);
        PlayFocusSound(items[_item].Kind);
        SpeechManager.Speak(DescribeItem(LootManager.Instance, items, _item));
    }

    public static void ToggleRegion()
    {
        if (!EnsureReady())
            return;
        ExitDrillSilently();

        if (_region == Region.Items)
        {
            _region = Region.Party;
            _hero = Clamp(_hero, 0, _slots.Count - 1);
            SpeechManager.Speak("Party review. " + DescribeHero(LootManager.Instance, _hero));
        }
        else
        {
            _region = Region.Items;
            var items = BuildItems(LootManager.Instance);
            if (_item >= 0 && items.Count > 0)
            {
                _item = Clamp(_item, 0, items.Count - 1);
                SpeechManager.Speak("Loot. " + DescribeItem(LootManager.Instance, items, _item));
            }
            else
            {
                SpeechManager.Speak("Loot. Left and right to browse the items.");
            }
        }
    }

    public static void JumpToHero(int n)
    {
        if (!EnsureReady())
            return;
        if (n < 1 || n > _slots.Count)
            return;
        ExitDrillSilently();
        _region = Region.Party;
        _hero = n - 1;
        PlayFocusSound(ItemKind.Restart);
        SpeechManager.Speak(DescribeHero(LootManager.Instance, _hero));
    }

    private static void PlayFocusSound(ItemKind kind)
    {
        if (GameManager.Instance == null || AudioManager.Instance == null)
            return;
        if (kind == ItemKind.Card)
        {
            if (GameManager.Instance.ConfigUseLegacySounds || AudioManager.Instance.soundCardHover == null)
                GameManager.Instance.PlayLibraryAudio("card_play");
            else
                GameManager.Instance.PlayAudio(AudioManager.Instance.soundCardHover, 0.1f);
        }
        else
        {
            GameManager.Instance.PlayAudio(AudioManager.Instance.soundButtonHover);
        }
    }

    // ======================= activation =======================

    public static void Activate()
    {
        if (!EnsureReady())
            return;
        var lm = LootManager.Instance;

        if (_region == Region.Party)
        {
            ActivatePartyHero(lm);
            return;
        }

        if (_item < 0)
        {
            SpeechManager.Speak("Nothing focused. Use the arrow keys first.");
            return;
        }
        var items = BuildItems(lm);
        if (items.Count == 0)
            return;
        _item = Clamp(_item, 0, items.Count - 1);
        var entry = items[_item];

        if (entry.Kind == ItemKind.Restart)
        {
            lm.RestartLoot();
            // Main player restarts outright; a multiplayer client's request raises a confirm
            // alert on the host, which the global alert announcements cover.
            if (GameManager.Instance.IsMainPlayer())
                SpeechManager.Speak("Restarting loot.");
            else
                SpeechManager.Speak("Restart request sent to the host.");
            return;
        }

        int active = ActiveSlot(lm);
        if (active < 0 || active >= 4)
        {
            SpeechManager.Speak("No hero is choosing right now. Wait a moment.");
            return;
        }
        if (GameManager.Instance.IsMultiplayer() && !lm.IsMyLoot)
        {
            SpeechManager.Speak(OwnerNick(lm, active) + " must make this pick.");
            return;
        }

        if (entry.Kind == ItemKind.Gold)
        {
            lm.LootGold(); // announcement comes from the LootGold postfix
            return;
        }

        var card = entry.Card;
        if (card == null)
            return;
        if (IsTaken(lm, card.lootId))
        {
            SpeechManager.Speak("Already taken" + TakenBySuffix(card.lootId) + ".");
            return;
        }
        // Same call CardItem's click branch makes; the ownership guard above is ours to enforce —
        // Looted itself doesn't check IsMyLoot, only the mouse path does.
        lm.Looted(card.lootId); // announcement comes from the Looted postfix
    }

    private static void ActivatePartyHero(LootManager lm)
    {
        if (_slots.Count == 0)
            return;
        _hero = Clamp(_hero, 0, _slots.Count - 1);
        int slot = _slots[_hero];

        if (GameManager.Instance.IsMultiplayer())
        {
            SpeechManager.Speak("Pick order is fixed in multiplayer.");
            return;
        }
        if (LootedFlag(lm, slot))
        {
            SpeechManager.Speak(HeroName(lm, slot) + " has already picked.");
            return;
        }
        if (slot == ActiveSlot(lm))
        {
            SpeechManager.Speak(HeroName(lm, slot) + " is already choosing.");
            return;
        }
        lm.ChangeCharacter(slot); // the turn watch announces the change
    }

    // ======================= drill-in (Ctrl+Up/Down) =======================

    public static void DrillNext(int dir)
    {
        if (!EnsureReady())
            return;

        if (!_drill)
        {
            var card = FocusedCard();
            if (card == null || card.CardData == null)
                return; // nothing to drill on this element
            _cardLines.Clear();
            _cardLines.AddRange(CardSpeech.DetailLines(card.CardData));
            _drill = true;
            _cardLineIndex = 0;
            SpeechManager.Speak(_cardLines.Count > 0 ? _cardLines[0] : "No description");
            return;
        }

        if (_cardLines.Count > 0)
        {
            _cardLineIndex = Clamp(_cardLineIndex + dir, 0, _cardLines.Count - 1);
            SpeechManager.Speak(_cardLines[_cardLineIndex]);
        }
    }

    public static bool TryDrillExit()
    {
        if (!_drill)
            return false;
        ExitDrillSilently();
        AnnounceFocus();
        return true;
    }

    private static void ExitDrillSilently()
    {
        _drill = false;
        _cardLines.Clear();
        _cardLineIndex = 0;
    }

    // ======================= review hotkeys =======================

    /// <summary>Alt+T: full detail for the focused item card; otherwise repeat the focus.</summary>
    public static void SpeakFocusedDetail()
    {
        if (!EnsureReady())
            return;
        var card = FocusedCard();
        if (card != null && card.CardData != null)
        {
            var sb = new StringBuilder(CardSpeech.FullDetail(card.CardData));
            var lm = LootManager.Instance;
            if (IsTaken(lm, card.lootId))
                sb.Append(". Already taken").Append(TakenBySuffix(card.lootId));
            else
                sb.Append(ComparisonClause(lm, card.CardData));
            SpeechManager.Speak(sb.ToString());
            return;
        }
        AnnounceFocus();
    }

    /// <summary>Alt+I: the screen overview plus each hero's picked status.</summary>
    public static void SpeakOverview()
    {
        var lm = LootManager.Instance;
        if (lm == null)
            return;
        if (!_ready)
        {
            SpeechManager.Speak("Loot is still appearing.");
            return;
        }
        var sb = new StringBuilder(BuildOverview(lm));
        for (int i = 0; i < _slots.Count; i++)
        {
            int slot = _slots[i];
            sb.Append(' ').Append(HeroName(lm, slot))
              .Append(LootedFlag(lm, slot) ? " has picked." : " has not picked.");
        }
        SpeechManager.Speak(sb.ToString());
    }

    public static void SpeakGold()
    {
        var cm = AtOManager.Instance != null ? AtOManager.Instance.CurrencyManager : null;
        int gold = cm != null ? cm.GetPlayerGold() : 0;
        SpeechManager.Speak("Gold: " + gold);
    }

    // ======================= pick / restart announcements (from patches) =======================

    /// <summary>An item pick landed for display slot <paramref name="slot"/> (any client).</summary>
    public static void OnItemTaken(int slot, string lootId)
    {
        var lm = LootManager.Instance;
        if (lm == null)
            return;
        string hero = HeroName(lm, slot);
        _takenBy[lootId] = hero;
        SpeechManager.SpeakQueued(hero + " takes " + ItemName(lootId) + ".");
    }

    public static void OnGoldTaken(int slot)
    {
        var lm = LootManager.Instance;
        if (lm == null)
            return;
        int gold = Traverse.Create(lm).Field<int>("goldQuantity").Value;
        SpeechManager.SpeakQueued(HeroName(lm, slot) + " takes " + gold + " gold.");
    }

    // ======================= describing =======================

    private static string BuildOverview(LootManager lm)
    {
        var parts = new List<string>();
        parts.Add("Item loot");

        if (lm.description != null && lm.description.gameObject.activeSelf)
        {
            string desc = CardSpeech.CleanFlat(lm.description.text);
            if (desc.Length > 0)
                parts.Add(desc);
        }

        var items = BuildItems(lm);
        var names = new List<string>();
        foreach (var e in items)
        {
            if (e.Kind == ItemKind.Card && e.Card != null && e.Card.CardData != null)
                names.Add(AccessibleMenuBase.StripRichText(e.Card.CardData.CardName));
        }
        if (names.Count > 0)
            parts.Add(names.Count + (names.Count == 1 ? " item: " : " items: ") + string.Join(", ", names.ToArray()));

        if (GoldVisible(lm))
            parts.Add("Gold pile: " + Traverse.Create(lm).Field<int>("goldQuantity").Value + " gold");

        int active = ActiveSlot(lm);
        if (active >= 0 && active < 4)
            parts.Add("Choosing: " + HeroName(lm, active) + OwnershipClause(lm, active));

        parts.Add("Left and right for loot, enter to take. Tab for party review. Control up and down for item details");
        return string.Join(". ", parts.ToArray()) + ".";
    }

    private static void AnnounceTurn(LootManager lm, int slot)
    {
        SpeechManager.SpeakQueued("Choosing: " + HeroName(lm, slot) + OwnershipClause(lm, slot) + ".");
    }

    /// <summary>In multiplayer, says whose pick this is; empty in single player.</summary>
    private static string OwnershipClause(LootManager lm, int slot)
    {
        if (!GameManager.Instance.IsMultiplayer())
            return "";
        var hero = HeroAt(lm, slot);
        if (hero == null || NetworkManager.Instance == null)
            return "";
        return hero.Owner == NetworkManager.Instance.GetPlayerNick()
            ? ", your pick"
            : ", waiting for " + NetworkManager.Instance.GetPlayerNickReal(hero.Owner);
    }

    private static string DescribeItem(LootManager lm, List<ItemEntry> items, int index)
    {
        var e = items[index];
        switch (e.Kind)
        {
            case ItemKind.Card:
            {
                int cardCount = 0, cardIndex = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Kind != ItemKind.Card)
                        continue;
                    cardCount++;
                    if (i == index)
                        cardIndex = cardCount;
                }
                var sb = new StringBuilder();
                sb.Append("Item ").Append(cardIndex).Append(" of ").Append(cardCount).Append(": ");
                sb.Append(CardSpeech.BriefLine(e.Card != null ? e.Card.CardData : null));
                if (e.Card != null)
                {
                    if (IsTaken(lm, e.Card.lootId))
                        sb.Append(". Already taken").Append(TakenBySuffix(e.Card.lootId));
                    else if (e.Card.CardData != null)
                        sb.Append(ComparisonClause(lm, e.Card.CardData));
                }
                return sb.ToString();
            }
            case ItemKind.Gold:
            {
                int gold = Traverse.Create(lm).Field<int>("goldQuantity").Value;
                int taken = Traverse.Create(lm).Field<int>("goldSelected").Value;
                var sb = new StringBuilder("Gold pile: ").Append(gold)
                    .Append(" gold. Full amount per hero who chooses it");
                if (taken == 1)
                    sb.Append(". Taken by 1 hero so far");
                else if (taken > 1)
                    sb.Append(". Taken by ").Append(taken).Append(" heroes so far");
                return sb.Append('.').ToString();
            }
            default: // Restart
                return "Restart button. Undoes all picks and redisplays this loot for the whole party. Press Enter to activate.";
        }
    }

    /// <summary>"{Active hero}'s current {slot}: {item}" for the same gear slot as the card.</summary>
    private static string ComparisonClause(LootManager lm, CardRealtimeData cd)
    {
        int active = ActiveSlot(lm);
        if (active < 0 || active >= 4)
            return "";
        var hero = HeroAt(lm, active);
        if (hero == null)
            return "";
        string slotWord = SlotWord(cd.CardType);
        if (slotWord == null)
            return "";
        string equippedId = EquippedId(hero, cd.CardType);
        return ". " + HeroName(lm, active) + "'s current " + slotWord + ": "
            + (string.IsNullOrEmpty(equippedId) ? "none" : ItemName(equippedId));
    }

    private static string DescribeHero(LootManager lm, int index)
    {
        int slot = _slots[index];
        var hero = HeroAt(lm, slot);
        if (hero == null)
            return "Empty slot.";

        var sb = new StringBuilder();
        sb.Append(HeroName(lm, slot));
        sb.Append(", hero ").Append(index + 1).Append(" of ").Append(_slots.Count).Append(". ");

        if (GameManager.Instance.IsMultiplayer() && NetworkManager.Instance != null)
            sb.Append(NetworkManager.Instance.GetPlayerNickReal(hero.Owner)).Append("'s hero. ");

        if (slot == ActiveSlot(lm))
            sb.Append("Currently choosing. ");
        else
            sb.Append(LootedFlag(lm, slot) ? "Has picked. " : "Has not picked. ");

        sb.Append("Wearing: ");
        sb.Append("weapon ").Append(EquippedName(hero.Weapon));
        sb.Append(", armor ").Append(EquippedName(hero.Armor));
        sb.Append(", jewelry ").Append(EquippedName(hero.Jewelry));
        sb.Append(", accessory ").Append(EquippedName(hero.Accesory));
        sb.Append(", pet ").Append(EquippedName(hero.Pet)).Append('.');

        var cl = lm.characterLootArray != null && slot < lm.characterLootArray.Length
            ? lm.characterLootArray[slot] : null;
        if (cl != null && cl.buttonCharacterDeckText != null)
        {
            string deck = CardSpeech.CleanFlat(cl.buttonCharacterDeckText.text);
            if (deck.Length > 0)
                sb.Append(' ').Append(deck).Append('.');
        }
        if (!GameManager.Instance.IsMultiplayer() && slot != ActiveSlot(lm) && !LootedFlag(lm, slot))
            sb.Append(" Press Enter to let them pick next.");
        return sb.ToString();
    }

    private static void AnnounceFocus()
    {
        var lm = LootManager.Instance;
        if (lm == null)
            return;
        if (_region == Region.Party)
        {
            if (_slots.Count > 0)
            {
                _hero = Clamp(_hero, 0, _slots.Count - 1);
                SpeechManager.Speak(DescribeHero(lm, _hero));
            }
            return;
        }
        if (_item < 0)
        {
            SpeechManager.Speak(BuildOverview(lm));
            return;
        }
        var items = BuildItems(lm);
        if (items.Count == 0)
            return;
        _item = Clamp(_item, 0, items.Count - 1);
        SpeechManager.Speak(DescribeItem(lm, items, _item));
    }

    // ======================= model helpers =======================

    /// <summary>
    /// The loot row in on-screen order: item cards (by spawn index — the lootId tail), the gold
    /// pile, the Restart button. Rebuilt on every access; taken cards stay listed (the game only
    /// dims them) and are described as taken.
    /// </summary>
    private static List<ItemEntry> BuildItems(LootManager lm)
    {
        var items = new List<ItemEntry>();
        if (lm == null || lm.cardContainer == null)
            return items;

        var cards = new List<CardItem>();
        foreach (UnityEngine.Transform child in lm.cardContainer)
        {
            var card = child != null ? child.GetComponent<CardItem>() : null;
            if (card != null && card.cardoutsideloot)
                cards.Add(card);
        }
        cards.Sort((a, b) => SpawnIndex(a.lootId).CompareTo(SpawnIndex(b.lootId)));
        foreach (var card in cards)
            items.Add(new ItemEntry { Kind = ItemKind.Card, Card = card });

        if (GoldVisible(lm))
            items.Add(new ItemEntry { Kind = ItemKind.Gold });

        if (lm.buttonRestart != null && lm.buttonRestart.gameObject.activeSelf)
            items.Add(new ItemEntry { Kind = ItemKind.Restart });

        return items;
    }

    /// <summary>Loot ids are "{cardId}_{spawnIndex}"; sort by the tail.</summary>
    private static int SpawnIndex(string lootId)
    {
        int cut = lootId != null ? lootId.LastIndexOf('_') : -1;
        if (cut >= 0 && int.TryParse(lootId.Substring(cut + 1), out int pos))
            return pos;
        return int.MaxValue;
    }

    private static bool GoldVisible(LootManager lm)
        => lm.botonGold != null && lm.botonGold.transform.gameObject.activeSelf;

    private static CardItem FocusedCard()
    {
        if (_region != Region.Items || _item < 0)
            return null;
        var items = BuildItems(LootManager.Instance);
        if (items.Count == 0)
            return null;
        int i = Clamp(_item, 0, items.Count - 1);
        return items[i].Kind == ItemKind.Card ? items[i].Card : null;
    }

    private static int ActiveSlot(LootManager lm)
        => Traverse.Create(lm).Field<int>("activeCharacter").Value;

    private static List<int> CharacterOrder(LootManager lm)
        => Traverse.Create(lm).Field<List<int>>("characterOrder").Value;

    private static bool LootedFlag(LootManager lm, int slot)
    {
        var looted = Traverse.Create(lm).Field<bool[]>("looted").Value;
        return looted != null && slot >= 0 && slot < looted.Length && looted[slot];
    }

    private static bool IsTaken(LootManager lm, string lootId)
    {
        var taken = Traverse.Create(lm).Field<List<string>>("lootedItems").Value;
        return taken != null && taken.Contains(lootId);
    }

    private static string TakenBySuffix(string lootId)
        => _takenBy.TryGetValue(lootId, out string by) ? " by " + by : "";

    /// <summary>Hero in display slot <paramref name="slot"/> (through the shuffled pick order).</summary>
    private static Hero HeroAt(LootManager lm, int slot)
    {
        var order = CharacterOrder(lm);
        if (order == null || slot < 0 || slot >= order.Count || AtOManager.Instance == null)
            return null;
        var hero = AtOManager.Instance.team.GetHero(order[slot]);
        return hero != null && hero.HeroData != null ? hero : null;
    }

    private static string HeroName(LootManager lm, int slot)
    {
        var hero = HeroAt(lm, slot);
        if (hero == null)
            return "Unknown hero";
        // Same source the screen's own subtitle uses.
        if (hero.HeroData.HeroSubClass != null)
            return AccessibleMenuBase.StripRichText(hero.HeroData.HeroSubClass.CharacterName);
        return AccessibleMenuBase.StripRichText(hero.SourceName);
    }

    private static string ItemName(string lootOrCardId)
    {
        string cardId = lootOrCardId;
        int cut = cardId != null ? cardId.LastIndexOf('_') : -1;
        if (cut >= 0 && int.TryParse(cardId.Substring(cut + 1), out _))
            cardId = cardId.Substring(0, cut);
        var cd = Globals.Instance != null ? Globals.Instance.GetCardData(cardId, instantiate: false) : null;
        return cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : cardId;
    }

    private static string EquippedName(string cardId)
        => string.IsNullOrEmpty(cardId) ? "none" : ItemName(cardId);

    private static string SlotWord(Enums.CardType type)
    {
        switch (type)
        {
            case Enums.CardType.Weapon: return "weapon";
            case Enums.CardType.Armor: return "armor";
            case Enums.CardType.Jewelry: return "jewelry";
            case Enums.CardType.Accesory: return "accessory";
            case Enums.CardType.Pet: return "pet";
            default: return null;
        }
    }

    private static string EquippedId(Hero hero, Enums.CardType type)
    {
        switch (type)
        {
            case Enums.CardType.Weapon: return hero.Weapon;
            case Enums.CardType.Armor: return hero.Armor;
            case Enums.CardType.Jewelry: return hero.Jewelry;
            case Enums.CardType.Accesory: return hero.Accesory;
            case Enums.CardType.Pet: return hero.Pet;
            default: return "";
        }
    }

    private static string OwnerNick(LootManager lm, int slot)
    {
        var hero = HeroAt(lm, slot);
        if (hero == null || !GameManager.Instance.IsMultiplayer() || NetworkManager.Instance == null)
            return "Another player";
        return NetworkManager.Instance.GetPlayerNickReal(hero.Owner);
    }

    private static bool EnsureReady()
    {
        if (LootManager.Instance == null)
            return false;
        if (!_ready)
        {
            SpeechManager.Speak("Loot is still appearing.");
            return false;
        }
        return true;
    }

    private static int Wrap(int v, int count) => ((v % count) + count) % count;

    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}

// ======================= announcement patches =======================

/// <summary>
/// An item pick landed. Fires on every client — local picks and NET_Looted both funnel through
/// Looted — so multiplayer partners' picks are announced too. The prefix captures whether the
/// call will actually land (Looted early-returns on repeat ids and finished turns) and for which
/// display slot, since the method sets activeCharacter to 4 before returning.
/// </summary>
[HarmonyPatch(typeof(LootManager), nameof(LootManager.Looted))]
internal static class LootItemTakenAnnouncePatch
{
    static void Prefix(LootManager __instance, string lootId, out int __state)
    {
        var tr = Traverse.Create(__instance);
        int slot = tr.Field<int>("activeCharacter").Value;
        var taken = tr.Field<System.Collections.Generic.List<string>>("lootedItems").Value;
        bool lands = slot >= 0 && slot < 4 && taken != null && !taken.Contains(lootId);
        __state = lands ? slot : -1;
    }

    static void Postfix(string lootId, int __state)
    {
        if (__state >= 0)
            LootScreenManager.OnItemTaken(__state, lootId);
    }
}

[HarmonyPatch(typeof(LootManager), nameof(LootManager.LootGold))]
internal static class LootGoldTakenAnnouncePatch
{
    static void Prefix(LootManager __instance, bool comingFromNet, out int __state)
    {
        var tr = Traverse.Create(__instance);
        int slot = tr.Field<int>("activeCharacter").Value;
        var looted = tr.Field<bool[]>("looted").Value;
        bool lands = (comingFromNet || __instance.IsMyLoot)
            && looted != null && slot >= 0 && slot < 4 && slot < looted.Length;
        __state = lands ? slot : -1;
    }

    static void Postfix(int __state)
    {
        if (__state >= 0)
            LootScreenManager.OnGoldTaken(__state);
    }
}
