namespace ObeliskAccess.Patches;

/// <summary>
/// Drives the game's <see cref="AlertManager"/> confirm dialogs from the keyboard. The town-family
/// contexts stay active while an alert is up (unlike map/event/combat, which suspend) because the
/// alerts there are ones the town screens themselves spawn — treasure claims, supply purchases,
/// tutorial-step warnings — and must be answerable in place. Announcement of the dialog text and of
/// the answer is handled globally by the AlertManager patches in SettingsMenuAccessibilityPatch.
/// </summary>
internal static class AlertHelper
{
    public static bool Active =>
        AlertManager.Instance != null && AlertManager.Instance.IsActive();

    /// <summary>Enter = accept. Returns true if an alert consumed the key.</summary>
    public static bool Confirm()
    {
        if (!Active)
            return false;
        // Sets the answer, fires the game's confirm delegate, and hides the dialog. Also correct
        // for single-button alerts (same as clicking their one Accept button).
        AlertManager.Instance.SetConfirmAnswer(true);
        return true;
    }

    /// <summary>Escape = cancel/dismiss. Returns true if an alert consumed the key.</summary>
    public static bool Cancel()
    {
        if (!Active)
            return false;
        // CloseAlert routes to SetConfirmAnswer(false) for double dialogs and to the plain
        // dismiss path for single-button ones.
        AlertManager.Instance.CloseAlert();
        return true;
    }
}
