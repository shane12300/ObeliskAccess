using System.Collections.Generic;
using System.Text;

namespace ObeliskAccess.Patches;

/// <summary>
/// Resolves the enemy roster a combat node will actually spawn, and turns it into speech. Shared by
/// the map's Alt+T node detail (which only needs the count) and the pre-combat corruption prompt
/// (which names the whole line-up, exactly as the sighted player sees it drawn in monster sprites).
///
/// The resolution replays the game's own three filters in order — the randomized roster used by
/// Obelisk challenges and the "randomcombats" madness trait, the madness-0 enemy removal, and the
/// sandbox <c>LessNPCs</c> hide — so the previewed line-up is the line-up that spawns.
///
/// <b>Cost:</b> <c>Functions.GetRandomCombat</c> reseeds <c>UnityEngine.Random</c>, so
/// <see cref="Resolve"/> must only ever run from a discrete event (a key press, or once per
/// corruption draw), never per frame. Callers that read the roster repeatedly cache the result.
/// </summary>
internal static class CombatPreview
{
    /// <summary>A resolved line-up: <see cref="Slots"/> is battlefield-position indexed (index 0 is
    /// the front position), with null for an empty or filtered-out slot.</summary>
    internal class Roster
    {
        public NPCData[] Slots;

        /// <summary>True when the line-up came from <c>GetRandomCombat</c>. Only then does the game
        /// draw champion badges, so only then may the mod speak a champion's immunity.</summary>
        public bool Randomized;

        /// <summary>The node id the champion immunity roll is seeded from.</summary>
        public string NodeDataId = "";

        public int Count
        {
            get
            {
                int n = 0;
                if (Slots != null)
                {
                    foreach (var npc in Slots)
                    {
                        if (npc != null)
                            n++;
                    }
                }
                return n;
            }
        }

        /// <summary>Occupied slot indices, front to back — the walk order for the enemy review.</summary>
        public List<int> Occupied
        {
            get
            {
                var list = new List<int>();
                if (Slots != null)
                {
                    for (int i = 0; i < Slots.Length; i++)
                    {
                        if (Slots[i] != null)
                            list.Add(i);
                    }
                }
                return list;
            }
        }
    }

    /// <summary>
    /// The line-up for a combat node, or null when the data is unavailable (unrevealed node,
    /// non-combat node, managers not up yet).
    /// </summary>
    public static Roster Resolve(NodeData nd, string assignedId)
    {
        var globals = Globals.Instance;
        var ato = AtOManager.Instance;
        var gm = GameManager.Instance;
        if (globals == null || ato == null || gm == null || nd == null)
            return null;

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
            // The tier is re-read from the live NodeData (the Node component's copy can be stale),
            // and the seed formula is the game's own — same node, same game id, same roster.
            var freshNd = globals.GetNodeData(nd.NodeId);
            var roll = Functions.GetRandomCombat(
                freshNd != null ? freshNd.NodeCombatTier : nd.NodeCombatTier,
                (nd.NodeId + ato.GetGameId() + combatId).GetDeterministicHashCode(),
                nd.NodeId);
            if (roll == null)
                return null;

            var slots = (NPCData[])roll.Clone();
            ApplyLessNpcs(slots, roll, lessNpcs);
            return new Roster { Slots = slots, Randomized = true, NodeDataId = nd.NodeId };
        }

        var cd = globals.GetCombatData(assignedId);
        if (cd == null || cd.NPCList == null)
            return null;

        var fixedSlots = (NPCData[])cd.NPCList.Clone();
        bool madness0Removal = ((gm.IsGameAdventure() && ato.GetMadnessDifficulty() == 0)
                || (gm.IsSingularity() && ato.GetSingularityMadness() == 0))
            && cd.NpcRemoveInMadness0Index > -1 && ato.GetActNumberForText() < 3;
        if (madness0Removal && cd.NpcRemoveInMadness0Index < fixedSlots.Length)
            fixedSlots[cd.NpcRemoveInMadness0Index] = null;

        // The hide order is built over the ORIGINAL list, before the madness-0 removal — so a hide
        // slot can land on an enemy that removal already took out and then hides nothing visible.
        // Replay that literally rather than "hide N of what is left".
        ApplyLessNpcs(fixedSlots, cd.NPCList, lessNpcs);
        return new Roster { Slots = fixedSlots, Randomized = false, NodeDataId = nd.NodeId };
    }

    /// <summary>The sandbox "fewer enemies" hide: weakest first (Hp, then list position), never a
    /// named or boss enemy, and never so many that the fight would be left empty.</summary>
    private static void ApplyLessNpcs(NPCData[] slots, NPCData[] hideSource, int lessNpcs)
    {
        if (lessNpcs <= 0 || slots == null || hideSource == null)
            return;

        int visible = 0;
        foreach (var npc in slots)
        {
            if (npc != null)
                visible++;
        }

        var order = new SortedDictionary<int, int>();
        for (int i = 0; i < hideSource.Length; i++)
        {
            var npc = hideSource[i];
            if (npc == null || npc.IsNamed || npc.IsBoss)
                continue;
            order.Add(npc.Hp * 10000 + i, i);
        }

        int hide = lessNpcs;
        if (hide >= visible)
            hide = visible - 1;
        if (hide > order.Count)
            hide = order.Count;
        if (hide <= 0)
            return;

        int taken = 0;
        foreach (var kv in order)
        {
            if (taken++ >= hide)
                break;
            if (kv.Value < slots.Length)
                slots[kv.Value] = null;
        }
    }

    /// <summary>"4 enemies" / "1 enemy" / "" when the roster is unknown.</summary>
    public static string CountPhrase(Roster roster)
    {
        int n = roster != null ? roster.Count : 0;
        if (n <= 0)
            return "";
        return n + (n == 1 ? " enemy" : " enemies");
    }

    /// <summary>Localized monster name (Globals fills NPCName from the monsters table at load).</summary>
    public static string Name(NPCData npc)
    {
        if (npc == null)
            return "Unknown";
        string name = AccessibleMenuBase.StripRichText(npc.NPCName);
        return name.Length > 0 ? name : npc.Id;
    }

    /// <summary>
    /// One review line: "Position 1, front: Bonepicker, 140 health, speed 12, champion, immune to
    /// Burn". Positions are battlefield slots, so the front/back tags name the two ends the game's
    /// own targeting cares about.
    /// </summary>
    public static string BriefLine(Roster roster, int slot)
    {
        var npc = roster != null && roster.Slots != null && slot >= 0 && slot < roster.Slots.Length
            ? roster.Slots[slot] : null;
        if (npc == null)
            return "Empty position";

        var sb = new StringBuilder();
        sb.Append("Position ").Append(slot + 1);
        if (slot == 0)
            sb.Append(", front");
        else if (slot == roster.Slots.Length - 1)
            sb.Append(", back");
        sb.Append(": ").Append(Name(npc));
        sb.Append(", ").Append(npc.Hp).Append(" health");
        sb.Append(", speed ").Append(npc.Speed);
        if (npc.IsBoss)
            sb.Append(", boss");
        else if (npc.IsNamed)
            sb.Append(", named");

        string champion = ChampionClause(roster, slot);
        if (champion.Length > 0)
            sb.Append(", ").Append(champion);
        return sb.ToString();
    }

    /// <summary>Full stats for the Alt+T read: the brief line plus energy, hand size and every
    /// non-zero resistance.</summary>
    public static string Detail(Roster roster, int slot)
    {
        var npc = roster != null && roster.Slots != null && slot >= 0 && slot < roster.Slots.Length
            ? roster.Slots[slot] : null;
        if (npc == null)
            return "No enemy details";

        var sb = new StringBuilder(BriefLine(roster, slot));
        sb.Append(". Energy ").Append(npc.Energy).Append(", ").Append(npc.EnergyTurn).Append(" per turn");
        sb.Append(", ").Append(npc.CardsInHand).Append(" cards in hand");

        var resists = new List<string>();
        AddResist(resists, "slashing", npc.ResistSlashing);
        AddResist(resists, "blunt", npc.ResistBlunt);
        AddResist(resists, "piercing", npc.ResistPiercing);
        AddResist(resists, "fire", npc.ResistFire);
        AddResist(resists, "cold", npc.ResistCold);
        AddResist(resists, "lightning", npc.ResistLightning);
        AddResist(resists, "mind", npc.ResistMind);
        AddResist(resists, "holy", npc.ResistHoly);
        AddResist(resists, "shadow", npc.ResistShadow);
        sb.Append(". ").Append(resists.Count == 0
            ? "No resistances"
            : "Resistances: " + string.Join(", ", resists.ToArray()));
        return sb.ToString();
    }

    private static void AddResist(List<string> parts, string word, int value)
    {
        if (value == 0)
            return;
        parts.Add(value < 0
            ? word + " weakness " + (-value)
            : word + " " + value);
    }

    /// <summary>
    /// The champion badge the game draws over the front (slot 0) and back (last slot) enemies when
    /// they are named in a randomized line-up, spoken as the immunity it announces. Empty otherwise
    /// — a fixed combat draws no badges, so speaking one would be information the screen never
    /// showed.
    /// </summary>
    private static string ChampionClause(Roster roster, int slot)
    {
        var npc = roster.Slots[slot];
        if (!roster.Randomized || npc == null || !npc.IsNamed)
            return "";
        if (slot != 0 && slot != roster.Slots.Length - 1)
            return "";

        string auraId = Functions.GetAuraCurseImmune(npc, roster.NodeDataId);
        var aura = Globals.Instance != null ? Globals.Instance.GetAuraCurseData(auraId) : null;
        if (aura == null)
            return "champion";
        string name = CardSpeech.AuraName(aura);
        return "champion, immune to " + (name.Length > 0 ? name : auraId);
    }
}
