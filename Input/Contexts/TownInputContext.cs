using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The town hub. Up/Down walk the hub items (buildings, upgrades, Ready, treasures), Enter
/// activates, Tab toggles the party strip (Up/Down heroes, 1–4 slot jump — same as the map).
/// Confirm alerts raised by hub actions (treasure claims, tutorial-step warnings) are owned by
/// the global alert dialogue, which outranks this context. All state and speech live in
/// <see cref="TownScreenManager"/>; this class only maps keys.
/// </summary>
public class TownInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            var tm = TownManager.Instance;
            if (tm == null)
                return false;
            if (CardCraftManager.Instance != null)          // a service screen owns input
                return false;
            if (tm.townUpgradeWindow != null && tm.townUpgradeWindow.IsActive())
                return false;
            if (tm.characterWindow != null && tm.characterWindow.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // Ctrl+G opens the give window — the Ctrl press must not synthetically click a hub building.
    public override bool UsesCtrlModifier => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            TownScreenManager.Move(-1);
        else if (direction.y < 0f)
            TownScreenManager.Move(1);
        // Left/Right: nothing on the hub, but swallow so the game's own controller nav
        // doesn't warp the cursor underneath us.
        return true;
    }

    public override bool OnConfirm()
    {
        TownScreenManager.Activate();
        return true;
    }

    // Escape: the hub is the base level — let the game keep it (pause/options menu).

    public override bool OnTab(bool backwards)
    {
        TownScreenManager.ToggleRegion();
        return true;
    }

    public override bool OnNumber(int n)
    {
        TownScreenManager.JumpToSlot(n - 1);
        return true;
    }
}
