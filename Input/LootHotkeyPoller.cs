using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the loot screen's Alt hotkeys (unbound letters, so the router patches never
/// fire for them) and for <see cref="LootScreenManager.Tick"/>. The tick runs outside the
/// active-context gate on purpose: readiness detection (cards animate in one at a time), turn
/// changes, and the finish announcement must keep working while an alert or a tutorial popup
/// momentarily owns input.
/// </summary>
public class LootHotkeyPoller : MonoBehaviour
{
    public IInputContext LootContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        LootScreenManager.Tick();

        if (!InputRouter.IsActive(LootContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame) LootScreenManager.SpeakFocusedDetail();
        else if (kb.iKey.wasPressedThisFrame) LootScreenManager.SpeakOverview();
        else if (kb.gKey.wasPressedThisFrame) LootScreenManager.SpeakGold();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
    }
}
