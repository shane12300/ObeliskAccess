using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the end-of-run screen's Alt review keys (Alt+I overview, Alt+T focused-card
/// detail in the unlocked-cards popup, Alt+R repeat). Its <see cref="FinishRunScreenManager.Tick"/>
/// call runs outside the input gate — it drives arrival detection (announced only after the
/// unlocked-cards popup is out of the way), the popup's mouse-close detection, and the
/// "Main menu available" announcement when the progression bars finish.
/// </summary>
public class FinishRunHotkeyPoller : MonoBehaviour
{
    public IInputContext FinishRunContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Lifecycle tick regardless of who owns input (an alert could sit on top).
        FinishRunScreenManager.Tick();

        if (!InputRouter.IsActive(FinishRunContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.iKey.wasPressedThisFrame)
            FinishRunScreenManager.SpeakOverview();
        else if (kb.tKey.wasPressedThisFrame && FinishRunScreenManager.CardsMode)
            FinishRunScreenManager.SpeakFocusedCardDetail();
        else if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
