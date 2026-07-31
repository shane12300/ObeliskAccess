using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using static ObeliskAccess.Patches.Nav;

namespace ObeliskAccess.Patches;

/// <summary>
/// All map-screen accessibility state and speech. The <c>MapInputContext</c> is a thin delegator
/// that calls into here; this class holds the focus model (node region vs party region, the
/// reachable-node row, the multi-level look-ahead stack, and the focused party slot) and builds the
/// spoken strings.
///
/// The map is a hand-authored graph of <c>Node</c>s whose per-run content lives in
/// <c>AtOManager.gameNodeAssigned</c>. Only nodes that are actually drawn (active in the hierarchy)
/// are ever surfaced, so a screen-reader player hears exactly the nodes a sighted player can see.
/// </summary>
internal static class MapNavigator
{
    public enum Region { Nodes, Party }

    /// <summary>One level of the look-ahead descent: the node we descended from (<see cref="parent"/>),
    /// its visible onward siblings, and which sibling is focused.</summary>
    private class LookLevel
    {
        public Node parent;
        public List<Node> siblings;
        public int index;
    }

    public static Region CurrentRegion { get; private set; } = Region.Nodes;

    private static readonly List<Node> _reachable = new List<Node>();
    private static int _reachIndex;
    private static readonly List<LookLevel> _look = new List<LookLevel>();
    private static readonly PartyStrip _party = new PartyStrip();
    private static string _cachedCurrentNode;
    private static readonly Dictionary<Node, Vector2Int> _coords = new Dictionary<Node, Vector2Int>();

    public static bool InLookAhead => _look.Count > 0;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Called when the map has finished drawing. Resets focus to the node region and arms
    /// the arrival summary — spoken from <see cref="TickArrival"/> on the first frame the map
    /// actually owns input. BeginMapContinueEnd fires while the screen can still be masked (MP
    /// barriers; a non-master's map is not even drawn until the master's node RPC lands), so
    /// speaking here narrated a map the player could not touch.</summary>
    public static void OnMapReady()
    {
        CurrentRegion = Region.Nodes;
        _reachIndex = 0;
        _party.Reset();
        _look.Clear();
        _cachedCurrentNode = AtOManager.Instance?.currentMapNode;
        ResetVoteState();
        RebuildCoordinates();
        RebuildReachable();
        _arrivalPending = true;
    }

    private static bool _arrivalPending;

    /// <summary>Per-frame (from the poller, outside every gate): speak the armed arrival summary
    /// once the map context is genuinely interactive — world shown, mask down, no event or
    /// corruption over it. Waits out MP sync barriers and speaks after whatever covered the map.</summary>
    public static void TickArrival()
    {
        if (!_arrivalPending)
            return;
        if (MapManager.Instance == null)
        {
            _arrivalPending = false; // scene torn down under the pending announce
            return;
        }
        if (!ObeliskAccess.Input.Contexts.MapInputContext.IsCurrentlyActive)
            return;
        _arrivalPending = false;
        EnsureFresh(); // the reachable row may have been rebuilt empty while the world was hidden
        SpeakMapInfo();
    }

    private static void EnsureFresh()
    {
        string cur = AtOManager.Instance?.currentMapNode;
        if (cur != _cachedCurrentNode)
        {
            _cachedCurrentNode = cur;
            _reachIndex = 0;
            _look.Clear();
            RebuildCoordinates();
            RebuildReachable();
        }
        else if (_reachable.Count == 0)
        {
            RebuildReachable();
        }
    }

    // ---------------------------------------------------------------- reachable row

    private static void RebuildReachable()
    {
        _reachable.Clear();
        var m = MapManager.Instance;
        var active = m?.nodeActive;
        if (active?.nodeData?.NodesConnected == null)
            return;

        var seen = new HashSet<string>();
        foreach (var conn in active.nodeData.NodesConnected)
        {
            if (conn?.NodeId == null || conn.NodeId == active.nodeData.NodeId || !seen.Add(conn.NodeId))
                continue;
            var node = GetNode(conn.NodeId);
            if (!IsVisible(node))
                continue;
            if (m.CanTravelToThisNode(node, active))
                _reachable.Add(node);
        }
        _reachable.Sort(CompareByScreenX);
        if (_reachIndex >= _reachable.Count)
            _reachIndex = 0;
    }

    public static void MoveReachable(int delta)
    {
        ExitLookAheadSilent();
        EnsureFresh();
        if (_reachable.Count == 0)
        {
            SpeechManager.Speak("No reachable nodes");
            return;
        }
        _reachIndex = Wrap(_reachIndex + delta, _reachable.Count);
        AnnounceNode(_reachable[_reachIndex], reachable: true);
    }

    public static void AnnounceCurrentReachable()
    {
        EnsureFresh();
        if (_reachable.Count == 0)
        {
            SpeechManager.Speak("No reachable nodes");
            return;
        }
        AnnounceNode(_reachable[_reachIndex], reachable: true);
    }

    // ---------------------------------------------------------------- look-ahead

    public static void LookAheadDescend()
    {
        EnsureFresh();
        Node from;
        Node cameFrom;
        if (_look.Count == 0)
        {
            if (_reachable.Count == 0)
            {
                SpeechManager.Speak("No reachable nodes");
                return;
            }
            from = _reachable[_reachIndex];
            cameFrom = MapManager.Instance?.nodeActive;
        }
        else
        {
            var top = _look[_look.Count - 1];
            from = top.siblings[top.index];
            cameFrom = top.parent;
        }

        var children = OnwardNodes(from, cameFrom);
        if (children.Count == 0)
        {
            SpeechManager.Speak("No further nodes ahead");
            return;
        }
        _look.Add(new LookLevel { parent = from, siblings = children, index = 0 });
        AnnounceNode(children[0], reachable: false);
    }

    public static void LookAheadAscend()
    {
        if (_look.Count == 0)
        {
            SpeechManager.Speak("At your position");
            return;
        }
        _look.RemoveAt(_look.Count - 1);
        if (_look.Count == 0)
        {
            EnsureFresh();
            if (_reachable.Count > 0)
                AnnounceNode(_reachable[_reachIndex], reachable: true, prefix: "Back to reachable, ");
            else
                SpeechManager.Speak("Back to your position");
        }
        else
        {
            var top = _look[_look.Count - 1];
            AnnounceNode(top.siblings[top.index], reachable: false);
        }
    }

    public static void LookAheadSibling(int delta)
    {
        if (_look.Count == 0)
            return; // no branch open; bare arrows drive the reachable row instead
        var top = _look[_look.Count - 1];
        top.index = Wrap(top.index + delta, top.siblings.Count);
        AnnounceNode(top.siblings[top.index], reachable: false);
    }

    public static void ExitLookAhead()
    {
        if (_look.Count == 0)
            return;
        _look.Clear();
        AnnounceCurrentReachable();
    }

    private static void ExitLookAheadSilent() => _look.Clear();

    // ---------------------------------------------------------------- travel

    public static void Confirm()
    {
        if (CurrentRegion == Region.Party)
        {
            // Open the character sheet through the game's own portrait-click path; the
            // CharacterWindowUI.Show postfix announces the arrival.
            if (!CharWindowScreenManager.OpenHeroSheet(_party.Slot))
                SpeechManager.Speak("No character focused");
            return;
        }
        if (_look.Count > 0)
        {
            SpeechManager.Speak("Not reachable yet");
            return;
        }
        var node = CurrentFocusNode();
        var m = MapManager.Instance;
        if (node == null || m == null)
        {
            SpeechManager.Speak("No node focused");
            return;
        }
        if (!m.CanTravelToThisNode(node))
        {
            SpeechManager.Speak("Not reachable");
            return;
        }
        if (MpSpeech.IsMp)
        {
            // Multiplayer travel is a vote. Mirror PlayerSelectedNode's own refusal guards
            // exactly, so "Voting…" is never spoken for a call the game silently drops.
            if (m.selectedNode)
            {
                SpeechManager.Speak("Vote already cast. Waiting for the other players.");
                return;
            }
            bool following = AtOManager.Instance != null && AtOManager.Instance.followingTheLeader;
            bool isMaster = NetworkManager.Instance != null && NetworkManager.Instance.IsMaster();
            if (following && !isMaster)
            {
                SpeechManager.Speak("Following the leader. " + MpSpeech.HostNick() + " chooses the destination.");
                return;
            }
            _localVoteAnnounced = true;
            SpeechManager.Speak((following ? "Leading the party to " : "Voting to travel to ") + NodeType(node));
            m.PlayerSelectedNode(node);
            return;
        }
        if (m.selectedNode)
        {
            // PlayerSelectedNode silently no-ops once travel is underway; don't re-claim it.
            SpeechManager.Speak("Already traveling.");
            return;
        }
        SpeechManager.Speak("Traveling to " + NodeType(node));
        m.PlayerSelectedNode(node);
    }

    // ---------------------------------------------------------------- multiplayer voting

    private static bool _localVoteAnnounced;
    private static readonly HashSet<string> _announcedVotes = new HashSet<string>();

    /// <summary>Proper node name for a vote announcement (navigation speech deliberately uses
    /// types + coordinates instead, but a partner's vote needs the name the tally shows).</summary>
    private static string VoteNodeName(string nodeId)
    {
        var nd = Globals.Instance?.GetNodeData(nodeId);
        string nm = nd != null ? AccessibleMenuBase.StripRichText(nd.NodeName) : "";
        return nm.Length > 0 ? nm : nodeId;
    }

    /// <summary>
    /// NET_SharePlayerSelectedNode postfix: the master re-broadcasts the whole vote dict after
    /// every vote. Re-parse the JSON ourselves (the game's dict is left stale on a parse failure)
    /// and announce only pairs not yet spoken; the set shrinking means a new voting round.
    /// </summary>
    public static void OnVoteShare(string keysJson, string valuesJson)
    {
        if (!MpSpeech.IsMp)
            return;
        string[] keys = JsonHelper.FromJson<string>(keysJson);
        string[] vals = JsonHelper.FromJson<string>(valuesJson);
        if (keys == null || vals == null || keys.Length == 0 || keys.Length != vals.Length)
        {
            Plugin.LogDebug("OnVoteShare: unparseable vote share, skipped");
            return;
        }
        // The game discards the whole broadcast (dict untouched) if ANY entry is empty or names an
        // unknown node — mirror that before announcing votes it never counted.
        var mm = MapManager.Instance;
        if (mm == null)
            return;
        for (int i = 0; i < keys.Length; i++)
            if (string.IsNullOrEmpty(keys[i]) || string.IsNullOrEmpty(vals[i]) || mm.GetNodeFromId(vals[i]) == null)
                return;

        if (keys.Length < _announcedVotes.Count)
            _announcedVotes.Clear(); // new voting round

        string localNick = MpSpeech.LocalNick();
        string lastName = "";
        for (int i = 0; i < keys.Length; i++)
        {
            if (string.IsNullOrEmpty(keys[i]) || string.IsNullOrEmpty(vals[i]))
                continue;
            string key = keys[i] + "|" + vals[i];
            lastName = VoteNodeName(vals[i]);
            if (!_announcedVotes.Add(key))
                continue;
            if (keys[i] != localNick)
            {
                SpeechManager.SpeakQueued(MpSpeech.DisplayNick(keys[i]) + " votes for " + lastName + ".");
            }
            else if (!_localVoteAnnounced)
            {
                // An un-announced local vote is either the game's follow-the-leader auto-vote or
                // a mouse-cast vote — only the former should claim we are "following".
                bool follow = AtOManager.Instance != null && AtOManager.Instance.followingTheLeader
                    && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster();
                SpeechManager.SpeakQueued(follow
                    ? "Following " + MpSpeech.HostNick() + " to " + lastName + "."
                    : "Your vote for " + lastName + " is in.");
                _localVoteAnnounced = true;
            }
        }

        int total = NetworkManager.Instance != null ? NetworkManager.Instance.GetNumPlayers() : 0;
        if (total > 0 && keys.Length < total)
        {
            SpeechManager.SpeakQueued(keys.Length + " of " + total + " votes in.");
            return;
        }
        bool unanimous = true;
        for (int i = 1; i < vals.Length; i++)
            if (vals[i] != vals[0])
            {
                unanimous = false;
                break;
            }
        SpeechManager.SpeakQueued(unanimous
            ? "All votes in. Traveling to " + VoteNodeName(vals[0]) + "."
            : "Votes differ. Conflict round starting.");
    }

    /// <summary>Reset per-map vote bookkeeping (called from OnMapReady).</summary>
    private static void ResetVoteState()
    {
        _localVoteAnnounced = false;
        _announcedVotes.Clear();
    }

    // ---------------------------------------------------------------- coordinates

    /// <summary>Assigns every drawn node a spoken coordinate: column = its left-to-right rank
    /// among the nodes of its row (1 = leftmost), row = how far into the map it sits (1 = the
    /// first selectable group, i.e. the nodes one travel from the map's entry; 2 = the group
    /// after, …). The entry node itself gets no coordinate — it can never be focused. Rows are
    /// BFS distance from the entry node over the drawn connection graph, treated as undirected so
    /// it works whether the map data stores links one-way or mirrored. Nodes cut off from the
    /// entry get no coordinate and are simply spoken without one.</summary>
    private static void RebuildCoordinates()
    {
        _coords.Clear();
        var dict = MapManager.Instance?.GetMapNodeDict();
        if (dict == null)
            return;

        var byId = new Dictionary<string, Node>();
        foreach (var n in dict.Values)
        {
            if (!IsVisible(n) || n.nodeData?.NodeId == null)
                continue;
            byId[n.nodeData.NodeId.ToLowerInvariant()] = n;
        }
        if (byId.Count == 0)
            return;

        var adj = new Dictionary<Node, List<Node>>();
        foreach (var n in byId.Values)
            adj[n] = new List<Node>();
        foreach (var n in byId.Values)
        {
            if (n.nodeData.NodesConnected == null)
                continue;
            foreach (var conn in n.nodeData.NodesConnected)
            {
                if (conn?.NodeId == null || conn.NodeId == n.nodeData.NodeId)
                    continue;
                if (!byId.TryGetValue(conn.NodeId.ToLowerInvariant(), out var t))
                    continue;
                if (!adj[n].Contains(t))
                    adj[n].Add(t);
                if (!adj[t].Contains(n))
                    adj[t].Add(n);
            }
        }

        var root = FindEntryNode(byId);
        if (root == null)
            return;

        var row = new Dictionary<Node, int> { [root] = 0 };
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var m in adj[n])
            {
                if (row.ContainsKey(m))
                    continue;
                row[m] = row[n] + 1;
                queue.Enqueue(m);
            }
        }

        var rows = new Dictionary<int, List<Node>>();
        foreach (var kv in row)
        {
            if (kv.Value == 0)
                continue; // the entry node itself is never focusable — no coordinate
            if (!rows.TryGetValue(kv.Value, out var group))
                rows[kv.Value] = group = new List<Node>();
            group.Add(kv.Key);
        }
        foreach (var kv in rows)
        {
            kv.Value.Sort(CompareByScreenX);
            for (int i = 0; i < kv.Value.Count; i++)
                _coords[kv.Value[i]] = new Vector2Int(i + 1, kv.Key);
        }
    }

    /// <summary>The drawn node the party entered this map on: the earliest entry of the run's
    /// visit history that is currently drawn (earlier maps' nodes are not drawn, so this lands on
    /// the current map's first node). Falls back to the node the party stands on.</summary>
    private static Node FindEntryNode(Dictionary<string, Node> byId)
    {
        var visited = AtOManager.Instance?.mapVisitedNodes;
        if (visited != null)
            foreach (var id in visited)
                if (id != null && byId.TryGetValue(id.ToLowerInvariant(), out var n))
                    return n;
        var active = MapManager.Instance?.nodeActive;
        return IsVisible(active) ? active : null;
    }

    /// <summary>Spoken "column, row" for a node, or null if it has no computed coordinate.</summary>
    private static string CoordText(Node node)
    {
        if (node == null)
            return null;
        if (!_coords.TryGetValue(node, out var c))
        {
            RebuildCoordinates();
            if (!_coords.TryGetValue(node, out c))
                return null;
        }
        return c.x + ", " + c.y;
    }

    // ---------------------------------------------------------------- party strip

    public static void ToggleRegion()
    {
        if (CurrentRegion == Region.Nodes)
        {
            CurrentRegion = Region.Party;
            _party.FocusFirst();
        }
        else
        {
            CurrentRegion = Region.Nodes;
            ExitLookAheadSilent();
            AnnounceCurrentReachable();
        }
    }

    public static void MoveParty(int delta) => _party.Move(delta);

    public static void JumpToSlot(int slot)
    {
        CurrentRegion = Region.Party;
        _party.JumpTo(slot);
    }

    // ---------------------------------------------------------------- read-outs

    public static void SpeakGold()
    {
        var cm = AtOManager.Instance?.CurrencyManager;
        int gold = cm != null ? cm.GetPlayerGold() : 0;
        SpeechManager.Speak("Gold: " + gold);
    }

    /// <summary>Hero in the given party slot (0–3), or null. The team is a <c>GameTeam</c> on
    /// <c>AtOManager</c> in this build, not a raw array.</summary>
    private static Hero GetHero(int index)
    {
        var team = AtOManager.Instance?.team;
        return team != null ? team.GetHero(index) : null;
    }

    /// <summary>Alt+T: the extra detail a sighted player would see on the node's hover popup
    /// (enemy count for combat, event name + rarity, terrain), honoring the game's masking of
    /// unrevealed nodes.</summary>
    public static void SpeakFocusedNodeDetail()
    {
        var node = CurrentFocusNode();
        if (node?.nodeData == null)
        {
            SpeechManager.Speak("No node focused");
            return;
        }

        var sb = new StringBuilder();
        string name = Globals.Instance?.GetNodeData(node.nodeData.NodeId)?.NodeName;
        name = string.IsNullOrEmpty(name) ? null : AccessibleMenuBase.StripRichText(name);
        if (name != null)
            sb.Append(name).Append(", ");
        sb.Append(NodeType(node));

        if (!IsRevealed(node))
        {
            sb.Append(", details hidden");
        }
        else
        {
            string action = node.GetNodeAction();
            string assignedId = node.GetNodeAssignedId();
            if (action == "combat")
            {
                int enemies = CombatEnemyCount(node, assignedId);
                if (enemies > 0)
                    sb.Append(", ").Append(enemies).Append(enemies == 1 ? " enemy" : " enemies");
            }
            else if (action == "event")
            {
                var ed = Globals.Instance?.GetEventData(assignedId);
                if (ed != null)
                {
                    // The game's popup prefers the localized "<EventId>_nm" text and only falls
                    // back to the raw asset EventName.
                    string enm = Texts.Instance != null ? Texts.Instance.GetText(ed.EventId + "_nm", "events") : "";
                    if (string.IsNullOrEmpty(enm))
                        enm = ed.EventName;
                    if (!string.IsNullOrEmpty(enm))
                        sb.Append(", ").Append(AccessibleMenuBase.StripRichText(enm));
                    string rarity = ShaderToRarity(ed.EventIconShader);
                    if (rarity != null)
                        sb.Append(", ").Append(rarity);
                }
            }
        }

        var ground = node.nodeData.NodeGround;
        if (ground != Enums.NodeGround.None && Texts.Instance != null)
        {
            string g = Texts.Instance.GetText(System.Enum.GetName(typeof(Enums.NodeGround), ground));
            if (!string.IsNullOrEmpty(g))
                sb.Append(", terrain ").Append(AccessibleMenuBase.StripRichText(g));
        }

        sb.Append(", ").Append(StateWord(node));
        string coord = CoordText(node);
        if (coord != null)
            sb.Append(", at ").Append(coord);
        SpeechManager.Speak(sb.ToString());
    }

    /// <summary>Enemy count exactly as the game's hover popup (PopupNode) computes it: the
    /// randomized roster for Obelisk challenges / "randomcombats" madness, the madness-0 enemy
    /// removal, and the sandbox LessNPCs hide. GetRandomCombat reseeds UnityEngine.Random, so this
    /// must only run from a discrete key handler (Alt+T), never per frame; the seed formula matches
    /// the one the real combat uses, so the previewed roster is the roster that will spawn.</summary>
    private static int CombatEnemyCount(Node node, string assignedId)
    {
        var globals = Globals.Instance;
        var ato = AtOManager.Instance;
        var gm = GameManager.Instance;
        var nd = node?.nodeData;
        if (globals == null || ato == null || gm == null || nd == null)
            return 0;

        bool randomized = nd.NodeCombatTier != 0
            && ((MadnessManager.Instance != null && MadnessManager.Instance.IsMadnessTraitActive("randomcombats"))
                || gm.IsObeliskChallenge()
                || ato.IsChallengeTraitActive("randomcombats"))
            && !nd.DisableRandom;
        int lessNpcs = SandboxManager.Instance != null ? SandboxManager.Instance.LessNPCs : 0;

        if (randomized)
        {
            var combatData = globals.GetCombatData(assignedId);
            string combatId = combatData != null ? combatData.CombatId : "";
            var freshNd = globals.GetNodeData(nd.NodeId);
            var roster = Functions.GetRandomCombat(
                freshNd != null ? freshNd.NodeCombatTier : nd.NodeCombatTier,
                (nd.NodeId + ato.GetGameId() + combatId).GetDeterministicHashCode(),
                nd.NodeId);
            if (roster == null)
                return 0;
            int visible = 0;
            int hideable = 0;
            foreach (var npc in roster)
            {
                if (npc == null)
                    continue;
                visible++;
                if (!npc.IsNamed && !npc.IsBoss)
                    hideable++;
            }
            int hide = lessNpcs;
            if (hide >= visible)
                hide = visible - 1;
            if (hide > hideable)
                hide = hideable;
            return hide > 0 ? visible - hide : visible;
        }

        var cd = globals.GetCombatData(assignedId);
        if (cd?.NPCList == null)
            return 0;
        bool madness0Removal = ((gm.IsGameAdventure() && ato.GetMadnessDifficulty() == 0)
                || (gm.IsSingularity() && ato.GetSingularityMadness() == 0))
            && cd.NpcRemoveInMadness0Index > -1 && ato.GetActNumberForText() < 3;

        int shown = 0;
        // The game's LessNPCs hide order: weakest first (Hp, then list position), non-named
        // non-boss only — built over ALL such entries, so a hide slot can land on the enemy the
        // madness-0 removal already hid and then hides nothing visible. Replay that literally.
        var hideOrder = new System.Collections.Generic.SortedDictionary<int, int>();
        for (int i = 0; i < cd.NPCList.Length; i++)
        {
            var npc = cd.NPCList[i];
            if (npc == null)
                continue;
            if (!(madness0Removal && cd.NpcRemoveInMadness0Index == i))
                shown++;
            if (!npc.IsNamed && !npc.IsBoss)
                hideOrder.Add(npc.Hp * 10000 + i, i);
        }
        int hides = lessNpcs;
        if (hides >= shown)
            hides = shown - 1;
        if (hides > hideOrder.Count)
            hides = hideOrder.Count;
        if (hides <= 0)
            return shown;
        int left = shown;
        int taken = 0;
        foreach (var kv in hideOrder)
        {
            if (taken++ >= hides)
                break;
            if (!(madness0Removal && cd.NpcRemoveInMadness0Index == kv.Value))
                left--;
        }
        return left;
    }

    /// <summary>Alt+I and auto-on-open: position + active quest trackers + the current map tip.</summary>
    public static void SpeakMapInfo()
    {
        var m = MapManager.Instance;
        var sb = new StringBuilder();

        var active = m?.nodeActive;
        if (active?.nodeData != null)
        {
            string nm = Globals.Instance?.GetNodeData(active.nodeData.NodeId)?.NodeName;
            sb.Append(string.IsNullOrEmpty(nm) ? "Current location" : AccessibleMenuBase.StripRichText(nm));
        }

        if (m?.trackList != null)
        {
            var tracks = new List<string>();
            foreach (Transform t in m.trackList)
            {
                var tmp = t.GetComponentInChildren<TMP_Text>();
                if (tmp == null)
                    continue;
                string s = AccessibleMenuBase.StripRichText(tmp.text);
                if (s.Length > 0)
                    tracks.Add(s);
            }
            if (tracks.Count > 0)
                Append(sb, "Objectives: " + string.Join("; ", tracks));
        }

        if (m?.tipText != null)
        {
            string tip = AccessibleMenuBase.StripRichText(m.tipText.text);
            if (tip.Length > 0)
                Append(sb, "Tip: " + tip);
        }

        if (MpSpeech.IsMp && AtOManager.Instance != null && AtOManager.Instance.followingTheLeader)
        {
            bool isMaster = NetworkManager.Instance != null && NetworkManager.Instance.IsMaster();
            Append(sb, isMaster
                ? "Follow the leader is on: your pick travels the party"
                : "Follow the leader is on: " + MpSpeech.HostNick() + " chooses the destination");
        }

        SpeechManager.Speak(sb.Length > 0 ? sb.ToString() : "Map");
    }

    // ---------------------------------------------------------------- helpers

    public static Node CurrentFocusNode()
    {
        if (CurrentRegion != Region.Nodes)
            return null;
        if (_look.Count > 0)
        {
            var top = _look[_look.Count - 1];
            return top.siblings[top.index];
        }
        if (_reachable.Count > 0 && _reachIndex < _reachable.Count)
            return _reachable[_reachIndex];
        return null;
    }

    /// <summary>Visible onward nodes from <paramref name="x"/>, excluding the one we arrived from,
    /// sorted left-to-right. Only drawn nodes are included, so this mirrors the on-screen graph.</summary>
    private static List<Node> OnwardNodes(Node x, Node cameFrom)
    {
        var result = new List<Node>();
        if (x?.nodeData?.NodesConnected == null)
            return result;
        var seen = new HashSet<string>();
        foreach (var conn in x.nodeData.NodesConnected)
        {
            if (conn?.NodeId == null)
                continue;
            if (cameFrom?.nodeData != null && conn.NodeId == cameFrom.nodeData.NodeId)
                continue;
            if (conn.NodeId == x.nodeData.NodeId || !seen.Add(conn.NodeId))
                continue;
            var node = GetNode(conn.NodeId);
            if (IsVisible(node))
                result.Add(node);
        }
        result.Sort(CompareByScreenX);
        return result;
    }

    private static Node GetNode(string id)
    {
        var dict = MapManager.Instance?.GetMapNodeDict();
        if (dict == null || id == null)
            return null;
        return dict.TryGetValue(id.ToLower(), out var n) ? n : null;
    }

    private static bool IsVisible(Node node)
        => node != null && node.gameObject != null && node.gameObject.activeInHierarchy;

    private static bool IsVisited(Node node)
    {
        var v = AtOManager.Instance?.mapVisitedNodes;
        return v != null && node?.nodeData != null && v.Contains(node.nodeData.NodeId);
    }

    private static bool IsRevealed(Node node)
    {
        if (IsVisited(node))
            return true;
        var pm = PlayerManager.Instance;
        if (pm == null || node?.nodeData == null)
            return false;
        return pm.IsNodeUnlocked(node.GetNodeAssignedId()) || pm.IsNodeUnlocked(node.nodeData.NodeId);
    }

    private static string NodeType(Node node)
    {
        var nd = node?.nodeData;
        if (nd != null && nd.GoToTown)
            return "Town";
        string action = node?.GetNodeAction();
        if (action == "combat")
            return "Combat";
        if (action == "event")
            return "Event";
        return "Unknown";
    }

    private static string StateWord(Node node)
    {
        if (_look.Count == 0 && _reachable.Contains(node))
            return "reachable";
        return IsVisited(node) ? "visited" : "locked";
    }

    private static void AnnounceNode(Node node, bool reachable, string prefix = "")
    {
        if (node == null)
        {
            SpeechManager.Speak("No node");
            return;
        }
        string state = reachable ? "reachable" : (IsVisited(node) ? "visited" : "locked");
        string coord = CoordText(node);
        SpeechManager.Speak(prefix + NodeType(node) + ", " + state
            + (coord != null ? ", at " + coord : ""));
    }

    private static string ShaderToRarity(Enums.MapIconShader shader)
    {
        switch (shader)
        {
            case Enums.MapIconShader.Green: return "uncommon";
            case Enums.MapIconShader.Blue: return "rare";
            case Enums.MapIconShader.Purple: return "epic";
            case Enums.MapIconShader.Orange: return "special";
            default: return null;
        }
    }

    private static int CompareByScreenX(Node a, Node b)
        => a.transform.position.x.CompareTo(b.transform.position.x);

    private static void Append(StringBuilder sb, string text)
    {
        if (sb.Length > 0)
            sb.Append(". ");
        sb.Append(text);
    }
}

/// <summary>Resets the navigator and speaks the map info the moment the map finishes drawing.</summary>
[HarmonyPatch(typeof(MapManager), "BeginMapContinueEnd")]
public class MapReadyPatch
{
    static void Postfix() => MapNavigator.OnMapReady();
}

/// <summary>
/// MP travel-vote tally. The master re-broadcasts the full nick→nodeId vote dict (parallel JSON
/// arrays) to every client after each vote — the game's only vote display is colored node markers,
/// so this is where the spoken tally comes from.
/// </summary>
[HarmonyPatch(typeof(MapManager), "NET_SharePlayerSelectedNode")]
public class MapVoteTallyAnnouncePatch
{
    static void Postfix(string _keys, string _values) => MapNavigator.OnVoteShare(_keys, _values);
}
