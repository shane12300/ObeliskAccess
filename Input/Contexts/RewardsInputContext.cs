using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The post-combat Rewards scene (also event rewards and town Divination rounds). Up/Down move
/// between hero rows, Left/Right across a row's choices (cards, dust, Deck button), Restart is a
/// final pseudo-row, Enter takes the focused choice, Ctrl+Up/Down drill the focused card's detail
/// lines, Escape exits the drill. The Singularity overwrite confirm and the multiplayer restart
/// confirm are owned by the global alert dialogue. All state and speech live in
/// <see cref="RewardsScreenManager"/>; this class only maps keys.
/// </summary>
public class RewardsInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            var rm = RewardsManager.Instance;
            if (rm == null)
                return false;
            // Yield while the deck/character window is open so the game's own handling (Escape
            // to close) works untouched; that window is not accessible yet.
            if (rm.characterWindowUI != null && rm.characterWindowUI.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (InputRouter.CtrlHeld)
        {
            // Ctrl+Up/Down = card detail drill (combat convention); Ctrl+Left/Right reserved.
            if (direction.y > 0f)
                RewardsScreenManager.DrillNext(-1);
            else if (direction.y < 0f)
                RewardsScreenManager.DrillNext(1);
            return true;
        }

        if (direction.y > 0f)
            RewardsScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            RewardsScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            RewardsScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            RewardsScreenManager.MoveHorizontal(1);
        // Always swallow so the game's own controller nav never warps the cursor underneath us.
        return true;
    }

    public override bool OnConfirm()
    {
        RewardsScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        // Only consume Escape to leave a drill; otherwise the game keeps its own behaviour.
        return RewardsScreenManager.TryDrillExit();
    }

    public override bool OnTab(bool backwards) => true;  // swallow; no tab regions on this screen

    public override bool OnNumber(int n) => true;        // swallow; no slot jumps here
}
