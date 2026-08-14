using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the pre-combat corruption prompt's Alt review keys (T detail, I overview,
/// R repeat) — those letters are unbound in the game's InputAction maps, so nothing but a poller
/// sees them.
///
/// The manager's tick runs <b>outside</b> the context gate: the prompt's arrival (and a master's
/// re-roll, which redraws without the prompt ever closing) must be announced even while an alert
/// or another modal owns input.
/// </summary>
public class CorruptionHotkeyPoller : MonoBehaviour
{
    public IInputContext CorruptionContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        CorruptionScreenManager.Tick();

        if (!InputRouter.IsActive(CorruptionContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame)
            CorruptionScreenManager.SpeakRowDetail();
        else if (kb.iKey.wasPressedThisFrame)
            CorruptionScreenManager.SpeakOverview();
        else if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
