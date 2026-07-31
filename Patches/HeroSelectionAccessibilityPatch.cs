using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Keyboard navigation + speech for the hero-selection screen (scene "HeroSelection") — the
/// start-of-run screen where the party is assembled and the run options (madness, sandbox, seed)
/// are set before Begin Adventure. Three Tab regions: the hero roster, the four party slots, and
/// the run-options list. The character window and perk tree that open over this screen have their
/// own managers (<see cref="CharPopupScreenManager"/>, <see cref="PerkTreeScreenManager"/>).
///
/// Mouse drag-and-drop is not emulated: Enter assigns the focused hero to the first empty slot,
/// digits 1–4 assign to a specific slot (replacing its occupant), and Enter on a filled party
/// slot clears it. All confirm/input dialogs this screen raises (seed entry, multiplayer
/// restarts) are owned by the global alert dialogue.
/// </summary>
internal static class HeroSelectionScreenManager
{
    private enum Region { Roster, Party, RunOptions }

    private sealed class RunRow
    {
        public Func<string> Read;
        public Action Activate;
    }

    private readonly struct FilterTab
    {
        public readonly string Key;
        public readonly string Name;
        public FilterTab(string key, string name) { Key = key; Name = name; }
    }

    // Filter tabs in the game's own visual order; keys match ShowHeroesByFilterAsync.
    private static readonly FilterTab[] FilterTabs =
    {
        new FilterTab("all", "All"),
        new FilterTab("warrior", "Warriors"),
        new FilterTab("scout", "Scouts"),
        new FilterTab("mage", "Mages"),
        new FilterTab("healer", "Healers"),
        new FilterTab("dlc", "Multiclass"),
        new FilterTab("locked", "Locked"),
    };

    private const int HeroesPerPage = 16; // ArrangeHeroSelections lays out 8 per row, 16 per page

    private static Region _region;
    private static int _tabIndex;
    private static int _rosterIndex = -1;          // -1 sentinel: first Down lands on hero 1
    private static string _rosterFocusId;          // survives the list re-sorting on pick/unpick
    private static int _partyIndex = -1;           // index into the active-box list
    private static int _runIndex = -1;             // index into _runRows
    private static readonly List<HeroSelection> _roster = new List<HeroSelection>();
    private static readonly List<int> _activeBoxes = new List<int>();
    private static readonly List<RunRow> _runRows = new List<RunRow>();

    // Lifecycle (poller tick, runs outside the input gate).
    private static int _sceneLatch;
    private static int _readyFrames;
    private static bool _arrivalAnnounced;
    private static bool _firstGameAuto;
    private static bool _lastBeginEnabled;
    private static bool _readyPrimed;
    private static readonly bool[] _lastReady = new bool[4];

    private static HeroSelectionManager Mgr => HeroSelectionManager.Instance;

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Per-frame tick from the poller — runs even while a modal owns input, so arrival
    /// detection, the Begin-button edge, and multiplayer ready changes are never missed.</summary>
    public static void Tick()
    {
        var hsm = Mgr;
        if (hsm == null)
        {
            if (_sceneLatch != 0)
                ResetState();
            return;
        }

        int id = hsm.GetInstanceID();
        if (id != _sceneLatch)
        {
            ResetState();
            _sceneLatch = id;
        }

        if (!_arrivalAnnounced)
        {
            if (hsm.heroSelectionDictionary == null || hsm.heroSelectionDictionary.Count == 0)
                return;
            if (IsFirstGameAuto())
            {
                _arrivalAnnounced = true;
                _firstGameAuto = true;
                SpeechManager.Speak("First adventure: party assigned automatically, starting the run.");
                return;
            }
            // charPopupGO is assigned near the end of the setup coroutine — a cheap "screen is
            // fully built" signal; a few extra frames let the layout settle.
            if (hsm.charPopupGO == null)
                return;
            if (++_readyFrames < 3)
                return;
            _arrivalAnnounced = true;
            SpeechManager.Speak(BuildOverview(hsm));
        }

        // "Begin Adventure available" rising edge (fires when the 4th hero lands, however placed).
        bool beginEnabled = hsm.botonBegin != null && hsm.botonBegin.gameObject.activeSelf
            && hsm.botonBegin.IsEnabled();
        if (beginEnabled && !_lastBeginEnabled && !_firstGameAuto)
            SpeechManager.SpeakQueued("Begin Adventure available.");
        _lastBeginEnabled = beginEnabled;

        // Multiplayer ready-state edges (several call paths funnel here, so poll rather than patch).
        if (GameManager.Instance != null && GameManager.Instance.IsMultiplayer()
            && NetworkManager.Instance != null)
        {
            for (int i = 0; i < 4; i++)
            {
                string owner = hsm.GetBoxOwnerFromIndex(i);
                bool ready = !string.IsNullOrEmpty(owner) && NetworkManager.Instance.IsPlayerReady(owner);
                if (_readyPrimed && ready != _lastReady[i] && !string.IsNullOrEmpty(owner))
                    SpeechManager.SpeakQueued(MpSpeech.DisplayNick(owner)
                        + (ready ? " is ready." : " is no longer ready."));
                _lastReady[i] = ready;
            }
            _readyPrimed = true;
        }
    }

    private static void ResetState()
    {
        _region = Region.Roster;
        _tabIndex = 0;
        _rosterIndex = -1;
        _rosterFocusId = null;
        _partyIndex = -1;
        _runIndex = -1;
        _roster.Clear();
        _activeBoxes.Clear();
        _runRows.Clear();
        _sceneLatch = 0;
        _readyFrames = 0;
        _arrivalAnnounced = false;
        _firstGameAuto = false;
        _lastBeginEnabled = false;
        _readyPrimed = false;
        for (int i = 0; i < 4; i++)
            _lastReady[i] = false;
    }

    private static bool IsFirstGameAuto()
        => GameManager.Instance != null && GameManager.Instance.IsGameAdventure()
        && !GameManager.Instance.IsMultiplayer()
        && AtOManager.Instance != null && AtOManager.Instance.IsFirstGame();

    /// <summary>True while the scene is mid auto-start (very first adventure) — every action key
    /// answers with the explanation instead of acting.</summary>
    public static bool FirstGameAuto => _firstGameAuto;

    // ------------------------------------------------------------------ overview

    public static void SpeakOverview()
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        if (_firstGameAuto)
        {
            SpeechManager.Speak("First adventure: party assigned automatically, starting the run.");
            return;
        }
        SpeechManager.Speak(BuildOverview(hsm));
    }

    private static string BuildOverview(HeroSelectionManager hsm)
    {
        var sb = new StringBuilder();
        sb.Append(ModeName()).Append(". ");

        int madness = CurrentMadness(hsm);
        if (madness > 0)
        {
            if (GameManager.Instance.IsGameAdventure())
                sb.Append("New Game Plus level ").Append(madness).Append(". ");
            else
                sb.Append("Madness level ").Append(madness).Append(". ");
        }

        if (hsm.gameSeed != null && hsm.gameSeed.gameObject.activeSelf && hsm.gameSeedTxt != null)
            sb.Append("Seed: ").Append(HeroSpeech.SpellSeed(hsm.gameSeedTxt.text)).Append(". ");

        int active = 0, filled = 0;
        for (int i = 0; i < 4; i++)
        {
            if (hsm.boxGO == null || i >= hsm.boxGO.Length || hsm.boxGO[i] == null
                || !hsm.boxGO[i].activeSelf)
                continue;
            active++;
            if (hsm.IsBoxFilled(hsm.boxGO[i]))
                filled++;
        }
        if (filled == 0)
            sb.Append("No heroes selected. ");
        else
            sb.Append(filled).Append(" of ").Append(active).Append(" slots filled. ");

        if (GameManager.Instance.IsWeeklyChallenge())
            sb.Append("The party is fixed this week. ");
        else if (GameManager.Instance.IsLoadingGame())
            sb.Append("Loading a saved game — the party can't be changed. ");

        sb.Append("Up and down to browse heroes, Enter to add to the party, "
            + "Tab for party and run options, Alt+C for a hero's character window, "
            + "Alt+I to repeat this overview.");
        return sb.ToString();
    }

    private static string ModeName()
    {
        var gm = GameManager.Instance;
        if (gm == null)
            return "Hero selection";
        switch (gm.GameType)
        {
            case Enums.GameType.Adventure:
                return AtOManager.Instance != null && AtOManager.Instance.IsCombatTool
                    ? "Practice mode" : "Adventure mode";
            case Enums.GameType.Challenge:
                return "Obelisk Challenge";
            case Enums.GameType.WeeklyChallenge:
                return "Weekly Challenge";
            case Enums.GameType.Singularity:
                return "Singularity";
            default:
                return "Hero selection";
        }
    }

    private static int CurrentMadness(HeroSelectionManager hsm)
    {
        var gm = GameManager.Instance;
        if (gm == null)
            return 0;
        switch (gm.GameType)
        {
            case Enums.GameType.Adventure: return hsm.NgValue;
            case Enums.GameType.Challenge: return hsm.ObeliskMadnessValue;
            case Enums.GameType.Singularity: return hsm.SingularityMadnessValue;
            default: return 0;
        }
    }

    // ------------------------------------------------------------------ navigation

    public static void MoveVertical(int delta)
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        switch (_region)
        {
            case Region.Roster: MoveRoster(delta); break;
            case Region.Party: MoveParty(delta); break;
            case Region.RunOptions: MoveRun(delta); break;
        }
    }

    public static void MoveHorizontal(int delta)
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        if (_region == Region.Roster)
        {
            CycleFilterTab(delta);
            return;
        }
        // Left/right have no meaning in the other regions — re-read the current item.
        AnnounceCurrent();
    }

    public static void CycleRegion(bool backwards)
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        int count = Enum.GetValues(typeof(Region)).Length;
        int next = ((int)_region + (backwards ? count - 1 : 1)) % count;
        _region = (Region)next;
        if (_region == Region.Roster)
            HeroSpeech.PlayHeroFocusSound();
        else
            HeroSpeech.PlayButtonFocusSound();
        switch (_region)
        {
            case Region.Roster:
                BuildRoster(hsm);
                if (_rosterIndex >= 0 && _rosterIndex < _roster.Count)
                    SpeechManager.Speak("Roster. " + HeroLineInContext(_roster[_rosterIndex]));
                else
                    SpeechManager.Speak("Roster, " + _roster.Count
                        + " heroes. Up and down to browse.");
                break;
            case Region.Party:
                BuildActiveBoxes(hsm);
                if (_activeBoxes.Count == 0)
                {
                    SpeechManager.Speak("Party. No slots available.");
                    break;
                }
                if (_partyIndex < 0 || _partyIndex >= _activeBoxes.Count)
                    _partyIndex = 0;
                SpeechManager.Speak("Party. " + SlotLine(hsm, _activeBoxes[_partyIndex]));
                break;
            case Region.RunOptions:
                BuildRunRows(hsm);
                if (_runRows.Count == 0)
                {
                    SpeechManager.Speak("Run options. Nothing available.");
                    break;
                }
                if (_runIndex < 0 || _runIndex >= _runRows.Count)
                    _runIndex = 0;
                SpeechManager.Speak("Run options. " + _runRows[_runIndex].Read());
                break;
        }
    }

    /// <summary>Escape: from Party or Run options, return focus to the Roster (consumed). In the
    /// Roster the key is left to the game (settings menu).</summary>
    public static bool Cancel()
    {
        if (Mgr == null || _region == Region.Roster)
            return false;
        _region = Region.Roster;
        BuildRoster(Mgr);
        if (_rosterIndex >= 0 && _rosterIndex < _roster.Count)
            SpeechManager.Speak("Roster. " + HeroLineInContext(_roster[_rosterIndex]));
        else
            SpeechManager.Speak("Roster. Up and down to browse heroes.");
        return true;
    }

    public static void AnnounceCurrent()
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        switch (_region)
        {
            case Region.Roster:
                BuildRoster(hsm);
                if (_rosterIndex >= 0 && _rosterIndex < _roster.Count)
                    SpeechManager.Speak(HeroLineInContext(_roster[_rosterIndex]));
                else
                    SpeechManager.Speak("Roster. Up and down to browse heroes.");
                break;
            case Region.Party:
                BuildActiveBoxes(hsm);
                if (_partyIndex >= 0 && _partyIndex < _activeBoxes.Count)
                    SpeechManager.Speak(SlotLine(hsm, _activeBoxes[_partyIndex]));
                break;
            case Region.RunOptions:
                BuildRunRows(hsm);
                if (_runIndex >= 0 && _runIndex < _runRows.Count)
                    SpeechManager.Speak(_runRows[_runIndex].Read());
                break;
        }
    }

    // ------------------------------------------------------------------ roster region

    private static void BuildRoster(HeroSelectionManager hsm)
    {
        _roster.Clear();
        if (hsm.heroSelectionDictionary == null)
            return;
        string key = FilterTabs[_tabIndex].Key;
        foreach (var pair in hsm.heroSelectionDictionary)
        {
            var hs = pair.Value;
            if (hs != null && PassesFilter(hs, key))
                _roster.Add(hs);
        }
        // Keep focus on the same hero across the list re-sorting that picking triggers.
        if (_rosterFocusId != null)
        {
            int found = _roster.FindIndex(h => h.Id == _rosterFocusId);
            if (found >= 0)
                _rosterIndex = found;
            else if (_rosterIndex >= _roster.Count)
                _rosterIndex = _roster.Count - 1;
        }
    }

    /// <summary>Replica of the game's private GetHeroSelectionFilter.</summary>
    private static bool PassesFilter(HeroSelection hs, string key)
    {
        switch (key)
        {
            case "all":
                return true;
            case "dlc":
                return hs.GetHeroClass().ToLower() != "none"
                    && hs.GetHeroClassSecondary().ToLower() != "none";
            case "locked":
                return hs.blocked;
            default:
                return hs.GetHeroClass().ToLower() == key
                    || hs.GetHeroClassSecondary().ToLower() == key;
        }
    }

    private static void MoveRoster(int delta)
    {
        var hsm = Mgr;
        BuildRoster(hsm);
        if (_roster.Count == 0)
        {
            SpeechManager.Speak("No heroes in this tab.");
            return;
        }
        int next = _rosterIndex < 0
            ? (delta > 0 ? 0 : _roster.Count - 1)
            : Mathf.Clamp(_rosterIndex + delta, 0, _roster.Count - 1);
        _rosterIndex = next;
        _rosterFocusId = _roster[next].Id;
        EnsurePageFor(hsm, next);
        HeroSpeech.PlayHeroFocusSound();
        SpeechManager.Speak(HeroLineInContext(_roster[next]));
    }

    /// <summary>Flips the visual page so the focused hero is on screen (16 heroes per page).</summary>
    private static void EnsurePageFor(HeroSelectionManager hsm, int index)
    {
        var container = TabContainer(hsm, FilterTabs[_tabIndex].Key);
        if (container == null || container.transform.childCount <= 1)
            return;
        int current = -1;
        for (int i = 0; i < container.transform.childCount; i++)
        {
            if (container.transform.GetChild(i).gameObject.activeSelf)
            {
                current = i;
                break;
            }
        }
        if (current < 0)
            return;
        int target = index / HeroesPerPage;
        int guard = 0;
        while (current < target && guard++ < 10) { hsm.moveHeroPageRight(); current++; }
        while (current > target && guard++ < 10) { hsm.moveHeroPageLeft(); current--; }
    }

    private static GameObject TabContainer(HeroSelectionManager hsm, string key)
    {
        switch (key)
        {
            case "dlc": return hsm.dlcsGO;
            case "locked": return hsm.lockedGO;
            case "warrior": return hsm.warriorsGO;
            case "scout": return hsm.scoutsGO;
            case "mage": return hsm.magesGO;
            case "healer": return hsm.healersGO;
            default: return hsm.allGO;
        }
    }

    private static void CycleFilterTab(int delta)
    {
        var hsm = Mgr;
        _tabIndex = (_tabIndex + delta + FilterTabs.Length) % FilterTabs.Length;
        hsm.ShowHeroesByFilterAsync(FilterTabs[_tabIndex].Key);
        _rosterIndex = -1;
        _rosterFocusId = null;
        BuildRoster(hsm);
        string intro = FilterTabs[_tabIndex].Name + ", "
            + _roster.Count + (_roster.Count == 1 ? " hero." : " heroes.");
        HeroSpeech.PlayButtonFocusSound();
        if (_roster.Count > 0)
        {
            _rosterIndex = 0;
            _rosterFocusId = _roster[0].Id;
            SpeechManager.Speak(intro + " " + HeroLineInContext(_roster[0]));
        }
        else
        {
            SpeechManager.Speak(intro);
        }
    }

    /// <summary>HeroLine with the weekly special case: every portrait is flagged blocked in
    /// weekly mode, where "locked" would be misleading noise.</summary>
    private static string HeroLineInContext(HeroSelection hs)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsWeeklyChallenge()
            && hs != null && HeroSpeech.ScdOf(hs) != null)
        {
            string line = HeroSpeech.ScdOf(hs).CharacterName;
            string subclass = HeroSpeech.SubclassWord(HeroSpeech.ScdOf(hs));
            if (subclass != "")
                line += ", " + subclass;
            if (hs.HeroPicked)
                line += ". " + HeroSpeech.PickedLine(hs);
            return line;
        }
        return HeroSpeech.HeroLine(hs);
    }

    // ------------------------------------------------------------------ party region

    private static void BuildActiveBoxes(HeroSelectionManager hsm)
    {
        _activeBoxes.Clear();
        if (hsm.boxGO == null)
            return;
        for (int i = 0; i < hsm.boxGO.Length && i < 4; i++)
        {
            if (hsm.boxGO[i] != null && hsm.boxGO[i].activeSelf)
                _activeBoxes.Add(i);
        }
    }

    private static void MoveParty(int delta)
    {
        var hsm = Mgr;
        BuildActiveBoxes(hsm);
        if (_activeBoxes.Count == 0)
        {
            SpeechManager.Speak("No party slots available.");
            return;
        }
        int next = _partyIndex < 0
            ? (delta > 0 ? 0 : _activeBoxes.Count - 1)
            : Mathf.Clamp(_partyIndex + delta, 0, _activeBoxes.Count - 1);
        _partyIndex = next;
        HeroSpeech.PlayButtonFocusSound();
        SpeechManager.Speak(SlotLine(hsm, _activeBoxes[next]));
    }

    private static string SlotLine(HeroSelectionManager hsm, int boxIndex)
    {
        var sb = new StringBuilder();
        sb.Append("Slot ").Append(boxIndex + 1).Append(": ");
        var hero = hsm.GetBoxHeroFromIndex(boxIndex);
        if (hero != null && HeroSpeech.ScdOf(hero) != null)
        {
            sb.Append(HeroSpeech.ScdOf(hero).CharacterName);
            string subclass = HeroSpeech.SubclassWord(HeroSpeech.ScdOf(hero));
            if (subclass != "")
                sb.Append(", ").Append(subclass);
        }
        else
        {
            sb.Append("empty");
        }
        if (GameManager.Instance != null && GameManager.Instance.IsMultiplayer()
            && NetworkManager.Instance != null)
        {
            string owner = hsm.GetBoxOwnerFromIndex(boxIndex);
            if (!string.IsNullOrEmpty(owner))
            {
                sb.Append(", ").Append(MpSpeech.DisplayNick(owner));
                sb.Append(NetworkManager.Instance.IsPlayerReady(owner) ? ", ready" : ", not ready");
                if (!hsm.IsYourBox(hsm.boxGO[boxIndex].name))
                    sb.Append(", controlled by them");
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ run options region

    private static void BuildRunRows(HeroSelectionManager hsm)
    {
        _runRows.Clear();

        if (hsm.madnessButton != null && hsm.madnessButton.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => MadnessRowText(),
                Activate = () => OpenDeferredPanel(madness: true),
            });
        }
        if (hsm.weeklyModifiersButton != null && hsm.weeklyModifiersButton.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => "Weekly modifiers. Press Enter to open the modifiers panel.",
                Activate = () => OpenDeferredPanel(madness: true),
            });
        }
        if (hsm.sandboxButton != null && hsm.sandboxButton.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => "Sandbox mode, " + (SandboxManager.Instance != null
                    && SandboxManager.Instance.IsEnabled() ? "on" : "off")
                    + ". Press Enter to open the sandbox panel.",
                Activate = () => OpenDeferredPanel(madness: false),
            });
        }
        if (hsm.gameSeed != null && hsm.gameSeed.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => SeedRowText(hsm),
                Activate = () => ActivateSeedRow(hsm),
            });
        }
        // Multiplayer non-host controls.
        if (hsm.readyButton != null && hsm.readyButton.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => "Ready: " + (IsLocalReady(hsm) ? "yes" : "no")
                    + ". Press Enter to toggle.",
                Activate = () =>
                {
                    hsm.Ready();
                    SpeechManager.Speak(IsLocalReady(hsm)
                        ? "You are ready." : "You are no longer ready.");
                },
            });
        }
        if (hsm.botonFollow != null && hsm.botonFollow.transform.parent != null
            && hsm.botonFollow.transform.parent.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => "Follow the leader: "
                    + (AtOManager.Instance != null && AtOManager.Instance.followingTheLeader
                        ? "on" : "off") + ". Press Enter to toggle.",
                Activate = () =>
                {
                    hsm.FollowTheLeader();
                    SpeechManager.Speak("Follow the leader "
                        + (AtOManager.Instance != null && AtOManager.Instance.followingTheLeader
                            ? "on." : "off."));
                },
            });
        }
        if (hsm.botonBegin != null && hsm.botonBegin.gameObject.activeSelf)
        {
            _runRows.Add(new RunRow
            {
                Read = () => BeginRowText(hsm),
                Activate = () => ActivateBeginRow(hsm),
            });
        }
    }

    private static string MadnessRowText()
    {
        var hsm = Mgr;
        int level = CurrentMadness(hsm);
        string label = GameManager.Instance != null && GameManager.Instance.IsGameAdventure()
            ? "New Game Plus level " + level
            : "Madness level " + level;
        return label + ". Press Enter to open the madness panel.";
    }

    private static void OpenDeferredPanel(bool madness)
    {
        if (madness)
        {
            if (MadnessManager.Instance == null)
                return;
            MadnessManager.Instance.ShowMadness();
            SpeechManager.Speak("Madness panel opened. This panel is not yet accessible — "
                + "press Escape to close it.");
        }
        else
        {
            if (SandboxManager.Instance == null)
                return;
            SandboxManager.Instance.ShowSandbox();
            SpeechManager.Speak("Sandbox panel opened. This panel is not yet accessible — "
                + "press Escape to close it.");
        }
    }

    private static string SeedRowText(HeroSelectionManager hsm)
    {
        string spelled = hsm.gameSeedTxt != null
            ? HeroSpeech.SpellSeed(hsm.gameSeedTxt.text) : "not set";
        bool canChange = hsm.gameSeedModify != null && hsm.gameSeedModify.gameObject.activeSelf;
        return "Game seed: " + spelled + ". "
            + (canChange ? "Press Enter to type a new seed." : "The seed can't be changed"
                + SeedFixedReason() + ".");
    }

    private static string SeedFixedReason()
    {
        var gm = GameManager.Instance;
        if (gm == null)
            return "";
        if (gm.IsWeeklyChallenge())
            return " — it is fixed for the weekly challenge";
        if (gm.IsLoadingGame())
            return " — it is fixed while loading a save";
        if (gm.IsMultiplayer() && NetworkManager.Instance != null
            && !NetworkManager.Instance.IsMaster())
            return " — only the host can change it";
        if (AtOManager.Instance != null && AtOManager.Instance.IsFirstGame())
            return " — it is fixed for the first adventure";
        return "";
    }

    private static void ActivateSeedRow(HeroSelectionManager hsm)
    {
        bool canChange = hsm.gameSeedModify != null && hsm.gameSeedModify.gameObject.activeSelf;
        if (!canChange)
        {
            SpeechManager.Speak("The seed can't be changed" + SeedFixedReason() + ".");
            return;
        }
        hsm.ChangeSeed(); // opens the game's input alert — the global alert dialogue owns it
    }

    private static string BeginRowText(HeroSelectionManager hsm)
    {
        if (hsm.botonBegin.IsEnabled())
            return "Begin Adventure. Press Enter to start the run.";
        int missing = 0;
        for (int i = 0; i < 4; i++)
        {
            if (hsm.boxGO != null && i < hsm.boxGO.Length && hsm.boxGO[i] != null
                && hsm.boxGO[i].activeSelf && !hsm.IsBoxFilled(hsm.boxGO[i]))
                missing++;
        }
        if (missing > 0)
            return "Begin Adventure, not available — party incomplete, " + missing
                + (missing == 1 ? " more hero needed." : " more heroes needed.");
        return "Begin Adventure, not available — waiting for all players to be ready.";
    }

    private static void ActivateBeginRow(HeroSelectionManager hsm)
    {
        if (!hsm.botonBegin.IsEnabled())
        {
            SpeechManager.Speak(BeginRowText(hsm));
            return;
        }
        hsm.BeginAdventure(); // the BeginAdventure postfix announces the departure
    }

    private static bool IsLocalReady(HeroSelectionManager hsm)
        => Traverse.Create(hsm).Field<bool>("statusReady").Value;

    private static void MoveRun(int delta)
    {
        var hsm = Mgr;
        BuildRunRows(hsm);
        if (_runRows.Count == 0)
        {
            SpeechManager.Speak("No run options available.");
            return;
        }
        int next = _runIndex < 0
            ? (delta > 0 ? 0 : _runRows.Count - 1)
            : Mathf.Clamp(_runIndex + delta, 0, _runRows.Count - 1);
        _runIndex = next;
        HeroSpeech.PlayButtonFocusSound();
        SpeechManager.Speak(_runRows[next].Read());
    }

    // ------------------------------------------------------------------ actions

    public static void Activate()
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        if (SpeakBlockedAction())
            return;
        switch (_region)
        {
            case Region.Roster:
                ActivateRosterHero();
                break;
            case Region.Party:
                ActivatePartySlot();
                break;
            case Region.RunOptions:
                BuildRunRows(hsm);
                if (_runIndex >= 0 && _runIndex < _runRows.Count)
                    _runRows[_runIndex].Activate();
                else
                    SpeechManager.Speak("No run option focused. Up and down to browse.");
                break;
        }
    }

    /// <summary>Shared "the party can't be edited" guard. Run-option rows are still usable.</summary>
    private static bool SpeakBlockedAction()
    {
        if (_region == Region.RunOptions)
            return false;
        if (_firstGameAuto)
        {
            SpeechManager.Speak("First adventure: the party is assigned automatically.");
            return true;
        }
        var gm = GameManager.Instance;
        if (gm != null && gm.IsWeeklyChallenge())
        {
            SpeechManager.Speak("The party is fixed this week.");
            return true;
        }
        if (gm != null && gm.IsLoadingGame())
        {
            SpeechManager.Speak("Loading a saved game — the party can't be changed.");
            return true;
        }
        return false;
    }

    private static void ActivateRosterHero()
    {
        var hsm = Mgr;
        BuildRoster(hsm);
        if (_rosterIndex < 0 || _rosterIndex >= _roster.Count)
        {
            SpeechManager.Speak("No hero focused. Up and down to browse.");
            return;
        }
        var hs = _roster[_rosterIndex];
        if (hs.blocked || hs.DlcBlocked)
        {
            SpeechManager.Speak(HeroSpeech.LockReason(hs));
            return;
        }
        if (hs.HeroPicked)
        {
            SpeechManager.Speak(HeroSpeech.ScdOf(hs).CharacterName + " is already in the party — "
                + HeroSpeech.PickedLine(hs) + ". Clear the slot first to move them.");
            return;
        }
        int slot = FirstEmptyOwnSlot(hsm);
        if (slot < 0)
        {
            // In MP only the player's own box can be replaced — 1-4 would refuse on the rest.
            SpeechManager.Speak(MpSpeech.IsMp
                ? "Your slot is already filled — press its number to replace that hero, or clear it first."
                : "Party is full — press 1 through 4 to replace a slot.");
            return;
        }
        AssignHero(hsm, hs, slot);
    }

    public static void HandleNumber(int n)
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        int boxIndex = n - 1;
        if (_region == Region.Roster)
        {
            if (SpeakBlockedAction())
                return;
            BuildRoster(hsm);
            if (_rosterIndex < 0 || _rosterIndex >= _roster.Count)
            {
                SpeechManager.Speak("No hero focused. Up and down to browse.");
                return;
            }
            var hs = _roster[_rosterIndex];
            if (hs.blocked || hs.DlcBlocked)
            {
                SpeechManager.Speak(HeroSpeech.LockReason(hs));
                return;
            }
            if (hs.HeroPicked)
            {
                SpeechManager.Speak(HeroSpeech.ScdOf(hs).CharacterName + " is already in the party — "
                    + HeroSpeech.PickedLine(hs) + ". Clear the slot first to move them.");
                return;
            }
            if (!SlotUsable(hsm, boxIndex, out string refusal))
            {
                SpeechManager.Speak(refusal);
                return;
            }
            AssignHero(hsm, hs, boxIndex);
            return;
        }
        // In the party and run-options regions, digits jump to the slot.
        BuildActiveBoxes(hsm);
        int pos = _activeBoxes.IndexOf(boxIndex);
        if (pos < 0)
        {
            SpeechManager.Speak("Slot " + n + " is not available.");
            return;
        }
        _region = Region.Party;
        _partyIndex = pos;
        HeroSpeech.PlayButtonFocusSound();
        SpeechManager.Speak(SlotLine(hsm, boxIndex));
    }

    private static int FirstEmptyOwnSlot(HeroSelectionManager hsm)
    {
        for (int i = 0; i < 4; i++)
        {
            if (SlotUsable(hsm, i, out _) && !hsm.IsBoxFilled(hsm.boxGO[i]))
                return i;
        }
        return -1;
    }

    private static bool SlotUsable(HeroSelectionManager hsm, int boxIndex, out string refusal)
    {
        refusal = "";
        if (boxIndex < 0 || hsm.boxGO == null || boxIndex >= hsm.boxGO.Length
            || hsm.boxGO[boxIndex] == null)
        {
            refusal = "Slot " + (boxIndex + 1) + " does not exist.";
            return false;
        }
        if (!hsm.boxGO[boxIndex].activeSelf)
        {
            refusal = "Slot " + (boxIndex + 1) + " is disabled.";
            return false;
        }
        if (GameManager.Instance != null && GameManager.Instance.IsMultiplayer()
            && !hsm.IsYourBox(hsm.boxGO[boxIndex].name))
        {
            string owner = hsm.GetBoxOwnerFromIndex(boxIndex);
            // Lowercase fallback: the name sits mid-sentence.
            string nick = MpSpeech.DisplayNick(owner, "another player");
            refusal = "Slot " + (boxIndex + 1) + " is controlled by " + nick + ".";
            return false;
        }
        return true;
    }

    /// <summary>Assigns a roster hero to a specific box with the same call sequence the game's
    /// random-dice uses (PickHero with the random flag ignores the stale mouse-over box).</summary>
    private static void AssignHero(HeroSelectionManager hsm, HeroSelection hs, int slot)
    {
        var displaced = hsm.GetBoxHeroFromIndex(slot);
        hs.PickHero(_comingFromRandom: true);
        hs.PickStop(slot);
        hs.HeroPicked = true;
        // spriteSR is internal to the game assembly — same fix-up SetRandomHero applies.
        var spriteSR = Traverse.Create(hs).Field<SpriteRenderer>("spriteSR").Value;
        if (spriteSR != null)
            spriteSR.enabled = true;
        if (hsm.GetBoxHeroFromIndex(slot) != hs)
        {
            SpeechManager.Speak("Could not assign " + HeroSpeech.ScdOf(hs).CharacterName
                + " to slot " + (slot + 1) + ".");
            return;
        }
        _rosterFocusId = hs.Id;
        var sb = new StringBuilder();
        sb.Append(HeroSpeech.ScdOf(hs).CharacterName).Append(" assigned to slot ").Append(slot + 1);
        if (displaced != null && displaced != hs && HeroSpeech.ScdOf(displaced) != null)
        {
            sb.Append(", replacing ").Append(HeroSpeech.ScdOf(displaced).CharacterName)
              .Append(" — they returned to the roster.");
        }
        else
        {
            int remaining = 0;
            for (int i = 0; i < 4; i++)
            {
                if (hsm.boxGO != null && i < hsm.boxGO.Length && hsm.boxGO[i] != null
                    && hsm.boxGO[i].activeSelf && !hsm.IsBoxFilled(hsm.boxGO[i]))
                    remaining++;
            }
            sb.Append('.');
            if (remaining > 0)
                sb.Append(' ').Append(remaining)
                  .Append(remaining == 1 ? " slot remaining." : " slots remaining.");
        }
        SpeechManager.Speak(sb.ToString());
    }

    private static void ActivatePartySlot()
    {
        var hsm = Mgr;
        BuildActiveBoxes(hsm);
        if (_partyIndex < 0 || _partyIndex >= _activeBoxes.Count)
        {
            SpeechManager.Speak("No slot focused. Up and down to browse the party.");
            return;
        }
        int boxIndex = _activeBoxes[_partyIndex];
        if (!SlotUsable(hsm, boxIndex, out string refusal))
        {
            SpeechManager.Speak(refusal);
            return;
        }
        var hero = hsm.GetBoxHeroFromIndex(boxIndex);
        if (hero != null)
        {
            string name = HeroSpeech.ScdOf(hero) != null ? HeroSpeech.ScdOf(hero).CharacterName : "The hero";
            hsm.ClearBox(boxIndex);
            SpeechManager.Speak(name + " returned to the roster. Slot " + (boxIndex + 1)
                + " is empty.");
            return;
        }
        // Empty slot: roll the random-hero dice (single-player only, matching the game).
        if (MpSpeech.IsMp)
        {
            SpeechManager.Speak("Random heroes aren't available in multiplayer.");
            return;
        }
        hsm.SetRandomHero(boxIndex);
        var landed = hsm.GetBoxHeroFromIndex(boxIndex);
        if (landed != null && HeroSpeech.ScdOf(landed) != null)
            SpeechManager.Speak("Random hero: " + HeroSpeech.ScdOf(landed).CharacterName
                + " assigned to slot " + (boxIndex + 1) + ".");
        else
            SpeechManager.Speak("No heroes available for random selection.");
    }

    // ------------------------------------------------------------------ review keys

    /// <summary>Alt+T: the focused hero's full sheet.</summary>
    public static void SpeakHeroSheet()
    {
        var hs = FocusedHero();
        if (hs == null)
        {
            SpeechManager.Speak(_region == Region.Party
                ? "The slot is empty." : "No hero focused.");
            return;
        }
        SpeechManager.Speak(HeroSpeech.HeroSheet(HeroSpeech.ScdOf(hs)));
    }

    /// <summary>Alt+C: open the character window for the focused hero.</summary>
    public static void OpenCharacterWindow()
    {
        var hsm = Mgr;
        if (hsm == null || hsm.charPopup == null)
            return;
        if (_firstGameAuto)
        {
            SpeechManager.Speak("First adventure: party assigned automatically, starting the run.");
            return;
        }
        var hs = FocusedHero();
        if (hs == null || HeroSpeech.ScdOf(hs) == null)
        {
            SpeechManager.Speak(_region == Region.Party
                ? "The slot is empty." : "No hero focused.");
            return;
        }
        if (!hs.blocked && !hs.DlcBlocked && !GameManager.Instance.IsWeeklyChallenge())
        {
            hs.RightClick(); // the game's own open path; also resets its controller state
        }
        else
        {
            // The game blocks right-click on locked portraits, but the window renders fine.
            hsm.charPopup.Init(HeroSpeech.ScdOf(hs));
            hsm.charPopup.Show();
        }
        // The CharPopup.Show postfix announces the window.
    }

    private static HeroSelection FocusedHero()
    {
        var hsm = Mgr;
        if (hsm == null)
            return null;
        if (_region == Region.Party)
        {
            BuildActiveBoxes(hsm);
            if (_partyIndex >= 0 && _partyIndex < _activeBoxes.Count)
                return hsm.GetBoxHeroFromIndex(_activeBoxes[_partyIndex]);
            return null;
        }
        BuildRoster(hsm);
        if (_rosterIndex >= 0 && _rosterIndex < _roster.Count)
            return _roster[_rosterIndex];
        return null;
    }

    // ------------------------------------------------------------------ patch entry points

    /// <summary>A remote player's hero pick landed (RPC echo, remote clients only).</summary>
    public static void OnRemoteHeroAssigned(string heroId, int boxId)
    {
        var hsm = Mgr;
        if (hsm == null || !_arrivalAnnounced)
            return;
        string name = heroId;
        if (hsm.heroSelectionDictionary != null
            && hsm.heroSelectionDictionary.TryGetValue(heroId, out var hs)
            && hs != null && HeroSpeech.ScdOf(hs) != null)
            name = HeroSpeech.ScdOf(hs).CharacterName;
        string owner = hsm.GetBoxOwnerFromIndex(boxId);
        SpeechManager.SpeakQueued(MpSpeech.DisplayNick(owner)
            + " chose " + name + " for slot " + (boxId + 1) + ".");
    }

    public static void OnRemoteBoxCleared(string heroName, int boxId)
    {
        if (Mgr == null || !_arrivalAnnounced || string.IsNullOrEmpty(heroName))
            return;
        SpeechManager.SpeakQueued(heroName + " removed from slot " + (boxId + 1) + ".");
    }

    public static void OnPlayerJoinedBox(string playerNick, int boxId)
    {
        if (Mgr == null || !_arrivalAnnounced)
            return;
        if (!MpSpeech.IsMp)
            return;
        SpeechManager.SpeakQueued(MpSpeech.DisplayNick(playerNick)
            + " joined, slot " + (boxId + 1) + ".");
    }

    public static void OnHostMadnessChanged(int level)
    {
        if (Mgr == null || !_arrivalAnnounced)
            return;
        SpeechManager.SpeakQueued("Host set madness level " + level + ".");
    }

    public static void OnHostSeedChanged(string seed)
    {
        if (Mgr == null || !_arrivalAnnounced)
            return;
        SpeechManager.SpeakQueued("Host changed the seed to " + HeroSpeech.SpellSeed(seed) + ".");
    }

    public static void OnSeedSet(string seed)
    {
        if (Mgr == null || !_arrivalAnnounced)
            return;
        SpeechManager.SpeakQueued("Seed set to " + HeroSpeech.SpellSeed(seed) + ".");
    }

    public static void OnBeginAdventure()
    {
        var hsm = Mgr;
        if (hsm == null)
            return;
        // The MP load-content failure path re-shows the button — no departure then.
        if (hsm.botonBegin != null && hsm.botonBegin.gameObject.activeSelf)
            return;
        if (GameManager.Instance == null || !GameManager.Instance.IsMainPlayer())
            return;
        if (_firstGameAuto)
            return; // the auto-start line already covered it
        SpeechManager.Speak("Beginning adventure.");
    }
}

// ======================= announcement patches =======================

/// <summary>A player was assigned to a party box (multiplayer lobby wiring).</summary>
[HarmonyPatch(typeof(HeroSelectionManager), nameof(HeroSelectionManager.AssignPlayerToBox))]
internal static class HeroSelectionPlayerJoinedAnnouncePatch
{
    static void Postfix(string playerNick, int boxId)
        => HeroSelectionScreenManager.OnPlayerJoinedBox(playerNick, boxId);
}

/// <summary>A remote player's hero pick (RPC target Others — never fires for local picks).</summary>
[HarmonyPatch(typeof(HeroSelectionManager), "NET_AssignHeroToBox")]
internal static class HeroSelectionRemoteAssignAnnouncePatch
{
    static void Postfix(string _hero, int _boxId)
        => HeroSelectionScreenManager.OnRemoteHeroAssigned(_hero, _boxId);
}

/// <summary>A remote player cleared a box. The prefix snapshots the occupant's name — the
/// original returns the hero to the roster before the postfix runs.</summary>
[HarmonyPatch(typeof(HeroSelectionManager), "NET_ClearBox")]
internal static class HeroSelectionRemoteClearAnnouncePatch
{
    static void Prefix(int _boxId, out string __state)
    {
        var hsm = HeroSelectionManager.Instance;
        var hero = hsm != null ? hsm.GetBoxHeroFromIndex(_boxId) : null;
        __state = hero != null && HeroSpeech.ScdOf(hero) != null
            ? HeroSpeech.ScdOf(hero).CharacterName : null;
    }

    static void Postfix(int _boxId, string __state)
        => HeroSelectionScreenManager.OnRemoteBoxCleared(__state, _boxId);
}

/// <summary>The host changed the NG+/madness level (adventure variant).</summary>
[HarmonyPatch(typeof(HeroSelectionManager), "NET_SetMadness")]
internal static class HeroSelectionHostMadnessAnnouncePatch
{
    static void Postfix(int mLevel) => HeroSelectionScreenManager.OnHostMadnessChanged(mLevel);
}

[HarmonyPatch(typeof(HeroSelectionManager), "NET_SetObeliskMadness")]
internal static class HeroSelectionHostObeliskMadnessAnnouncePatch
{
    static void Postfix(int mLevel) => HeroSelectionScreenManager.OnHostMadnessChanged(mLevel);
}

[HarmonyPatch(typeof(HeroSelectionManager), "NET_SetSingularityMadness")]
internal static class HeroSelectionHostSingularityMadnessAnnouncePatch
{
    static void Postfix(int mLevel) => HeroSelectionScreenManager.OnHostMadnessChanged(mLevel);
}

/// <summary>The host pushed a new seed to this client.</summary>
[HarmonyPatch(typeof(HeroSelectionManager), "NET_SetSeed")]
internal static class HeroSelectionHostSeedAnnouncePatch
{
    static void Postfix(string _seed) => HeroSelectionScreenManager.OnHostSeedChanged(_seed);
}

/// <summary>The local seed changed (covers the accept path of the seed input alert).</summary>
[HarmonyPatch(typeof(HeroSelectionManager), "SetSeed")]
internal static class HeroSelectionSeedSetAnnouncePatch
{
    static void Postfix(string _seed) => HeroSelectionScreenManager.OnSeedSet(_seed);
}

/// <summary>The run is starting.</summary>
[HarmonyPatch(typeof(HeroSelectionManager), nameof(HeroSelectionManager.BeginAdventure))]
internal static class HeroSelectionBeginAnnouncePatch
{
    static void Postfix() => HeroSelectionScreenManager.OnBeginAdventure();
}
