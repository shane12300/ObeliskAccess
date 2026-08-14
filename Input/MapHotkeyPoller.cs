using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Polls the map screen's Alt-modified hotkeys each frame. These letters (G/T/I) are unbound in the
/// game's input asset, so the router's InputController patches never fire for them — frame polling is
/// the only way to see them.
///
/// Alt+G speaks gold, Alt+T the focused node's detail, Alt+I the map info. The hotkeys only act while
/// the map context actually owns input (so they're suspended under the corruption modal and any
/// higher-priority screen).
/// </summary>
public class MapHotkeyPoller : MonoBehaviour
{
    /// <summary>The map context instance; hotkeys fire only when it is the active context.</summary>
    public IInputContext MapContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Deferred arrival summary — runs outside the gate so it can wait out masks and modals.
        MapNavigator.TickArrival();

        if (!InputRouter.IsActive(MapContext))
            return;

        // Ctrl+G opens the MP give-gold/dust window (before the Alt gate — Ctrl, not Alt).
        // Unlike every other poller key this one does NOT require Alt, so it needs the gates the
        // Alt keys get for free: opening the give panel under a live chat field or the on-screen
        // keyboard would put a second screen under the user's typing. Alt+Ctrl+G is not this key.
        if (InputRouter.CtrlHeld && !InputRouter.AltHeld && kb.gKey.wasPressedThisFrame
            && !ChatSpeech.Typing && !RouterGuards.OskActive)
        {
            GiveScreenManager.TryOpen();
            return;
        }

        if (!InputRouter.AltHeld)
            return;

        if (kb.gKey.wasPressedThisFrame)
            MapNavigator.SpeakGold();
        else if (kb.tKey.wasPressedThisFrame)
            MapNavigator.SpeakFocusedNodeDetail();
        else if (kb.iKey.wasPressedThisFrame)
            MapNavigator.SpeakMapInfo();
    }
}
