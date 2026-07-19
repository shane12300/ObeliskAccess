using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Modal context for tutorial popups. While a tutorial is open it sits above the menu context, so
/// Up/Down walk the popup line-by-line and Enter activates the focused button — the arrows are
/// swallowed to keep the popup a focus trap. Escape is left to the game (its default closes the
/// popup, which <c>TutorialClosePatch</c> announces).
/// </summary>
public class TutorialInputContext : InputContextBase
{
    public override bool IsActive => TutorialPopupManager.Active;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            TutorialPopupManager.Move(-1); // Up -> previous line
        else if (direction.y < 0f)
            TutorialPopupManager.Move(1);  // Down -> next line
        // Left/Right do nothing, but still swallow so nothing falls through the modal.
        return true;
    }

    public override bool OnConfirm()
    {
        TutorialPopupManager.Activate();
        return true;
    }
}
