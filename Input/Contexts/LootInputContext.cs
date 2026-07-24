using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The item-loot scene (post-boss/chest item picks, also Obelisk-challenge chests). Arrows walk
/// the loot row (items, gold pile, Restart), Enter takes the focused thing for the hero whose
/// turn it is, Tab toggles the party-review region (1–4 jump straight to a hero), Ctrl+Up/Down
/// drill the focused item's detail lines and Escape exits the drill. The multiplayer restart
/// confirm is answered in place through <see cref="AlertHelper"/>. All state and speech live in
/// <see cref="LootScreenManager"/>; this class only maps keys.
/// </summary>
public class LootInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            var lm = LootManager.Instance;
            if (lm == null)
                return false;
            // Yield while the deck/character window is open so the game's own handling (Escape
            // to close) works untouched; that window is not accessible yet.
            if (lm.characterWindowUI != null && lm.characterWindowUI.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (AlertHelper.Active)
            return true;

        if (InputRouter.CtrlHeld)
        {
            // Ctrl+Up/Down = card detail drill (combat convention); Ctrl+Left/Right reserved.
            if (direction.y > 0f)
                LootScreenManager.DrillNext(-1);
            else if (direction.y < 0f)
                LootScreenManager.DrillNext(1);
            return true;
        }

        // Each region is a one-dimensional list; both axes traverse it (the loot row reads
        // left-to-right, the party column top-to-bottom).
        if (direction.x > 0f || direction.y < 0f)
            LootScreenManager.MoveFocus(1);
        else if (direction.x < 0f || direction.y > 0f)
            LootScreenManager.MoveFocus(-1);
        // Always swallow so the game's own controller nav never warps the cursor underneath us.
        return true;
    }

    public override bool OnConfirm()
    {
        if (AlertHelper.Confirm())
            return true;
        LootScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        if (AlertHelper.Cancel())
            return true;
        // Only consume Escape to leave a drill; otherwise the game keeps its own behaviour.
        return LootScreenManager.TryDrillExit();
    }

    public override bool OnTab(bool backwards)
    {
        LootScreenManager.ToggleRegion();
        return true;
    }

    public override bool OnNumber(int n)
    {
        LootScreenManager.JumpToHero(n);
        return true;
    }
}
