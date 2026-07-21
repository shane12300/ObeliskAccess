using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the Rewards screen's Alt hotkeys (unbound letters, so the router patches never
/// fire for them) and for <see cref="RewardsScreenManager.Tick"/>. The tick runs outside the
/// active-context gate on purpose: readiness detection (the rows animate in over ~2 seconds) must
/// keep working while the "cardsReward" tutorial popup or an alert momentarily owns input.
/// </summary>
public class RewardsHotkeyPoller : MonoBehaviour
{
    public IInputContext RewardsContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        RewardsScreenManager.Tick();

        if (!InputRouter.IsActive(RewardsContext))
            return;

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        if (kb.tKey.wasPressedThisFrame) RewardsScreenManager.SpeakFocusedDetail();
        else if (kb.iKey.wasPressedThisFrame) RewardsScreenManager.SpeakOverview();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
    }
}
