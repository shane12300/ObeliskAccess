using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Modal context for the pre-combat corruption prompt. Registered above the map context so that
/// while the prompt is up it owns input (and the map's hotkeys are suspended).
///
/// Up/Down walk the offer's rows, Left/Right walk sub-items (the enemy line-up, or hop between the
/// two reward rows), Enter performs the focused row's action, and 1/2 select reward A/B from
/// anywhere. Only Enter and the digits change anything — arrow keys never commit the run.
///
/// Escape is left to the game (its pause menu): the prompt has no cancel, travel is already
/// committed and Continue is the only way out.
/// </summary>
public class CorruptionInputContext : InputContextBase
{
    /// <summary>Static screen-open test for patches and pollers with no context instance.
    /// (The router's key suppressions are declared via the <see cref="IInputContext"/>
    /// flags, not this property.)</summary>
    public static bool IsCurrentlyActive => CorruptionScreenManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    /// <summary>Alt+T/I/R review keys, so the game's bare-Alt synthetic right-click must not fire
    /// underneath (it would right-click whatever the stale cursor sits on).</summary>
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        CorruptionScreenManager.Move(
            direction.x < 0f ? -1 : (direction.x > 0f ? 1 : 0),
            direction.y < 0f ? -1 : (direction.y > 0f ? 1 : 0));
        return true;
    }

    public override bool OnConfirm()
    {
        CorruptionScreenManager.Confirm();
        return true;
    }

    public override bool OnNumber(int n) => CorruptionScreenManager.Number(n);
}
