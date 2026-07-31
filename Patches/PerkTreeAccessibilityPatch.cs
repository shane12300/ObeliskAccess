using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Keyboard navigation + speech for the perk tree overlay (<c>PerkTree</c>), where perk points
/// are spent before a run. Tab cycles the four categories (General / Physical / Elemental /
/// Mystical) and then a Controls region (Confirm / Reset / Import / Export / save slots / Exit).
/// Within a category, Up/Down move between the tree's rows (announcing the row's point
/// threshold), Left/Right between the nodes of a row, Enter toggles the focused perk, and Space
/// confirms the build. Escape closes — the game itself raises the unsaved-changes confirm, which
/// the global alert dialogue owns.
///
/// Scope: everywhere the tree opens — the hero-selection scene, and mid-run over the character
/// sheet's Perks tab (map, town incl. tier 0, combat, rewards, loot). The context is registered
/// above the character-sheet context and all the screens beneath it.
/// </summary>
internal static class PerkTreeScreenManager
{
    private enum Focus { Nodes, Controls }

    private sealed class NodeEntry
    {
        public PerkNode Node;        // the node SelectPerk needs (child node for choices)
        public PerkNodeData PND;     // the child's own data (Perk is always non-null)
        public bool IsChoice;
        public string GroupId;       // choose-one parent's id — a grid row can hold several groups
        public int ChoiceIndex;      // 1-based within the group
        public int ChoiceCount;
    }

    private sealed class ControlRow
    {
        public Func<string> Read;
        public Action Activate;
    }

    private static bool _open;
    private static Focus _focus;
    private static int _category;                 // 0-3
    private static readonly List<List<NodeEntry>> _grid = new List<List<NodeEntry>>();
    private static int _row;
    private static int _col;
    private static int _ctrlIndex = -1;
    private static readonly List<ControlRow> _ctrlRows = new List<ControlRow>();
    private static int _openedFrame = -1;
    private static int _confirmAnnouncedFrame = -1;

    private static readonly string[] CategoryKeys = { "general", "physical", "elemental", "mystical" };

    private static PerkTree Tree => PerkTree.Instance;

    public static bool IsOpen
        => _open && Tree != null && Tree.IsActive();

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Poller tick: drops state when the tree went away without HideAction (safety).</summary>
    public static void Tick()
    {
        if (_open && (Tree == null || !Tree.IsActive()))
            ResetState();
    }

    private static void ResetState()
    {
        _open = false;
        _focus = Focus.Nodes;
        _category = 0;
        _grid.Clear();
        _row = 0;
        _col = 0;
        _ctrlIndex = -1;
        _ctrlRows.Clear();
        _openedFrame = -1;
    }

    /// <summary>PerkTree.Show landed (from the character window's Perks tab, or the portrait
    /// perk-badge button — the latter with no character window beneath).</summary>
    public static void OnOpened()
    {
        var tree = Tree;
        if (tree == null || !tree.IsActive())
            return;
        _open = true;
        _openedFrame = Time.frameCount;
        _focus = Focus.Nodes;
        _category = 0;
        _row = 0;
        _col = 0;
        _ctrlIndex = -1;
        BuildGrid();
        var sb = new StringBuilder();
        sb.Append("Perk tree: ").Append(HeroName()).Append(". ");
        sb.Append(PointsSummary()).Append(' ');
        sb.Append(CategoryName(0)).Append(" category. ");
        if (!tree.CanModify())
            sb.Append("Perks can't be changed right now — review only. ");
        else
            sb.Append("Enter takes or removes a perk, Space saves, "
                + "Tab switches category. Unsaved changes ask before closing. ");
        sb.Append("Up and down for rows, left and right for perks.");
        SpeechManager.Speak(sb.ToString());
    }

    public static void OnClosed()
    {
        if (!_open)
            return;
        ResetState();
        string closing = "Perk tree closed.";
        if (CharWindowScreenManager.IsOpen)
        {
            // Mid-run: the tree closed back onto the character sheet beneath it.
            closing += " " + CharWindowScreenManager.ReturnAnnouncement();
        }
        else if (CharPopupScreenManager.IsOpen)
        {
            string id = HeroSelectionManager.Instance != null
                && HeroSelectionManager.Instance.charPopup != null
                ? HeroSelectionManager.Instance.charPopup.GetActive() : "";
            if (!string.IsNullOrEmpty(id))
            {
                int points = PlayerManager.Instance.GetPerkPointsAvailable(id);
                closing += " Character window, " + points
                    + (points == 1 ? " perk point" : " perk points") + " remaining.";
            }
        }
        SpeechManager.Speak(closing);
    }

    /// <summary>The build was saved (Confirm button by mouse, or our Space path).</summary>
    public static void OnConfirmed()
    {
        if (!_open || Time.frameCount == _confirmAnnouncedFrame)
            return;
        SpeechManager.Speak("Perks saved for " + HeroName() + ".");
    }

    private static string HeroName()
    {
        var scd = Tree != null ? Globals.Instance.GetSubClassData(Tree.GetActiveHero()) : null;
        return scd != null ? scd.CharacterName : "this hero";
    }

    // ------------------------------------------------------------------ points

    private static int UsedPoints()
        => Traverse.Create(Tree).Field<int>("usedPoints").Value;

    private static int TotalPoints()
        => Traverse.Create(Tree).Field<int>("totalAvailablePoints").Value;

    private static List<string> SelectedPerks()
        => Traverse.Create(Tree).Field<List<string>>("selectedPerks").Value;

    private static Dictionary<string, List<string>> TeamPerks()
        => Traverse.Create(Tree).Field<Dictionary<string, List<string>>>("teamPerks").Value;

    private static Dictionary<string, PerkNode> PerkNodes()
        => Traverse.Create(Tree).Field<Dictionary<string, PerkNode>>("perkNodes").Value;

    private static string PointsSummary()
        => Tree.GetPointsAvailable() + " of " + TotalPoints() + " points available, "
            + UsedPoints() + " used.";

    /// <summary>Alt+I: points summary with the per-category split and the dirty flag.</summary>
    public static void SpeakPointsSummary()
    {
        if (!IsOpen)
            return;
        var perCategory = Traverse.Create(Tree).Field<int[]>("usedPointsCategory").Value;
        var sb = new StringBuilder();
        sb.Append(PointsSummary());
        if (perCategory != null && perCategory.Length >= 4)
        {
            sb.Append(' ');
            for (int i = 0; i < 4; i++)
            {
                sb.Append(CategoryName(i)).Append(' ').Append(perCategory[i]);
                sb.Append(i < 3 ? ", " : ". ");
            }
        }
        if (IsDirty())
            sb.Append("Unsaved changes — press Space to save.");
        SpeechManager.Speak(sb.ToString());
    }

    private static bool IsDirty()
        => Tree != null && Tree.buttonConfirm != null && Tree.buttonConfirm.buttonEnabled;

    private static string CategoryName(int index)
    {
        string text = CardSpeech.CleanFlat(Texts.Instance.GetText(CategoryKeys[index]));
        return text == "" ? CategoryKeys[index] : Functions.UppercaseFirst(text);
    }

    // ------------------------------------------------------------------ grid building

    /// <summary>Builds the per-category grid: rows by the node's Row, nodes by Column; a
    /// choose-one parent is replaced in place by one entry per connected child, so the choices
    /// are walked left/right exactly where the parent sits.</summary>
    private static void BuildGrid()
    {
        _grid.Clear();
        var tree = Tree;
        if (tree == null)
            return;
        var datas = tree.GetPerkNodeDatas();
        var nodes = PerkNodes();
        if (datas == null || nodes == null)
            return;

        // Children of choose-one groups appear in the data dict too — exclude them from the
        // top-level walk; they re-enter through their parent's expansion.
        var connectedIds = new HashSet<string>();
        foreach (var pair in datas)
        {
            var connected = pair.Value.PerksConnected;
            if (connected == null)
                continue;
            foreach (var child in connected)
            {
                if (child != null)
                    connectedIds.Add(child.Id);
            }
        }

        var byRow = new SortedDictionary<int, List<PerkNodeData>>();
        foreach (var pair in datas)
        {
            var pnd = pair.Value;
            if (pnd.Type != _category || connectedIds.Contains(pnd.Id))
                continue;
            if (!byRow.TryGetValue(pnd.Row, out var list))
                byRow[pnd.Row] = list = new List<PerkNodeData>();
            list.Add(pnd);
        }

        foreach (var pair in byRow)
        {
            var entries = new List<NodeEntry>();
            pair.Value.Sort((a, b) => a.Column.CompareTo(b.Column));
            foreach (var pnd in pair.Value)
            {
                var connected = pnd.PerksConnected;
                if (connected != null && connected.Length > 0)
                {
                    for (int i = 0; i < connected.Length; i++)
                    {
                        var child = connected[i];
                        if (child == null || child.Perk == null
                            || !nodes.TryGetValue(child.Id, out var childNode))
                            continue;
                        entries.Add(new NodeEntry
                        {
                            Node = childNode,
                            PND = child,
                            IsChoice = true,
                            GroupId = pnd.Id,
                            ChoiceIndex = i + 1,
                            ChoiceCount = connected.Length,
                        });
                    }
                }
                else if (pnd.Perk != null && nodes.TryGetValue(pnd.Id, out var node))
                {
                    entries.Add(new NodeEntry { Node = node, PND = pnd });
                }
            }
            if (entries.Count > 0)
            {
                _grid.Add(entries);
            }
        }
    }

    // ------------------------------------------------------------------ node speech

    private static string NodeLine(NodeEntry entry)
    {
        var sb = new StringBuilder();
        // An available perk carries no state prefix — absence of "selected"/"locked"/"not
        // enough points" means it can be taken, and the shorter line reads faster.
        string state = NodeState(entry);
        if (state != "")
            sb.Append(state).Append(". ");
        if (entry.IsChoice)
        {
            sb.Append("Choice ").Append(entry.ChoiceIndex).Append(" of ")
              .Append(entry.ChoiceCount);
            string sibling = SelectedSibling(entry);
            if (sibling != "" && !IsSelected(entry))
                sb.Append(" — taking this replaces the chosen option");
            sb.Append(". ");
        }
        string stack = StackNote(entry);
        if (stack != "")
            sb.Append(stack).Append(". ");
        sb.Append(Sentence(ShortEffect(entry.PND)));
        sb.Append(" Costs ").Append(Cost(entry)).Append(Cost(entry) == 1 ? " point." : " points.");
        return sb.ToString();
    }

    /// <summary>Ensures a game-text fragment ends a sentence before more clauses follow.</summary>
    private static string Sentence(string text)
    {
        text = text.TrimEnd();
        return text.EndsWith(".") || text.EndsWith("!") || text.EndsWith("?")
            ? text : text + ".";
    }

    private static int Cost(NodeEntry entry)
        => Tree.GetPointsForNode(entry.PND);

    private static bool IsSelected(NodeEntry entry)
        => entry.Node != null && entry.Node.IsSelected();

    private static string NodeState(NodeEntry entry)
    {
        var node = entry.Node;
        if (IsSelected(entry))
            return "Selected";
        if (IsTownLocked(entry))
            return "Locked in town";
        if (node != null && node.IsLocked())
        {
            // The row threshold and a missing prerequisite can both apply — report every
            // unmet gate, row first (the game's tooltip order).
            bool rowLocked = UsedPoints() < Tree.GetPointsNeeded(entry.PND.Row);
            bool prereqMissing = entry.PND.PerkRequired != null
                && !PerkSelected(entry.PND.PerkRequired);
            string rowClause = "the row needs " + Tree.GetPointsNeeded(entry.PND.Row)
                + " points spent below";
            string prereqClause = CardSpeech.CleanFlat(Texts.Instance.GetText("previousRequired"));
            if (rowLocked && prereqMissing)
                return "Locked — " + rowClause + ", and " + prereqClause;
            if (prereqMissing)
                return "Locked — " + prereqClause;
            return "Locked — " + rowClause;
        }
        // With a same-group choice already selected, Enter swaps — the game charges no
        // points for a swap, so a low balance is no obstacle.
        if (entry.IsChoice && SelectedSibling(entry) != "")
            return "";
        int available = Tree.GetPointsAvailable();
        int cost = Cost(entry);
        if (available < cost)
            return "Not enough points — need " + (cost - available) + " more";
        return ""; // available — no prefix, the effect leads
    }

    /// <summary>The per-node town lock (gold/shard perks in a tier-0 town): the game shows a
    /// lock icon and <c>SelectPerk</c> refuses; row locks don't cover it.</summary>
    private static bool IsTownLocked(NodeEntry entry)
        => entry.Node != null && entry.Node.iconLock != null
            && entry.Node.iconLock.gameObject.activeSelf;

    private static bool PerkSelected(PerkNodeData pnd)
    {
        if (pnd == null || pnd.Perk == null)
            return false;
        var selected = SelectedPerks();
        return selected != null && selected.Contains(pnd.Perk.Id.ToLower());
    }

    private static string SelectedSibling(NodeEntry entry)
    {
        if (!entry.IsChoice)
            return "";
        // Find a selected sibling of the SAME choose-one group — a grid row can hold several
        // independent groups (e.g. Physical row 7 has both the Leech and Rust triples), and a
        // selection in one group says nothing about the other.
        if (_row < 0 || _row >= _grid.Count)
            return "";
        foreach (var other in _grid[_row])
        {
            if (other != entry && other.IsChoice && other.GroupId == entry.GroupId
                && IsSelected(other))
                return ShortEffect(other.PND);
        }
        return "";
    }

    private static string StackNote(NodeEntry entry)
    {
        bool notStack = entry.PND.NotStack;
        if (!notStack)
            return "";
        var team = TeamPerks();
        if (team == null || entry.PND.Perk == null
            || !team.TryGetValue(entry.PND.Perk.Id, out var holders) || holders == null
            || holders.Count == 0)
            return "Does not stack";
        var names = new List<string>();
        foreach (var subclass in holders)
        {
            var scd = Globals.Instance.GetSubClassData(subclass);
            names.Add(scd != null ? scd.CharacterName : subclass);
        }
        return "Does not stack — " + string.Join(" and ", names) + " already has this";
    }

    /// <summary>A compact effect phrase, mirroring the ladder NewPerkDescription uses but
    /// without the status/cost markup.</summary>
    private static string ShortEffect(PerkNodeData pnd)
    {
        var perk = pnd != null ? pnd.Perk : null;
        if (perk == null)
            return "";
        string text;
        if (perk.CustomDescription != null && perk.CustomDescription.Trim() != "")
            text = Texts.Instance.GetText(perk.CustomDescription);
        else if (perk.MaxHealth > 0)
            text = string.Format(Texts.Instance.GetText("itemMaxHp"), perk.IconTextValue);
        else if (perk.AdditionalCurrency > 0)
            text = string.Format(Texts.Instance.GetText("itemInitialCurrencySingle"), perk.IconTextValue);
        else if (perk.AdditionalShards > 0)
            text = string.Format(Texts.Instance.GetText("itemInitialShardsSingle"), perk.IconTextValue);
        else if (perk.SpeedQuantity > 0)
            text = string.Format(Texts.Instance.GetText("itemSpeed"), perk.IconTextValue);
        else if (perk.HealQuantity > 0)
            text = string.Format(Texts.Instance.GetText("itemHealDone").Replace("<c>", ""), perk.IconTextValue);
        else if (perk.EnergyBegin > 0)
            text = string.Format(Texts.Instance.GetText("itemInitialEnergy"), perk.IconTextValue);
        else if (perk.AuracurseBonus != null)
            // The game's string ("Charges that you apply {0}") never names the aura — sighted
            // players read it off the node's icon, so speech must supply it or every charge
            // perk in the tree (68 of them) sounds identical.
            text = CardSpeech.AuraName(perk.AuracurseBonus) + " — "
                + string.Format(Texts.Instance.GetText("perkAuraDescription"), perk.IconTextValue);
        else if (perk.ResistModified == Enums.DamageType.All)
            text = string.Format(Texts.Instance.GetText("itemAllResistances"), perk.IconTextValue);
        else if (perk.DamageFlatBonus == Enums.DamageType.All)
            text = string.Format(Texts.Instance.GetText("itemAllDamages"), perk.IconTextValue);
        else if (perk.DamageFlatBonus != 0)
            text = string.Format(Texts.Instance.GetText("itemSingleDamage"),
                Texts.Instance.GetText(Enum.GetName(typeof(Enums.DamageType), perk.DamageFlatBonus)),
                perk.IconTextValue);
        else
            text = "";
        string clean = CardSpeech.CleanFlat(text);
        return clean == "" ? "Perk" : Functions.UppercaseFirst(clean);
    }

    private static string RowIntro(int gridRow, NodeEntry focused)
    {
        // Threshold from the focused entry's own data row, not the grid row's nominal one:
        // the game locks each node by its data row, and one choose-one cluster (Stealth)
        // carries children a row below the square they're drawn in.
        int needed = Tree.GetPointsNeeded(focused.PND.Row);
        if (needed <= 0)
            return "Row " + (gridRow + 1) + ".";
        int used = UsedPoints();
        if (used >= needed)
            return "Row " + (gridRow + 1) + " — requires " + needed + " points spent, unlocked.";
        return "Row " + (gridRow + 1) + " — locked, spend " + (needed - used)
            + " more " + (needed - used == 1 ? "point" : "points") + " in lower rows.";
    }

    // ------------------------------------------------------------------ input handlers

    public static void MoveVertical(int delta)
    {
        if (!IsOpen)
            return;
        if (_focus == Focus.Controls)
        {
            MoveControls(delta);
            return;
        }
        BuildGrid();
        if (_grid.Count == 0)
        {
            SpeechManager.Speak("No perks in this category.");
            return;
        }
        _row = Mathf.Clamp(_row + delta, 0, _grid.Count - 1);
        _col = Mathf.Clamp(_col, 0, _grid[_row].Count - 1);
        HeroSpeech.PlayPerkFocusSound();
        SpeechManager.Speak(RowIntro(_row, _grid[_row][_col]) + " " + NodeLine(_grid[_row][_col]));
    }

    public static void MoveHorizontal(int delta)
    {
        if (!IsOpen)
            return;
        if (_focus == Focus.Controls)
        {
            MoveControls(delta);
            return;
        }
        BuildGrid();
        if (_grid.Count == 0)
        {
            SpeechManager.Speak("No perks in this category.");
            return;
        }
        _row = Mathf.Clamp(_row, 0, _grid.Count - 1);
        _col = Mathf.Clamp(_col + delta, 0, _grid[_row].Count - 1);
        HeroSpeech.PlayPerkFocusSound();
        SpeechManager.Speak(NodeLine(_grid[_row][_col]));
    }

    public static void CycleCategory(bool backwards)
    {
        if (!IsOpen)
            return;
        // 5 stops: the four categories, then Controls.
        int current = _focus == Focus.Controls ? 4 : _category;
        int next = (current + (backwards ? 4 : 1)) % 5;
        HeroSpeech.PlayButtonFocusSound();
        if (next == 4)
        {
            _focus = Focus.Controls;
            BuildControlRows();
            _ctrlIndex = _ctrlRows.Count > 0 ? 0 : -1;
            SpeechManager.Speak("Controls. "
                + (_ctrlRows.Count > 0 ? _ctrlRows[0].Read() : "Nothing available."));
            return;
        }
        _focus = Focus.Nodes;
        _category = next;
        Tree.SetCategory(next);
        _row = 0;
        _col = 0;
        BuildGrid();
        var perCategory = Traverse.Create(Tree).Field<int[]>("usedPointsCategory").Value;
        string spent = perCategory != null && next < perCategory.Length
            ? ", " + perCategory[next] + " points spent here" : "";
        string landing = _grid.Count > 0
            ? " " + RowIntro(0, _grid[0][0]) + " " + NodeLine(_grid[0][0])
            : " No perks.";
        SpeechManager.Speak(CategoryName(next) + " category" + spent + "." + landing);
    }

    public static void Activate()
    {
        if (!IsOpen)
            return;
        if (Time.frameCount == _openedFrame)
            return; // the Enter that opened the tree
        if (_focus == Focus.Controls)
        {
            if (_ctrlIndex >= 0 && _ctrlIndex < _ctrlRows.Count)
                _ctrlRows[_ctrlIndex].Activate();
            return;
        }
        BuildGrid();
        if (_grid.Count == 0 || _row >= _grid.Count)
            return;
        _col = Mathf.Clamp(_col, 0, _grid[_row].Count - 1);
        var entry = _grid[_row][_col];
        var tree = Tree;

        if (!tree.IsOwner)
        {
            SpeechManager.Speak("You can only edit your own hero's perks.");
            return;
        }
        if (!tree.CanModify())
        {
            SpeechManager.Speak("Perks can't be changed right now.");
            return;
        }
        if (IsTownLocked(entry))
        {
            SpeechManager.Speak("Locked in town — this perk can only be changed"
                + " before starting a run.");
            return;
        }
        if (entry.Node != null && entry.Node.IsLocked())
        {
            SpeechManager.Speak(NodeState(entry) + ".");
            return;
        }

        string perkId = entry.PND.Perk.Id.ToLower();
        var before = SelectedPerks();
        bool wasSelected = before != null && before.Contains(perkId);
        // A take that displaces a same-group choice is a swap: announce what it replaced.
        string replaced = !wasSelected ? SelectedSibling(entry) : "";
        tree.SelectPerk(entry.PND.Perk.Id, entry.Node);
        var after = SelectedPerks();
        bool isSelected = after != null && after.Contains(perkId);

        if (isSelected == wasSelected)
        {
            // Refused — say why.
            if (!wasSelected)
            {
                int missing = Cost(entry) - tree.GetPointsAvailable();
                SpeechManager.Speak(missing > 0 && replaced == ""
                    ? "Not enough points — need " + missing + " more."
                    : "Could not take this perk.");
            }
            else
            {
                SpeechManager.Speak(RemovalRefusalReason(entry));
            }
            return;
        }
        if (isSelected)
            SpeechManager.Speak("Took " + Sentence(ShortEffect(entry.PND))
                + (replaced != "" ? " Replaced " + Sentence(replaced) : "")
                + " " + tree.GetPointsAvailable() + " points left.");
        else
            SpeechManager.Speak("Removed " + Sentence(ShortEffect(entry.PND)) + " "
                + tree.GetPointsAvailable() + " points available.");
    }

    private static string RemovalRefusalReason(NodeEntry entry)
    {
        // A selected perk that SelectPerk refused to remove: either a dependent perk needs it,
        // or removing it would drop a lower row below a higher selected row's threshold.
        var datas = Tree.GetPerkNodeDatas();
        if (datas != null)
        {
            foreach (var pair in datas)
            {
                var pnd = pair.Value;
                if (pnd.PerkRequired != null && pnd.PerkRequired.Id == entry.PND.Id
                    && PerkSelected(pnd))
                    return "Can't remove — a selected perk requires it.";
            }
        }
        return "Can't remove — a higher row would fall below its point requirement.";
    }

    /// <summary>Space: save the build.</summary>
    public static void Confirm()
    {
        if (!IsOpen)
            return;
        var tree = Tree;
        if (!tree.CanModify() || !tree.IsOwner)
        {
            SpeechManager.Speak("Perks can't be changed right now.");
            return;
        }
        if (!IsDirty())
        {
            SpeechManager.Speak("No changes to save.");
            return;
        }
        if (tree.GetPointsAvailable() < 0)
        {
            SpeechManager.Speak("Too many points spent — remove a perk first.");
            return;
        }
        _confirmAnnouncedFrame = Time.frameCount;
        tree.PerksAssignConfirm();
        SpeechManager.Speak("Perks saved for " + HeroName() + ".");
    }

    public static bool Cancel()
    {
        if (!IsOpen)
            return false;
        Tree.Hide(); // raises the unsaved-changes confirm itself when dirty
        return true;
    }

    /// <summary>Alt+T: the game's full tooltip for the focused node (incl. the party list).</summary>
    public static void SpeakNodeDetail()
    {
        if (!IsOpen)
            return;
        if (_focus == Focus.Controls)
        {
            if (_ctrlIndex >= 0 && _ctrlIndex < _ctrlRows.Count)
                SpeechManager.Speak(_ctrlRows[_ctrlIndex].Read());
            return;
        }
        BuildGrid();
        if (_grid.Count == 0 || _row >= _grid.Count)
            return;
        _col = Mathf.Clamp(_col, 0, _grid[_row].Count - 1);
        var entry = _grid[_row][_col];
        string text = entry.Node != null
            ? CardSpeech.CleanFlat(entry.Node.NewPerkDescription(entry.PND.Perk)) : "";
        if (text == "")
        {
            SpeechManager.Speak(NodeLine(entry));
            return;
        }
        var perk = entry.PND.Perk;
        if (perk != null && perk.AuracurseBonus != null)
        {
            // The game's tooltip leans on the node icon plus a keynote side panel to say
            // which aura the charges belong to — speech carries both: name up front,
            // glossary line after.
            string aura = CardSpeech.AuraName(perk.AuracurseBonus);
            text = aura + " — " + text;
            var kn = Globals.Instance.GetKeyNotesData(perk.AuracurseBonus.Id);
            string glossary = kn != null ? CardSpeech.CleanFlat(kn.Description) : "";
            if (glossary != "")
                text = Sentence(text) + " " + aura + ": " + glossary;
        }
        SpeechManager.Speak(text);
    }

    // ------------------------------------------------------------------ controls region

    private static void MoveControls(int delta)
    {
        BuildControlRows();
        if (_ctrlRows.Count == 0)
        {
            SpeechManager.Speak("No controls available.");
            return;
        }
        _ctrlIndex = _ctrlIndex < 0
            ? (delta > 0 ? 0 : _ctrlRows.Count - 1)
            : Mathf.Clamp(_ctrlIndex + delta, 0, _ctrlRows.Count - 1);
        HeroSpeech.PlayButtonFocusSound();
        SpeechManager.Speak(_ctrlRows[_ctrlIndex].Read());
    }

    private static void BuildControlRows()
    {
        _ctrlRows.Clear();
        var tree = Tree;
        if (tree == null)
            return;

        if (tree.buttonConfirm != null && tree.buttonConfirm.gameObject.activeSelf)
        {
            _ctrlRows.Add(new ControlRow
            {
                Read = () => IsDirty()
                    ? "Confirm — unsaved changes. Press Enter to save."
                    : "Confirm — no changes to save.",
                Activate = Confirm,
            });
        }
        if (tree.buttonReset != null && tree.buttonReset.gameObject.activeSelf)
        {
            _ctrlRows.Add(new ControlRow
            {
                Read = () => "Reset — deselect every perk. Press Enter.",
                Activate = () =>
                {
                    // PerksReset is a no-op with nothing selected (private selectedPerks).
                    var sel = Traverse.Create(tree)
                        .Field<List<string>>("selectedPerks").Value;
                    if (sel == null || sel.Count == 0)
                    {
                        SpeechManager.Speak("No perks selected — nothing to reset.");
                        return;
                    }
                    tree.PerksReset();
                    SpeechManager.Speak("All perks deselected. " + tree.GetPointsAvailable()
                        + " points available. Press Space or Confirm to save.");
                },
            });
        }
        if (tree.buttonImport != null && tree.buttonImport.gameObject.activeSelf)
        {
            _ctrlRows.Add(new ControlRow
            {
                Read = () => "Import a perk build from a shared code. Press Enter.",
                Activate = () => tree.ImportTree(), // paste alert — the alert dialogue owns it
            });
            _ctrlRows.Add(new ControlRow
            {
                Read = () => "Export this perk build as a shareable code. Press Enter.",
                Activate = () => tree.ExportTree(), // copy alert
            });
        }
        if (tree.saveSlots != null && tree.saveSlots.gameObject.activeSelf
            && tree.perkSlot != null)
        {
            for (int i = 0; i < tree.perkSlot.Length; i++)
            {
                var slot = tree.perkSlot[i];
                if (slot == null || !slot.gameObject.activeSelf)
                    continue;
                int index = i;
                bool filled = slot.deleteButton != null && slot.deleteButton.gameObject.activeSelf;
                if (filled)
                {
                    _ctrlRows.Add(new ControlRow
                    {
                        Read = () => "Slot " + (index + 1) + ": "
                            + SlotTitle(slot) + ", " + SlotPoints(slot)
                            + " points. Press Enter to load.",
                        Activate = () => tree.LoadPerkConfig(index), // confirm alert
                    });
                    _ctrlRows.Add(new ControlRow
                    {
                        Read = () => "Delete saved build " + SlotTitle(slot) + ". Press Enter.",
                        Activate = () => tree.RemovePerkSlot(index), // confirm alert
                    });
                }
                else
                {
                    _ctrlRows.Add(new ControlRow
                    {
                        Read = () => "Slot " + (index + 1)
                            + ": empty. Press Enter to save the current build.",
                        Activate = () => tree.SavePerkSlot(index), // name-input alert
                    });
                }
            }
        }
        _ctrlRows.Add(new ControlRow
        {
            Read = () => "Exit the perk tree. Press Enter.",
            Activate = () => tree.Hide(),
        });
    }

    private static string SlotTitle(PerkSlot slot)
        => slot.title != null ? AccessibleMenuBase.StripRichText(slot.title.text) : "saved build";

    private static string SlotPoints(PerkSlot slot)
        => slot.cards != null ? AccessibleMenuBase.StripRichText(slot.cards.text) : "?";
}

// ======================= announcement patches =======================

/// <summary>The tree opened — on the hero-selection screen or mid-run from the character
/// sheet's Perks tab (any scene the sheet exists in, town tier 0 included).</summary>
[HarmonyPatch(typeof(PerkTree), nameof(PerkTree.Show))]
internal static class PerkTreeShowAnnouncePatch
{
    static void Postfix() => PerkTreeScreenManager.OnOpened();
}

/// <summary>The tree actually closed (HideAction runs after any unsaved-changes confirm).</summary>
[HarmonyPatch(typeof(PerkTree), nameof(PerkTree.HideAction))]
internal static class PerkTreeHideAnnouncePatch
{
    static void Postfix() => PerkTreeScreenManager.OnClosed();
}

/// <summary>The build was saved — covers mouse clicks on Confirm too.</summary>
[HarmonyPatch(typeof(PerkTree), nameof(PerkTree.PerksAssignConfirm))]
internal static class PerkTreeConfirmAnnouncePatch
{
    static void Postfix() => PerkTreeScreenManager.OnConfirmed();
}
