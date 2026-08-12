using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Multiplayer lobby scene (LobbyManager). The scene has no panel enum — the open panel is derived
/// live from which root transform is active (Room &gt; Create &gt; Join &gt; region select), and rows are
/// rebuilt from live UI state on every read, alert-dialogue style. The lobby's buttons are
/// inspector-wired Canvas UI (the fields are bare Transforms), so every action calls the public
/// LobbyManager method the button would have called — never a simulated click.
///
/// Flow notes that shape the rows: the Create panel is the *room* setup for a game configured on
/// the main menu ("Set up a new game" leaves the scene via CreateMultiplayerGame and comes back
/// with a save slot, which is when JustConnectedToPhoton routes to ShowCreate); join-by-code,
/// room passwords, exit-room and version-mismatch all run through game AlertManager dialogs that
/// the global alert context already owns; kick has NO game confirm, so it gets a two-step Enter
/// here. Text entry (room name / password) uses <see cref="TmpEditSession"/>.
/// </summary>
internal static class LobbyScreenManager
{
    private enum Panel { None, Connecting, Region, Create, Join, Room }
    private enum JoinRegion { Rooms, Controls }

    private class Row
    {
        public string Speech;
        public Action Activate;
        public TmpEditSession Edit;        // Enter starts editing instead of activating
        public TMP_Dropdown Dropdown;      // Enter opens the spoken option walk
        public Action DropdownApply;       // runs after the chosen value is written back
        public int KickSlot = -1;          // two-step Enter kick target
        public string RoomName;            // set on room-browser rows, for identity re-validation
    }

    private static Panel _panel = Panel.None;
    private static int _index;
    private static string _focusedRoom;   // name of the focused room-browser row, if any
    private static JoinRegion _joinRegion = JoinRegion.Rooms;
    private static bool _sceneAnnounced;
    private static bool _panelAnnounced;
    private static int _panelWaitFrames;

    // Dropdown sub-mode (spoken option walk — the visual dropdown is never opened).
    private static TMP_Dropdown _ddOpen;
    private static Action _ddApply;
    private static int _ddPending;

    // Two-step kick.
    private static int _kickArmedSlot = -1;
    private static float _kickArmedTime;
    private static string _kickArmedNick; // occupant at arm time — rendered order re-packs on any change

    // Edge trackers.
    private static string _lastStatusSpoken = "";
    private static int _lastRoomCount = -1;
    private static int _roomCountPollFrames;
    private static bool _launchWasActive;
    private static int _lastOccupants = -1;

    private static readonly TmpEditSession _editName = new TmpEditSession(
        () => LobbyManager.Instance != null ? LobbyManager.Instance.UICreateName : null);
    private static readonly TmpEditSession _editPwd = new TmpEditSession(
        () => LobbyManager.Instance != null ? LobbyManager.Instance.UICreatePwd : null);

    static LobbyScreenManager()
    {
        _editName.OnEnded += t => SpeechManager.Speak(t.Trim().Length > 0
            ? "Room name set to " + AccessibleMenuBase.StripRichText(t) + "."
            : "Room name left empty.");
        _editPwd.OnEnded += t => SpeechManager.Speak(t.Trim().Length > 0
            ? "Password set." : "Password left empty.");
    }

    /// <summary>Scene presence is the whole activity test — the Lobby scene is MP-only.</summary>
    public static bool Active => LobbyManager.Instance != null;

    public static bool AnyEditing => _editName.Editing || _editPwd.Editing;
    private static bool AnyEndedThisFrame => _editName.EndedThisFrame || _editPwd.EndedThisFrame;

    // ---------------------------------------------------------------- lifecycle

    public static void Tick()
    {
        var lm = LobbyManager.Instance;
        if (lm == null)
        {
            if (_panel != Panel.None)
                ResetAll();
            return;
        }

        _editName.Tick();
        _editPwd.Tick();

        if (!_sceneAnnounced)
        {
            _sceneAnnounced = true;
            SpeechManager.Speak("Multiplayer lobby.");
            // Take the create-panel TMP fields out of the UI navigation graph (same hardening
            // as the chat input): selection-based navigation — the game's Tab NextField walk or
            // Unity arrow-key nav — activates a selected TMP field outside any edit session,
            // silently eating keystrokes while Enter dual-fires field submit + row activation.
            // Mouse clicks and the mod's own edit sessions are unaffected.
            if (lm.UICreateName != null)
                lm.UICreateName.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            if (lm.UICreatePwd != null)
                lm.UICreatePwd.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        }

        // Panel edge: reset focus, then announce the arrival overview (the Room panel waits for
        // its slot rows, which fill from the network a few frames after ShowRoom).
        Panel now = CurrentPanel(lm);
        if (now != _panel)
        {
            _panel = now;
            _index = 0;
            _focusedRoom = null;
            _joinRegion = JoinRegion.Rooms;
            _ddOpen = null;
            _kickArmedSlot = -1;
            // A panel switch mid-edit (mouse Back, auto re-route) must not let the session's
            // Tick later fire OnEnded and speak "Room name set to…" over the new panel.
            _editName.Abort();
            _editPwd.Abort();
            _panelAnnounced = false;
            _panelWaitFrames = 0;
            _launchWasActive = false;
            _lastOccupants = -1;
            _lastRoomCount = -1;
        }
        if (!_panelAnnounced && _panel != Panel.None)
        {
            _panelWaitFrames++;
            bool ready = _panel != Panel.Room
                || AnySlotActive(lm)
                || _panelWaitFrames > 90;
            if (ready && _panelWaitFrames >= 2)
            {
                _panelAnnounced = true;
                SpeechManager.Speak(PanelOverview(lm));
            }
        }

        if (_panel == Panel.Join && ++_roomCountPollFrames >= 30)
        {
            _roomCountPollFrames = 0;
            int count = RoomRows(lm).Count;
            if (_lastRoomCount >= 0 && count != _lastRoomCount)
                SpeechManager.SpeakQueued(count == 0
                    ? "Room list updated: no rooms."
                    : "Room list updated: " + count + (count == 1 ? " room." : " rooms."));
            _lastRoomCount = count;
        }

        if (_panel == Panel.Room && _panelAnnounced)
        {
            bool launch = lm.buttonLaunch != null && lm.buttonLaunch.gameObject.activeSelf;
            if (launch && !_launchWasActive)
                SpeechManager.SpeakQueued("Launch available — " + Occupants() + " players in room.");
            _launchWasActive = launch;

            int occ = Occupants();
            if (_lastOccupants >= 0 && occ != _lastOccupants)
                SpeechManager.SpeakQueued("Now " + occ + " of " + MaxPlayers() + " players.");
            _lastOccupants = occ;
        }

        // A two-step kick arm quietly expires.
        if (_kickArmedSlot >= 0 && Time.unscaledTime - _kickArmedTime > 10f)
            _kickArmedSlot = -1;
    }

    private static void ResetAll()
    {
        _panel = Panel.None;
        _index = 0;
        _focusedRoom = null;
        _sceneAnnounced = false;
        _panelAnnounced = false;
        _ddOpen = null;
        _kickArmedSlot = -1;
        _lastStatusSpoken = "";
        _lastRoomCount = -1;
        _lastOccupants = -1;
        _launchWasActive = false;
        _editName.Abort();
        _editPwd.Abort();
    }

    private static Panel CurrentPanel(LobbyManager lm)
    {
        if (lm.RoomT != null && lm.RoomT.gameObject.activeSelf)
            return Panel.Room;
        if (lm.CreateRoomT != null && lm.CreateRoomT.gameObject.activeSelf)
            return Panel.Create;
        if (lm.JoinRoomT != null && lm.JoinRoomT.gameObject.activeSelf)
            return Panel.Join;
        if (lm.regions != null && lm.regions.gameObject.activeSelf)
            return Panel.Region;
        return Panel.Connecting;
    }

    // ---------------------------------------------------------------- rows

    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();
        var lm = LobbyManager.Instance;
        if (lm == null)
            return rows;

        switch (CurrentPanel(lm))
        {
            case Panel.Region: AddRegionRows(rows, lm); break;
            case Panel.Connecting: AddConnectingRow(rows, lm); break;
            case Panel.Join: AddJoinRows(rows, lm); break;
            case Panel.Create: AddCreateRows(rows, lm); break;
            case Panel.Room: AddRoomRows(rows, lm); break;
        }
        return rows;
    }

    private static void AddRegionRows(List<Row> rows, LobbyManager lm)
    {
        AddCrossplayRow(rows, lm);
        AddQuickRegion(rows, lm, "Europe", "eu");
        AddQuickRegion(rows, lm, "United States", "us");
        AddQuickRegion(rows, lm, "Asia", "asia");
        rows.Add(new Row
        {
            Speech = "All regions: " + DropdownOption(lm.dropRegion) + ". Press Enter to choose",
            Dropdown = lm.dropRegion,
            DropdownApply = () =>
            {
                SpeechManager.Speak("Connecting to " + DropdownOption(lm.dropRegion) + ".");
                lm.SelectRegion();
            },
        });
    }

    private static void AddConnectingRow(List<Row> rows, LobbyManager lm)
    {
        rows.Add(new Row
        {
            Speech = StatusText(lm).Length > 0 ? StatusText(lm) : "Connecting",
            Activate = () => SpeechManager.Speak(StatusText(lm).Length > 0 ? StatusText(lm) : "Connecting"),
        });
    }

    private static void AddJoinRows(List<Row> rows, LobbyManager lm)
    {
        if (_joinRegion == JoinRegion.Rooms)
        {
            var roomRows = RoomRows(lm);
            if (roomRows.Count == 0)
            {
                rows.Add(new Row
                {
                    Speech = "No rooms found. The list refreshes automatically",
                    Activate = () => SpeechManager.Speak("No rooms found. The list refreshes automatically."),
                });
            }
            foreach (var rl in roomRows)
            {
                var captured = rl;
                rows.Add(new Row
                {
                    Speech = RoomRowSpeech(captured),
                    RoomName = RoomRowName(captured),
                    Activate = () =>
                    {
                        SpeechManager.Speak("Joining " + RoomRowName(captured) + ".");
                        captured.JoinRoom(); // password prompt = game AlertInput, alert context owns it
                    },
                });
            }
        }
        else
        {
            rows.Add(new Row
            {
                Speech = "Join by room code, button",
                Activate = () => lm.JoinRoomById(), // game AlertInput
            });
            rows.Add(new Row
            {
                Speech = "Set up a new game, button — opens the game setup on the main menu",
                Activate = () =>
                {
                    SpeechManager.Speak("Opening game setup on the main menu.");
                    lm.CreateMultiplayerGame();
                },
            });
            if (lm.regionsDisconnect != null && lm.regionsDisconnect.gameObject.activeSelf)
                rows.Add(new Row
                {
                    Speech = "Disconnect from region, button",
                    Activate = () =>
                    {
                        SpeechManager.Speak("Disconnecting.");
                        lm.DisconnectRegion(_fromButton: true);
                    },
                });
        }
    }

    private static void AddCreateRows(List<Row> rows, LobbyManager lm)
    {
        rows.Add(new Row
        {
            Speech = "Room name: " + FieldValue(lm.UICreateName, "empty") + ". Press Enter to type",
            Edit = _editName,
        });
        rows.Add(new Row
        {
            Speech = "Players: " + DropdownOption(lm.UICreatePlayers) + ". Press Enter to choose",
            Dropdown = lm.UICreatePlayers,
            DropdownApply = () => SpeechManager.Speak("Players: " + DropdownOption(lm.UICreatePlayers) + "."),
        });
        rows.Add(ToggleRow(lm.UITogglePwd, "Password protection",
            ", the password field appears below when on"));
        if (lm.UITogglePwd != null && lm.UITogglePwd.isOn)
            rows.Add(new Row
            {
                Speech = "Password: " + FieldValue(lm.UICreatePwd, "empty")
                    + " — will be uppercased. Press Enter to type",
                Edit = _editPwd,
            });
        rows.Add(ToggleRow(lm.UIToggleLfm, "Looking for more players", ""));
        rows.Add(new Row
        {
            Speech = "Create room, button",
            Activate = () =>
            {
                string name = lm.UICreateName != null ? lm.UICreateName.text.Trim() : "";
                if (name.Length == 0)
                {
                    SpeechManager.Speak("Room name is empty — nothing created.");
                    return;
                }
                SpeechManager.Speak("Creating room " + AccessibleMenuBase.StripRichText(name) + ".");
                lm.CreateRoom();
            },
        });
        rows.Add(new Row
        {
            Speech = "Back to the room list, button",
            Activate = () => lm.GoBack(),
        });
    }

    private static void AddRoomRows(List<Row> rows, LobbyManager lm)
    {
        rows.Add(new Row { Speech = RoomHeader(), Activate = () => SpeechManager.Speak(RoomHeader()) });
        if (lm.roomWaiting != null)
        {
            string status = CardSpeech.CleanFlat(lm.roomWaiting.text);
            if (status.Length > 0)
                rows.Add(new Row { Speech = status, Activate = () => SpeechManager.Speak(status) });
        }
        if (lm.roomSlots != null)
            for (int i = 0; i < lm.roomSlots.Length; i++)
            {
                var slot = lm.roomSlots[i];
                if (slot == null || !slot.gameObject.activeSelf)
                    continue;
                int idx = i;
                bool kickable = lm.roomSlotsKick != null && idx < lm.roomSlotsKick.Length
                    && lm.roomSlotsKick[idx] != null && lm.roomSlotsKick[idx].gameObject.activeSelf;
                rows.Add(new Row
                {
                    Speech = SlotSpeech(lm, idx, kickable),
                    KickSlot = kickable ? idx : -1,
                    Activate = kickable ? (Action)null
                        : () => SpeechManager.Speak(SlotSpeech(lm, idx, kickable: false)),
                });
            }
        if (lm.buttonSteam != null && lm.buttonSteam.gameObject.activeSelf)
            rows.Add(new Row
            {
                Speech = "Invite a Steam friend, button",
                Activate = () =>
                {
                    SpeechManager.Speak("Opening the Steam invite overlay.");
                    lm.InviteSteam();
                },
            });
        rows.Add(LaunchRow(lm));
        rows.Add(new Row
        {
            Speech = "Exit room, button",
            Activate = () => lm.ExitRoom(), // game confirm alert
        });
    }

    private static void AddCrossplayRow(List<Row> rows, LobbyManager lm)
    {
        var toggle = lm.UICrossPlay;
        if (toggle == null)
            return;
        bool locked = !toggle.interactable;
        rows.Add(new Row
        {
            Speech = "Crossplay, " + (toggle.isOn ? "on" : "off")
                + (locked ? ", locked — Paradox login required" : ", toggle"),
            Activate = () =>
            {
                if (locked)
                {
                    SpeechManager.Speak("Crossplay is locked — Paradox login required.");
                    return;
                }
                toggle.isOn = !toggle.isOn; // onValueChanged is inspector-wired to SetCrossPlayEnabled
                SpeechManager.Speak("Crossplay " + (toggle.isOn ? "on." : "off."));
            },
        });
    }

    private static void AddQuickRegion(List<Row> rows, LobbyManager lm, string label, string code)
    {
        rows.Add(new Row
        {
            Speech = label + ", quick connect button",
            Activate = () =>
            {
                SpeechManager.Speak("Connecting to " + label + ".");
                lm.SetRegion(code);
            },
        });
    }

    private static Row ToggleRow(UnityEngine.UI.Toggle toggle, string label, string hint)
    {
        return new Row
        {
            Speech = label + ", " + (toggle != null && toggle.isOn ? "on" : "off") + ", toggle" + hint,
            Activate = () =>
            {
                if (toggle == null)
                    return;
                toggle.isOn = !toggle.isOn;
                SpeechManager.Speak(label + " " + (toggle.isOn ? "on." : "off."));
            },
        };
    }

    private static Row LaunchRow(LobbyManager lm)
    {
        bool isMaster = NetworkManager.Instance != null && NetworkManager.Instance.IsMaster();
        bool launchable = lm.buttonLaunch != null && lm.buttonLaunch.gameObject.activeSelf;
        if (launchable)
            return new Row
            {
                Speech = "Launch adventure, button",
                Activate = () =>
                {
                    SpeechManager.Speak("Launching adventure.");
                    lm.LaunchGame(); // version mismatch = game alert
                },
            };
        string speech = isMaster
            ? "Launch adventure, unavailable — need at least 2 players"
            : "Waiting for " + MpSpeech.HostNick() + " to launch";
        return new Row { Speech = speech, Activate = () => SpeechManager.Speak(speech + ".") };
    }

    // ---------------------------------------------------------------- input (from the context)

    public static void Move(int dir)
    {
        if (AnyEditing)
            return; // arrows belong to the text caret
        if (_ddOpen != null)
        {
            PreviewDropdown(dir);
            return;
        }
        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Mathf.Clamp(_index, 0, rows.Count - 1);
        _index = ((_index + dir) % rows.Count + rows.Count) % rows.Count;
        _kickArmedSlot = -1;
        _focusedRoom = rows[_index].RoomName;
        SpeechManager.Speak(rows[_index].Speech + ", " + (_index + 1) + " of " + rows.Count);
    }

    public static void Confirm()
    {
        if (AnyEditing)
        {
            if (_editName.Editing) _editName.EndEdit();
            if (_editPwd.Editing) _editPwd.EndEdit();
            return;
        }
        if (AnyEndedThisFrame)
            return; // TMP consumed this Enter to end the edit — don't also activate a row
        if (_ddOpen != null)
        {
            ApplyDropdown();
            return;
        }
        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Mathf.Clamp(_index, 0, rows.Count - 1);

        // The room browser's rows are live GridTransform children and the game DESTROYS a RoomList
        // the moment its room fills or closes (RoomReceived, ~1-2s cadence). A raw index would then
        // join whatever slid into this position — a full, wrong-version or passworded room the user
        // never heard. Re-find the announced room by name, the way the town hub anchors on
        // _focusedKey and the kick path re-checks the occupant nick.
        if (_focusedRoom != null)
        {
            int again = rows.FindIndex(r => r.RoomName == _focusedRoom);
            if (again < 0)
            {
                _focusedRoom = rows[_index].RoomName;
                SpeechManager.Speak("That room is gone. Now on: " + rows[_index].Speech + ".");
                return;
            }
            _index = again;
        }

        var row = rows[_index];

        if (row.Edit != null)
        {
            row.Edit.BeginEdit("Editing");
            return;
        }
        if (row.Dropdown != null)
        {
            OpenDropdown(row);
            return;
        }
        if (row.KickSlot >= 0)
        {
            DoKickStep(row.KickSlot);
            return;
        }
        row.Activate?.Invoke();
    }

    /// <summary>Escape. Returns true when consumed.</summary>
    public static bool Cancel()
    {
        if (AnyEditing)
        {
            if (_editName.Editing) _editName.EndEdit();
            if (_editPwd.Editing) _editPwd.EndEdit();
            return true;
        }
        if (AnyEndedThisFrame)
            return true;
        if (_ddOpen != null)
        {
            _ddOpen = null;
            SpeechManager.Speak("Cancelled.");
            return true;
        }
        if (_kickArmedSlot >= 0)
        {
            _kickArmedSlot = -1;
            SpeechManager.Speak("Kick cancelled.");
            return true;
        }
        var lm = LobbyManager.Instance;
        if (lm == null)
            return false;
        switch (CurrentPanel(lm))
        {
            case Panel.Create:
                lm.GoBack();
                return true;
            case Panel.Room:
                lm.ExitRoom(); // game confirm alert
                return true;
            default:
                return false; // leave the game's own Escape default in place
        }
    }

    public static void Tab()
    {
        if (AnyEditing || _ddOpen != null)
            return;
        var lm = LobbyManager.Instance;
        if (lm != null && CurrentPanel(lm) == Panel.Join)
        {
            _joinRegion = _joinRegion == JoinRegion.Rooms ? JoinRegion.Controls : JoinRegion.Rooms;
            _index = 0;
            _focusedRoom = null;
            var rows = BuildRows();
            SpeechManager.Speak((_joinRegion == JoinRegion.Rooms
                ? "Rooms, " + rows.Count + (rows.Count == 1 ? " row. " : " rows. ")
                : "Controls. ")
                + (rows.Count > 0 ? rows[0].Speech : ""));
            return;
        }
        SpeakOverview();
    }

    public static void SpeakOverview()
    {
        var lm = LobbyManager.Instance;
        if (lm != null)
            SpeechManager.Speak(PanelOverview(lm));
    }

    // ---------------------------------------------------------------- dropdown sub-mode

    private static void OpenDropdown(Row row)
    {
        if (row.Dropdown == null || row.Dropdown.options.Count == 0)
            return;
        _ddOpen = row.Dropdown;
        _ddApply = row.DropdownApply;
        _ddPending = row.Dropdown.value;
        SpeechManager.Speak(DropdownOptionAt(_ddOpen, _ddPending) + ", "
            + (_ddPending + 1) + " of " + _ddOpen.options.Count
            + ". Up and Down to change, Enter to select, Escape to cancel.");
    }

    private static void PreviewDropdown(int dir)
    {
        int count = _ddOpen.options.Count;
        _ddPending = ((_ddPending + dir) % count + count) % count;
        SpeechManager.Speak(DropdownOptionAt(_ddOpen, _ddPending) + ", " + (_ddPending + 1) + " of " + count);
    }

    private static void ApplyDropdown()
    {
        var dd = _ddOpen;
        var apply = _ddApply;
        _ddOpen = null;
        _ddApply = null;
        if (dd == null)
            return;
        dd.SetValueWithoutNotify(_ddPending); // apply() below is the sole action — never onValueChanged
        dd.RefreshShownValue();
        apply?.Invoke();
    }

    // ---------------------------------------------------------------- kick (two-step)

    /// <summary>The nick actually rendered on slot <paramref name="idx"/> — and the one the
    /// game's KickPlayer(idx) will hit. DrawLobbyNames and KickPlayer both walk the non-inactive
    /// Photon player list by index; NetworkManager's PlayerPositionList is a DIFFERENT ordering
    /// (leaves blank a hole, joins fill the first hole), so after a leave-then-join the two
    /// disagree and position-based nicks would name the wrong player.</summary>
    private static string NickAtRenderedSlot(int idx)
    {
        var list = Photon.Pun.PhotonNetwork.InRoom
            ? Photon.Pun.PhotonNetwork.PlayerList
            : NetworkManager.Instance?.PlayerList;
        if (list == null)
            return "";
        int n = 0;
        foreach (var p in list)
        {
            if (p == null || p.IsInactive)
                continue;
            if (n == idx)
                return p.NickName ?? "";
            n++;
        }
        return "";
    }

    private static void DoKickStep(int slot)
    {
        var lm = LobbyManager.Instance;
        if (lm == null || NetworkManager.Instance == null)
            return;
        string rawNick = NickAtRenderedSlot(slot);
        string nick = MpSpeech.DisplayNick(rawNick);
        if (_kickArmedSlot != slot)
        {
            // Kick has no game confirm dialog — require a deliberate second Enter.
            _kickArmedSlot = slot;
            _kickArmedNick = rawNick;
            _kickArmedTime = Time.unscaledTime;
            SpeechManager.Speak("Press Enter again to kick " + nick + ".");
            return;
        }
        // Slots do NOT compact in the position list, but the rendered order re-packs on every
        // change: if the players changed, this slot index may hold someone else — a stale
        // second Enter must not kick them.
        if (rawNick != _kickArmedNick)
        {
            _kickArmedNick = rawNick;
            _kickArmedTime = Time.unscaledTime;
            SpeechManager.Speak("The players changed — this slot is now " + nick
                + ". Press Enter again to kick them.");
            return;
        }
        _kickArmedSlot = -1;
        _kickArmedNick = null;
        SpeechManager.Speak("Kicked " + nick + ".");
        lm.KickPlayer(slot);
    }

    // ---------------------------------------------------------------- text builders

    private static string PanelOverview(LobbyManager lm)
    {
        var rows = BuildRows();
        string first = rows.Count > 0 ? rows[0].Speech : "";
        switch (CurrentPanel(lm))
        {
            case Panel.Region:
                return "Region selection. " + rows.Count + " rows: crossplay, quick regions, and the"
                    + " full region list. Up and Down to review, Enter to activate. " + first;
            case Panel.Connecting:
                return StatusText(lm).Length > 0 ? StatusText(lm) : "Connecting.";
            case Panel.Join:
            {
                int count = RoomRows(lm).Count;
                return "Join game. " + (count == 0 ? "No rooms listed yet." : count + (count == 1 ? " room listed." : " rooms listed."))
                    + " Up and Down review rooms, Enter joins; Tab switches to the controls"
                    + " (join by code, new game, disconnect).";
            }
            case Panel.Create:
                return "Game room setup. " + rows.Count + " rows: room name, player count, password,"
                    + " looking-for-more, create, back. Up and Down to review, Enter to activate.";
            case Panel.Room:
                return "In room. " + RoomHeader() + " " + Occupants() + " of " + MaxPlayers()
                    + " players. Up and Down review the room, Enter activates; Escape leaves the room.";
            default:
                return "Multiplayer lobby.";
        }
    }

    private static string RoomHeader()
    {
        var nm = NetworkManager.Instance;
        if (nm == null)
            return "Room.";
        // Being kicked leaves RoomT visible until the async OnLeftRoom; the description/password
        // getters deref CurrentRoom without a null check (unlike GetRoomName).
        if (Photon.Pun.PhotonNetwork.CurrentRoom == null)
            return "Leaving the room.";
        var sb = new StringBuilder();
        string desc = nm.GetRoomDescription();
        if (!string.IsNullOrEmpty(desc))
            sb.Append(AccessibleMenuBase.StripRichText(desc)).Append(". ");
        string code = nm.GetRoomName();
        if (!string.IsNullOrEmpty(code))
            sb.Append("Room code ").Append(HeroSpeech.SpellSeed(code)).Append(".");
        string pwd = nm.GetRoomPassword();
        if (!string.IsNullOrEmpty(pwd))
            sb.Append(" Password ").Append(HeroSpeech.SpellSeed(pwd)).Append(".");
        return sb.Length > 0 ? sb.ToString() : "Room.";
    }

    private static string SlotSpeech(LobbyManager lm, int idx, bool kickable)
    {
        string text = CardSpeech.CleanFlat(lm.roomSlots[idx].text);
        var sb = new StringBuilder();
        sb.Append("Slot ").Append(idx + 1).Append(": ").Append(text);
        var nm = NetworkManager.Instance;
        string rendered = NickAtRenderedSlot(idx);
        if (nm != null && rendered.Length > 0 && rendered == nm.GetPlayerNick())
            sb.Append(", that's you");
        if (kickable)
            sb.Append(". Press Enter to kick");
        return sb.ToString();
    }

    private static List<RoomList> RoomRows(LobbyManager lm)
    {
        var rows = new List<RoomList>();
        if (lm.GridTransform != null)
            rows.AddRange(lm.GridTransform.GetComponentsInChildren<RoomList>(includeInactive: false));
        return rows;
    }

    private static string RoomRowName(RoomList rl)
    {
        string desc = rl._Name != null ? CardSpeech.CleanFlat(rl._Name.text) : "";
        return desc.Length > 0 ? desc : "room " + rl.RoomName;
    }

    private static string RoomRowSpeech(RoomList rl)
    {
        var sb = new StringBuilder();
        sb.Append(RoomRowName(rl));
        if (rl._Creator != null)
        {
            string creator = CardSpeech.CleanFlat(rl._Creator.text);
            if (creator.Length > 0)
                sb.Append(", by ").Append(creator);
        }
        if (rl._Players != null)
        {
            string players = CardSpeech.CleanFlat(rl._Players.text).Replace("/", " of ");
            if (players.Length > 0)
                sb.Append(", ").Append(players).Append(" players");
        }
        if (rl._Lock != null && rl._Lock.gameObject.activeSelf)
            sb.Append(", password protected");
        if (rl.lfm != null && rl.lfm.activeSelf)
            sb.Append(", looking for more");
        if (rl.IsFull())
            sb.Append(", full");
        if (rl._Version != null)
        {
            string version = CardSpeech.CleanFlat(rl._Version.text);
            if (version.Length > 0)
                sb.Append(", version ").Append(version);
        }
        return sb.ToString();
    }

    private static string FieldValue(TMP_InputField field, string emptyWord)
    {
        string v = field != null ? AccessibleMenuBase.StripRichText(field.text) : "";
        return v.Length > 0 ? v : emptyWord;
    }

    private static string DropdownOption(TMP_Dropdown dd)
        => dd != null ? DropdownOptionAt(dd, dd.value) : "";

    private static string DropdownOptionAt(TMP_Dropdown dd, int i)
        => dd != null && i >= 0 && i < dd.options.Count
            ? AccessibleMenuBase.StripRichText(dd.options[i].text)
            : "";

    private static string StatusText(LobbyManager lm)
        => lm.statusTM != null ? CardSpeech.CleanFlat(lm.statusTM.text) : "";

    private static bool AnySlotActive(LobbyManager lm)
    {
        if (lm.roomSlots == null)
            return false;
        foreach (var s in lm.roomSlots)
            if (s != null && s.gameObject.activeSelf)
                return true;
        return false;
    }

    /// <summary>Room capacity — NetworkManager's own GetMaxPlayers is private, so read the same
    /// Photon room property it reads.</summary>
    private static int MaxPlayers()
    {
        var room = Photon.Pun.PhotonNetwork.CurrentRoom;
        return room != null ? room.MaxPlayers : 0;
    }

    private static int Occupants()
    {
        // NetworkManager's player count is the authority.
        return NetworkManager.Instance != null ? NetworkManager.Instance.GetNumPlayers() : 0;
    }

    /// <summary>Status-line announcements (connection progress, the connected summary). Deduped
    /// against the last spoken string — the game re-writes the same status repeatedly.</summary>
    internal static void OnStatusChanged()
    {
        var lm = LobbyManager.Instance;
        if (lm == null || !_sceneAnnounced)
            return;
        string status = StatusText(lm);
        if (status.Length == 0 || status == _lastStatusSpoken)
            return;
        _lastStatusSpoken = status;
        SpeechManager.SpeakQueued(status);
    }
}

/// <summary>Connection status lines ("Connecting…", the connected region summary).</summary>
[HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.SetStatus))]
public class LobbyStatusAnnouncePatch
{
    static void Postfix() => LobbyScreenManager.OnStatusChanged();
}

/// <summary>A wrong room password fails with NO feedback at all in the game (GetRoomPassword
/// simply does nothing on mismatch — no alert, no retry): the player hears the input alert
/// close and lands back on the Join panel unexplained. On a match the method clears the private
/// roomPassword field before joining, so a non-empty field after the call means mismatch.
/// Cancelling the prompt fires this same delegate WITHOUT a submit (the CloseAlert path),
/// which also mismatches — only speak for an actual submit.</summary>
[HarmonyPatch(typeof(LobbyManager), "GetRoomPassword")]
public class LobbyWrongPasswordAnnouncePatch
{
    static void Postfix(LobbyManager __instance)
    {
        if (!AlertDialogueManager.InputSubmittedThisFrame)
            return;
        string pwd = Traverse.Create(__instance).Field<string>("roomPassword").Value;
        if (!string.IsNullOrEmpty(pwd))
            SpeechManager.Speak("Wrong password. Press Enter on the room to try again.");
    }
}
