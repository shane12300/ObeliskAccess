using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Keyboard navigation + speech for the character window (<c>CharPopup</c>) on the
/// hero-selection screen. Tab/Shift+Tab cycle the window's tabs (Stats / Perks / Rank / Skins /
/// Card Backs / Singularity Cards, skipping unavailable ones); Up/Down walk the rows of the
/// current tab; Enter activates a row; Escape closes. The Perks tab is virtual — its single row
/// launches the PerkTree overlay, which has its own manager.
///
/// Open/close is driven by postfixes on <c>CharPopup.Show</c>/<c>Close</c>; the Show guard is
/// <c>IsOpened()</c> because <c>CharPopupMini</c> silently primes the popup (Init + tab calls)
/// on every hero click without ever opening it.
/// </summary>
internal static class CharPopupScreenManager
{
    internal enum Tab { Stats, Perks, Rank, Skins, CardBacks, Singularity }

    private sealed class Row
    {
        public Func<string> Read;
        public Action Activate;      // null = read-only row
        public CardItem Card;        // Alt+T reads the full card text
        public Func<string> Detail;  // Alt+T for non-card rows (traits)
        public int Page = -1;        // card-backs: which page this row lives on
    }

    private static bool _open;
    private static Tab _tab;
    private static int _rowIndex = -1;
    private static readonly List<Row> _rows = new List<Row>();
    private static int _openedFrame = -1;
    private static int _cardbackCategory;
    private static bool _selfTabSwitch;

    private static readonly Tab[] TabOrder =
        { Tab.Stats, Tab.Perks, Tab.Rank, Tab.Skins, Tab.CardBacks, Tab.Singularity };

    private static HeroSelectionManager Mgr => HeroSelectionManager.Instance;
    private static CharPopup Popup => Mgr != null ? Mgr.charPopup : null;

    /// <summary>Live "the window is open" check, so a mouse close can never strand the state.</summary>
    public static bool IsOpen
        => _open && Popup != null && Popup.IsOpened();

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Per-frame tick from the poller: drops state when the window went away by mouse.</summary>
    public static void Tick()
    {
        if (_open && (Popup == null || !Popup.IsOpened()))
            ResetState();
    }

    private static void ResetState()
    {
        _open = false;
        _rowIndex = -1;
        _rows.Clear();
        _openedFrame = -1;
        _cardbackCategory = 0;
    }

    /// <summary>CharPopup.Show landed (keyboard or mouse right-click).</summary>
    public static void OnOpened()
    {
        var popup = Popup;
        if (popup == null || !popup.IsOpened())
            return;
        _open = true;
        _openedFrame = Time.frameCount;
        _tab = ResolveVisibleTab(popup);
        _rowIndex = -1;
        _cardbackCategory = 0;
        BuildRows();
        var sb = new StringBuilder();
        sb.Append("Character window: ").Append(HeroTitle()).Append(". ");
        sb.Append(TabName(_tab)).Append(" tab");
        string unavailable = UnavailableNote();
        if (unavailable != "")
            sb.Append(". ").Append(unavailable);
        sb.Append(". Up and down to read, Tab to switch tabs, Escape to close.");
        SpeechManager.Speak(sb.ToString());
    }

    public static void OnClosed()
    {
        if (!_open)
            return;
        ResetState();
        SpeechManager.Speak("Character window closed.");
    }

    /// <summary>A tab switched by the game (mouse click on a side button).</summary>
    public static void OnGameTabShown(Tab tab)
    {
        if (_selfTabSwitch || !IsOpen || tab == _tab)
            return;
        _tab = tab;
        _rowIndex = -1;
        BuildRows();
        SpeechManager.Speak(TabName(tab) + " tab. " + RowCountHint());
    }

    private static Tab ResolveVisibleTab(CharPopup popup)
    {
        var hsm = Mgr;
        if (hsm != null && hsm.CardBacksPopUp != null && hsm.CardBacksPopUp.activeSelf)
            return Tab.CardBacks;
        if (popup.groupRank != null && popup.groupRank.gameObject.activeSelf)
            return Tab.Rank;
        if (popup.groupSkins != null && popup.groupSkins.gameObject.activeSelf)
            return Tab.Skins;
        if (popup.groupSingularityCards != null && popup.groupSingularityCards.gameObject.activeSelf)
            return Tab.Singularity;
        return Tab.Stats;
    }

    // ------------------------------------------------------------------ hero context

    private static string ActiveHeroId()
        => Popup != null ? Popup.GetActive() : "";

    private static SubClassData ActiveScd()
    {
        string id = ActiveHeroId();
        return string.IsNullOrEmpty(id) ? null : Globals.Instance.GetSubClassData(id);
    }

    private static HeroSelection ActiveHeroSelection()
    {
        var hsm = Mgr;
        string id = ActiveHeroId();
        if (hsm == null || hsm.heroSelectionDictionary == null || string.IsNullOrEmpty(id))
            return null;
        hsm.heroSelectionDictionary.TryGetValue(id, out var hs);
        return hs;
    }

    private static string HeroTitle()
    {
        var scd = ActiveScd();
        if (scd == null)
            return "unknown hero";
        string subclass = HeroSpeech.SubclassWord(scd);
        return scd.CharacterName + (subclass != "" ? ", " + subclass : "");
    }

    private static bool HeroBlocked()
    {
        var hs = ActiveHeroSelection();
        return hs != null && (hs.blocked || hs.DlcBlocked);
    }

    private static bool TabAvailable(Tab tab)
    {
        bool obelisk = GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge();
        switch (tab)
        {
            case Tab.Perks:
            case Tab.Rank:
                return !obelisk && !HeroBlocked();
            case Tab.CardBacks:
                return !HeroBlocked();
            case Tab.Singularity:
                return GameManager.Instance != null && GameManager.Instance.IsSingularity();
            default:
                return true;
        }
    }

    private static string UnavailableNote()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge())
            return "Perks and Rank are not available in Obelisk challenges";
        if (HeroBlocked())
            return "Perks, Rank and Card Backs are not available for locked heroes";
        return "";
    }

    private static string TabName(Tab tab)
    {
        switch (tab)
        {
            case Tab.Stats: return "Stats";
            case Tab.Perks: return "Perks";
            case Tab.Rank: return "Rank";
            case Tab.Skins: return "Skins";
            case Tab.CardBacks: return "Card Backs";
            case Tab.Singularity: return "Singularity Cards";
            default: return tab.ToString();
        }
    }

    // ------------------------------------------------------------------ tab switching

    public static void CycleTab(bool backwards)
    {
        if (!IsOpen)
            return;
        int start = Array.IndexOf(TabOrder, _tab);
        int count = TabOrder.Length;
        for (int step = 1; step <= count; step++)
        {
            int next = (start + (backwards ? count - step : step)) % count;
            if (!TabAvailable(TabOrder[next]))
                continue;
            SwitchToTab(TabOrder[next]);
            return;
        }
    }

    private static void SwitchToTab(Tab tab)
    {
        var popup = Popup;
        var hsm = Mgr;
        _selfTabSwitch = true;
        try
        {
            // Leaving the card-backs tab closes its popup panel.
            if (_tab == Tab.CardBacks && tab != Tab.CardBacks
                && hsm != null && hsm.CardBacksPopUp != null)
                hsm.CardBacksPopUp.SetActive(false);
            switch (tab)
            {
                case Tab.Stats: popup.ShowStats(); break;
                case Tab.Perks: break; // virtual tab — no game UI change
                case Tab.Rank: popup.ShowRank(); break;
                case Tab.Skins: popup.ShowSkins(); break;
                case Tab.CardBacks: popup.ShowCardbacks(); break;
                case Tab.Singularity: popup.ShowSingularityCards(); break;
            }
        }
        finally
        {
            _selfTabSwitch = false;
        }
        _tab = tab;
        _rowIndex = -1;
        HeroSpeech.PlayButtonFocusSound();
        if (tab == Tab.CardBacks)
        {
            // Pin the panel to the first category so page flips target the container our
            // rows were built from.
            _cardbackCategory = 0;
            var panel = CardbackPanel();
            if (panel != null && panel.cardStartingPositions != null
                && panel.cardStartingPositions.Length > 0)
                panel.EnableTab(0);
        }
        BuildRows();
        SpeechManager.Speak(TabName(tab) + " tab. " + RowCountHint());
    }

    private static string RowCountHint()
    {
        if (_tab == Tab.Perks && _rows.Count == 1)
            return _rows[0].Read();
        return _rows.Count == 0
            ? "Nothing to show."
            : _rows.Count + (_rows.Count == 1 ? " row." : " rows. Up and down to read.");
    }

    // ------------------------------------------------------------------ row building

    private static void BuildRows()
    {
        _rows.Clear();
        var popup = Popup;
        var scd = ActiveScd();
        if (popup == null || scd == null)
            return;
        switch (_tab)
        {
            case Tab.Stats: BuildStatsRows(popup, scd); break;
            case Tab.Perks: BuildPerksRows(scd); break;
            case Tab.Rank: BuildRankRows(popup, scd); break;
            case Tab.Skins: BuildSkinsRows(popup); break;
            case Tab.CardBacks: BuildCardbackRows(); break;
            case Tab.Singularity: BuildSingularityRows(popup); break;
        }
    }

    private static void BuildStatsRows(CharPopup popup, SubClassData scd)
    {
        string id = scd.Id;
        bool obelisk = GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge();

        _rows.Add(new Row { Read = () => HeroTitle() + ", " + HeroSpeech.ClassWords(scd) });

        foreach (string line in CardSpeech.SplitLines(
            Texts.Instance.GetText(id + "_description", "class")))
        {
            string captured = line;
            _rows.Add(new Row { Read = () => captured });
        }
        string strength = CardSpeech.CleanFlat(Texts.Instance.GetText(id + "_strength", "class"));
        if (strength != "")
            _rows.Add(new Row { Read = () => strength });

        _rows.Add(new Row { Read = () => StatLine(scd, obelisk) });
        _rows.Add(new Row { Read = () => HeroSpeech.ResistLine(scd) });

        AddTraitRow(scd.Trait0, 0);
        AddTraitRow(scd.Trait1A, 1);
        AddTraitRow(scd.Trait1B, 1);
        AddTraitRow(scd.Trait2A, 2);
        AddTraitRow(scd.Trait2B, 2);
        AddTraitRow(scd.Trait3A, 3);
        AddTraitRow(scd.Trait3B, 3);
        AddTraitRow(scd.Trait4A, 4);
        AddTraitRow(scd.Trait4B, 4);

        // Signature item + starting cards, read from the CardItems the game populated in Init.
        if (popup.cardItemT != null && popup.cardItemT.gameObject.activeSelf)
        {
            var item = popup.cardItemT.GetComponent<CardItem>();
            if (item != null && item.CardData != null)
                _rows.Add(new Row
                {
                    Read = () => "Signature item: " + CardSpeech.BriefLine(item.CardData),
                    Card = item,
                });
        }
        if (popup._CardParent != null)
        {
            foreach (Transform child in popup._CardParent)
            {
                if (!child.gameObject.activeSelf)
                    continue;
                var card = child.GetComponent<CardItem>();
                if (card == null || card.CardData == null)
                    continue;
                var captured = card;
                _rows.Add(new Row
                {
                    Read = () => "Starting card: " + CardSpeech.BriefLine(captured.CardData),
                    Card = captured,
                });
            }
        }

        if (HeroClassicModeManager.IsSupported(id) && Mgr != null)
        {
            _rows.Add(new Row
            {
                Read = () => "Classic variant: "
                    + (Mgr.IsClassicHeroVariant(id) ? "on" : "off")
                    + ". Press Enter to toggle.",
                Activate = () =>
                {
                    Popup.ToggleClassicMode(id);
                    SpeechManager.Speak("Classic variant "
                        + (Mgr.IsClassicHeroVariant(id) ? "on." : "off."));
                },
            });
        }
    }

    private static string StatLine(SubClassData scd, bool obelisk)
    {
        var pm = PlayerManager.Instance;
        int hp = scd.Hp;
        int energy = scd.Energy;
        int speed = scd.Speed;
        if (!obelisk)
        {
            hp += pm.GetPerkMaxHealth(scd.Id);
            energy += pm.GetPerkEnergyBegin(scd.Id);
            speed += pm.GetPerkSpeed(scd.Id);
        }
        return "Health " + hp + ", energy " + energy + " plus " + scd.EnergyTurn
            + " per turn, speed " + speed + ".";
    }

    private static void AddTraitRow(TraitData trait, int requiredTier)
    {
        string name = HeroSpeech.TraitName(trait);
        if (name == "")
            return;
        var scd = ActiveScd();
        var captured = trait;
        _rows.Add(new Row
        {
            Read = () =>
            {
                int tier = PlayerManager.Instance.GetCharacterTier(scd.Id, "trait");
                string line = requiredTier == 0
                    ? "Base trait: " + name
                    : "Trait at tier " + requiredTier + ": " + name;
                if (requiredTier > tier)
                    line += " — locked";
                return line;
            },
            Detail = () =>
            {
                string detail = HeroSpeech.TraitDetail(captured);
                return detail == "" ? name : name + ": " + detail;
            },
        });
    }

    private static void BuildPerksRows(SubClassData scd)
    {
        _rows.Add(new Row
        {
            Read = () =>
            {
                int points = PlayerManager.Instance.GetPerkPointsAvailable(scd.Id);
                return "Spend perk points — " + points
                    + (points == 1 ? " point" : " points")
                    + " available. Press Enter to open the perk tree.";
            },
            Activate = () => Popup.ShowPerks(),
        });
    }

    private static void BuildRankRows(CharPopup popup, SubClassData scd)
    {
        string id = scd.Id;
        _rows.Add(new Row
        {
            Read = () =>
            {
                var pm = PlayerManager.Instance;
                int rank = pm.GetPerkRank(id);
                int next = pm.GetPerkNextLevelPoints(id);
                if (next <= 0)
                    return "Rank " + rank + " — maximum rank reached.";
                return "Rank " + rank + " — " + pm.GetProgress(id) + " of " + next
                    + " toward rank " + (rank + 1) + ".";
            },
        });

        if (popup.progressionRows != null)
        {
            for (int i = 0; i < popup.progressionRows.Length; i++)
            {
                var row = popup.progressionRows[i];
                if (row == null || row.title == null || row.description == null)
                    continue;
                int index = i;
                _rows.Add(new Row
                {
                    Read = () =>
                    {
                        bool unlocked = index < Globals.Instance.RankLevel.Count
                            && Globals.Instance.RankLevel[index]
                                <= PlayerManager.Instance.GetPerkRank(id);
                        return "Rank " + AccessibleMenuBase.StripRichText(row.title.text) + ": "
                            + CardSpeech.CleanFlat(row.description.text)
                            + (unlocked ? "" : " — locked");
                    },
                });
            }
        }

        _rows.Add(new Row
        {
            Read = () => SuppliesRowText(id),
            Activate = () => ActivateSuppliesRow(id),
        });
    }

    private static string SuppliesRowText(string id)
    {
        var pm = PlayerManager.Instance;
        int supplies = pm.GetPlayerSupplyActual();
        string reason = SuppliesDisabledReason(id, supplies);
        return "Use supplies to level up — " + supplies
            + (supplies == 1 ? " supply" : " supplies") + " available"
            + (reason == "" ? ". Press Enter to spend one supply." : ". Not available — " + reason);
    }

    /// <summary>Empty string when the button is usable; the game enables it only at rank 10–49
    /// with every other hero maxed (or freely in the practice tool).</summary>
    private static string SuppliesDisabledReason(string id, int supplies)
    {
        var pm = PlayerManager.Instance;
        int rank = pm.GetPerkRank(id);
        bool combatTool = AtOManager.Instance != null && AtOManager.Instance.IsCombatTool;
        if (rank >= 50)
            return "this hero is already at maximum rank.";
        if (!combatTool && rank < 10)
            return "requires rank 10.";
        if (!combatTool && pm.TotalPointsSpentInSupplys() < 276)
            return "all other heroes must reach maximum rank first.";
        if (supplies < 1)
            return "no supplies available.";
        return "";
    }

    private static void ActivateSuppliesRow(string id)
    {
        var pm = PlayerManager.Instance;
        if (SuppliesDisabledReason(id, pm.GetPlayerSupplyActual()) != "")
        {
            SpeechManager.Speak(SuppliesRowText(id));
            return;
        }
        Mgr.LevelWithSupplies(id);
        int rank = pm.GetPerkRank(id);
        int next = pm.GetPerkNextLevelPoints(id);
        SpeechManager.Speak("Supply spent. Rank " + rank
            + (next > 0 ? ", " + pm.GetProgress(id) + " of " + next + " toward rank " + (rank + 1)
                : ", maximum rank") + ". "
            + pm.GetPlayerSupplyActual() + " supplies left.");
    }

    private static void BuildSkinsRows(CharPopup popup)
    {
        if (popup.botonSkinBase == null)
            return;
        foreach (var boton in popup.botonSkinBase)
        {
            if (boton == null || !boton.gameObject.activeSelf)
                continue;
            var captured = boton;
            _rows.Add(new Row
            {
                Read = () => SkinLine(captured),
                Activate = () => ActivateSkin(captured),
            });
        }
    }

    private static string SkinLine(BotonSkin boton)
    {
        string name = boton.skinName != null
            ? AccessibleMenuBase.StripRichText(boton.skinName.text) : "Skin";
        if (boton.lockT != null && boton.lockT.gameObject.activeSelf)
            return name + " — locked, " + SkinLockReason(boton);
        if (boton.overT != null && boton.overT.gameObject.activeSelf)
            return name + " — equipped";
        return name + ". Press Enter to equip.";
    }

    private static string SkinLockReason(BotonSkin boton)
    {
        var data = Traverse.Create(boton).Field<SkinData>("skinData").Value;
        if (data == null)
            return "not yet unlocked.";
        if (data.Sku != "" && SteamManager.Instance != null
            && !SteamManager.Instance.PlayerHaveDLC(data.Sku))
            return "requires the " + SteamManager.Instance.GetDLCName(data.Sku) + " DLC.";
        if (data.PerkLevel > 0)
            return "unlocks at rank " + data.PerkLevel + ".";
        return "not yet unlocked.";
    }

    private static void ActivateSkin(BotonSkin boton)
    {
        if (boton.lockT != null && boton.lockT.gameObject.activeSelf)
        {
            SpeechManager.Speak(SkinLine(boton));
            return;
        }
        string name = boton.skinName != null
            ? AccessibleMenuBase.StripRichText(boton.skinName.text) : "Skin";
        boton.OnMouseUp(); // guarded internally; rebuilds the skin list (DoSkins)
        _rowIndex = -1;    // rows were rebuilt under us
        BuildRows();
        SpeechManager.Speak(name + " equipped.");
    }

    private static void BuildCardbackRows()
    {
        var panel = CardbackPanel();
        if (panel == null || panel.cardStartingPositions == null
            || panel.cardStartingPositions.Length == 0)
            return;
        _cardbackCategory = Mathf.Clamp(_cardbackCategory, 0,
            panel.cardStartingPositions.Length - 1);
        var refTransform = panel.cardStartingPositions[_cardbackCategory].refTransform;
        if (refTransform == null)
            return;
        for (int page = 0; page < refTransform.childCount; page++)
        {
            var pageT = refTransform.GetChild(page);
            foreach (Transform child in pageT)
            {
                var boton = child.GetComponent<BotonCardback>();
                if (boton == null)
                    continue;
                var captured = boton;
                int capturedPage = page;
                _rows.Add(new Row
                {
                    Read = () => CardbackLine(captured),
                    Activate = () => ActivateCardback(captured),
                    Page = capturedPage,
                });
            }
        }
    }

    private static CardBackSelectionPanel CardbackPanel()
    {
        var hsm = Mgr;
        return hsm != null && hsm.CardBacksPopUp != null
            ? hsm.CardBacksPopUp.GetComponent<CardBackSelectionPanel>() : null;
    }

    private static string CardbackLine(BotonCardback boton)
    {
        string name = boton.cardbackName != null
            ? AccessibleMenuBase.StripRichText(boton.cardbackName.text) : "Card back";
        bool unlocked = Traverse.Create(boton).Field<bool>("unlocked").Value;
        if (!unlocked)
        {
            var data = Traverse.Create(boton).Field<CardbackData>("cardbackData").Value;
            string reason = data != null && data.RankLevel > 0
                ? "unlocks at rank " + data.RankLevel + "." : "not yet unlocked.";
            return name + " — locked, " + reason;
        }
        if (boton.overT != null && boton.overT.gameObject.activeSelf)
            return name + " — equipped";
        return name + ". Press Enter to equip.";
    }

    private static void ActivateCardback(BotonCardback boton)
    {
        bool unlocked = Traverse.Create(boton).Field<bool>("unlocked").Value;
        if (!unlocked)
        {
            SpeechManager.Speak(CardbackLine(boton));
            return;
        }
        string name = boton.cardbackName != null
            ? AccessibleMenuBase.StripRichText(boton.cardbackName.text) : "Card back";
        boton.OnMouseUp();
        SpeechManager.Speak(name + " equipped.");
    }

    private static void BuildSingularityRows(CharPopup popup)
    {
        var cards = Traverse.Create(popup).Field<CardItem[]>("cardsSING").Value;
        if (cards == null)
            return;
        foreach (var card in cards)
        {
            if (card == null || !card.gameObject.activeSelf || card.CardData == null)
                continue;
            var captured = card;
            _rows.Add(new Row
            {
                Read = () => CardSpeech.BriefLine(captured.CardData),
                Card = captured,
            });
        }
    }

    // ------------------------------------------------------------------ input handlers

    public static void MoveVertical(int delta)
    {
        if (!IsOpen)
            return;
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
        EnsureCardbackPage(next);
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
        if (_tab != Tab.CardBacks)
        {
            // Left/right only mean something on the card-backs tab; re-read the current row.
            if (_rowIndex >= 0 && _rowIndex < _rows.Count)
                SpeechManager.Speak(_rows[_rowIndex].Read());
            return;
        }
        var panel = CardbackPanel();
        if (panel == null || panel.cardStartingPositions == null
            || panel.cardStartingPositions.Length == 0)
            return;
        int count = panel.cardStartingPositions.Length;
        _cardbackCategory = (_cardbackCategory + delta + count) % count;
        panel.EnableTab(_cardbackCategory);
        _rowIndex = -1;
        BuildRows();
        HeroSpeech.PlayButtonFocusSound();
        string category = panel.cardStartingPositions[_cardbackCategory].category;
        SpeechManager.Speak(Functions.UppercaseFirst(category) + " card backs, "
            + _rows.Count + (_rows.Count == 1 ? " item." : " items."));
    }

    private static void EnsureCardbackPage(int rowIndex)
    {
        if (_tab != Tab.CardBacks || rowIndex < 0 || rowIndex >= _rows.Count)
            return;
        int page = _rows[rowIndex].Page;
        if (page < 0)
            return;
        var panel = CardbackPanel();
        var refTransform = panel != null
            && _cardbackCategory < panel.cardStartingPositions.Length
            ? panel.cardStartingPositions[_cardbackCategory].refTransform : null;
        if (refTransform == null || page >= refTransform.childCount)
            return;
        if (!refTransform.GetChild(page).gameObject.activeSelf)
            panel.EnablePage(page);
    }

    private static void BuildRowsIfEmpty()
    {
        if (_rows.Count == 0)
            BuildRows();
    }

    public static void Activate()
    {
        if (!IsOpen)
            return;
        // The Enter that opened the window also arrives as a Confirm — ignore it.
        if (Time.frameCount == _openedFrame)
            return;
        BuildRowsIfEmpty();
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

    /// <summary>Escape: from the card-backs tab, close its panel and land on Stats; otherwise
    /// close the window.</summary>
    public static bool Cancel()
    {
        if (!IsOpen)
            return false;
        if (_tab == Tab.CardBacks)
        {
            SwitchToTab(Tab.Stats);
            return true;
        }
        Popup.Close(); // the Close postfix announces
        return true;
    }

    // ------------------------------------------------------------------ review keys

    /// <summary>Alt+T: full detail of the focused row (card text, trait description).</summary>
    public static void SpeakRowDetail()
    {
        if (!IsOpen)
            return;
        BuildRowsIfEmpty();
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
        {
            SpeechManager.Speak("Up and down to read this tab first.");
            return;
        }
        var row = _rows[_rowIndex];
        if (row.Card != null && row.Card.CardData != null)
            SpeechManager.Speak(CardSpeech.FullDetail(row.Card.CardData));
        else if (row.Detail != null)
            SpeechManager.Speak(row.Detail());
        else
            SpeechManager.Speak(row.Read());
    }

    /// <summary>Alt+I: window headline.</summary>
    public static void SpeakHeadline()
    {
        if (!IsOpen)
            return;
        BuildRowsIfEmpty();
        SpeechManager.Speak("Character window: " + HeroTitle() + ". " + TabName(_tab)
            + " tab, " + _rows.Count + (_rows.Count == 1 ? " row." : " rows."));
    }
}

// ======================= announcement patches =======================

/// <summary>The window opened (keyboard Alt+C or mouse right-click). The IsOpened guard filters
/// out CharPopupMini's silent priming, which never calls Show.</summary>
[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.Show))]
internal static class CharPopupShowAnnouncePatch
{
    static void Postfix() => CharPopupScreenManager.OnOpened();
}

/// <summary>The window closed (Escape, mouse, or picking a hero). The prefix snapshots the open
/// state — Close is a no-op when the window is already closed.</summary>
[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.Close))]
internal static class CharPopupCloseAnnouncePatch
{
    static void Prefix(CharPopup __instance, out bool __state)
        => __state = __instance.IsOpened();

    static void Postfix(bool __state)
    {
        if (__state)
            CharPopupScreenManager.OnClosed();
    }
}

// Mouse clicks on the window's own tab buttons — resync the manager's tab state. Each patch
// guards on the manager side (open + not a self-inflicted switch + actually different).
[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.ShowStats))]
internal static class CharPopupShowStatsResyncPatch
{
    static void Postfix() => CharPopupScreenManager.OnGameTabShown(CharPopupScreenManager.Tab.Stats);
}

[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.ShowRank))]
internal static class CharPopupShowRankResyncPatch
{
    static void Postfix() => CharPopupScreenManager.OnGameTabShown(CharPopupScreenManager.Tab.Rank);
}

[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.ShowSkins))]
internal static class CharPopupShowSkinsResyncPatch
{
    static void Postfix() => CharPopupScreenManager.OnGameTabShown(CharPopupScreenManager.Tab.Skins);
}

[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.ShowCardbacks))]
internal static class CharPopupShowCardbacksResyncPatch
{
    static void Postfix()
        => CharPopupScreenManager.OnGameTabShown(CharPopupScreenManager.Tab.CardBacks);
}

[HarmonyPatch(typeof(CharPopup), nameof(CharPopup.ShowSingularityCards))]
internal static class CharPopupShowSingularityResyncPatch
{
    static void Postfix()
        => CharPopupScreenManager.OnGameTabShown(CharPopupScreenManager.Tab.Singularity);
}
