using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the MP conflict screen: Alt+R repeat, plus the manager's lifecycle tick —
/// the tick runs outside the input gate so a conflict torn down while a modal owns input
/// (host disconnect) still gets its close detected.
/// </summary>
public class ConflictHotkeyPoller : MonoBehaviour
{
    public IInputContext ConflictContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        ConflictScreenManager.Tick();

        if (!InputRouter.IsActive(ConflictContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
