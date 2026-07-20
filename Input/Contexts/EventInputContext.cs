using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Context for the map-event (story dialog) book screen. Up/Down walk the event line-by-line
/// (title, description, then the choices at the bottom) and Enter activates the focused choice
/// or button — arrows are swallowed to keep the book a focus trap. Escape is left to the game.
/// Corruption/Tutorial/Settings outrank this context by registration order, so the corruption
/// prompt or the first-roll tutorial popup opening over an event take input automatically.
/// </summary>
public class EventInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            if (EventManager.Instance == null || !EventScreenManager.Active)
                return false;
            if (AlertManager.Instance != null && AlertManager.Instance.IsActive())
                return false;
            var m = MapManager.Instance;
            if (m != null && m.characterWindow != null && m.characterWindow.gameObject.activeSelf && m.characterWindow.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            EventScreenManager.Move(-1); // Up -> previous line
        else if (direction.y < 0f)
            EventScreenManager.Move(1);  // Down -> next line
        // Left/Right do nothing, but still swallow so nothing falls through the modal.
        return true;
    }

    public override bool OnConfirm()
    {
        EventScreenManager.Activate();
        return true;
    }
}
