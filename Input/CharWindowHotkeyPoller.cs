using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the in-run character sheet's Alt review hotkeys (unbound letters the
/// InputAction system never fires for): Alt+T row detail, Alt+I sheet overview, Alt+R repeat.
/// Also drives the manager's lifecycle tick (a cheap no-op while the sheet is closed), which
/// drops state if the window disappears without a Hide call (scene change).
/// </summary>
public class CharWindowHotkeyPoller : MonoBehaviour
{
    public IInputContext CharWindowContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        ObeliskAccess.Patches.CharWindowScreenManager.Tick();

        if (!InputRouter.IsActive(CharWindowContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame)
            ObeliskAccess.Patches.CharWindowScreenManager.SpeakRowDetail();
        else if (kb.iKey.wasPressedThisFrame)
            ObeliskAccess.Patches.CharWindowScreenManager.SpeakOverview();
        else if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
