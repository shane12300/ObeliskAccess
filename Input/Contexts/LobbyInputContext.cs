using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Multiplayer lobby (scene "Lobby"). Scene-exclusive, so priority barely matters — it sits in the
/// bottom band with the other scene screens, below Alert (kick/password/version/exit dialogs pop
/// over it) and Settings. All rows and text live in <c>LobbyScreenManager</c>.
/// </summary>
public class LobbyInputContext : InputContextBase
{
    /// <summary>Static so the RouterInputPatches Enter-swallow list can gate on it.</summary>
    public static bool IsCurrentlyActive => LobbyScreenManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+I/R/Y/M/P keys — suppress the game's bare-Alt synthetic right-click.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            LobbyScreenManager.Move(-1);
        else if (direction.y < 0f)
            LobbyScreenManager.Move(1);
        // Sideways swallowed too: the game's ControllerMovement would warp the cursor.
        return true;
    }

    public override bool OnConfirm()
    {
        LobbyScreenManager.Confirm();
        return true;
    }

    public override bool OnCancel() => LobbyScreenManager.Cancel();

    public override bool OnTab(bool backwards)
    {
        LobbyScreenManager.Tab();
        return true;
    }
}
