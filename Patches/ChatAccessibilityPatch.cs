using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

/// <summary>
/// Multiplayer text chat (ChatManager — DontDestroyOnLoad, alive on every scene of an MP session).
/// Deliberately NOT an input context: the chat floats over whatever screen is active, and taking
/// input priority would smother the screen beneath. Instead:
/// - incoming messages are spoken from a <c>ChatText</c> postfix (toggle-gated — that method is
///   the single sink for remote messages, local echoes and the joined/kicked system lines);
/// - Alt+Y starts a <see cref="TmpEditSession"/> over the game's own input field and Enter sends
///   through the field's onSubmit → ChatSend wiring;
/// - while the field is focused, <see cref="Typing"/> gates the whole input router (see
///   RouterInputPatches) — the game's own DoKeyBinding already early-returns on focused input
///   fields, this mirrors that for the mod's routed events, and the session's EndedThisFrame
///   stamp drops the submit Enter that TMP consumed first;
/// - Alt+M walks the rolling 20-message history (private <c>chatContent</c>, via Traverse).
/// Gotchas honoured: never call <c>ShowChat()</c> while the chat is already expanded (its
/// AddListener stacks a duplicate onSubmit → every message would send twice), and the <c>status</c>
/// field is bugged (always "closed") — open state is <c>chatGO.activeSelf</c>.
/// </summary>
internal static class ChatSpeech
{
    private static readonly TmpEditSession _session = new TmpEditSession(
        () => ChatManager.Instance != null && ChatManager.Instance.gameObject.activeSelf
            ? ChatManager.Instance.chatInput : null);

    private static int _historyIndex = -1; // -1 = next Alt+M starts at the newest message
    private static bool _sendArmed;        // an Alt+Y session is ours (vs a mouse click focus)

    static ChatSpeech()
    {
        _session.OnEnded += OnEditEnded;
    }

    /// <summary>True while the chat input owns the keyboard (any focus path, including a mouse
    /// click) — plus the frame an edit ended, so the ending Enter/Escape is never re-routed.</summary>
    public static bool Typing
    {
        get
        {
            var cm = ChatManager.Instance;
            if (cm != null && cm.gameObject.activeSelf && cm.chatInput != null && cm.chatInput.isFocused)
                return true;
            return _session.EndedThisFrame;
        }
    }

    /// <summary>Escape pressed while typing: end the session; ChatSend never ran, so this is a
    /// cancel (the typed text stays in the field for a later Alt+Y).</summary>
    public static void CancelTyping()
    {
        if (_session.Editing)
        {
            _sendArmed = false;
            _session.EndEdit();
            SpeechManager.Speak("Chat cancelled.");
        }
        else
        {
            // Mouse-focused field (no session): just defocus it. Stamp the frame so this
            // Escape ends only the typing and is not also routed as a Cancel below.
            var cm = ChatManager.Instance;
            if (cm != null && cm.chatInput != null && cm.chatInput.isFocused)
            {
                cm.chatInput.DeactivateInputField();
                // Also drop the EventSystem selection: the game's DoKeyBinding early-returns
                // while a TMP field is merely SELECTED, so leaving it would permanently kill
                // game-owned keys (e.g. the energy-transfer Enter) after a mouse-click focus.
                TmpEditSession.ClearSelection(cm.chatInput);
                _session.MarkEnded();
            }
        }
    }

    /// <summary>Per-frame session mirror (from the poller, outside every gate).</summary>
    public static void Tick() => _session.Tick();

    // ---------------------------------------------------------------- Alt+Y / Alt+M / Alt+P

    public static void BeginSend()
    {
        var cm = ChatManager.Instance;
        if (cm == null || !cm.gameObject.activeSelf || cm.chatInput == null)
            return;
        // Expand only when collapsed — ShowChat() while open would stack a second onSubmit
        // listener and double-send every message from then on.
        if (cm.chatGO != null && !cm.chatGO.gameObject.activeSelf)
            cm.ShowChat();
        _sendArmed = true;
        _session.BeginEdit("Chat message");
    }

    public static void SpeakHistory()
    {
        var cm = ChatManager.Instance;
        if (cm == null || !cm.gameObject.activeSelf)
            return;
        var content = Traverse.Create(cm).Field<List<string>>("chatContent").Value;
        if (content == null || content.Count == 0)
        {
            SpeechManager.Speak("No chat messages.");
            return;
        }
        // First press = newest; each further press steps one older; wraps back to newest.
        _historyIndex = _historyIndex < 0 ? content.Count - 1 : _historyIndex - 1;
        if (_historyIndex < 0)
            _historyIndex = content.Count - 1;
        int fromNewest = content.Count - _historyIndex;
        SpeechManager.Speak("Message " + fromNewest + " of " + content.Count + ": "
            + CardSpeech.CleanFlat(content[_historyIndex]));
    }

    public static void OpenPlayersPanel()
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.GetNumPlayers() < 2)
        {
            SpeechManager.Speak("No other players.");
            return;
        }
        if (AlertManager.Instance != null)
            AlertManager.Instance.ShowPlayers();
    }

    // ---------------------------------------------------------------- internals

    private static void OnEditEnded(string text)
    {
        // ChatSend runs from TMP's onSubmit BEFORE focus is lost, clears the field and refocuses
        // it (the game keeps you typing). OnSendCompleted below has already handled that case and
        // ended the session; reaching here with armed still set means the edit ended WITHOUT a
        // send — an empty submit (TMP deactivates, ChatSend no-ops on "") or a focus loss.
        if (!_sendArmed)
            return;
        _sendArmed = false;
        SpeechManager.Speak(text.Trim().Length > 0 ? "Not sent — press Alt Y to try again." : "Nothing sent.");
    }

    /// <summary>ChatSend postfix (only reached for a non-empty send): exit typing mode — the game
    /// re-activates the field to keep a sighted player typing, which would trap a screen-reader
    /// user; one message per Alt+Y instead. Two traps this must defuse:
    /// 1. The game's ActivateInputField is DEFERRED (a pending flag TMP consumes next LateUpdate),
    ///    so an immediate DeactivateInputField here is a no-op against it — the field re-focuses
    ///    one frame after "Sent." and the Typing gate silently mutes the router again. Arm the
    ///    session's deferred deactivate, which polls until the refocus lands and undoes it.
    /// 2. This runs for ANY non-empty MP send, session or not — a mouse-focused send must exit
    ///    typing too, or the game's refocus traps that player in a muted router with no feedback.
    /// MarkEnded stamps the frame so the submit Enter is never also routed to the screen below
    /// (Abort, unlike EndEdit, sets no stamp).</summary>
    internal static void OnSendCompleted()
    {
        var cm = ChatManager.Instance;
        if (cm == null)
            return;
        _sendArmed = false;
        if (cm.chatInput != null && cm.chatInput.isFocused)
            cm.chatInput.DeactivateInputField();
        _session.Abort(); // silent — the ChatText echo of our own message is the confirmation
        _session.MarkEnded();
        _session.ArmDeactivate(); // catch the game's deferred re-focus next frame(s)
        SpeechManager.Speak("Sent.");
    }

    internal static void OnChatLine(string renderedLine)
    {
        _historyIndex = -1; // a new message restarts Alt+M at the newest
        if (AccessibilityOptions.SpeakChat == null || !AccessibilityOptions.SpeakChat.Value)
            return;
        string line = CardSpeech.CleanFlat(renderedLine);
        if (line.Length > 0)
            SpeechManager.SpeakQueued(line);
    }
}

/// <summary>EnableChat runs when an MP session brings the chat overlay up. Take the input field
/// out of the UI navigation graph: the game's Tab handler (NextField → FindSelectableOnDown →
/// SetSelectedGameObject) and Unity's own arrow-key UI navigation can otherwise land selection on
/// it, and a TMP input field activates itself on select — silently focusing the chat, which mutes
/// the whole input router via the Typing gate until Escape. Mouse clicks and Alt+Y don't go
/// through selection-based navigation, so both still focus the field normally.</summary>
[HarmonyPatch(typeof(ChatManager), nameof(ChatManager.EnableChat))]
public class ChatInputNavigationPatch
{
    static void Postfix(ChatManager __instance)
    {
        if (__instance != null && __instance.chatInput != null)
            __instance.chatInput.navigation = new Navigation { mode = Navigation.Mode.None };
    }
}

/// <summary>Every chat line lands here: remote messages (GotChat), the local echo of our own
/// send, and the joined/kicked system lines. The original early-returns outside MP but this
/// postfix still runs — hence the explicit gate. The `text` arg is the rendered line without the
/// timestamp the method prepends for display.</summary>
[HarmonyPatch(typeof(ChatManager), nameof(ChatManager.ChatText))]
public class ChatLineAnnouncePatch
{
    static void Postfix(string text)
    {
        if (MpSpeech.IsMp)
            ChatSpeech.OnChatLine(text);
    }
}

/// <summary>The room welcome line bypasses ChatText (it rebuilds the transcript directly).</summary>
[HarmonyPatch(typeof(ChatManager), nameof(ChatManager.WelcomeMsg))]
public class ChatWelcomeAnnouncePatch
{
    static void Postfix(ChatManager __instance)
    {
        if (!MpSpeech.IsMp || AccessibilityOptions.SpeakChat == null || !AccessibilityOptions.SpeakChat.Value)
            return;
        // WelcomeMsg resets the buffer, so chatText holds exactly the welcome line — read the
        // rendered text instead of re-running the localization format (stays correct if the game
        // changes the string's shape).
        string line = __instance.chatText != null ? CardSpeech.CleanFlat(__instance.chatText.text) : "";
        if (line.Length > 0)
            SpeechManager.SpeakQueued(line);
    }
}

/// <summary>A completed send: exit typing mode and confirm. The prefix checks there was anything
/// to send — an empty submit no-ops inside ChatSend, and the "Nothing sent" case is spoken by the
/// session's own end path instead.</summary>
[HarmonyPatch(typeof(ChatManager), nameof(ChatManager.ChatSend))]
public class ChatSendCompletedPatch
{
    static void Prefix(ChatManager __instance, string chatStr, out bool __state)
    {
        string text = __instance.chatInput != null ? __instance.chatInput.text.Trim() : "";
        if (!string.IsNullOrEmpty(chatStr))
            text = chatStr.Trim();
        __state = text.Length > 0 && MpSpeech.IsMp;
    }

    static void Postfix(bool __state)
    {
        if (__state)
            ChatSpeech.OnSendCompleted();
    }
}
