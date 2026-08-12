using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Context for the act-transition screen (scene "IntroNewGame"): title, act intro story text, and
/// a Continue button. Up/Down walk the rows; Enter continues from any row (the screen's only,
/// harmless action); Escape is left to the game, whose default on this scene is the same skip.
/// The game's own Enter is swallowed in the router prefix because the context self-activates —
/// the synthetic click would otherwise also press whatever the cursor rests on.
/// </summary>
public class IntroInputContext : InputContextBase
{
    /// <summary>Static screen-open test for patches and pollers with no context instance.
    /// (The router's key suppressions are declared via the <see cref="IInputContext"/>
    /// flags, not this property.)</summary>
    public static bool IsCurrentlyActive => IntroScreenManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+R review key — suppress the game's bare-Alt synthetic right-click.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            IntroScreenManager.Move(-1);
        else if (direction.y < 0f)
            IntroScreenManager.Move(1);
        // Left/Right: nothing, but swallow — otherwise the game's controller nav warps the cursor.
        return true;
    }

    public override bool OnConfirm()
    {
        IntroScreenManager.Activate();
        return true;
    }
}
