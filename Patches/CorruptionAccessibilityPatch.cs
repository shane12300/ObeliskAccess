using System.Collections.Generic;
using System.Text;
using Cards;
using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// The pre-combat corruption prompt (<c>CorruptionManager</c>, reached through
/// <c>MapManager.Instance.corruption</c>), as a walkable row list.
///
/// The screen offers a corruption — a modifier card applied to the combat you are about to enter —
/// in exchange for one of two rewards and a score bonus. A sighted player sees the corruption card
/// rendered full size with its rules text, the monster line-up that will spawn, champion badges
/// with the aura each champion is immune to, the score the acceptance is worth, and (for the "free
/// card" reward) the granted card itself. All of that is surfaced here; the previous
/// implementation spoke only the difficulty word and the two reward blurbs.
///
/// <b>Navigation is non-destructive.</b> The rows are Header / Corruption card / Enemies / Reward A
/// / Reward B / (free card) / Accept / Continue; Up and Down walk them, Left and Right walk
/// sub-items (the enemy line-up, or hop between the two rewards), and only Enter mutates state.
/// The old design bound Left/Right directly to "choose that reward AND accept" and Up/Down to the
/// accept toggle, so simply exploring the screen with the arrow keys committed the run.
///
/// Choosing a reward implicitly accepts the corruption (the game's own <c>ChooseReward</c> calls
/// <c>BoxClicked</c> when the box is not yet ticked), so that is announced as it happens.
/// Confirming with the box unticked declines the corruption and travels.
/// </summary>
internal static class CorruptionScreenManager
{
    private enum RowKind { Header, Corruption, Enemies, RewardA, RewardB, RewardCard, Accept, Continue }

    private class Row
    {
        public RowKind Kind;
        public string Text;
    }

    // ---- lifecycle state -----------------------------------------------------------------

    /// <summary>True once DrawCorruptionCo has built the reward labels for the CURRENT draw;
    /// cleared when a new draw starts (InitCorruption locally, DrawCorruptionFromNet via RPC).
    ///
    /// The prompt GameObject activates BEFORE the MP draw barrier fills the labels in
    /// (DrawCorruptionCo parks on a network sync before touching any UI); acting — or reading — in
    /// that window is wrong: the children still hold the PREVIOUS corruption's labels (or the
    /// prefab's placeholder text on the first one), so heuristics like "banner active" or "label
    /// non-empty" pass on stale state. In single-player the coroutine body has no yields, so the
    /// window never exists. The only reliable signal is the fill itself: <c>CorruptionText</c> is
    /// called exactly twice, only from DrawCorruptionCo's label-fill block.</summary>
    private static bool _labelsFilled;

    /// <summary>Bumped once per completed label fill. The poller announces when it sees a serial it
    /// has not spoken yet — which also re-announces a master's NextCorruption re-roll (the prompt
    /// stays active across re-rolls, so an "announced once while open" edge would miss them).</summary>
    internal static int FillSerial { get; private set; }

    /// <summary>True once CorruptionContinue succeeded for the current draw. The prompt stays on
    /// screen for the seconds until the combat scene loads (TravelToThisNodeCorruption polls
    /// corruptionSetted), and the game silently ignores every choice call in that window — so the
    /// mod must refuse instead of narrating outcomes that never happened.</summary>
    private static bool _settled;

    private static int _announcedSerial = -1;
    private static int _index;
    private static int _enemyIndex = -1;

    // The resolved enemy line-up, cached per draw: CombatPreview.Resolve reseeds
    // UnityEngine.Random, so it must not run on every arrow press (let alone per frame).
    private static CombatPreview.Roster _roster;
    private static int _rosterSerial = -1;

    public static bool Active
        => MapManager.Instance != null && MapManager.Instance.IsCorruptionOver();

    private static bool Drawn => _labelsFilled;

    /// <summary>In MP only the master answers the prompt: the game disables both reward buttons,
    /// the accept box and Continue on other clients (showing an "only the host" banner) — but the
    /// public MapManager wrappers the mod drives have no such gate, so without this check a
    /// non-master would mutate purely-local state while the mod narrates false "accepted" outcomes
    /// (and a stray CorruptionContinue could even silently decline for the whole party). Reading is
    /// never gated — a non-master sees the whole screen.</summary>
    private static bool NonMasterMp
        => MpSpeech.IsMp && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster();

    internal static void OnDrawStarted()
    {
        _labelsFilled = false;
        _settled = false;
        _index = 0;
        _enemyIndex = -1;
    }

    internal static void OnConfirmResolved(bool settled) => _settled = settled;

    internal static void OnLabelFilled()
    {
        if (_labelsFilled)
            return; // second CorruptionText call of the same fill
        _labelsFilled = true;
        FillSerial++;
    }

    // ---- poller tick ---------------------------------------------------------------------

    /// <summary>Driven every frame from <c>CorruptionHotkeyPoller</c>, outside its context gate:
    /// the prompt opens over the map (whose own context stands down for it) and can be redrawn
    /// while an alert sits on top, so arrival detection must not depend on owning input.</summary>
    public static void Tick()
    {
        if (!Active)
        {
            _enemyIndex = -1;
            return;
        }
        if (FillSerial == _announcedSerial || !Drawn)
            return;

        _announcedSerial = FillSerial;
        _index = 0;
        _enemyIndex = -1;
        AnnounceArrival();
    }

    private static void AnnounceArrival()
    {
        var rows = BuildRows();
        if (rows.Count == 0)
            return;

        SpeechManager.Speak(rows[0].Text);

        var card = CorruptionCard();
        if (card != null)
        {
            var sb = new StringBuilder("Corruption: ");
            sb.Append(AccessibleMenuBase.StripRichText(card.CardName));
            string desc = CardSpeech.CleanFlat(card.DescriptionNormalized);
            if (desc.Length > 0)
                sb.Append(". ").Append(desc);
            SpeechManager.SpeakQueued(sb.ToString());
        }

        string lineup = EnemyLineupPhrase();
        if (lineup.Length > 0)
            SpeechManager.SpeakQueued(lineup);

        foreach (var row in rows)
        {
            if (row.Kind == RowKind.RewardA || row.Kind == RowKind.RewardB)
                SpeechManager.SpeakQueued(row.Text);
        }

        string score = ScorePhrase();
        if (score.Length > 0)
            SpeechManager.SpeakQueued(score);

        SpeechManager.SpeakQueued(NonMasterMp
            ? "Only " + MpSpeech.HostNick() + " chooses the corruption. Up and Down to review."
            : "Up and Down to review, Enter to act.");
    }

    // ---- row model -----------------------------------------------------------------------

    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();
        var c = MapManager.Instance != null ? MapManager.Instance.corruption : null;
        if (c == null || !Drawn)
            return rows;

        var header = new StringBuilder("Corruption offer");
        string difficulty = Strip(c.textDifficulty != null ? c.textDifficulty.text : null);
        if (difficulty.Length > 0)
            header.Append(", difficulty ").Append(difficulty);
        var card = CorruptionCard();
        if (card != null)
            header.Append(", ").Append(card.CardRarity.ToString().ToLowerInvariant()).Append(" corruption");
        rows.Add(new Row { Kind = RowKind.Header, Text = header.ToString() });

        var corruption = new StringBuilder("Corruption: ");
        if (card != null)
        {
            corruption.Append(AccessibleMenuBase.StripRichText(card.CardName));
            string desc = CardSpeech.CleanFlat(card.DescriptionNormalized);
            if (desc.Length > 0)
                corruption.Append(". ").Append(desc);
        }
        else
        {
            corruption.Append("unknown");
        }
        rows.Add(new Row { Kind = RowKind.Corruption, Text = corruption.ToString() });

        string count = CombatPreview.CountPhrase(Roster());
        rows.Add(new Row
        {
            Kind = RowKind.Enemies,
            Text = count.Length > 0 ? "Enemies: " + count : "Enemy line-up unknown",
        });

        rows.Add(new Row { Kind = RowKind.RewardA, Text = RewardRowText("Reward A", c.rewardBotA) });
        rows.Add(new Row { Kind = RowKind.RewardB, Text = RewardRowText("Reward B", c.rewardBotB) });

        string freeCard = FreeCardRowText(c);
        if (freeCard.Length > 0)
            rows.Add(new Row { Kind = RowKind.RewardCard, Text = freeCard });

        bool accepted = Accepted();
        var accept = new StringBuilder("Accept corruption: ");
        accept.Append(accepted ? "yes" : "no");
        string score = ScorePhrase();
        if (score.Length > 0)
            accept.Append(". ").Append(score);
        // This row is the ONLY way back out of an accepted corruption — choosing a reward can only
        // ever turn acceptance on (the game's ChooseReward ticks the box), so a player who picked a
        // reward to hear it has no other route back. Spell the toggle out rather than leaving it to
        // be discovered; the row is skipped in the hint when it cannot be acted on anyway.
        if (!_settled && !NonMasterMp)
            accept.Append(accepted
                ? ". Enter to decline, clearing the reward choice."
                : ". Enter to accept.");
        rows.Add(new Row { Kind = RowKind.Accept, Text = accept.ToString() });

        string continueText;
        if (_settled)
            continueText = "Continue. Confirmed, traveling.";
        else if (NonMasterMp)
            continueText = "Continue. Waiting for " + MpSpeech.HostNick() + ".";
        else
            continueText = "Continue.";
        rows.Add(new Row { Kind = RowKind.Continue, Text = continueText });

        return rows;
    }

    private static string RewardRowText(string label, BotonGeneric reward)
    {
        var sb = new StringBuilder(label);
        string text = RewardText(reward);
        sb.Append(": ").Append(text.Length > 0 ? text : "unknown");
        sb.Append(Selected(reward) ? ". Selected." : ". Not selected.");
        return sb.ToString();
    }

    /// <summary>
    /// The "free card" reward renders an actual card beside the buttons, so it gets a row of its
    /// own. Which of the two rewards it belongs to comes from the private reward ids — both are
    /// rolled from the same pack, and the game shows one card for whichever slot is "herocard".
    /// </summary>
    private static string FreeCardRowText(CorruptionManager c)
    {
        var t = Traverse.Create(c);
        string idA = t.Field<string>("corruptionRewardId").Value;
        string idB = t.Field<string>("corruptionRewardIdB").Value;
        bool a = idA == "herocard";
        bool b = idB == "herocard";
        if (!a && !b)
            return "";

        var ato = AtOManager.Instance;
        var card = ato != null && Globals.Instance != null
            ? Globals.Instance.GetCardData(ato.corruptionRewardCard, instantiate: false)
            : null;
        if (card == null)
            return "";

        var sb = new StringBuilder("Free card");
        if (a != b)
            sb.Append(" from reward ").Append(a ? "A" : "B");
        sb.Append(": ").Append(AccessibleMenuBase.StripRichText(card.CardName));
        sb.Append(", ").Append(card.CardRarity.ToString().ToLowerInvariant());

        var hero = ato != null && ato.team != null ? ato.team.GetHero(ato.corruptionRewardChar) : null;
        if (hero != null)
            sb.Append(", for ").Append(AccessibleMenuBase.StripRichText(hero.SourceName));

        string desc = CardSpeech.CleanFlat(card.DescriptionNormalized);
        if (desc.Length > 0)
            sb.Append(". ").Append(desc);
        return sb.ToString();
    }

    // ---- navigation ----------------------------------------------------------------------

    public static void Move(int dx, int dy)
    {
        if (!ReadyToRead())
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;

        if (dy != 0)
        {
            int previous = _index;
            _index = Nav.Clamp(_index + (dy > 0 ? -1 : 1), 0, rows.Count - 1);
            if (rows[_index].Kind != RowKind.Enemies || previous != _index)
                _enemyIndex = -1;
            SpeechManager.Speak(rows[_index].Text);
            return;
        }

        if (dx == 0)
            return;

        _index = Nav.Clamp(_index, 0, rows.Count - 1);
        switch (rows[_index].Kind)
        {
            case RowKind.Enemies:
                WalkEnemies(dx > 0 ? 1 : -1);
                break;

            // The two reward rows are a pair, so sideways hops between them — cheaper than
            // finding the other one with Up/Down when comparing the offers.
            case RowKind.RewardA:
            case RowKind.RewardB:
                int other = rows.FindIndex(r =>
                    r.Kind == (rows[_index].Kind == RowKind.RewardA ? RowKind.RewardB : RowKind.RewardA));
                if (other >= 0)
                {
                    _index = other;
                    SpeechManager.Speak(rows[_index].Text);
                }
                break;

            // No sub-items: re-read rather than swallow the key silently.
            default:
                SpeechManager.Speak(rows[_index].Text);
                break;
        }
    }

    private static void WalkEnemies(int dir)
    {
        var roster = Roster();
        var occupied = roster != null ? roster.Occupied : new List<int>();
        if (occupied.Count == 0)
        {
            SpeechManager.Speak("Enemy line-up unknown");
            return;
        }

        // From "not entered yet" either direction lands on the front position.
        _enemyIndex = _enemyIndex < 0 ? 0 : Nav.Clamp(_enemyIndex + dir, 0, occupied.Count - 1);
        SpeechManager.Speak(CombatPreview.BriefLine(roster, occupied[_enemyIndex]));
    }

    // ---- actions -------------------------------------------------------------------------

    public static void Confirm()
    {
        if (!ReadyToRead())
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Nav.Clamp(_index, 0, rows.Count - 1);

        switch (rows[_index].Kind)
        {
            case RowKind.RewardA:
                SelectReward("A");
                break;
            case RowKind.RewardB:
                SelectReward("B");
                break;
            case RowKind.Accept:
                ToggleAccept();
                break;
            case RowKind.Continue:
                DoContinue();
                break;
            case RowKind.Corruption:
                SpeechManager.Speak("No action here. Alt+T for the corruption's full text.");
                break;
            case RowKind.Enemies:
                SpeechManager.Speak("No action here. Left and right review the enemies.");
                break;
            case RowKind.RewardCard:
                SpeechManager.Speak("No action here. Alt+T for the full card.");
                break;
            default:
                SpeechManager.Speak("No action here.");
                break;
        }
    }

    /// <summary>1 and 2 select reward A and B from anywhere, moving the focus with them.</summary>
    public static bool Number(int n)
    {
        if (n != 1 && n != 2)
            return false;
        if (!ReadyToRead())
            return true;

        var rows = BuildRows();
        int target = rows.FindIndex(r => r.Kind == (n == 1 ? RowKind.RewardA : RowKind.RewardB));
        if (target >= 0)
        {
            _index = target;
            _enemyIndex = -1;
        }
        SelectReward(n == 1 ? "A" : "B");
        return true;
    }

    private static void SelectReward(string which)
    {
        if (RefuseIfNotChoosable())
            return;

        var mm = MapManager.Instance;
        if (mm == null)
            return;
        bool wasAccepted = Accepted();
        mm.CorruptionSelectReward(which);

        // Read the outcome back from live state rather than assuming it: ChooseReward also ticks
        // the accept box when it was clear, and in MP it is a no-op for a non-master.
        var c = mm.corruption;
        var bot = c != null ? (which == "A" ? c.rewardBotA : c.rewardBotB) : null;
        var sb = new StringBuilder("Reward ").Append(which);
        sb.Append(Selected(bot) ? " selected" : " not selected");
        string text = RewardText(bot);
        if (text.Length > 0)
            sb.Append(": ").Append(text);

        bool accepted = Accepted();
        if (!accepted)
            sb.Append(". Corruption not accepted.");
        else if (wasAccepted)
            sb.Append(". Corruption accepted.");
        else
            // The pick just turned acceptance on as a side effect. Say once how to undo it — the
            // reward rows themselves can never turn it back off.
            sb.Append(". Corruption accepted; the accept row declines it again.");
        SpeechManager.Speak(sb.ToString());
    }

    private static void ToggleAccept()
    {
        if (RefuseIfNotChoosable())
            return;

        MapManager.Instance?.CorruptionBox();
        // Un-ticking the box clears any reward choice (the game's ShowClicked does it), so say so.
        SpeechManager.Speak(Accepted()
            ? "Corruption accepted. Choose a reward."
            : "Corruption declined. Reward choice cleared.");
    }

    private static void DoContinue()
    {
        if (RefuseIfNotChoosable())
            return;

        bool accepted = Accepted();
        // CorruptionContinue can refuse (box ticked but no reward chosen -> the game raises its
        // "corruptionSelect" alert, spoken by the global alert dialogue). The settled flag flips
        // synchronously via the CorruptionContinue postfix, so read it after the call and only
        // announce a confirmation that actually happened.
        MapManager.Instance?.CorruptionContinue();
        if (_settled)
            SpeechManager.Speak(accepted
                ? "Corruption accepted. Traveling."
                : "Corruption declined. Traveling.");
    }

    private static bool RefuseIfNotChoosable()
    {
        if (_settled)
        {
            SpeechManager.Speak("The corruption is locked in. Traveling.");
            return true;
        }
        if (!Drawn)
        {
            SpeechManager.Speak("The corruption offer is still appearing.");
            return true;
        }
        if (NonMasterMp)
        {
            SpeechManager.Speak("Only " + MpSpeech.HostNick() + " chooses the corruption.");
            return true;
        }
        return false;
    }

    // ---- review keys ---------------------------------------------------------------------

    /// <summary>Alt+T: everything the sighted player could inspect on the focused row.</summary>
    public static void SpeakRowDetail()
    {
        if (!ReadyToRead())
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Nav.Clamp(_index, 0, rows.Count - 1);

        switch (rows[_index].Kind)
        {
            case RowKind.Header:
            case RowKind.Corruption:
            {
                var card = CorruptionCard();
                SpeechManager.Speak(card != null
                    ? CardSpeech.FullDetail(card)
                    : "No corruption details");
                break;
            }

            case RowKind.Enemies:
            {
                var roster = Roster();
                var occupied = roster != null ? roster.Occupied : new List<int>();
                if (occupied.Count == 0)
                {
                    SpeechManager.Speak("Enemy line-up unknown");
                    break;
                }
                if (_enemyIndex >= 0 && _enemyIndex < occupied.Count)
                {
                    SpeechManager.Speak(CombatPreview.Detail(roster, occupied[_enemyIndex]));
                    break;
                }
                // Nothing walked yet: read the whole line-up.
                SpeechManager.Speak("Enemies: " + CombatPreview.CountPhrase(roster));
                foreach (int slot in occupied)
                    SpeechManager.SpeakQueued(CombatPreview.BriefLine(roster, slot));
                break;
            }

            case RowKind.RewardCard:
            {
                var ato = AtOManager.Instance;
                var card = ato != null && Globals.Instance != null
                    ? Globals.Instance.GetCardData(ato.corruptionRewardCard, instantiate: false)
                    : null;
                SpeechManager.Speak(card != null ? CardSpeech.FullDetail(card) : "No card details");
                break;
            }

            case RowKind.Accept:
                SpeechManager.Speak("Accepting adds the corruption to the coming fight and grants the "
                    + "chosen reward. " + ScorePhrase()
                    + " Enter on this row switches acceptance on and off; declining also clears the "
                    + "reward choice. Choosing a reward accepts the corruption on its own, so this "
                    + "row is the only way back out.");
                break;

            default:
                SpeechManager.Speak(rows[_index].Text);
                break;
        }
    }

    /// <summary>Alt+I: the whole offer in one pass, without moving the focus.</summary>
    public static void SpeakOverview()
    {
        if (!ReadyToRead())
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            if (row.Kind == RowKind.Continue)
                continue;
            if (sb.Length > 0)
                sb.Append(". ");
            sb.Append(row.Text.TrimEnd('.'));
        }
        if (NonMasterMp)
            sb.Append(". Only ").Append(MpSpeech.HostNick()).Append(" chooses the corruption");
        SpeechManager.Speak(sb.ToString());
    }

    // ---- MP echoes -----------------------------------------------------------------------

    /// <summary>The host's choices reach non-masters only as RPCs (their own buttons are
    /// disabled); speak them or the wait between the offer and the combat is dead silence.</summary>
    internal static void OnHostChoseReward(short choosed)
    {
        if (!NonMasterMp || !Drawn)
            return;
        var c = MapManager.Instance != null ? MapManager.Instance.corruption : null;
        if (choosed == 1 || choosed == 2)
        {
            string reward = RewardText(choosed == 1 ? c?.rewardBotA : c?.rewardBotB);
            SpeechManager.SpeakQueued(MpSpeech.HostNick() + " chose reward " + (choosed == 1 ? "A" : "B")
                + (reward.Length > 0 ? ": " + reward : "") + ".");
        }
        else
        {
            SpeechManager.SpeakQueued(MpSpeech.HostNick() + " cleared the reward choice.");
        }
    }

    internal static void OnHostBoxToggled(bool status)
    {
        if (!NonMasterMp || !Drawn)
            return;
        SpeechManager.SpeakQueued(status
            ? MpSpeech.HostNick() + " accepted the corruption."
            : MpSpeech.HostNick() + " declined the corruption.");
    }

    // ---- state reads ---------------------------------------------------------------------

    private static bool ReadyToRead()
    {
        if (!Active)
            return false;
        if (!Drawn)
        {
            SpeechManager.Speak("The corruption offer is still appearing.");
            return false;
        }
        return true;
    }

    /// <summary>The corruption card being offered. The id is private on CorruptionManager, but the
    /// draw mirrors it onto the public <c>AtOManager.corruptionIdCard</c> in the same block that
    /// fills the labels — so it is current exactly when <see cref="Drawn"/> is.</summary>
    private static CardRealtimeData CorruptionCard()
    {
        var ato = AtOManager.Instance;
        if (ato == null || Globals.Instance == null || string.IsNullOrEmpty(ato.corruptionIdCard))
            return null;
        return Globals.Instance.GetCardData(ato.corruptionIdCard, instantiate: false);
    }

    /// <summary>The enemy line-up for the node being travelled to, resolved once per draw.</summary>
    private static CombatPreview.Roster Roster()
    {
        if (_rosterSerial == FillSerial)
            return _roster;

        _rosterSerial = FillSerial;
        _roster = null;

        var c = MapManager.Instance != null ? MapManager.Instance.corruption : null;
        if (c == null || Globals.Instance == null)
            return null;

        // The prompt keeps the target node's ids privately; using them (rather than the map's
        // focused node) guarantees the preview is the fight this corruption belongs to.
        var t = Traverse.Create(c);
        string dataId = t.Field<string>("nodeSelectedDataId").Value;
        string assignedId = t.Field<string>("nodeSelectedAssignedId").Value;
        if (string.IsNullOrEmpty(dataId))
            return null;

        _roster = CombatPreview.Resolve(Globals.Instance.GetNodeData(dataId), assignedId);
        return _roster;
    }

    private static string EnemyLineupPhrase()
    {
        var roster = Roster();
        var occupied = roster != null ? roster.Occupied : new List<int>();
        if (occupied.Count == 0)
            return "";

        var sb = new StringBuilder("Enemies: ").Append(CombatPreview.CountPhrase(roster)).Append(". ");
        for (int i = 0; i < occupied.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(CombatPreview.Name(roster.Slots[occupied[i]]));
        }
        return sb.ToString();
    }

    /// <summary>The game's own "accepting is worth N score" line.</summary>
    private static string ScorePhrase()
    {
        var c = MapManager.Instance != null ? MapManager.Instance.corruption : null;
        // Sprite-tag recovery for the same reason as the reward labels — the score line can carry
        // an icon for the score symbol itself.
        return c != null && c.textAcceptScore != null
            ? CardSpeech.CleanFlat(c.textAcceptScore.text) : "";
    }

    /// <summary>Whether the accept box is ticked — mirrored by the X being shown, which the game
    /// does on every client (ShowClicked runs from both the local click and the master's RPC).</summary>
    private static bool Accepted()
    {
        var box = MapManager.Instance?.corruption?.corruptionBoxX;
        return box != null && box.gameObject.activeSelf;
    }

    /// <summary>Whether a reward button is the chosen one. <c>permaBorder</c> is the flag behind the
    /// gold tint and border the game paints on the selection, and it is set on every client, so it
    /// reads correctly for the host's remote choice too.</summary>
    private static bool Selected(BotonGeneric reward)
        => reward != null && Traverse.Create(reward).Field<bool>("permaBorder").Value;

    /// <summary>
    /// A reward button's label. This must go through <see cref="CardSpeech.CleanFlat"/>, not a bare
    /// strip: the currency rewards name their amounts only as <c>&lt;sprite name=gold&gt;</c> /
    /// <c>&lt;sprite name=dust&gt;</c> icons after each number, so stripping tags left a row of bare
    /// numbers ("The party will gain 720 720 1") with no clue what any of them were.
    /// </summary>
    private static string RewardText(BotonGeneric reward)
        => reward != null && reward.text != null ? CardSpeech.CleanFlat(reward.text.text) : "";

    private static string Strip(string text) => AccessibleMenuBase.StripRichText(text ?? "");
}

// ======================= draw-lifecycle patches =======================
// A new draw begins either locally (InitCorruption — master, and every SP prompt; also the
// master's NextCorruption re-roll, which calls InitCorruption again while the prompt is open) or
// from the master's RPC (DrawCorruptionFromNet on non-masters). Both start DrawCorruptionCo, which
// in MP parks on a network barrier BEFORE touching the labels — so the fresh-draw mark must be
// cleared here, at the start, and only set again by the fill itself.

[HarmonyPatch(typeof(CorruptionManager), nameof(CorruptionManager.InitCorruption))]
internal static class CorruptionInitDrawPatch
{
    static void Prefix() => CorruptionScreenManager.OnDrawStarted();
}

[HarmonyPatch(typeof(CorruptionManager), nameof(CorruptionManager.DrawCorruptionFromNet))]
internal static class CorruptionNetDrawPatch
{
    static void Prefix() => CorruptionScreenManager.OnDrawStarted();
}

/// <summary>
/// <c>CorruptionText</c> (private) is called exactly twice, only from DrawCorruptionCo's label-fill
/// block — the first reliable moment the reward labels reflect the current draw. Both calls land in
/// the same synchronous run, so by the time the poller's next Update reads the labels, both are set.
/// </summary>
[HarmonyPatch(typeof(CorruptionManager), "CorruptionText")]
internal static class CorruptionLabelsFilledPatch
{
    static void Postfix() => CorruptionScreenManager.OnLabelFilled();
}

/// <summary>Runs synchronously inside every confirm (the mod's Enter and the mouse path alike), so
/// the settled flag is current the moment CorruptionContinue returns. corruptionSetted is private
/// on MapManager — Traverse.</summary>
[HarmonyPatch(typeof(MapManager), nameof(MapManager.CorruptionContinue))]
internal static class CorruptionContinueSettledPatch
{
    static void Postfix(MapManager __instance)
        => CorruptionScreenManager.OnConfirmResolved(
            Traverse.Create(__instance).Field<bool>("corruptionSetted").Value);
}

/// <summary>Master's reward pick arriving on the other clients (1 = A, 2 = B, 0 = cleared).</summary>
[HarmonyPatch(typeof(MapManager), nameof(MapManager.NET_ChooseRewardCorruption))]
internal static class CorruptionNetChoosePatch
{
    static void Postfix(short choosed) => CorruptionScreenManager.OnHostChoseReward(choosed);
}

/// <summary>Master's accept-box toggle arriving on the other clients.</summary>
[HarmonyPatch(typeof(MapManager), nameof(MapManager.NET_BoxClicked))]
internal static class CorruptionNetBoxPatch
{
    static void Postfix(bool status) => CorruptionScreenManager.OnHostBoxToggled(status);
}
