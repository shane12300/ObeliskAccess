using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the multiplayer lobby: Alt+R repeat and Alt+I panel overview, plus the
/// manager's lifecycle tick (scene/panel arrival announcements, room-list and slot edges, and the
/// text-edit session mirrors) — the tick runs outside the input gate so panel changes driven by
/// the network (a join completing under an alert) are still noticed.
/// </summary>
public class LobbyHotkeyPoller : MonoBehaviour
{
    public IInputContext LobbyContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        LobbyScreenManager.Tick();

        if (!InputRouter.IsActive(LobbyContext))
            return;
        if (LobbyScreenManager.AnyEditing)
            return; // no hotkeys while typing in a room-name/password field

        if (!InputRouter.AltHeld)
            return;

        if (kb.rKey.wasPressedThisFrame)
            SpeechManager.RepeatLast();
        else if (kb.iKey.wasPressedThisFrame)
            LobbyScreenManager.SpeakOverview();
    }
}
