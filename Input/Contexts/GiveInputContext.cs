using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// MP give-gold/dust window (GiveManager) — a modal over the map or town, registered above Event/
/// Town/Map per the game's own modality order (the game force-closes the upgrades and character
/// windows when it opens). ←/→ target, ↑/↓ amount (Ctrl/Shift/both scale the step), Tab currency,
/// Enter send, Escape close.
/// </summary>
public class GiveInputContext : InputContextBase
{
    /// <summary>Static so the RouterInputPatches suppressions can gate on it.</summary>
    public static bool IsCurrentlyActive => GiveScreenManager.Open;

    public override bool IsActive => IsCurrentlyActive;


    public override bool OnMove(Vector2 direction)
    {
        if (direction.x < 0f)
            GiveScreenManager.CycleTarget(-1);
        else if (direction.x > 0f)
            GiveScreenManager.CycleTarget(1);
        else if (direction.y > 0f)
            GiveScreenManager.AdjustQuantity(1);
        else if (direction.y < 0f)
            GiveScreenManager.AdjustQuantity(-1);
        return true;
    }

    public override bool OnConfirm()
    {
        GiveScreenManager.Confirm();
        return true;
    }

    public override bool OnCancel()
    {
        GiveScreenManager.Close();
        return true;
    }

    public override bool OnTab(bool backwards)
    {
        GiveScreenManager.SwitchCurrency();
        return true;
    }

    public override bool OnNumber(int n) => true; // focus trap — digits do nothing here
}
