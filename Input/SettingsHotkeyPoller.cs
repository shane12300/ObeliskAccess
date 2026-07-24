using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the settings menu's Alt+T tooltip key. Mirrors the other screen pollers:
/// the letter is unbound in the game's input asset, so the <c>InputController</c> router patches
/// never fire for it and per-frame polling is the only way to observe it.
/// </summary>
public class SettingsHotkeyPoller : MonoBehaviour
{
    public IInputContext SettingsContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (!InputRouter.IsActive(SettingsContext))
            return;

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        if (kb.tKey.wasPressedThisFrame)
            Patches.SettingsMenuManager.SpeakTooltip();
    }
}
