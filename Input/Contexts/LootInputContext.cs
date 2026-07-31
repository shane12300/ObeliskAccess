using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The item-loot scene (post-boss/chest item picks, also Obelisk-challenge chests). Arrows walk
/// the loot row (items, gold pile, Restart), Enter takes the focused thing for the hero whose
/// turn it is, Tab toggles the party-review region (1–4 jump straight to a hero), Ctrl+Up/Down
/// drill the focused item's detail lines and Escape exits the drill. The multiplayer restart
/// confirm is owned by the global alert dialogue. All state and speech live in
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
            // Yield while the character sheet is open — CharWindowInputContext (registered
            // above us) owns it.
            if (lm.characterWindowUI != null && lm.characterWindowUI.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // Ctrl+Up/Down is the item drill — the bare-Ctrl click could take an item under the cursor.
    public override bool UsesCtrlModifier => true;
    // Alt+T/I/G/R review keys — the bare-Alt right-click could pop a loot item's window.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
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
        LootScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
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
