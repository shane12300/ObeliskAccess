using System;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// One explicit-edit-mode session over a TMP_InputField — the generalized form of the alert
/// dialogue's edit machinery (see <c>AlertDialogueManager</c>, which keeps its own tested local
/// copy for now; consolidating it onto this class is a tracked cleanup). The field is resolved
/// live through <c>FieldProvider</c> every frame, never cached, so a screen closing under the
/// session can only ever make it abort.
///
/// Responsibilities ported verbatim from the alert implementation:
/// - <see cref="ArmDeactivate"/> undoes a game auto-focus once (120-frame give-up);
/// - <see cref="Tick"/> mirrors the text into <c>_editCache</c> every focused frame, so TMP's
///   native Escape revert can be undone, and notices TMP ending the edit on its own;
/// - <see cref="EndedThisFrame"/> lets callers drop the very Enter/Escape that ended the edit
///   (TMP processes it first; without the stamp the same key would also activate a row).
/// The session speaks the edit-start prompt itself; the end announcement is the caller's via
/// <see cref="OnEnded"/> (each screen phrases its own confirmation).
/// </summary>
internal sealed class TmpEditSession
{
    private readonly Func<TMPro.TMP_InputField> _fieldProvider;
    private bool _editing;
    private string _editCache = "";
    private bool _deactivatePending;
    private int _deactivateFrames;
    private int _endedFrame = -1;

    /// <summary>Fired with the final text when an edit ends (any path: Enter, Escape, TMP
    /// self-deactivating). Not fired by <see cref="Abort"/>.</summary>
    public event Action<string> OnEnded;

    public TmpEditSession(Func<TMPro.TMP_InputField> fieldProvider)
    {
        _fieldProvider = fieldProvider;
    }

    public bool Editing => _editing;

    public bool EndedThisFrame => Time.frameCount == _endedFrame;

    /// <summary>Stamp this frame as an edit-end even when no session was running (e.g. the chat
    /// send postfix ending a TMP-native or mouse-focused edit). The stamp is what stops the very
    /// key that ended the edit from also being routed to the screen below.</summary>
    public void MarkEnded() => _endedFrame = Time.frameCount;

    /// <summary>Arm the one-shot undo of a game auto-focus (alert-style fields only).</summary>
    public void ArmDeactivate()
    {
        _deactivatePending = true;
        _deactivateFrames = 0;
    }

    /// <summary>Per-frame tick (from a poller, outside any input gate).</summary>
    public void Tick()
    {
        var field = _fieldProvider();
        if (field == null)
        {
            Abort(); // screen gone under us — silent reset
            return;
        }

        if (_deactivatePending)
        {
            if (field.isFocused)
            {
                field.DeactivateInputField();
                _deactivatePending = false;
            }
            else if (++_deactivateFrames > 120)
            {
                _deactivatePending = false; // never focused — nothing to undo
            }
            return;
        }

        if (_editing)
        {
            if (field.isFocused)
                _editCache = field.text; // live mirror, survives TMP's Escape revert
            else
                EndEdit(); // TMP ended the edit itself (its own Enter/Escape handling)
        }
    }

    /// <summary>Start editing: focus the field and speak the prompt.
    /// <paramref name="announcePrefix"/> is e.g. "Editing room name".</summary>
    public void BeginEdit(string announcePrefix)
    {
        var field = _fieldProvider();
        if (field == null)
            return;
        _editing = true;
        _deactivatePending = false;
        _editCache = field.text;
        field.Select();
        field.ActivateInputField();
        string value = AccessibleMenuBase.StripRichText(field.text);
        SpeechManager.Speak(announcePrefix + (value.Length > 0 ? ", current text " + value : "")
            + ". Type your text, then press Enter when done.");
    }

    /// <summary>End the edit keeping the typed text (restores TMP's Escape revert), then raise
    /// <see cref="OnEnded"/>. Safe to call when not editing (no-op).</summary>
    public void EndEdit()
    {
        if (!_editing)
            return;
        _editing = false;
        _endedFrame = Time.frameCount;
        var field = _fieldProvider();
        string text = _editCache;
        if (field != null)
        {
            if (field.text != _editCache)
                field.text = _editCache;
            if (field.isFocused)
                field.DeactivateInputField();
            text = field.text;
        }
        ClearSelection(field);
        OnEnded?.Invoke(text ?? "");
    }

    /// <summary>Silent reset — no OnEnded, no speech (used when the screen disappears).</summary>
    public void Abort()
    {
        _editing = false;
        _deactivatePending = false;
        ClearSelection(_fieldProvider());
    }

    /// <summary>Drop the EventSystem selection if it still points at the given field. BeginEdit's
    /// Select() leaves the field selected after the edit ends, and a selected TMP field is a
    /// focus-steal hazard: any selection-based navigation that lands back on it re-activates it
    /// (OnSelect → ActivateInputField) outside any session — the chat-lockup mechanism. Internal
    /// so sessionless end paths (e.g. defocusing a mouse-focused chat field) can use it too.</summary>
    internal static void ClearSelection(TMPro.TMP_InputField field)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && field != null && es.currentSelectedGameObject == field.gameObject)
            es.SetSelectedGameObject(null);
    }
}
