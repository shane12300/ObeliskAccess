using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The map screen. Two focus regions (nodes and the party strip), toggled with Tab:
/// <list type="bullet">
/// <item>Nodes: Left/Right walk the reachable nodes; Ctrl+Up/Down descend/ascend the look-ahead of
/// visible-but-not-yet-reachable nodes, Ctrl+Left/Right cycle siblings at that depth; Enter travels.</item>
/// <item>Party: Up/Down read each hero; number keys 1–4 jump to a slot; Enter opens the
/// character sheet for the focused hero.</item>
/// </list>
/// Sits below the corruption modal so travel into a combat can't leave input stranded. All the state
/// and speech lives in <see cref="MapNavigator"/>; this class only maps keys to it.
/// </summary>
public class MapInputContext : InputContextBase
{
    /// <summary>The map's activation test, exposed statically so the <c>DoFirePerformed</c> prefix
    /// (which has no context instance) can gate its Ctrl-click suppression on it.</summary>
    public static bool IsCurrentlyActive
    {
        get
        {
            var m = MapManager.Instance;
            if (m == null)
                return false;
            if (m.IsCorruptionOver())                 // corruption modal owns input
                return false;
            if (EventManager.Instance != null)         // event overlay is up
                return false;
            if (m.characterWindow != null && m.characterWindow.IsActive())
                return false;
            if (m.IsCharacterUnlock())
                return false;
            // World hidden while a mask/loading screen is up, or while an event replaces it.
            if (m.worldTransform == null || !m.worldTransform.gameObject.activeSelf)
                return false;
            // The map mask (ShowMask) does NOT hide the world — MP sync barriers (travel votes,
            // corruption-reward resolution, event closes) sit masked with the world drawn. The
            // mask is only visual for the game (its clicks are physics raycasts), so without
            // this gate a type-ahead Enter could cast an irreversible travel vote or open the
            // give window mid-barrier.
            if (m.maskObject != null && m.maskObject.gameObject.activeSelf)
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // Ctrl is the look-ahead modifier (and Ctrl+G opens the give window) — the game's bare-Ctrl
    // click must not press a node under the stale cursor.
    public override bool UsesCtrlModifier => true;

    public override bool OnMove(Vector2 direction)
    {
        if (MapNavigator.CurrentRegion == MapNavigator.Region.Party)
        {
            if (direction.y > 0f)
                MapNavigator.MoveParty(-1);
            else if (direction.y < 0f)
                MapNavigator.MoveParty(1);
            return true;
        }

        if (InputRouter.CtrlHeld)
        {
            if (direction.y > 0f)
                MapNavigator.LookAheadDescend();
            else if (direction.y < 0f)
                MapNavigator.LookAheadAscend();
            else if (direction.x < 0f)
                MapNavigator.LookAheadSibling(-1);
            else if (direction.x > 0f)
                MapNavigator.LookAheadSibling(1);
            return true;
        }

        if (direction.x < 0f)
            MapNavigator.MoveReachable(-1);
        else if (direction.x > 0f)
            MapNavigator.MoveReachable(1);
        // Bare Up/Down in the node region: nothing to move to, but still swallow so the game's own
        // node-list navigation doesn't warp the cursor underneath us.
        return true;
    }

    public override bool OnConfirm()
    {
        MapNavigator.Confirm();
        return true;
    }

    public override bool OnTab(bool backwards)
    {
        MapNavigator.ToggleRegion();
        return true;
    }

    public override bool OnNumber(int n)
    {
        MapNavigator.JumpToSlot(n - 1);
        return true;
    }

    public override bool OnCancel()
    {
        // Escape backs out of a look-ahead branch; otherwise defer to the game (pause menu).
        if (MapNavigator.InLookAhead)
        {
            MapNavigator.ExitLookAhead();
            return true;
        }
        return false;
    }
}
