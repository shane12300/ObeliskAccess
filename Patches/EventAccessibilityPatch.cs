using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Patches;

/// <summary>
/// Makes the map-event (story dialog) book screen accessible. Follows the tutorial-popup
/// structure: on open the title + first body line are spoken and focus lands on that first line;
/// Up/Down walk a flat list of title, description lines, the "requirements not met" counter,
/// the choices, and finally the map-peek button; Enter activates. Dice rolls are narrated
/// play-by-play with queued speech, and the outcome text is read as the game writes it.
///
/// Parity rule: every spoken string traces to on-screen TMP text or to a hover popup the game
/// itself renders (probability dice, "not enough gold", card previews, roll-mode explainers).
/// Internal-only data — raw roll thresholds, reward/follow-up fields, requirement ids — is
/// never read, so a screen-reader user learns exactly what a sighted player would.
/// </summary>
internal static class EventScreenManager
{
    private enum Phase
    {
        None,      // no event open
        Choosing,  // description + choices on screen, optionSelected == -1
        Resolving, // choice made, costs paid, roll/animation running — input locked
        Result     // outcome text written, Continue available
    }

    private struct Entry
    {
        public string Speech;
        public Reply Reply;          // non-null => a choice row
        public BotonGeneric Button;  // non-null => Continue or Hide/Show
        public bool IsHideShow;      // Enter toggles the map peek instead of Clicked()
    }

    private struct PendingCardRead
    {
        public string CardId;
        public float ReadAt;
    }

    private static readonly Regex _spriteTag =
        new Regex(@"<sprite\s+name=""?([^"">]+?)""?\s*/?>", RegexOptions.Compiled);

    private static readonly List<Entry> _entries = new List<Entry>();
    private static readonly List<string> _textLines = new List<string>(); // title + description
    private static readonly List<PendingCardRead> _pendingCardReads = new List<PendingCardRead>();

    private static int _index;
    private static Phase _phase = Phase.None;
    private static int _replySnapshotHash;
    private static bool _repliesAnnounced;
    private static int _spokenResultLength;
    private static int _rollCardsCalls;
    private static string _selectedChoiceText = "";

    public static bool Active => _phase != Phase.None && EventManager.Instance != null;

    // ------------------------------------------------------------------ lifecycle

    public static void OnEventOpened()
    {
        var em = EventManager.Instance;
        if (em == null || em.title == null || em.description == null)
            return;
        // SetEvent bails without showing anything when the event data fails to resolve.
        if (Traverse.Create(em).Field<EventData>("currentEvent").Value == null)
            return;

        Reset();
        _phase = Phase.Choosing;

        string title = Clean(em.title.text);
        _textLines.Add(title.Length > 0 ? title : "Event");
        _textLines.AddRange(SplitBookText(em.description.text));

        RebuildEntries(preserveFocus: false);
        _index = _entries.Count > 1 ? 1 : 0;

        string announcement = _textLines[0];
        if (_textLines.Count > 1)
            announcement += ". " + _textLines[1];
        SpeechManager.Speak(announcement);
    }

    public static void OnOptionSelected(int optionIndex)
    {
        var em = EventManager.Instance;
        if (em == null)
            return;
        if (_phase == Phase.Resolving)
        {
            // MP: NET_SelectAnswer lands here once every vote is in — after a conflict
            // tie-breaker the winning option can differ from the local vote, and the result
            // row would otherwise report the LOSING option as "Chosen".
            UpdateChosenText(optionIndex, announceIfChanged: true);
            return;
        }
        if (_phase != Phase.Choosing)
            return;
        // The game rejects selections after the first; only act on the accepted one.
        if (em.optionSelected != optionIndex)
            return;

        UpdateChosenText(optionIndex, announceIfChanged: false);

        _phase = Phase.Resolving;
        _rollCardsCalls = 0;
        _spokenResultLength = 0;

        // Covers Enter, mouse clicks, and multiplayer partners' picks. (The game's 0.5s
        // auto-select of a lone option used to arrive here too — it is now suppressed by
        // EventAutoSelectSuppressPatch; single-choice events wait for Enter.) A follower's
        // auto-vote (AutoSelectClient with follow-the-leader on) also lands here un-flagged —
        // phrase it as the host's pick, not the player's.
        bool followAuto = !UserSelectionInProgress
            && MpSpeech.IsMp
            && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster()
            && AtOManager.Instance != null && AtOManager.Instance.followingTheLeader;
        SpeechManager.Speak((followAuto ? "Following the host: " : "Selected: ") + _selectedChoiceText);
        // In MP this was only the local VOTE: the event resolves when every player has chosen,
        // which can be minutes away. Without this the wait is dead silence.
        if (WaitingForVotes)
            SpeechManager.SpeakQueued("Waiting for the other players to choose.");
    }

    /// <summary>Resolve an option index to its reply text and remember it as the chosen option;
    /// optionally speak the correction when it replaces a different remembered choice.</summary>
    private static void UpdateChosenText(int optionIndex, bool announceIfChanged)
    {
        foreach (var r in GetVisibleReplies())
        {
            if (r == null || r.GetOptionIndex() != optionIndex || r.replyText == null)
                continue;
            string text = Clean(r.replyText.text);
            if (text.Length > 0 && text != _selectedChoiceText)
            {
                _selectedChoiceText = text;
                if (announceIfChanged)
                    SpeechManager.SpeakQueued("The party chose: " + text);
            }
            return;
        }
    }

    /// <summary>True while the MP vote is still open — some player has not chosen yet. This is
    /// the unbounded wait (a reading partner can take minutes); the roll animation after every
    /// vote is in stays deliberately silent like single player (bounded, a few seconds).</summary>
    private static bool WaitingForVotes
    {
        get
        {
            var em = EventManager.Instance;
            return em != null && MpSpeech.IsMp && NetworkManager.Instance != null
                && em.MultiplayerPlayerSelection != null
                && em.MultiplayerPlayerSelection.Count < NetworkManager.Instance.GetNumPlayers();
        }
    }

    /// <summary>A partner's vote arriving (NET_OptionSelected runs on every client). Speaks who
    /// chose what plus the running tally — the game's only display is a small nick marker.</summary>
    public static void OnMpVote(string playerNick, int option)
    {
        var em = EventManager.Instance;
        if (em == null || option < 0 || !MpSpeech.IsMp || NetworkManager.Instance == null)
            return;
        if (playerNick == NetworkManager.Instance.GetPlayerNick())
            return; // the local vote already spoke ("Selected: …")
        string text = "";
        foreach (var r in GetVisibleReplies())
        {
            if (r != null && r.GetOptionIndex() == option && r.replyText != null)
            {
                text = Clean(r.replyText.text);
                break;
            }
        }
        int votes = em.MultiplayerPlayerSelection != null ? em.MultiplayerPlayerSelection.Count : 0;
        int total = NetworkManager.Instance.GetNumPlayers();
        SpeechManager.SpeakQueued(MpSpeech.DisplayNick(playerNick) + " chose"
            + (text.Length > 0 ? ": " + text : " an option") + ". "
            + votes + " of " + total + " chosen.");
    }

    public static void OnCardDrawn(int heroIndex)
    {
        if (_phase != Phase.Resolving)
            return;
        var em = EventManager.Instance;
        if (em == null)
            return;

        try
        {
            var heroes = em.Heroes;
            if (heroes == null || heroIndex < 0 || heroIndex >= heroes.Length || heroes[heroIndex] == null)
                return;

            var tr = Traverse.Create(em);
            var rollManager = tr.Field<RollManager>("rollManager").Value;
            if (rollManager?.charRollDatas == null || heroIndex >= rollManager.charRollDatas.Length)
                return;
            var data = rollManager.charRollDatas[heroIndex];
            if (data == null || data.CardData == null)
                return;

            string line = heroes[heroIndex].SourceName + " draws " + Clean(data.CardData.CardName);
            // The game only displays the number for energy-cost rolls; card-type rolls show none.
            var replySelected = tr.Field<EventReplyData>("replySelected").Value;
            if (replySelected == null || replySelected.SsRollCard == Enums.CardType.None)
                line += ", " + data.Result;

            SpeechManager.SpeakQueued(line + ".");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Event card-draw announce failed: {e.Message}");
        }
    }

    public static void OnResultTitle(bool success, int heroIndex)
    {
        if (_phase != Phase.Resolving && _phase != Phase.Result)
            return;
        var em = EventManager.Instance;
        if (em == null)
            return;

        try
        {
            // Mirror the override the game applies before choosing which label to show.
            if (SandboxManager.Instance != null && SandboxManager.Instance.AlwaysPassEventRoll)
                success = true;

            var tr = Traverse.Create(em);
            var rollManager = tr.Field<RollManager>("rollManager").Value;
            var replySelected = tr.Field<EventReplyData>("replySelected").Value;
            bool isCritical = rollManager != null && replySelected != null
                && rollManager.isCritical(replySelected, success);

            var helper = success ? em.cardOK : em.cardKO;
            var label = helper?.GetResult(heroIndex, isCritical);
            string text = label != null ? Clean(label.text) : "";
            if (text.Length == 0)
                text = success ? "Success" : "Failure";

            var heroes = em.Heroes;
            if (heroIndex >= 0 && heroes != null && heroIndex < heroes.Length && heroes[heroIndex] != null)
                SpeechManager.SpeakQueued(heroes[heroIndex].SourceName + ": " + text + ".");
            else
                SpeechManager.SpeakQueued(text + ".");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Event result-title announce failed: {e.Message}");
        }
    }

    public static void OnRollCardsStarted()
    {
        if (_phase != Phase.Resolving)
            return;
        _rollCardsCalls++;
        // A second deal within one resolution is the Competition tie re-roll animation.
        if (_rollCardsCalls > 1)
            SpeechManager.SpeakQueued("Tie. Re-rolling.");
    }

    public static void OnFinish()
    {
        var em = EventManager.Instance;
        if (em == null || _phase == Phase.None)
            return;

        _phase = Phase.Result;

        // Speak only what Finish just appended; it runs twice when a roll had both winners
        // and losers, and FinalResolution clears the text at the start of each resolution.
        string full = em.result != null ? em.result.text : "";
        if (full.Length < _spokenResultLength)
            _spokenResultLength = 0;
        if (full.Length > _spokenResultLength)
        {
            string delta = full.Substring(_spokenResultLength);
            _spokenResultLength = full.Length;
            foreach (var line in SplitBookText(delta))
                SpeechManager.SpeakQueued(line);
        }

        RebuildEntries(preserveFocus: false);
    }

    public static void OnRewardCard(string cardId)
    {
        if (_phase == Phase.None)
            return;
        // The reward-card coroutine waits 0.2s and may swap the id (NG+ injury upgrade)
        // before showing anything, so defer the read until the card is actually displayed.
        _pendingCardReads.Add(new PendingCardRead
        {
            CardId = cardId,
            ReadAt = Time.unscaledTime + 0.6f
        });
    }

    public static void OnEventClosed()
    {
        if (_phase != Phase.None)
            Reset();
    }

    /// <summary>
    /// Per-frame upkeep, driven by <c>EventHotkeyPoller</c> regardless of which context owns
    /// input: destroy safety net, the reply watcher (SetReplys is a coroutine with waits, a
    /// random cull and an auto-select, so polling the visible set is the robust signal), and
    /// the deferred reward-card reads.
    /// </summary>
    public static void Tick()
    {
        if (_phase == Phase.None)
            return;
        var em = EventManager.Instance;
        if (em == null)
        {
            Reset();
            return;
        }

        try
        {
            for (int i = _pendingCardReads.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _pendingCardReads[i].ReadAt)
                    continue;
                string cardId = _pendingCardReads[i].CardId;
                _pendingCardReads.RemoveAt(i);
                SpeakRewardCard(em, cardId);
            }

            if (_phase != Phase.Choosing)
                return;

            int hash = ComputeReplyHash(em);
            if (hash == _replySnapshotHash)
                return;
            _replySnapshotHash = hash;

            var replies = GetVisibleReplies();
            RebuildEntries(preserveFocus: true);

            if (!_repliesAnnounced && replies.Count > 0)
            {
                _repliesAnnounced = true;
                string msg = replies.Count == 1 ? "1 choice." : replies.Count + " choices.";
                if (em.notMeeted != null && em.notMeeted.gameObject.activeSelf)
                {
                    string nm = Clean(em.notMeeted.text);
                    if (nm.Length > 0)
                        msg += " " + nm + ".";
                }
                SpeechManager.SpeakQueued(msg);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Event tick failed: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ input

    public static void Move(int dir)
    {
        if (!Active || _entries.Count == 0)
            return;
        // Input is locked while the roll/animation plays, like a sighted player watching it —
        // but the MP vote wait is unbounded, so it answers instead of staying silent.
        if (_phase == Phase.Resolving)
        {
            if (WaitingForVotes)
                SpeechManager.Speak("Waiting for the other players to choose.");
            return;
        }

        _index += dir;
        if (_index < 0)
            _index = 0;
        else if (_index >= _entries.Count)
            _index = _entries.Count - 1;

        SpeechManager.Speak(_entries[_index].Speech);
    }

    public static void Activate()
    {
        if (!Active || _entries.Count == 0)
            return;
        if (_phase == Phase.Resolving)
        {
            if (WaitingForVotes)
                SpeechManager.Speak("Waiting for the other players to choose.");
            return;
        }
        var em = EventManager.Instance;
        if (_index < 0 || _index >= _entries.Count)
            return;
        var entry = _entries[_index];

        if (entry.IsHideShow)
        {
            if (MapManager.Instance == null)
                return;
            MapManager.Instance.ShowHideEvent();
            if (entry.Button != null)
            {
                string label = Clean(entry.Button.GetText()) + ", button";
                _entries[_index] = new Entry
                {
                    Speech = label,
                    Button = entry.Button,
                    IsHideShow = true
                };
                SpeechManager.Speak(label);
            }
            return;
        }

        if (_phase == Phase.Choosing)
        {
            if (entry.Reply != null)
            {
                if (em.optionSelected != -1)
                    return;
                // Follow-the-leader: the game's own mouse path (Reply.OnMouseUp) refuses
                // follower picks — the host's choice is auto-applied to everyone. Calling
                // SelectThisOption directly would bypass that gate, register a divergent vote,
                // and trigger the conflict screen follow mode exists to prevent.
                if (MpSpeech.IsMp && NetworkManager.Instance != null
                    && !NetworkManager.Instance.IsMaster()
                    && AtOManager.Instance != null && AtOManager.Instance.followingTheLeader)
                {
                    SpeechManager.Speak("Following the leader — the host chooses for the party.");
                    return;
                }
                if (entry.Reply.replyButtonBlocked != null
                    && entry.Reply.replyButtonBlocked.gameObject.activeSelf)
                {
                    // Clicking does nothing for sighted players either; they see this popup.
                    SpeechManager.Speak(SafeText("notEnoughGold", "Not enough gold"));
                    return;
                }
                UserSelectionInProgress = true;
                try
                {
                    entry.Reply.SelectThisOption();
                }
                finally
                {
                    UserSelectionInProgress = false;
                }
            }
            else
            {
                SpeechManager.Speak("Press Down to reach the choices");
            }
            return;
        }

        // Result phase: activate the focused button, else fall back to Continue.
        if (entry.Button != null)
        {
            entry.Button.Clicked();
            return;
        }
        if (em.continueButton != null && em.continueButton.gameObject.activeSelf)
            em.continueButton.Clicked();
    }

    /// <summary>
    /// Alt+T: speaks the hover-only information a sighted player sees when mousing over the
    /// focused choice — probability popup, blocked reason, card previews, roll-mode explainer.
    /// </summary>
    public static void SpeakFocusedDetail()
    {
        if (!Active || _entries.Count == 0 || _index < 0 || _index >= _entries.Count)
            return;
        var reply = _entries[_index].Reply;
        if (reply == null)
        {
            SpeechManager.Speak("No additional details");
            return;
        }

        var parts = new List<string>();
        try
        {
            bool blocked = reply.replyButtonBlocked != null
                && reply.replyButtonBlocked.gameObject.activeSelf;

            if (reply.probDice != null && reply.probDice.gameObject.activeSelf
                && reply.probPopup != null && !string.IsNullOrEmpty(reply.probPopup.text))
                parts.Add(Clean(reply.probPopup.text));

            if (blocked)
                parts.Add(SafeText("notEnoughGold", "Not enough gold"));
            else
                AddCardPreviewParts(reply, parts);

            if (reply.replyRoll != null && reply.replyRoll.gameObject.activeSelf)
            {
                var popup = reply.replyRoll.GetComponent<PopupText>();
                if (popup != null && !string.IsNullOrEmpty(popup.id))
                {
                    string desc = SafeText(popup.id, "");
                    if (desc.Length > 0)
                        parts.Add(Clean(desc));
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Event detail read failed: {e.Message}");
        }

        SpeechManager.Speak(parts.Count > 0 ? string.Join(". ", parts.ToArray()) : "No additional details");
    }

    // ------------------------------------------------------------------ entry building

    private static void RebuildEntries(bool preserveFocus)
    {
        var em = EventManager.Instance;
        if (em == null)
            return;

        int focusedOption = -1;
        int oldIndex = _index;
        if (preserveFocus && _index >= 0 && _index < _entries.Count && _entries[_index].Reply != null)
            focusedOption = _entries[_index].Reply.GetOptionIndex();

        _entries.Clear();
        foreach (var line in _textLines)
            _entries.Add(new Entry { Speech = line });

        int firstResultIndex = -1;

        if (_phase == Phase.Choosing)
        {
            if (em.notMeeted != null && em.notMeeted.gameObject.activeSelf)
            {
                string nm = Clean(em.notMeeted.text);
                if (nm.Length > 0)
                    _entries.Add(new Entry { Speech = nm });
            }
            var replies = GetVisibleReplies();
            for (int i = 0; i < replies.Count; i++)
                _entries.Add(new Entry
                {
                    Speech = BuildChoiceSpeech(replies[i], i + 1, replies.Count),
                    Reply = replies[i]
                });
        }
        else if (_phase == Phase.Result)
        {
            if (_selectedChoiceText.Length > 0)
                _entries.Add(new Entry { Speech = "Chosen: " + _selectedChoiceText });
            var resultLines = SplitBookText(em.result != null ? em.result.text : "");
            if (resultLines.Count > 0)
                firstResultIndex = _entries.Count;
            foreach (var line in resultLines)
                _entries.Add(new Entry { Speech = line });
            if (em.continueButton != null && em.continueButton.gameObject.activeSelf)
                _entries.Add(new Entry
                {
                    Speech = Clean(em.continueButton.GetText()) + ", button",
                    Button = em.continueButton
                });
        }

        // The map-peek Hide/Show button is on screen for the whole event.
        var mm = MapManager.Instance;
        var hideShow = mm != null && mm.eventShowHideButton != null
            ? mm.eventShowHideButton.GetComponent<BotonGeneric>()
            : null;
        if (hideShow != null && hideShow.gameObject.activeInHierarchy)
            _entries.Add(new Entry
            {
                Speech = Clean(hideShow.GetText()) + ", button",
                Button = hideShow,
                IsHideShow = true
            });

        if (preserveFocus)
        {
            int restored = -1;
            if (focusedOption >= 0)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Reply != null && _entries[i].Reply.GetOptionIndex() == focusedOption)
                    {
                        restored = i;
                        break;
                    }
                }
            }
            _index = restored >= 0 ? restored : Mathf.Clamp(oldIndex, 0, Math.Max(0, _entries.Count - 1));
        }
        else if (_phase == Phase.Result && firstResultIndex >= 0)
        {
            _index = firstResultIndex;
        }
        else
        {
            _index = Mathf.Clamp(_index, 0, Math.Max(0, _entries.Count - 1));
        }
    }

    private static string BuildChoiceSpeech(Reply reply, int n, int of)
    {
        var sb = new StringBuilder();
        sb.Append("Choice ").Append(n).Append(" of ").Append(of).Append(": ");
        sb.Append(reply.replyText != null ? Clean(reply.replyText.text) : "");

        if (reply.replyRoll != null && reply.replyRoll.gameObject.activeSelf
            && reply.replyRollText != null)
        {
            string roll = Clean(reply.replyRollText.text);
            if (roll.Length > 0)
                sb.Append(". Roll: ").Append(roll);
        }
        if (reply.replyButtonBlocked != null && reply.replyButtonBlocked.gameObject.activeSelf)
            sb.Append(". Blocked");
        if (reply.dlcText != null && reply.dlcText.gameObject.activeSelf)
        {
            string dlc = Clean(reply.dlcText.text);
            if (dlc.Length > 0)
                sb.Append(". ").Append(dlc);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True while our own Enter handling is inside <c>Reply.SelectThisOption</c> — lets the
    /// auto-select suppression patch tell the mod's deliberate pick from the game's timer.
    /// </summary>
    internal static bool UserSelectionInProgress;

    internal static int VisibleReplyCount() => GetVisibleReplies().Count;

    private static List<Reply> GetVisibleReplies()
    {
        var result = new List<Reply>();
        var em = EventManager.Instance;
        if (em == null || em.replysT == null)
            return result;
        foreach (var r in em.replysT.GetComponentsInChildren<Reply>(false))
        {
            if (r != null)
                result.Add(r);
        }
        result.Sort((a, b) => a.GetOptionIndex().CompareTo(b.GetOptionIndex()));
        return result;
    }

    private static int ComputeReplyHash(EventManager em)
    {
        int h = 17;
        if (em.notMeeted != null && em.notMeeted.gameObject.activeSelf)
            h = h * 31 + 1;
        var replies = GetVisibleReplies();
        h = h * 31 + replies.Count;
        foreach (var r in replies)
        {
            h = h * 31 + r.GetOptionIndex();
            h = h * 31 + ((r.replyButtonBlocked != null && r.replyButtonBlocked.gameObject.activeSelf) ? 1 : 0);
        }
        return h;
    }

    // ------------------------------------------------------------------ hover-info mirrors

    /// <summary>
    /// Mirrors <c>Reply.OnMouseEnter</c>'s card previews, names only: the shown card, or the
    /// item the choice would corrupt (and what it becomes), or the item it would remove.
    /// </summary>
    private static void AddCardPreviewParts(Reply reply, List<string> parts)
    {
        var data = reply.GetEventReplyData();
        if (data == null || Globals.Instance == null)
            return;

        if (data.ReplyShowCard != null)
        {
            var cd = Globals.Instance.GetCardData(data.ReplyShowCard.Id, instantiate: false);
            if (cd != null)
                parts.Add("Shows card: " + Clean(cd.CardName));
            return;
        }

        if (data.SsCorruptItemSlot != Enums.ItemSlot.None)
        {
            Hero hero;
            string itemId = FindHeroSlotItem(data.RequiredClass, data.SsCorruptItemSlot, out hero);
            if (itemId == null)
                return;
            var cd = Functions.GetCardDataFromCardData(
                Globals.Instance.GetCardData(itemId, instantiate: false), "");
            if (cd == null || cd.UpgradesToRare == null)
                return;
            var corrupted = Globals.Instance.GetCardData(cd.UpgradesToRare.Id, instantiate: false);
            if (corrupted != null)
                parts.Add(Clean(cd.CardName) + " corrupts to " + Clean(corrupted.CardName));
            return;
        }

        if (data.SsRemoveItemSlot != Enums.ItemSlot.None)
        {
            Hero hero;
            string itemId = FindHeroSlotItem(data.RequiredClass, data.SsRemoveItemSlot, out hero);
            if (itemId == null)
                return;
            var cd = Globals.Instance.GetCardData(itemId, instantiate: false);
            if (cd != null && hero != null)
                parts.Add("Removes " + Clean(cd.CardName) + " from " + hero.SourceName);
        }
    }

    private static string FindHeroSlotItem(SubClassData requiredClass, Enums.ItemSlot slot, out Hero hero)
    {
        hero = null;
        var em = EventManager.Instance;
        if (em == null || requiredClass == null || Globals.Instance == null)
            return null;
        var subClass = Globals.Instance.GetSubClassData(requiredClass.Id);
        if (subClass == null)
            return null;

        var heroes = em.Heroes;
        if (heroes == null)
            return null;
        foreach (var h in heroes)
        {
            if (h == null || h.HeroData == null || h.HeroData.HeroSubClass.Id != subClass.Id)
                continue;
            hero = h;
            string itemId;
            switch (slot)
            {
                case Enums.ItemSlot.Weapon: itemId = h.Weapon; break;
                case Enums.ItemSlot.Armor: itemId = h.Armor; break;
                case Enums.ItemSlot.Jewelry: itemId = h.Jewelry; break;
                case Enums.ItemSlot.Accesory: itemId = h.Accesory; break;
                case Enums.ItemSlot.Pet: itemId = h.Pet; break;
                default: itemId = ""; break;
            }
            return string.IsNullOrEmpty(itemId) ? null : itemId;
        }
        return null;
    }

    private static void SpeakRewardCard(EventManager em, string cardId)
    {
        // Pick the same helper the game picks (injuries go to the KO frame).
        var pendingData = Globals.Instance != null
            ? Globals.Instance.GetCardData(cardId, instantiate: false)
            : null;
        var helper = pendingData != null && pendingData.CardClass == Enums.CardClass.Injury
            ? em.cardKO
            : em.cardOK;

        string caption = helper != null && helper.Text != null ? Clean(helper.Text.text) : "";

        // Read the displayed card's name (covers the NG+ injury upgrade swap).
        string cardName = "";
        var item = helper != null && helper.parent != null
            ? helper.parent.GetComponentInChildren<CardItem>()
            : null;
        var displayed = item != null ? item.GetCardData() : null;
        if (displayed == null)
            displayed = pendingData;
        if (displayed != null)
            cardName = Clean(displayed.CardName);

        string line = (caption + " " + cardName).Trim();
        if (line.Length > 0)
            SpeechManager.SpeakQueued(line);
    }

    // ------------------------------------------------------------------ text helpers

    /// <summary>
    /// Splits book text into spoken lines: TMP <c>&lt;br&gt;</c> tags and real newlines both
    /// break lines; each segment gets sprite-tag conversion then rich-text stripping.
    /// </summary>
    private static List<string> SplitBookText(string raw)
        => AccessibleMenuBase.SplitLines(raw, Clean);

    /// <summary>Sprite-tag conversion + rich-text strip, the pipeline for all event speech.</summary>
    private static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return AccessibleMenuBase.StripRichText(ConvertSpriteTags(raw));
    }

    /// <summary>
    /// Converts TMP <c>&lt;sprite name=X&gt;</c> icons to words before stripping, so reward
    /// lines keep their units ("gold 75" instead of a bare "75"). Unknown names (perk icons)
    /// fall back to the sprite name itself. Kept local — StripRichText's other callers rely
    /// on tags vanishing entirely.
    /// </summary>
    private static string ConvertSpriteTags(string raw)
    {
        return _spriteTag.Replace(raw, m =>
        {
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "gold": return " gold ";
                case "dust": return " dust ";
                case "experience": return " experience ";
                case "supply": return " supplies ";
                case "heart": return " health ";
                case "cards": return " "; // decorative deck icon on the roll banner
                default: return " " + m.Groups[1].Value + " ";
            }
        });
    }

    private static string SafeText(string id, string fallback)
    {
        try
        {
            string text = Texts.Instance != null ? Texts.Instance.GetText(id) : "";
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }

    private static void Reset()
    {
        _phase = Phase.None;
        _entries.Clear();
        _textLines.Clear();
        _pendingCardReads.Clear();
        _index = 0;
        _replySnapshotHash = 0;
        _repliesAnnounced = false;
        _spokenResultLength = 0;
        _rollCardsCalls = 0;
        _selectedChoiceText = "";
    }
}

[HarmonyPatch(typeof(EventManager), nameof(EventManager.SetEvent))]
public class EventOpenPatch
{
    static void Postfix()
    {
        EventScreenManager.OnEventOpened();
    }
}

[HarmonyPatch(typeof(EventManager), nameof(EventManager.SelectOption))]
public class EventSelectOptionPatch
{
    static void Postfix(int _index)
    {
        EventScreenManager.OnOptionSelected(_index);
    }
}

[HarmonyPatch(typeof(EventManager), nameof(EventManager.NET_SelectAnswer))]
public class EventNetSelectAnswerPatch
{
    static void Postfix(int _answerId)
    {
        EventScreenManager.OnOptionSelected(_answerId);
    }
}

/// <summary>Every vote (local and partner) lands here on every client; the manager filters the
/// local echo and speaks partner picks with the running tally.</summary>
[HarmonyPatch(typeof(EventManager), nameof(EventManager.NET_OptionSelected))]
public class EventMpVoteAnnouncePatch
{
    static void Postfix(string _playerNick, int _option)
    {
        EventScreenManager.OnMpVote(_playerNick, _option);
    }
}

/// <summary>The MP Continue button is a READY TOGGLE (statusReady + SetManualReady), not a
/// close: a second press silently un-readies, and for follow-the-leader non-masters the whole
/// call is a silent no-op. Speak all three outcomes — the ambient ready-count line covers
/// partners but deliberately says nothing on the un-ready-to-zero edge, and its non-master
/// round-trip lag leaves the local press feeling dead.</summary>
[HarmonyPatch(typeof(EventManager), nameof(EventManager.Ready))]
public class EventReadyToggleAnnouncePatch
{
    static void Prefix(EventManager __instance, out bool __state)
    {
        __state = Traverse.Create(__instance).Field<bool>("statusReady").Value;
    }

    static void Postfix(EventManager __instance, bool forceIt, bool __state)
    {
        // SP and town route straight to the close path — other announcements own those.
        if (!MpSpeech.IsMp || TownManager.Instance != null)
            return;
        bool now = Traverse.Create(__instance).Field<bool>("statusReady").Value;
        if (now == __state)
        {
            // Unchanged = the follower early-out (a real press only; the game's own forced
            // sync calls pass forceIt and may legitimately no-op).
            if (!forceIt && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster()
                && AtOManager.Instance != null && AtOManager.Instance.followingTheLeader)
                SpeechManager.Speak("Following the leader — the host continues for the party.");
            return;
        }
        SpeechManager.Speak(now
            ? "Ready to continue — waiting for the other players."
            : "No longer ready to continue.");
    }
}

/// <summary>
/// The mod's one deliberate behaviour change (documented in the README under "Design
/// philosophy"): in single player the game auto-selects a lone event choice 0.5 seconds after
/// showing it (tail of the EventManager reply-layout coroutine). That window is calibrated to a
/// sighted player who has already seen the whole screen — for a screen-reader user it cuts off
/// the event text mid-sentence and makes the option unreviewable (Alt+T can never be reached).
/// This prefix swallows exactly that call, so lone-option events wait for Enter like every other
/// event. The gate mirrors the game's own auto-select conditions (single player, nothing selected
/// yet, exactly one visible choice) and exempts every deliberate selection path: the mod's Enter
/// handling (flag), a real mouse click, and gamepad A — the latter two arrive as this same method
/// via Reply.OnMouseUp and the game's synthetic cursor click. A blocked call is one-shot; the
/// game has no retry, and all its other paths (multiplayer, tutorial, hidden-option events)
/// already run without the auto-select, so waiting is a fully supported state.
/// </summary>
[HarmonyPatch(typeof(Reply), nameof(Reply.SelectThisOption))]
public class EventAutoSelectSuppressPatch
{
    static bool Prefix()
    {
        if (GameManager.Instance == null || GameManager.Instance.IsMultiplayer())
            return true;
        if (EventScreenManager.UserSelectionInProgress)
            return true;
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            return true;
        var pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
            return true;
        var em = EventManager.Instance;
        if (em == null || em.optionSelected != -1)
            return true;
        if (EventScreenManager.VisibleReplyCount() != 1)
            return true;
        return false; // the game's 0.5s auto-select of the lone option — suppressed
    }
}

[HarmonyPatch(typeof(EventManager), "DoCard")]
public class EventDoCardPatch
{
    static void Postfix(int heroIndex)
    {
        EventScreenManager.OnCardDrawn(heroIndex);
    }
}

[HarmonyPatch(typeof(EventManager), "ShowResultTitle")]
public class EventShowResultTitlePatch
{
    static void Postfix(bool success, int heroIndex)
    {
        EventScreenManager.OnResultTitle(success, heroIndex);
    }
}

[HarmonyPatch(typeof(EventManager), "RollCards")]
public class EventRollCardsPatch
{
    static void Postfix()
    {
        EventScreenManager.OnRollCardsStarted();
    }
}

[HarmonyPatch(typeof(EventManager), "Finish")]
public class EventFinishPatch
{
    static void Postfix()
    {
        EventScreenManager.OnFinish();
    }
}

[HarmonyPatch(typeof(EventManager), "GenerateRewardCard")]
public class EventRewardCardPatch
{
    static void Postfix(string cardId)
    {
        EventScreenManager.OnRewardCard(cardId);
    }
}

[HarmonyPatch(typeof(EventManager), nameof(EventManager.CloseEvent))]
public class EventClosePatch
{
    static void Postfix()
    {
        EventScreenManager.OnEventClosed();
    }
}
