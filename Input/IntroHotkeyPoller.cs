using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the act-transition screen's Alt+R repeat key — the letter is unbound in the
/// game's InputAction maps, so only a poller sees it. The screen's story text is the longest
/// single announcement in the mod, so repeat matters here.
/// </summary>
public class IntroHotkeyPoller : MonoBehaviour
{
    public IInputContext IntroContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (!InputRouter.IsActive(IntroContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
