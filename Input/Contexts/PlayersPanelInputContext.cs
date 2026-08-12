using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// MP players panel — an AlertManager-family modal, registered immediately below the alert
/// context. The two can never be active together: the alert dialogue requires <c>popupT</c> and
/// this panel requires <c>playersT</c>, and the game cross-deactivates the faces.
/// </summary>
public class PlayersPanelInputContext : InputContextBase
{
    /// <summary>Static screen-open test for patches and pollers with no context instance.
    /// (The router's key suppressions are declared via the <see cref="IInputContext"/>
    /// flags, not this property.)</summary>
    public static bool IsCurrentlyActive => PlayersPanelManager.PanelOpen;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+R repeats the panel; the bare-Alt right-click raycast ignores the panel's canvas and
    // would click whatever sits beneath it.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            PlayersPanelManager.Move(-1);
        else if (direction.y < 0f)
            PlayersPanelManager.Move(1);
        return true;
    }

    public override bool OnConfirm()
    {
        PlayersPanelManager.Confirm();
        return true;
    }

    public override bool OnCancel()
    {
        PlayersPanelManager.Close();
        return true;
    }

    // Swallow Tab/digits like the alert does — a modal focus trap.
    public override bool OnTab(bool backwards) => true;
    public override bool OnNumber(int n) => true;
}
