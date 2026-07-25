using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

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
public static class MapNavigator
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
    private static int _partyIndex;
    private static string _cachedCurrentNode;
    private static readonly Dictionary<Node, Vector2Int> _coords = new Dictionary<Node, Vector2Int>();

    public static bool InLookAhead => _look.Count > 0;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Called when the map has finished drawing. Resets focus to the node region and speaks
    /// the map info summary.</summary>
    public static void OnMapReady()
    {
        CurrentRegion = Region.Nodes;
        _reachIndex = 0;
        _partyIndex = 0;
        _look.Clear();
        _cachedCurrentNode = AtOManager.Instance?.currentMapNode;
        RebuildCoordinates();
        RebuildReachable();
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
            if (!CharWindowScreenManager.OpenHeroSheet(_partyIndex))
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
        SpeechManager.Speak("Traveling to " + NodeType(node));
        m.PlayerSelectedNode(node);
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
            FocusFirstParty();
        }
        else
        {
            CurrentRegion = Region.Nodes;
            ExitLookAheadSilent();
            AnnounceCurrentReachable();
        }
    }

    private static void FocusFirstParty()
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        if (!occ.Contains(_partyIndex))
            _partyIndex = occ[0];
        AnnounceHero(_partyIndex);
    }

    public static void MoveParty(int delta)
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        int pos = occ.IndexOf(_partyIndex);
        pos = pos < 0 ? 0 : Wrap(pos + delta, occ.Count);
        _partyIndex = occ[pos];
        AnnounceHero(_partyIndex);
    }

    public static void JumpToSlot(int slot)
    {
        CurrentRegion = Region.Party;
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Slot " + (slot + 1) + " empty");
            return;
        }
        _partyIndex = slot;
        AnnounceHero(slot);
    }

    private static void AnnounceHero(int slot)
    {
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Empty slot");
            return;
        }
        SpeechManager.Speak(BuildHeroLine(h));
    }

    /// <summary>One spoken hero summary line; shared with the town party strip.</summary>
    internal static string BuildHeroLine(Hero h)
    {
        var sb = new StringBuilder();
        sb.Append(h.SourceName);
        sb.Append(", ").Append(h.HpCurrent).Append(" of ").Append(h.Hp).Append(" health");
        sb.Append(", level ").Append(h.Level);
        if (h.Level < 5)
            sb.Append(", experience ").Append(h.Experience);
        CountCards(h, out int deck, out int injuries);
        sb.Append(", ").Append(deck).Append(deck == 1 ? " card" : " cards");
        if (injuries > 0)
            sb.Append(", ").Append(injuries).Append(injuries == 1 ? " injury" : " injuries");
        if (h.IsReadyForLevelUp())
            sb.Append(", ready to level up");
        return sb.ToString();
    }

    private static void CountCards(Hero h, out int deck, out int injuries)
    {
        deck = 0;
        injuries = 0;
        if (h.Cards == null)
            return;
        foreach (var id in h.Cards)
        {
            var cd = Globals.Instance?.GetCardData(id, false);
            if (cd != null && cd.CardClass == Enums.CardClass.Injury)
                injuries++;
            else
                deck++;
        }
    }

    private static List<int> OccupiedSlots()
    {
        var list = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            var h = GetHero(i);
            if (h != null && h.HeroData != null)
                list.Add(i);
        }
        return list;
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
                var cd = Globals.Instance?.GetCombatData(assignedId);
                int enemies = 0;
                if (cd?.NPCList != null)
                    foreach (var npc in cd.NPCList)
                        if (npc != null)
                            enemies++;
                if (enemies > 0)
                    sb.Append(", ").Append(enemies).Append(enemies == 1 ? " enemy" : " enemies");
            }
            else if (action == "event")
            {
                var ed = Globals.Instance?.GetEventData(assignedId);
                if (ed != null)
                {
                    if (!string.IsNullOrEmpty(ed.EventName))
                        sb.Append(", ").Append(AccessibleMenuBase.StripRichText(ed.EventName));
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

    private static int Wrap(int value, int count)
    {
        if (count <= 0)
            return 0;
        value %= count;
        return value < 0 ? value + count : value;
    }

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
