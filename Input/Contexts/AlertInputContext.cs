using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Owns the keyboard while any <see cref="AlertManager"/> dialog is up — registered above every
/// other context, since alerts are modal over everything (including the tutorial popup they can
/// spawn over). All behaviour lives in <see cref="AlertDialogueManager"/>: Up/Down walk the
/// alert's text lines and option buttons, Enter activates only on an option row, Escape follows
/// CloseAlert semantics. Tab and digits are consumed so they cannot leak to the screen beneath.
/// Input alerts use an explicit edit mode: Enter on the text-field row starts typing (the TMP
/// field is focused), Enter or Escape ends it keeping the typed text, and only then do arrows
/// resume walking rows.
/// </summary>
public class AlertInputContext : InputContextBase
{
    /// <summary>
    /// True while an alert popup owns input. Static screen-open test for patches and pollers with
    /// no context instance; the router's key suppressions (game Enter's synthetic click, bare-Ctrl
    /// click, combat emote letters) are declared via the <see cref="IInputContext"/> flags, not
    /// this property. Excludes the multiplayer player-list panel, which shares
    /// <c>AlertManager.IsActive()</c>.
    /// </summary>
    public static bool IsCurrentlyActive => AlertDialogueManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+R repeats the alert; the raycast behind a bare Alt ignores the alert's UI canvas
    // entirely, so without these flags a press under an alert clicked whatever sat beneath.
    public override bool UsesAltLetters => true;
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            AlertDialogueManager.Move(-1);
        else if (direction.y < 0f)
            AlertDialogueManager.Move(1);
        // Left/Right: consumed silently — this also swallows the game's own alert
        // ControllerMovement, whose cursor-warp walk would fight the spoken focus.
        return true;
    }

    public override bool OnConfirm()
    {
        AlertDialogueManager.Confirm();
        return true;
    }

    public override bool OnCancel()
    {
        AlertDialogueManager.Cancel();
        return true;
    }

    public override bool OnTab(bool backwards) => true;

    public override bool OnNumber(int n) => true;
}
