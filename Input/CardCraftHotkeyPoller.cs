using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the service screens' Alt hotkeys (unbound letters the router patches never
/// see) and for <see cref="CardCraftScreenManager.Tick"/>. The tick runs outside the
/// active-context gate on purpose: screen-open detection must work even while a modal above the
/// craft screen (tutorial popup, alert) momentarily owns input.
/// </summary>
public class CardCraftHotkeyPoller : MonoBehaviour
{
    public IInputContext CardCraftContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        CardCraftScreenManager.Tick();

        if (!InputRouter.IsActive(CardCraftContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame) CardCraftScreenManager.SpeakFocusedDetail();
        else if (kb.gKey.wasPressedThisFrame) CardCraftScreenManager.SpeakCurrencies();
        else if (kb.iKey.wasPressedThisFrame) CardCraftScreenManager.SpeakOverview();
        else if (kb.fKey.wasPressedThisFrame) CardCraftScreenManager.ToggleFilterModal();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
    }
}
