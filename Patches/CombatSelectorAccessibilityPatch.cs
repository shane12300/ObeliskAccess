using System.Collections.Generic;
using System.Text;
using Cards;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// State + speech for the in-combat card-selection windows: <c>UIDiscardSelector</c> (a cast card
/// demands cards from hand be discarded / put on the deck / vanished) and <c>UIDeckCards</c>
/// (look-at-top-of-deck windows, the discover "choose a card to add" window, and the plain
/// draw/discard pile viewers). <see cref="Input.Contexts.CombatSelectorInputContext"/> delegates
/// here: Left/Right review the candidate cards, Enter toggles selection (auto-confirming mandatory
/// single-pick windows), Space confirms, Ctrl+Up/Down drill into the description, Alt+T full detail.
///
/// Digit keys are deliberately left to the game — <c>BattleKeyboard.KeyboardNum</c> already toggles
/// the Nth container card while these windows are up — and every toggle (digit, mouse, our Enter,
/// or a multiplayer echo) funnels through <c>MatchManager.SelectCardToDiscard/SelectCardToAddcard</c>,
/// whose postfixes announce the result.
/// </summary>
public static class CombatSelectorManager
{
    private enum Mode
    {
        None,
        DiscardHand, // UIDiscardSelector: pick cards from hand (discard / top-deck / vanish)
        PileView,    // UIDeckCards type 0/1: draw or discard pile viewer, no selection
        Look,        // UIDeckCards type 2: look at top N of deck, optionally discard/vanish up to Y
        Discover,    // UIDeckCards type 3: choose card(s) to add to hand/deck
    }

    private static Mode _mode;
    private static Enums.CardPlace _place;  // DiscardHand: where the chosen cards go
    private static bool _nonLimited;        // DiscardHand: player decides how many (no fixed quota)
    private static bool _toVanish;          // Look: chosen cards vanish rather than discard
    private static int _lookQuota;          // Look: "up to N" (0 = pure peek, nothing selectable)
    private static int _focus = -1;
    private static bool _ready;             // container finished filling; arrival announced
    private static int _lastCount = -1;
    private static float _lastCountAt;
    private static bool _suppressCloseAnnounce;
    private static readonly List<string> _drillLines = new List<string>();
    private static int _drillIndex = -1;

    // ======================= open / close =======================

    public static void OnDiscardSelectorOpened(Enums.CardPlace place, bool nonLimited)
    {
        Plugin.LogDebug($"[selector] DiscardSelector.TurnOn place={place} nonLimited={nonLimited}");
        Reset();
        _mode = Mode.DiscardHand;
        _place = place;
        _nonLimited = nonLimited;

        var sb = new StringBuilder();
        if (nonLimited)
        {
            sb.Append("Choose any number of cards to ").Append(PlaceWord(place)).Append('.');
        }
        else
        {
            int q = DiscardQuota;
            sb.Append("Choose ").Append(q).Append(' ').Append(CardWord(q))
              .Append(" to ").Append(PlaceWord(place)).Append('.');
        }
        AppendKeyHints(sb, singleTake: !nonLimited && DiscardQuota == 1);
        SpeechManager.Speak(sb.ToString());
    }

    public static void OnDeckWindowOpened(int type, int cardQuantity, int cardTotal, bool toVanish)
    {
        Plugin.LogDebug(
            $"[selector] DeckCards.TurnOn type={type} qty={cardQuantity} total={cardTotal} vanish={toVanish}");
        Reset();
        var m = MatchManager.Instance;
        var sb = new StringBuilder();
        switch (type)
        {
            case 0:
            case 1:
            {
                _mode = Mode.PileView;
                int n = m == null ? 0 : (type == 0 ? m.CountHeroDeck() : m.CountHeroDiscard());
                sb.Append(type == 0 ? "Draw pile, " : "Discard pile, ")
                  .Append(n).Append(' ').Append(CardWord(n))
                  .Append(". Left and Right to review, Escape to close.");
                break;
            }
            case 2:
            {
                _mode = Mode.Look;
                _lookQuota = cardQuantity > 0 ? cardQuantity : 0;
                _toVanish = toVanish;
                sb.Append("Looking at ").Append(cardTotal).Append(' ').Append(CardWord(cardTotal))
                  .Append(" from the top of your deck.");
                if (_lookQuota > 0)
                {
                    sb.Append(" You may ").Append(toVanish ? "vanish" : "discard")
                      .Append(" up to ").Append(_lookQuota).Append('.');
                    AppendKeyHints(sb, singleTake: false);
                }
                else
                {
                    sb.Append(" Look only.");
                    sb.Append(CanAct(m) ? " Left and Right to review, Space to continue."
                                        : " Waiting for another player.");
                }
                break;
            }
            case 3:
            {
                _mode = Mode.Discover;
                int q = AddQuota;
                sb.Append("Choose ").Append(q).Append(" of ").Append(cardTotal)
                  .Append(" cards to add.");
                AppendKeyHints(sb, singleTake: q == 1);
                break;
            }
            default:
                return;
        }
        SpeechManager.Speak(sb.ToString());
    }

    public static void OnClosed()
    {
        Plugin.LogDebug($"[selector] window TurnOff (mode={_mode})");
        if (_mode == Mode.None)
            return;
        bool suppress = _suppressCloseAnnounce;
        Reset();
        if (!suppress)
            SpeechManager.Speak("Window closed.");
    }

    private static void AppendKeyHints(StringBuilder sb, bool singleTake)
    {
        if (!CanAct(MatchManager.Instance))
        {
            sb.Append(" Waiting for another player.");
            return;
        }
        sb.Append(singleTake
            ? " Left and Right to review, Enter to take."
            : " Left and Right to review, Enter to select, Space to confirm.");
    }

    // ======================= per-frame tick (from CombatHotkeyPoller) =======================

    /// <summary>
    /// The look/discover windows spawn their temporary cards over several frames (the game's cast
    /// coroutine yields per card), so the arrival announcement waits until the container's card
    /// count has been stable for a moment. Also drops state if the window vanished without
    /// <c>TurnOff</c> (combat teardown).
    /// </summary>
    public static void Tick()
    {
        if (_mode == Mode.None)
            return;
        var m = MatchManager.Instance;
        if (m == null)
        {
            Reset();
            return;
        }
        bool windowActive = _mode == Mode.DiscardHand
            ? m.DiscardSelector != null && m.DiscardSelector.IsActive()
            : m.DeckCardsWindow != null && m.DeckCardsWindow.IsActive();
        if (!windowActive)
        {
            Reset();
            return;
        }
        if (_ready)
            return;

        int count = Cards().Count;
        if (count != _lastCount)
        {
            _lastCount = count;
            _lastCountAt = Time.unscaledTime;
            return;
        }
        if (count > 0 && Time.unscaledTime - _lastCountAt > 0.4f)
        {
            _ready = true;
            _focus = 0;
            var cards = Cards();
            SpeechManager.SpeakQueued(
                cards.Count + " " + CardWord(cards.Count) + ". " + FocusLine(0, cards));
        }
    }

    // ======================= navigation / actions =======================

    public static bool MoveFocus(int dir)
    {
        if (_mode == Mode.None)
            return false;
        var cards = Cards();
        if (cards.Count == 0)
        {
            SpeechManager.Speak("No cards yet.");
            return true;
        }
        _ready = true; // navigating early cancels the pending arrival announcement
        ExitDrill();
        _focus = _focus < 0 ? 0 : Clamp(_focus + dir, 0, cards.Count - 1);
        SpeechManager.Speak(FocusLine(_focus, cards));
        return true;
    }

    public static bool EnterPressed()
    {
        var m = MatchManager.Instance;
        if (_mode == Mode.None || m == null)
            return false;
        if (_mode == Mode.PileView)
        {
            SpeechManager.Speak("View only. Press Escape to close.");
            return true;
        }
        if (_mode == Mode.Look && _lookQuota <= 0)
        {
            SpeechManager.Speak("Look only. Press Space to continue.");
            return true;
        }
        if (!CanAct(m))
        {
            SpeechManager.Speak("Waiting for another player.");
            return true;
        }
        var cards = Cards();
        if (cards.Count == 0)
        {
            SpeechManager.Speak("No cards yet.");
            return true;
        }
        int i = Clamp(_focus < 0 ? 0 : _focus, 0, cards.Count - 1);
        _focus = i;
        ExitDrill();
        var card = cards[i];
        if (_mode == Mode.Discover)
            m.SelectCardToAddcard(card);
        else
            m.SelectCardToDiscard(card); // the announce postfix speaks the toggle

        // Mandatory single-pick windows: taking the card IS the whole choice — confirm at once.
        // Only from this local Enter path: SelectCardTo* also runs from multiplayer echoes, and
        // auto-confirming there would fire the confirm RPC from the wrong client.
        if (_mode == Mode.DiscardHand && !_nonLimited && DiscardQuota == 1
            && m.CardsLeftForDiscard() == 0)
            ConfirmNow(m);
        else if (_mode == Mode.Discover && AddQuota == 1 && m.CardsLeftForAddcard() == 0)
            ConfirmNow(m);
        return true;
    }

    public static bool SpacePressed()
    {
        var m = MatchManager.Instance;
        if (_mode == Mode.None || m == null)
            return false;
        if (_mode == Mode.PileView)
        {
            m.DrawDeckScreenDestroy(); // TurnOff postfix announces the close
            return true;
        }
        if (!CanAct(m))
        {
            SpeechManager.Speak("Waiting for another player.");
            return true;
        }
        int chosen = SelectedCount();
        switch (_mode)
        {
            case Mode.DiscardHand:
            {
                int left = m.CardsLeftForDiscard();
                if (!_nonLimited && left > 0)
                {
                    SpeechManager.Speak("Select " + left + " more " + CardWord(left) + " first.");
                    return true;
                }
                SpeechManager.Speak(chosen > 0
                    ? "Confirmed, " + chosen + " " + CardWord(chosen) + " to " + PlaceWord(_place) + "."
                    : "Continuing without choosing.");
                _suppressCloseAnnounce = true;
                m.AssignDiscardAction();
                return true;
            }
            case Mode.Look:
            {
                SpeechManager.Speak(chosen > 0
                    ? "Confirmed, " + chosen + " " + CardWord(chosen) + " to "
                      + (_toVanish ? "vanish" : "discard") + "."
                    : "Continuing.");
                _suppressCloseAnnounce = true;
                m.AssignLookDiscardAction();
                return true;
            }
            case Mode.Discover:
            {
                int left = m.CardsLeftForAddcard();
                if (left > 0)
                {
                    SpeechManager.Speak("You must select " + left + " more " + CardWord(left) + ".");
                    return true;
                }
                SpeechManager.Speak("Confirmed.");
                _suppressCloseAnnounce = true;
                m.AssignLookDiscardAction();
                return true;
            }
        }
        return false;
    }

    public static bool CancelPressed()
    {
        if (_mode == Mode.None)
            return false;
        if (_drillIndex >= 0)
        {
            ExitDrill();
            var cards = Cards();
            if (cards.Count > 0)
            {
                int i = Clamp(_focus < 0 ? 0 : _focus, 0, cards.Count - 1);
                SpeechManager.Speak(FocusLine(i, cards));
            }
            return true;
        }
        if (_mode == Mode.PileView)
        {
            MatchManager.Instance?.DrawDeckScreenDestroy();
            return true;
        }
        // Selection windows have no cancel — the game's cast coroutine is parked on the choice.
        SpeechManager.Speak(EscapeReminder());
        return true;
    }

    // Ctrl+Up/Down: walk the focused card's description lines, as in combat. Escape exits.
    public static void Drill(int dir)
    {
        if (_mode == Mode.None)
            return;
        var cards = Cards();
        if (cards.Count == 0)
            return;
        int i = Clamp(_focus < 0 ? 0 : _focus, 0, cards.Count - 1);
        _focus = i;
        if (_drillIndex < 0)
        {
            _drillLines.Clear();
            _drillLines.AddRange(CardSpeech.DetailLines(cards[i].CardData));
            _drillIndex = 0;
        }
        else
        {
            _drillIndex = Clamp(_drillIndex + dir, 0, _drillLines.Count - 1);
        }
        if (_drillIndex < _drillLines.Count)
            SpeechManager.Speak(_drillLines[_drillIndex]);
    }

    // Alt+T: the standard full-detail read on the focused card.
    public static void SpeakFullDetail()
    {
        if (_mode == Mode.None)
            return;
        var cards = Cards();
        if (cards.Count == 0)
            return;
        int i = Clamp(_focus < 0 ? 0 : _focus, 0, cards.Count - 1);
        SpeechManager.Speak(CardSpeech.FullDetail(cards[i].CardData));
    }

    // ======================= toggle announcements =======================

    /// <summary>
    /// Called from the SelectCardTo* postfixes with a snapshot of the selection list taken before
    /// the call. Announces select/deselect with running counts, names the card evicted when the
    /// game auto-drops the oldest pick over quota, and pulls focus to the toggled card so a digit
    /// or mouse toggle leaves Enter/arrows coherent. A snapshot that matches the after-state means
    /// the game rejected the toggle — say nothing.
    /// </summary>
    public static void AnnounceToggle(CardItem card, List<CardItem> before, bool addcard)
    {
        if (_mode == Mode.None || card == null || card.CardData == null)
            return;
        if (addcard != (_mode == Mode.Discover))
            return;
        var after = SelectedList(addcard);
        if (after == null)
            return;
        bool was = before != null && before.Contains(card);
        bool now = after.Contains(card);
        if (was == now)
            return;

        var sb = new StringBuilder(AccessibleMenuBase.StripRichText(card.CardData.CardName))
            .Append(now ? " selected" : " deselected");
        int chosen = after.Count;
        if (_mode == Mode.DiscardHand && _nonLimited)
        {
            sb.Append(", ").Append(chosen).Append(" chosen");
        }
        else
        {
            int quota = _mode == Mode.Discover ? AddQuota
                      : _mode == Mode.Look ? _lookQuota
                      : DiscardQuota;
            if (quota > 0)
                sb.Append(", ").Append(chosen).Append(" of ").Append(quota);
        }
        if (now && before != null)
        {
            foreach (var b in before)
            {
                if (b != null && b != card && b.CardData != null && !after.Contains(b))
                {
                    sb.Append(", replacing ")
                      .Append(AccessibleMenuBase.StripRichText(b.CardData.CardName));
                    break;
                }
            }
        }
        sb.Append('.');

        var cards = Cards();
        int idx = cards.IndexOf(card);
        if (idx >= 0)
            _focus = idx;
        SpeechManager.Speak(sb.ToString());
    }

    // ======================= helpers =======================

    private static void ConfirmNow(MatchManager m)
    {
        _suppressCloseAnnounce = true;
        SpeechManager.SpeakQueued("Confirmed.");
        if (_mode == Mode.DiscardHand)
            m.AssignDiscardAction();
        else
            m.AssignLookDiscardAction();
    }

    private static string EscapeReminder()
    {
        var m = MatchManager.Instance;
        switch (_mode)
        {
            case Mode.DiscardHand:
            {
                if (_nonLimited)
                    return "Space to confirm your choice.";
                int left = m != null ? m.CardsLeftForDiscard() : 0;
                return left > 0
                    ? "You must select " + left + " more " + CardWord(left) + ", then Space to confirm."
                    : "Space to confirm.";
            }
            case Mode.Look:
                return _lookQuota > 0 ? "Space to confirm." : "Space to continue.";
            case Mode.Discover:
            {
                int left = m != null ? m.CardsLeftForAddcard() : 0;
                return left > 0
                    ? "You must select " + left + " more " + CardWord(left) + ", then Space to confirm."
                    : "Space to confirm.";
            }
        }
        return "";
    }

    private static string FocusLine(int i, List<CardItem> cards)
    {
        var card = cards[i];
        string s = (i + 1) + " of " + cards.Count + ": " + CardSpeech.BriefLine(card.CardData);
        if (IsSelected(card))
            s += ", selected";
        return s;
    }

    /// <summary>The candidate cards, in container order — the same enumeration the game's digit
    /// keys use, so spoken positions line up with the on-card number badges.</summary>
    private static List<CardItem> Cards()
    {
        var list = new List<CardItem>();
        var container = Container();
        if (container == null)
            return list;
        for (int i = 0; i < container.childCount; i++)
        {
            var ci = container.GetChild(i).GetComponent<CardItem>();
            if (ci != null && ci.CardData != null)
                list.Add(ci);
        }
        return list;
    }

    private static Transform Container()
    {
        var m = MatchManager.Instance;
        if (m == null)
            return null;
        return _mode == Mode.DiscardHand
            ? (m.DiscardSelector != null ? m.DiscardSelector.cardContainer : null)
            : (m.DeckCardsWindow != null ? m.DeckCardsWindow.cardContainer : null);
    }

    private static bool IsSelected(CardItem card)
    {
        var list = SelectedList(_mode == Mode.Discover);
        return list != null && list.Contains(card);
    }

    private static int SelectedCount()
    {
        var list = SelectedList(_mode == Mode.Discover);
        return list != null ? list.Count : 0;
    }

    private static List<CardItem> SelectedList(bool addcard)
    {
        var m = MatchManager.Instance;
        if (m == null)
            return null;
        return Traverse.Create(m)
            .Field<List<CardItem>>(addcard ? "CICardAddcard" : "CICardDiscard").Value;
    }

    private static int DiscardQuota =>
        MatchManager.Instance == null ? 0
        : Traverse.Create(MatchManager.Instance).Field<int>("GlobalDiscardCardsNum").Value;

    private static int AddQuota =>
        MatchManager.Instance == null ? 0
        : Traverse.Create(MatchManager.Instance).Field<int>("GlobalAddcardCardsNum").Value;

    // Mirrors BattleKeyboard's gate: act when it's your combat turn or your add/discard turn.
    private static bool CanAct(MatchManager m)
        => m != null && (m.IsYourTurn() || m.IsYourTurnForAddDiscard());

    private static string PlaceWord(Enums.CardPlace p)
    {
        switch (p)
        {
            case Enums.CardPlace.TopDeck: return "put on top of your deck";
            case Enums.CardPlace.BottomDeck: return "put on the bottom of your deck";
            case Enums.CardPlace.Vanish: return "vanish";
            default: return "discard";
        }
    }

    private static string CardWord(int n) => n == 1 ? "card" : "cards";

    private static void ExitDrill()
    {
        _drillIndex = -1;
        _drillLines.Clear();
    }

    private static void Reset()
    {
        _mode = Mode.None;
        _place = Enums.CardPlace.Discard;
        _nonLimited = false;
        _toVanish = false;
        _lookQuota = 0;
        _focus = -1;
        _ready = false;
        _lastCount = -1;
        _lastCountAt = 0f;
        _suppressCloseAnnounce = false;
        ExitDrill();
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}

// ======================= Harmony patches =======================

/// <summary>
/// Debug-mode diagnostic: log every cast with the deck-effect fields that should route it into a
/// selection window, so a silent failure shows where the chain broke.
/// </summary>
[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.CastCard))]
internal static class CastCardDiagnosticPatch
{
    static void Prefix(CardItem theCardItem, CardRealtimeData _card)
    {
        if (!AccessibilityOptions.DebugEnabled)
            return;
        var cd = _card ?? (theCardItem != null ? theCardItem.CardData : null);
        if (cd == null)
        {
            Plugin.Logger.LogInfo("[selector] CastCard: (no card data)");
            return;
        }
        Plugin.Logger.LogInfo(
            "[selector] CastCard: " + cd.CardName
            + " look=" + cd.LookCards
            + " lookDiscardUpTo=" + cd.LookCardsDiscardUpTo
            + " lookVanishUpTo=" + cd.LookCardsVanishUpTo
            + " discard=" + cd.DiscardCard
            + " addChoose=" + cd.AddCardChoose);
    }
}

[HarmonyPatch(typeof(UIDiscardSelector), nameof(UIDiscardSelector.TurnOn))]
internal static class DiscardSelectorOpenAnnouncePatch
{
    static void Postfix(Enums.CardPlace _cardPlace, bool _nonLimitedNumCards)
        => CombatSelectorManager.OnDiscardSelectorOpened(_cardPlace, _nonLimitedNumCards);
}

[HarmonyPatch(typeof(UIDiscardSelector), nameof(UIDiscardSelector.TurnOff))]
internal static class DiscardSelectorCloseAnnouncePatch
{
    static void Postfix() => CombatSelectorManager.OnClosed();
}

[HarmonyPatch(typeof(UIDeckCards), nameof(UIDeckCards.TurnOn))]
internal static class DeckCardsOpenAnnouncePatch
{
    static void Postfix(int type, int cardQuantity, int cardTotal, bool toVanish)
        => CombatSelectorManager.OnDeckWindowOpened(type, cardQuantity, cardTotal, toVanish);
}

[HarmonyPatch(typeof(UIDeckCards), nameof(UIDeckCards.TurnOff))]
internal static class DeckCardsCloseAnnouncePatch
{
    static void Postfix() => CombatSelectorManager.OnClosed();
}

[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.SelectCardToDiscard))]
internal static class SelectCardToDiscardAnnouncePatch
{
    static void Prefix(MatchManager __instance, out List<CardItem> __state)
    {
        var list = Traverse.Create(__instance).Field<List<CardItem>>("CICardDiscard").Value;
        __state = list != null ? new List<CardItem>(list) : null;
    }

    static void Postfix(CardItem cardItem, List<CardItem> __state)
        => CombatSelectorManager.AnnounceToggle(cardItem, __state, addcard: false);
}

[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.SelectCardToAddcard))]
internal static class SelectCardToAddcardAnnouncePatch
{
    static void Prefix(MatchManager __instance, out List<CardItem> __state)
    {
        var list = Traverse.Create(__instance).Field<List<CardItem>>("CICardAddcard").Value;
        __state = list != null ? new List<CardItem>(list) : null;
    }

    static void Postfix(CardItem cardItem, List<CardItem> __state)
        => CombatSelectorManager.AnnounceToggle(cardItem, __state, addcard: true);
}
