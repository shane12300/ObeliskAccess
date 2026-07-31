using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the event screen's Alt hotkeys (unbound letters, so the router patches
/// never fire for them) and for <see cref="EventScreenManager.Tick"/>. The tick runs outside
/// the active-context gate on purpose: the reply watcher, the destroy safety net and the
/// deferred reward-card reads must keep working while a modal above the event (tutorial popup,
/// alert, corruption prompt) momentarily owns input.
/// </summary>
public class EventHotkeyPoller : MonoBehaviour
{
    public IInputContext EventContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        EventScreenManager.Tick();

        if (!InputRouter.IsActive(EventContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame) EventScreenManager.SpeakFocusedDetail();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
    }
}
