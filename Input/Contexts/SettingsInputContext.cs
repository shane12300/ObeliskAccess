using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Modal context for the settings panel. Sits above the menu context so all navigation, slider
/// adjustment, tab switching, dropdown editing, and the settings-owned confirm dialogs are handled
/// by <see cref="SettingsMenuManager"/>. Arrows are swallowed (the panel is otherwise mouse-only);
/// Escape only consumes the key when it cancels an open dropdown, otherwise the game closes the panel.
/// </summary>
public class SettingsInputContext : InputContextBase
{
    /// <summary>Static so the <c>RouterInputPatches</c> suppressions can gate on it.</summary>
    public static bool IsCurrentlyActive => SettingsMenuManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        SettingsMenuManager.HandleArrow(direction);
        return true;
    }

    public override bool OnConfirm()
    {
        SettingsMenuManager.HandleEnter();
        return true;
    }

    public override bool OnTab(bool backwards)
    {
        SettingsMenuManager.HandleTab(backwards);
        return true;
    }

    public override bool OnCancel()
    {
        // Escape cancels an open dropdown without closing the panel; otherwise defer to the game.
        if (SettingsMenuManager.DropdownOpen)
            return SettingsMenuManager.CancelDropdown();
        return false;
    }
}
