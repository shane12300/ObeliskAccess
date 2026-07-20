using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The town upgrades window (supply tree), modal above the hub. Left/Right pick the building
/// column, Up/Down walk its upgrade chain, Tab cycles grid / sell-supply / exit, Enter buys via
/// the game's confirm alert (answered in place through <see cref="AlertHelper"/>), Escape closes
/// the sell panel then the window. All state and speech live in
/// <see cref="TownUpgradeScreenManager"/>; this class only maps keys.
/// </summary>
public class TownUpgradeInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            var tm = TownManager.Instance;
            if (tm == null || tm.townUpgradeWindow == null)
                return false;
            if (!tm.townUpgradeWindow.IsActive())
                return false;
            // Alerts stay with us — the buy confirmation must be answerable in place.
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (AlertHelper.Active)
            return true;

        if (direction.y > 0f)
            TownUpgradeScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            TownUpgradeScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            TownUpgradeScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            TownUpgradeScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        if (AlertHelper.Confirm())
            return true;
        TownUpgradeScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        if (AlertHelper.Cancel())
            return true;
        return TownUpgradeScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        if (AlertHelper.Active)
            return true;
        TownUpgradeScreenManager.CycleFocus();
        return true;
    }

    public override bool OnNumber(int n) => true; // swallow; no slot jumps in the tree
}
