using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the combat review hotkeys (Alt + letter). Mirrors <see cref="MapHotkeyPoller"/>:
/// these letters are unbound in the game's input asset, so the <c>InputController</c> router patches
/// never fire for them and per-frame polling is the only way to observe them.
///
/// Also drives the combat-event announcement flush every frame — deliberately outside the
/// active-context gate, because damage/heal/status text arrives while the board is animating (a
/// "busy" state where the context may momentarily read as suppressed) and must still be spoken.
/// </summary>
public class CombatHotkeyPoller : MonoBehaviour
{
    public IInputContext CombatContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Flush any buffered combat events regardless of focus/modal state.
        CombatNavigator.TickFlush();

        if (!InputRouter.IsActive(CombatContext))
            return;

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        // Per-character reads (focused character, else the active hero):
        if (kb.hKey.wasPressedThisFrame) CombatNavigator.SpeakHealth();
        else if (kb.bKey.wasPressedThisFrame) CombatNavigator.SpeakBlock();
        else if (kb.eKey.wasPressedThisFrame) CombatNavigator.SpeakEnergy();
        else if (kb.sKey.wasPressedThisFrame) CombatNavigator.SpeakStatusList();
        // Global reads:
        else if (kb.vKey.wasPressedThisFrame) CombatNavigator.SpeakBattlefield();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
        else if (kb.oKey.wasPressedThisFrame) CombatNavigator.SpeakRoundAndOrder();
        else if (kb.dKey.wasPressedThisFrame) CombatNavigator.SpeakPileCounts();
        else if (kb.iKey.wasPressedThisFrame) CombatNavigator.SpeakRevealedIntent();
        else if (kb.tKey.wasPressedThisFrame) CombatNavigator.SpeakTooltip();
    }
}
