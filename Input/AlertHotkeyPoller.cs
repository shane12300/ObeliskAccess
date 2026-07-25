using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the alert dialogue's Alt+R repeat key. Every per-screen poller gates on its
/// own context, so they all fall silent while the top-priority alert context owns input — this
/// poller restores repeat there. Also drives the text-field edit-mode tick (undoing the game's
/// auto-focus, mirroring typed text, noticing TMP ending an edit on its own); alert open/close
/// itself stays patch-driven.
/// </summary>
public class AlertHotkeyPoller : MonoBehaviour
{
    public IInputContext AlertContext;
    public IInputContext PlayersContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        ObeliskAccess.Patches.AlertDialogueManager.Tick();

        if (!InputRouter.IsActive(AlertContext)
            && !(PlayersContext != null && InputRouter.IsActive(PlayersContext)))
            return;

        if (!(kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            return;

        if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
    }
}
