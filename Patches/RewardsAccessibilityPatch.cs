using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using static ObeliskAccess.Patches.Nav;

namespace ObeliskAccess.Patches;

/// <summary>
/// Accessible navigation and speech for the post-combat Rewards scene (<c>RewardsManager</c>).
/// The same scene also serves map-event card rewards and town Divination rounds
/// (<c>typeOfReward</c> 2), so one manager covers all three.
///
/// The screen is modelled as a table: Up/Down move between hero rows, Left/Right move within a
/// row across its card options, then the dust option, then the Deck button. The Restart button is
/// a pseudo-row below the hero rows. Ctrl+Up/Down drill the focused card's detail lines (combat
/// convention); Enter activates. Columns are rebuilt from live objects on every access because the
/// game destroys unchosen cards the moment a pick lands (ours or, in multiplayer, a partner's).
/// </summary>
internal static class RewardsScreenManager
{
    private enum ColKind { Card, Dust, Deck }

    private struct ColEntry
    {
        public ColKind Kind;
        public CardItem Card; // only for Kind == Card
    }

    // Rows: display index -> hero slot (0..3). Row index == _rowHeroes.Count is the Restart
    // pseudo-row while the button is visible.
    private static readonly List<int> _rowHeroes = new List<int>();
    private static int _row = -1; // -1 sentinel: no row focused yet
    private static int _col;

    // Instance id of the RewardsManager the current state belongs to. Same-scene restarts
    // (RelaunchRewards) destroy and re-Awake the manager inside one LoadScene step, so
    // Instance never reads null between two Update frames — the id swap is the only
    // observable reload signal (CardCraftScreenManager convention). Never cleared by
    // ResetState: it must survive the reset so the swap is detected exactly once.
    private static int _instanceId;
    private static bool _ready;
    private static bool _announcedArrival;
    private static bool _announcedClose;

    // Ctrl+Up/Down card drill (same shape as CombatNavigator's).
    private static bool _drill;
    private static readonly List<string> _cardLines = new List<string>();
    private static int _cardLineIndex;

    // Card pending the Singularity overwrite confirm.
    private static string _pendingCardName;

    // ======================= lifecycle =======================

    /// <summary>
    /// Per-frame tick from the poller, outside the active-context gate: the rows animate in over
    /// ~2 seconds (longer in multiplayer, which also waits on network sync) and the "cardsReward"
    /// tutorial popup may own input when the screen settles, so readiness detection must keep
    /// running regardless of who owns the keyboard.
    /// </summary>
    public static void Tick()
    {
        var rm = RewardsManager.Instance;
        if (rm == null)
        {
            if (_instanceId != 0)
            {
                _instanceId = 0;
                ResetState();
            }
            return;
        }
        int id = rm.GetInstanceID();
        if (id != _instanceId)
        {
            ResetState();
            _instanceId = id;
        }

        if (_ready)
            return;

        // A restart keeps the dying screen alive for several frames (ChangeSceneCo yields
        // before LoadScene); re-arming on it would speak the stale overview and mark the
        // fresh screen as already announced. The new instance re-triggers detection.
        if (Traverse.Create(rm).Field<bool>("reseting").Value)
            return;

        if (!AllRowsSettled(rm))
            return;

        _rowHeroes.Clear();
        for (int h = 0; h < 4; h++)
        {
            if (HeroAt(rm, h) != null && rm.characterRewardArray[h].gameObject.activeSelf)
                _rowHeroes.Add(h);
        }
        if (_rowHeroes.Count == 0)
            return;

        _ready = true;
        if (!_announcedArrival)
        {
            _announcedArrival = true;
            // Queued: the tutorial popup announcement (first visit) fires just before the rows
            // finish animating, and must not be cut off.
            SpeechManager.SpeakQueued(BuildOverview(rm));
        }
    }

    private static void ResetState()
    {
        _ready = false;
        _announcedArrival = false;
        _announcedClose = false;
        _rowHeroes.Clear();
        _row = -1;
        _col = 0;
        _drill = false;
        _cardLines.Clear();
        _cardLineIndex = 0;
        _pendingCardName = null;
    }

    /// <summary>
    /// A row is settled once its card flip animations are done and — for our own unselected rows —
    /// the dust option has appeared (it activates ~0.15s after the last card). Rows the game never
    /// spawns (empty hero slots) don't count.
    /// </summary>
    private static bool AllRowsSettled(RewardsManager rm)
    {
        bool any = false;
        for (int h = 0; h < 4; h++)
        {
            if (HeroAt(rm, h) == null)
                continue;
            any = true;

            var slot = rm.characterRewardArray[h];
            if (slot == null || !slot.gameObject.activeSelf)
                return false;

            // A row whose pick has already landed needs no settling. This is load-bearing in MP:
            // a partner's card pick (NET_CardSelected → ShowSelected) DESTROYS the row's other
            // cards mid-flip and nulls the CharacterReward's card list, so animationsDone is
            // never set for it — without this the gate starves and every key answers "Rewards
            // are still appearing" forever, stranding the whole party.
            if (rm.cardSelectedArr[h] != null)
                continue;

            var cr = slot.GetComponent<CharacterReward>();
            if (cr == null || !Traverse.Create(cr).Field<bool>("animationsDone").Value)
                return false;

            bool mine = Traverse.Create(cr).Field<bool>("isMyReward").Value;
            if (mine && rm.cardSelectedArr[h] == null
                && (cr.quantityDust == null || !cr.quantityDust.gameObject.activeSelf))
                return false;
        }
        return any;
    }

    // ======================= navigation =======================

    public static void MoveVertical(int delta)
    {
        if (!EnsureReady())
            return;
        ExitDrillSilently();

        // Once every hero has chosen the game hides Restart and auto-closes in 0.4s (on the
        // master; clients keep the button visible) — drop the pseudo-row on all clients so
        // arrows can't land on a dead control during the close window.
        int rowCount = _rowHeroes.Count + (RestartVisible && !_announcedClose ? 1 : 0);
        if (rowCount == 0)
            return;

        if (_row < 0)
            _row = delta > 0 ? 0 : rowCount - 1;
        else
            _row = Wrap(_row + delta, rowCount);

        AnnounceRow();
    }

    public static void MoveHorizontal(int delta)
    {
        if (!EnsureReady())
            return;
        ExitDrillSilently();

        if (_row < 0)
        {
            // Fresh screen: the first horizontal press seats focus on the first row.
            MoveVertical(1);
            return;
        }
        if (IsRestartRow)
        {
            SpeechManager.Speak(RestartText());
            return;
        }

        var cr = CurrentReward();
        var cols = BuildColumns(cr);
        if (cols.Count == 0)
            return;
        _col = Wrap(_col + delta, cols.Count);
        PlayFocusSound(cols[_col].Kind);
        SpeechManager.Speak(DescribeColumn(cr, cols, _col));
    }

    /// <summary>
    /// The hover sound sighted players get: the game's controller nav warps the cursor onto the
    /// option, whose OnMouseEnter plays it. We swallow the arrows (no cursor warp), so play the
    /// same sounds ourselves — cards per CardItem.fOnMouseEnter, buttons per BotonGeneric.
    /// </summary>
    private static void PlayFocusSound(ColKind kind)
    {
        if (kind == ColKind.Card)
            HeroSpeech.PlayCardFocusSound();
        else
            HeroSpeech.PlayButtonFocusSound();
    }

    // ======================= activation =======================

    public static void Activate()
    {
        if (!EnsureReady())
            return;
        var rm = RewardsManager.Instance;

        // A partner's confirmed restart masks the screen ~1s before the reload; the master
        // drops any pick RPC sent during it, so a local Enter would mutate this screen only
        // (row shrinks, nothing announced) and be wiped by the reload. Refuse with a reason.
        if (GameManager.Instance != null && GameManager.Instance.IsMaskActive())
        {
            SpeechManager.Speak("Restarting — please wait.");
            return;
        }

        if (_row < 0)
        {
            SpeechManager.Speak("Nothing focused. Use the arrow keys first.");
            return;
        }
        if (IsRestartRow)
        {
            // The game hides the button only on the master once all picks land; a non-master's
            // late Enter would RPC a restart request at the host mid-teardown.
            if (_announcedClose || !RestartVisible)
            {
                SpeechManager.Speak("Restart is no longer available — everyone has chosen.");
                return;
            }
            rm.RestartRewards();
            return;
        }

        int h = _rowHeroes[_row];
        var cr = CurrentReward();
        var cols = BuildColumns(cr);
        if (cols.Count == 0)
            return;
        _col = Clamp(_col, 0, cols.Count - 1);
        var entry = cols[_col];

        if (entry.Kind == ColKind.Deck)
        {
            // Opens the in-run character sheet; CharWindowInputContext (registered above us)
            // takes over and its Show postfix announces the arrival.
            rm.ShowDeck(h);
            return;
        }

        string chosen = ChosenText(rm, h);
        if (chosen != null)
        {
            SpeechManager.Speak("Already chose " + chosen + ".");
            return;
        }
        if (!IsMine(cr))
        {
            SpeechManager.Speak(MpSpeech.OwnerNick(rm.theTeam[h]) + " must choose this reward.");
            return;
        }

        if (entry.Kind == ColKind.Dust)
        {
            cr.DustSelected(MpSpeech.LocalNick());
            return;
        }

        // Card pick — mirror CardItem's reward click branch, including the Singularity
        // "overwrites the version already in the deck" confirm.
        var card = entry.Card;
        if (card == null)
            return;
        if (GameManager.Instance.IsSingularity()
            && card.cardAlreadyInDeckT != null && card.cardAlreadyInDeckT.gameObject.activeSelf)
        {
            _pendingCardName = card.gameObject.name;
            string haveName = Traverse.Create(card).Field<string>("cardAlreadyHaveName").Value;
            AlertManager.buttonClickDelegate = OnOverwriteAnswered;
            AlertManager.Instance.AlertConfirmDouble(
                string.Format(Texts.Instance.GetText("cardWillOverwrite"), haveName));
            return;
        }
        rm.SetCardReward(MpSpeech.LocalNick(), card.gameObject.name);
    }

    private static void OnOverwriteAnswered()
    {
        AlertManager.buttonClickDelegate = (AlertManager.OnButtonClickDelegate)Delegate.Remove(
            AlertManager.buttonClickDelegate, new AlertManager.OnButtonClickDelegate(OnOverwriteAnswered));
        if (AlertManager.Instance.GetConfirmAnswer() && !string.IsNullOrEmpty(_pendingCardName)
            && RewardsManager.Instance != null)
        {
            RewardsManager.Instance.SetCardReward(MpSpeech.LocalNick(), _pendingCardName);
        }
        _pendingCardName = null;
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

    /// <summary>Alt+T: full card detail for the focused card; otherwise repeat the focus.</summary>
    public static void SpeakFocusedDetail()
    {
        if (!EnsureReady())
            return;
        var card = FocusedCard();
        if (card != null && card.CardData != null)
        {
            var sb = new StringBuilder(CardSpeech.FullDetail(card.CardData));
            if (card.cardAlreadyInDeckT != null && card.cardAlreadyInDeckT.gameObject.activeSelf)
                sb.Append(". ").Append(CardSpeech.CleanFlat(card.cardAlreadyInDeckText.text));
            SpeechManager.Speak(sb.ToString());
            return;
        }
        AnnounceFocus();
    }

    /// <summary>Alt+I: the screen overview (title, bonuses, what to do).</summary>
    public static void SpeakOverview()
    {
        var rm = RewardsManager.Instance;
        if (rm == null)
            return;
        if (!_ready)
        {
            SpeechManager.Speak("Rewards are still appearing.");
            return;
        }
        SpeechManager.Speak(BuildOverview(rm));
    }

    // ======================= selection / close announcements (from patches) =======================

    /// <summary>A reward landed for hero slot <paramref name="heroIndex"/> ("dust" or a card id).</summary>
    public static void OnRewardChosen(int heroIndex, string rewardId)
    {
        var rm = RewardsManager.Instance;
        if (rm == null)
            return;
        var hero = HeroAt(rm, heroIndex);
        if (hero == null)
            return;

        string what = rewardId == "dust"
            ? rm.dustQuantity + " dust"
            : CardName(rewardId);
        SpeechManager.SpeakQueued(AccessibleMenuBase.StripRichText(hero.SourceName) + " takes " + what + ".");

        // If the pick hit the focused row, the unchosen cards are being destroyed under us:
        // drop any drill and reseat the column; the next arrow press reads live state.
        if (_ready && _row >= 0 && !IsRestartRow && _rowHeroes[_row] == heroIndex)
        {
            ExitDrillSilently();
            _col = 0;
        }
    }

    /// <summary>Mirrors the game's CheckAllAssigned: announce the imminent auto-close once.</summary>
    public static void OnMaybeAllAssigned(RewardsManager rm)
    {
        if (_announcedClose || rm == null)
            return;
        int numRewards = Traverse.Create(rm).Field<int>("numRewards").Value;
        if (numRewards <= 0)
            return;
        for (int i = 0; i < numRewards; i++)
        {
            if (rm.cardSelectedArr[i] == null)
                return;
        }
        _announcedClose = true;
        // Destination-neutral: combat/event rewards return to the map, divination to the town.
        SpeechManager.SpeakQueued("All heroes have chosen. Leaving rewards.");
    }

    public static void OnRestart()
    {
        // On a non-master MP client RestartRewards does NOT restart: it hides the local Restart
        // button and asks the master via RPC (the master gets a confirm alert; a decline changes
        // nothing here). Announcing "Restarting" and wiping state would fake a completed restart.
        if (MpSpeech.IsMp && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster())
        {
            SpeechManager.Speak("Restart request sent to the host. Waiting for them to confirm.");
            return;
        }
        SpeechManager.Speak("Restarting rewards.");
        // Immediate cleanup; Tick's "reseting" guard keeps the dying screen from re-arming,
        // and the reloaded manager's new instance id re-triggers detection from scratch.
        ResetState();
    }

    // ======================= describing =======================

    private static string BuildOverview(RewardsManager rm)
    {
        var parts = new List<string>();

        string title = CardSpeech.CleanFlat(rm.title != null ? rm.title.text : "");
        if (title.Length > 0)
            parts.Add(title);
        string subtitle = CardSpeech.CleanFlat(rm.subtitle != null ? rm.subtitle.text : "");
        if (subtitle.Length > 0)
            parts.Add(subtitle);

        if (rm.corruptionReward != null && rm.corruptionReward.gameObject.activeSelf)
        {
            var sb = new StringBuilder("Corruption bonus: ");
            sb.Append(CardSpeech.CleanFlat(rm.corruptionRewardText != null ? rm.corruptionRewardText.text : ""));
            if (rm.corruptionRewardBgCard != null && rm.corruptionRewardBgCard.gameObject.activeSelf)
            {
                var bonusCard = rm.corruptionRewardBgCard.GetComponentInChildren<CardItem>();
                if (bonusCard != null && bonusCard.CardData != null)
                    sb.Append(" gains ").Append(CardSpeech.BriefLine(bonusCard.CardData));
            }
            parts.Add(sb.ToString());
        }

        if (rm.typeOfReward == 1 && (rm.goldEach > 0 || rm.experienceEach > 0))
            parts.Add("Each hero gains " + rm.goldEach + " gold and " + rm.experienceEach + " experience");

        parts.Add("Each hero picks one card, or " + rm.dustQuantity + " dust");
        parts.Add(_rowHeroes.Count + (_rowHeroes.Count == 1 ? " hero row" : " hero rows")
            + ". Up and down for heroes, left and right for choices, enter to take. Control up and down for card details");

        return string.Join(". ", parts.ToArray()) + ".";
    }

    private static void AnnounceRow()
    {
        var rm = RewardsManager.Instance;
        if (rm == null)
            return;
        if (IsRestartRow)
        {
            PlayFocusSound(ColKind.Deck); // button hover sound
            SpeechManager.Speak(RestartText());
            return;
        }

        int h = _rowHeroes[_row];
        var hero = rm.theTeam[h];
        var cr = CurrentReward();
        var cols = BuildColumns(cr);
        _col = cols.Count > 0 ? Clamp(_col, 0, cols.Count - 1) : 0;
        if (cols.Count > 0)
            PlayFocusSound(cols[_col].Kind);

        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(hero.SourceName));
        sb.Append(", row ").Append(_row + 1).Append(" of ").Append(_rowHeroes.Count).Append(". ");

        if (GameManager.Instance.IsMultiplayer() && !IsMine(cr))
            sb.Append(MpSpeech.OwnerNick(rm.theTeam[h])).Append("'s reward. ");

        // Per-hero gold/XP is deliberately NOT read here — the overview already announces the
        // shared amounts, and repeating them on every row slows navigation down.

        string chosen = ChosenText(rm, h);
        if (chosen != null)
            sb.Append("Already chose ").Append(chosen).Append(". ");

        if (cols.Count > 0)
            sb.Append(DescribeColumn(cr, cols, _col));

        SpeechManager.Speak(sb.ToString());
    }

    private static string DescribeColumn(CharacterReward cr, List<ColEntry> cols, int index)
    {
        var e = cols[index];
        switch (e.Kind)
        {
            case ColKind.Card:
            {
                int cardCount = 0;
                for (int i = 0; i < cols.Count; i++)
                {
                    if (cols[i].Kind == ColKind.Card)
                        cardCount++;
                }
                var sb = new StringBuilder();
                sb.Append("Option ").Append(index + 1).Append(" of ").Append(cardCount).Append(": ");
                sb.Append(CardSpeech.BriefLine(e.Card != null ? e.Card.CardData : null));
                if (e.Card != null && e.Card.cardAlreadyInDeckT != null
                    && e.Card.cardAlreadyInDeckT.gameObject.activeSelf)
                {
                    sb.Append(". ").Append(CardSpeech.CleanFlat(e.Card.cardAlreadyInDeckText.text));
                }
                return sb.ToString();
            }
            case ColKind.Dust:
            {
                string amount = cr.quantityDustText != null ? cr.quantityDustText.text : "";
                return "Take " + amount + " dust" + (DustSelectable(cr) ? "." : ". Already taken.");
            }
            default: // Deck
                return CardSpeech.CleanFlat(cr.buttonCharacterDeckText != null
                    ? cr.buttonCharacterDeckText.text : "Deck") + ", button.";
        }
    }

    private static void AnnounceFocus()
    {
        var rm = RewardsManager.Instance;
        if (rm == null)
            return;
        if (_row < 0)
        {
            SpeechManager.Speak(BuildOverview(rm));
            return;
        }
        if (IsRestartRow)
        {
            SpeechManager.Speak(RestartText());
            return;
        }
        var cr = CurrentReward();
        var cols = BuildColumns(cr);
        if (cols.Count == 0)
            return;
        _col = Clamp(_col, 0, cols.Count - 1);
        SpeechManager.Speak(DescribeColumn(cr, cols, _col));
    }

    private static string RestartText()
        => "Restart button. Restores and redisplays this reward screen for the whole party. Press Enter to activate.";

    // ======================= model helpers =======================

    private static bool RestartVisible
    {
        get
        {
            var rm = RewardsManager.Instance;
            return rm != null && rm.buttonRestart != null && rm.buttonRestart.gameObject.activeSelf;
        }
    }

    private static bool IsRestartRow => _row >= 0 && _row >= _rowHeroes.Count;

    private static CharacterReward CurrentReward()
    {
        var rm = RewardsManager.Instance;
        if (rm == null || _row < 0 || _row >= _rowHeroes.Count)
            return null;
        var slot = rm.characterRewardArray[_rowHeroes[_row]];
        return slot != null ? slot.GetComponent<CharacterReward>() : null;
    }

    /// <summary>
    /// The row's live columns, in on-screen order: cards (by spawn position), the dust option
    /// (while its transform survives — after a card pick the game destroys it), the Deck button.
    /// Never cached: picks destroy card objects at any time.
    /// </summary>
    private static List<ColEntry> BuildColumns(CharacterReward cr)
    {
        var cols = new List<ColEntry>();
        if (cr == null)
            return cols;

        if (cr.cardsByInternalId != null)
        {
            var cards = new List<CardItem>();
            foreach (var card in cr.cardsByInternalId.Values)
            {
                if (card != null) // Unity-alive; destroyed picks drop out
                    cards.Add(card);
            }
            cards.Sort((a, b) => PositionToken(a.name).CompareTo(PositionToken(b.name)));
            foreach (var card in cards)
                cols.Add(new ColEntry { Kind = ColKind.Card, Card = card });
        }

        if (cr.quantityDust != null && cr.quantityDust.gameObject.activeSelf)
            cols.Add(new ColEntry { Kind = ColKind.Dust });

        if (cr.buttonCharacterDeck != null && cr.buttonCharacterDeck.gameObject.activeSelf)
            cols.Add(new ColEntry { Kind = ColKind.Deck });

        return cols;
    }

    /// <summary>Card objects are named "card_{cardId}_{heroIdx}_{position}"; sort by the tail.</summary>
    private static int PositionToken(string goName)
    {
        int cut = goName != null ? goName.LastIndexOf('_') : -1;
        if (cut >= 0 && int.TryParse(goName.Substring(cut + 1), out int pos))
            return pos;
        return int.MaxValue;
    }

    private static CardItem FocusedCard()
    {
        if (_row < 0 || IsRestartRow)
            return null;
        var cols = BuildColumns(CurrentReward());
        if (cols.Count == 0)
            return null;
        int i = Clamp(_col, 0, cols.Count - 1);
        return cols[i].Kind == ColKind.Card ? cols[i].Card : null;
    }

    /// <summary>Same enabled test as the game's own controller list (the dust button's collider).</summary>
    private static bool DustSelectable(CharacterReward cr)
    {
        if (cr == null || cr.quantityDust == null || !cr.quantityDust.gameObject.activeSelf
            || cr.quantityDust.childCount == 0)
            return false;
        var collider = cr.quantityDust.GetChild(0).GetComponent<BoxCollider2D>();
        return collider != null && collider.enabled;
    }

    private static Hero HeroAt(RewardsManager rm, int h)
    {
        if (rm.theTeam == null || h < 0 || h >= rm.theTeam.Length)
            return null;
        var hero = rm.theTeam[h];
        return hero != null && hero.HeroData != null ? hero : null;
    }

    private static string ChosenText(RewardsManager rm, int h)
    {
        string sel = rm.cardSelectedArr != null ? rm.cardSelectedArr[h] : null;
        if (string.IsNullOrEmpty(sel))
            return null;
        return sel == "dust" ? rm.dustQuantity + " dust" : CardName(sel);
    }

    private static string CardName(string cardId)
        => CardSpeech.CardName(cardId);

    private static bool IsMine(CharacterReward cr)
        => cr != null && Traverse.Create(cr).Field<bool>("isMyReward").Value;

    private static bool EnsureReady()
    {
        if (RewardsManager.Instance == null)
            return false;
        if (!_ready)
        {
            SpeechManager.Speak("Rewards are still appearing.");
            return false;
        }
        return true;
    }


}

// ======================= announcement patches =======================

/// <summary>
/// A card pick landed (fires in single player too — the selection funnels through the same
/// RPC-shaped method — and for multiplayer partners' picks).
/// </summary>
[HarmonyPatch(typeof(RewardsManager), "NET_CardSelected")]
internal static class RewardsCardSelectedAnnouncePatch
{
    static void Postfix(short _index, string cardId)
        => RewardsScreenManager.OnRewardChosen(_index, cardId);
}

[HarmonyPatch(typeof(RewardsManager), "NET_DustSelected")]
internal static class RewardsDustSelectedAnnouncePatch
{
    static void Postfix(short _index)
        => RewardsScreenManager.OnRewardChosen(_index, "dust");
}

/// <summary>Announce the auto-close: once every hero has a pick the game leaves after 0.4s.</summary>
[HarmonyPatch(typeof(RewardsManager), "CheckAllAssigned")]
internal static class RewardsAllAssignedAnnouncePatch
{
    static void Postfix(RewardsManager __instance)
        => RewardsScreenManager.OnMaybeAllAssigned(__instance);
}

[HarmonyPatch(typeof(RewardsManager), "RestartRewards")]
internal static class RewardsRestartAnnouncePatch
{
    static void Postfix()
        => RewardsScreenManager.OnRestart();
}
