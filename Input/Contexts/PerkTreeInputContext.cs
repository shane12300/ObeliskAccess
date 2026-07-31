using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The perk tree overlay (<c>PerkTree</c>) — the topmost modal both of the hero-selection family
/// (over the character window or from a portrait's perk-badge button) and of the in-run
/// character sheet, whose Perks tab opens the same tree mid-run (map, town incl. tier 0, combat,
/// rewards, loot). Registered above the character-sheet context accordingly. Space is the
/// confirm-build key; verified inert in the game outside combat, so it needs no suppression in
/// the router patches — and in combat the game blocks its own Space while the character window
/// is open beneath the tree. All state and speech live in <see cref="PerkTreeScreenManager"/>.
/// </summary>
public class PerkTreeInputContext : InputContextBase
{
    /// <summary>Screen-open: the perk tree overlay is up and neither deferred panel (madness/
    /// sandbox) has taken over. The tree is the topmost of its family, so this usually also
    /// means it owns input — but like every context, this is raw screen state.</summary>
    public static bool IsCurrentlyActive
    {
        get
        {
            if (!PerkTreeScreenManager.IsOpen)
                return false;
            if (MadnessManager.Instance != null && MadnessManager.Instance.IsActive())
                return false;
            if (SandboxManager.Instance != null && SandboxManager.Instance.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // Alt+T/I review keys — over combat (the tree opens mid-run from the sheet) they collide
    // with the game's emote binds, and a bare Alt would right-click under the stale cursor.
    public override bool UsesAltLetters => true;
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            PerkTreeScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            PerkTreeScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            PerkTreeScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            PerkTreeScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        PerkTreeScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        return PerkTreeScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        PerkTreeScreenManager.CycleCategory(backwards);
        return true;
    }

    public override bool OnSpace()
    {
        PerkTreeScreenManager.Confirm();
        return true;
    }

    // Swallow digits — they'd otherwise fall through to the hero-selection screen below.
    public override bool OnNumber(int n) => true;
}
