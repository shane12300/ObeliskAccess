using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the alert dialogue's Alt+R repeat key. Every per-screen poller gates on its
/// own context, so they all fall silent while the top-priority alert context owns input — this
/// poller restores repeat there. No lifecycle tick: alert open/close is patch-driven.
/// </summary>
public class AlertHotkeyPoller : MonoBehaviour
{
    public IInputContext AlertContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (!InputRouter.IsActive(AlertContext))
            return;

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
