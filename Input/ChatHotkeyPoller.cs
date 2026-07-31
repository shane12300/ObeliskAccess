using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// One global poller for the multiplayer chat overlay — deliberately context-free: the chat's
/// ChatManager is DontDestroyOnLoad and floats over every MP scene, so its keys are gated on
/// game state (in MP + chat enabled) rather than on any screen's input context.
/// Alt+Y — type and send a message in place; Alt+M — step back through recent messages;
/// Alt+P — open the players panel. The edit-session tick runs outside all gates.
/// </summary>
public class ChatHotkeyPoller : MonoBehaviour
{
    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        ChatSpeech.Tick();

        if (GameManager.Instance == null || !GameManager.Instance.IsMultiplayer())
            return;
        var cm = ChatManager.Instance;
        if (cm == null || !cm.gameObject.activeSelf)
            return;
        if (ChatSpeech.Typing)
            return; // no hotkeys while typing (Alt is never part of typing anyway — belt and braces)

        if (!InputRouter.AltHeld)
            return;

        if (kb.yKey.wasPressedThisFrame)
            ChatSpeech.BeginSend();
        else if (kb.mKey.wasPressedThisFrame)
            ChatSpeech.SpeakHistory();
        else if (kb.pKey.wasPressedThisFrame)
            ChatSpeech.OpenPlayersPanel();
    }
}
