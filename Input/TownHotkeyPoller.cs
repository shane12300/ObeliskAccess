using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the town hub's Alt hotkeys (unbound letters, so the router patches never see
/// them) and for <see cref="TownScreenManager.Tick"/>. The tick runs outside the active-context
/// gate on purpose: arrival announcement and sub-screen close detection must keep working while a
/// modal above the hub (service screen, tutorial popup, alert) owns input.
/// </summary>
public class TownHotkeyPoller : MonoBehaviour
{
    public IInputContext TownContext;
    public IInputContext TownUpgradeContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Isolated so one throwing cannot starve the other (see SafeTick).
        InputRouter.SafeTick(TownScreenManager.Tick, "town-hub");
        InputRouter.SafeTick(TownUpgradeScreenManager.Tick, "town-upgrades");

        // Ctrl+G opens the MP give-gold/dust window (before the Alt gate — Ctrl, not Alt).
        // Unlike every other poller key this one does NOT require Alt, so it needs the gates the
        // Alt keys get for free: opening the give panel under a live chat field or the on-screen
        // keyboard would put a second screen under the user's typing. Alt+Ctrl+G is not this key.
        if (InputRouter.CtrlHeld && !InputRouter.AltHeld && kb.gKey.wasPressedThisFrame
            && !ChatSpeech.Typing && !RouterGuards.OskActive
            && InputRouter.IsActive(TownContext))
        {
            GiveScreenManager.TryOpen();
            return;
        }

        if (!InputRouter.AltHeld)
            return;

        if (InputRouter.IsActive(TownUpgradeContext))
        {
            if (kb.tKey.wasPressedThisFrame) TownUpgradeScreenManager.SpeakFocusedDetail();
            else if (kb.gKey.wasPressedThisFrame) TownScreenManager.SpeakCurrencies();
            else if (kb.iKey.wasPressedThisFrame) TownUpgradeScreenManager.SpeakOverview();
            else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
        }
        else if (InputRouter.IsActive(TownContext))
        {
            if (kb.tKey.wasPressedThisFrame) TownScreenManager.SpeakFocusedDetail();
            else if (kb.gKey.wasPressedThisFrame) TownScreenManager.SpeakCurrencies();
            else if (kb.iKey.wasPressedThisFrame) TownScreenManager.SpeakOverview();
            else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
        }
    }
}
