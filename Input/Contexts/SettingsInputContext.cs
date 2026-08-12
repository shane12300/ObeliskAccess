using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Modal context for the settings panel. Sits above the menu context so all navigation, slider
/// adjustment, tab switching and dropdown editing are handled by
/// <see cref="SettingsMenuManager"/>. The panel's own confirm dialogs (reset prompts) belong to
/// the global alert dialogue, which outranks this context. Arrows are swallowed (the panel is otherwise mouse-only);
/// Escape only consumes the key when it cancels an open dropdown, otherwise the game closes the panel.
/// </summary>
public class SettingsInputContext : InputContextBase
{
    /// <summary>Static screen-open test for patches and pollers with no context instance.
    /// (The router's key suppressions are declared via the <see cref="IInputContext"/>
    /// flags, not this property.)</summary>
    public static bool IsCurrentlyActive => SettingsMenuManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+T reads the option tooltip — the game's bare-Alt right-click would land beneath the
    // panel (in combat: a card or character).
    public override bool SuppressesBareAlt => true;

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
