using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The town hub. Up/Down walk the hub items (buildings, upgrades, Ready, treasures), Enter
/// activates, Tab toggles the party strip (Up/Down heroes, 1–4 slot jump — same as the map).
/// Unlike map/event/combat this context stays active while a confirm alert is up: the hub's own
/// actions (treasure claims, tutorial-step warnings) raise alerts that must be answerable in
/// place, so Enter/Escape route to <see cref="AlertHelper"/> first. All state and speech live in
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
            // Deliberately NOT excluded: an active AlertManager dialog — this context answers it.
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (AlertHelper.Active)
            return true; // inert while a dialog is up, but don't let the game move beneath it

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
        if (AlertHelper.Confirm())
            return true;
        TownScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        // Escape answers "no" to an open dialog; otherwise the hub is the base level — let the
        // game keep Escape (pause/options menu).
        return AlertHelper.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        if (AlertHelper.Active)
            return true;
        TownScreenManager.ToggleRegion();
        return true;
    }

    public override bool OnNumber(int n)
    {
        if (AlertHelper.Active)
            return true;
        TownScreenManager.JumpToSlot(n - 1);
        return true;
    }
}
