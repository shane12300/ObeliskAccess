using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Polls the map screen's Alt-modified hotkeys each frame. These letters (G/T/I) are unbound in the
/// game's input asset, so the router's InputController patches never fire for them — frame polling is
/// the only way to see them. Also speaks the corruption prompt's summary on the frame it opens.
///
/// Alt+G speaks gold, Alt+T the focused node's detail, Alt+I the map info. The hotkeys only act while
/// the map context actually owns input (so they're suspended under the corruption modal and any
/// higher-priority screen).
/// </summary>
public class MapHotkeyPoller : MonoBehaviour
{
    /// <summary>The map context instance; hotkeys fire only when it is the active context.</summary>
    public IInputContext MapContext;

    private int _corruptionAnnouncedSerial;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Announce the corruption prompt once per DRAW (its own context owns input, so this can't
        // live in the map hotkey gate below). Keyed on the fill serial, not on the prompt becoming
        // active: in MP the prompt activates long before the draw barrier fills the labels in, and
        // an activation-edge announce would read the previous draw's (or the prefab's placeholder)
        // text. The serial also re-announces a master's NextCorruption re-roll, which redraws
        // without the prompt ever closing.
        bool corruptionActive = MapManager.Instance != null && MapManager.Instance.IsCorruptionOver();
        if (corruptionActive && CorruptionAccessibility.FillSerial != _corruptionAnnouncedSerial)
        {
            if (CorruptionAccessibility.AnnounceSummary())
                _corruptionAnnouncedSerial = CorruptionAccessibility.FillSerial;
        }

        // Deferred arrival summary — runs outside the gate so it can wait out masks and modals.
        MapNavigator.TickArrival();

        if (!InputRouter.IsActive(MapContext))
            return;

        // Ctrl+G opens the MP give-gold/dust window (before the Alt gate — Ctrl, not Alt).
        if (InputRouter.CtrlHeld && kb.gKey.wasPressedThisFrame)
        {
            GiveScreenManager.TryOpen();
            return;
        }

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        if (kb.gKey.wasPressedThisFrame)
            MapNavigator.SpeakGold();
        else if (kb.tKey.wasPressedThisFrame)
            MapNavigator.SpeakFocusedNodeDetail();
        else if (kb.iKey.wasPressedThisFrame)
            MapNavigator.SpeakMapInfo();
    }
}
