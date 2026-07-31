using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Presents every <see cref="AlertManager"/> dialog as a walkable dialogue: the alert body is the
/// top line(s), followed by one row per visible option button. Up/Down move through the rows,
/// Enter activates only when focused on an option row (so a destructive confirm can never be
/// accepted by accident), Escape follows the game's <c>CloseAlert</c> semantics. Covers all five
/// alert shapes — confirm single/double (including the buttonless multiplayer "waiting" variant),
/// text input, copy (export) and paste (import) — plus answers made by mouse or gamepad, which are
/// announced from the <c>SetConfirmAnswer</c> patch. The multiplayer player-list panel shares
/// <c>AlertManager.IsActive()</c> but lives on a different panel (<c>playersT</c>), so activity is
/// gated on <c>popupT</c>. Input handled by <see cref="ObeliskAccess.Input.Contexts.AlertInputContext"/>.
/// </summary>
internal static class AlertDialogueManager
{
    private enum Kind { None, Confirm, ConfirmDouble, Input, CopyPaste, PasteCopy }

    private struct Row
    {
        public string Speech;
        public Action Activate; // null = plain text row
        public bool IsEdit;     // the text-field row: Enter starts editing, not an answer
        public bool IsSubmit;   // the input-accept row: AlertInputSuccess announces itself and
                                // never goes through SetConfirmAnswer — skip answer bookkeeping
    }

    private static Kind _kind = Kind.None;
    private static readonly List<string> _textLines = new List<string>();
    private static int _index;
    private static bool _active;

    // Frame stamps used to serialise speech around answers:
    //  - _answeredFrame: an answer (ours, mouse, or gamepad) happened this frame. Set in the
    //    SetConfirmAnswer PREFIX (the original calls HideAlert before our postfix runs) so the
    //    HideAlert postfix stays silent, and read by OnOpened so a chained alert (opened by the
    //    previous alert's delegate) queues instead of clobbering the answer announcement.
    //  - _suppressAnswerSpeech: our own Confirm() already spoke the chosen label.
    //  - _inputSuccessFrame: AlertInputSuccess ran this frame (TMP's own submit wiring may fire it
    //    on Enter before our Confirm sees the key) — dedupes a double submit. Stamped in a PREFIX:
    //    AlertInputSuccess calls HideAlert inside itself, so a postfix stamp would come too late
    //    for OnHidden's silence check.
    private static int _answeredFrame = -1;
    private static bool _suppressAnswerSpeech;
    private static int _inputSuccessFrame = -1;

    // Labels captured in the SetConfirmAnswer prefix; the postfix runs after HideAlert wiped them.
    private static string _labelAccept = "";
    private static string _labelCancel = "";

    // Text-field edit mode, via the shared TmpEditSession (which this manager's local machinery
    // was generalized into). The alert opens in review mode (ArmDeactivate undoes the game's own
    // auto-focus of the field) so arrows walk rows; Enter on the field row starts editing, Enter
    // or Escape while editing ends it, keeping the typed text (the session's cache survives TMP's
    // native Escape revert). EndedThisFrame lets Confirm/Cancel drop the very key that ended an
    // edit — without it, the session Tick noticing TMP's own end first would let that same
    // Enter/Escape fall through and activate a row or answer the dialog.
    private static readonly TmpEditSession _edit = new TmpEditSession(EditField);

    static AlertDialogueManager()
    {
        _edit.OnEnded += text =>
        {
            string value = AccessibleMenuBase.StripRichText(text);
            SpeechManager.Speak(value.Length > 0
                ? "Text entered: " + value + ". Down to reach the accept button."
                : "Text field left empty.");
        };
    }

    /// <summary>True while an alert popup (not the player-list panel) is being presented.</summary>
    public static bool Active
    {
        get
        {
            var am = AlertManager.Instance;
            return _active && am != null && am.IsActive() && am.popupT.gameObject.activeSelf;
        }
    }

    // ------------------------------------------------------------------ lifecycle (from patches)

    public static void OnOpened(int kind)
    {
        var am = AlertManager.Instance;
        if (am == null)
            return;

        _kind = (Kind)kind;
        _textLines.Clear();
        string body = _kind == Kind.CopyPaste || _kind == Kind.PasteCopy
            ? am.alertTextCP.text
            : am.alertText.text;
        _textLines.AddRange(AccessibleMenuBase.SplitLines(body));
        _index = 0;
        _active = true;

        var options = new List<Row>();
        foreach (var row in BuildRows())
            if (row.Activate != null)
                options.Add(row);

        var sb = new StringBuilder();
        sb.Append(string.Join(" ", _textLines.ToArray()));
        if (sb.Length > 0)
            sb.Append(". ");
        if (options.Count == 0)
        {
            sb.Append("No options, waiting.");
        }
        else
        {
            sb.Append(options.Count == 1 ? "1 option: " : options.Count + " options: ");
            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(options[i].Speech);
            }
            sb.Append(". Down to review.");
        }
        if (_kind == Kind.Input || _kind == Kind.PasteCopy)
        {
            sb.Append(" Down to the text field, then Enter to type.");
            // The game auto-focuses the field a moment after opening; the armed session undoes
            // that so the dialog starts in review mode and Enter on the field row starts editing
            // explicitly.
            _edit.Abort();
            _edit.ArmDeactivate();
        }

        // A chained alert (opened from the previous alert's confirm delegate) must not clobber
        // the just-spoken answer; otherwise interrupt as usual — the dialog owns the screen.
        if (Time.frameCount == _answeredFrame)
            SpeechManager.SpeakQueued(sb.ToString());
        else
            SpeechManager.Speak(sb.ToString());
    }

    public static void OnAnswerCaptured(bool status)
    {
        // SetConfirmAnswer prefix: capture labels before HideAlert wipes them, and stamp the
        // frame so the HideAlert postfix (which runs mid-original) stays silent.
        var am = AlertManager.Instance;
        if (am == null || !_active)
            return;
        // HideAlert hides the button parents but never clears their .text, so a label read from
        // a hidden button is whatever the *previous* dialog wrote — only trust visible buttons.
        bool singleVisible = am.alertTextSingleButton.transform.parent.gameObject.activeSelf;
        bool rightVisible = am.alertTextRightButton.transform.parent.gameObject.activeSelf;
        bool leftVisible = am.alertTextLeftButton.transform.parent.gameObject.activeSelf;
        _labelAccept = singleVisible ? AccessibleMenuBase.StripRichText(am.alertTextSingleButton.text)
            : rightVisible ? AccessibleMenuBase.StripRichText(am.alertTextRightButton.text)
            : "Accepted.";
        _labelCancel = leftVisible
            ? AccessibleMenuBase.StripRichText(am.alertTextLeftButton.text)
            : "Cancelled.";
        _answeredFrame = Time.frameCount;
    }

    public static void OnAnswered(bool status)
    {
        if (!_active && Time.frameCount != _answeredFrame)
            return; // an answer for an alert we never presented (shouldn't happen; stay quiet)

        if (_suppressAnswerSpeech)
            _suppressAnswerSpeech = false; // our Confirm() already spoke the label
        else
            SpeechManager.Speak(status ? _labelAccept : _labelCancel);
    }

    /// <summary>AlertInputSuccess prefix: it calls HideAlert inside itself, so the frames must be
    /// stamped before the original runs or OnHidden speaks "Alert closed." ahead of the submit
    /// announcement. Also lets delegate postfixes (e.g. the lobby's wrong-password check) tell a
    /// real submit from a cancel that merely fired the same delegate.</summary>
    public static void OnInputSubmitStarting()
    {
        _inputSuccessFrame = Time.frameCount;
        _answeredFrame = Time.frameCount;
    }

    /// <summary>True only in the frame the input alert's text was actually submitted — a
    /// cancel/Escape fires the alert's delegate without any submit.</summary>
    public static bool InputSubmittedThisFrame => Time.frameCount == _inputSuccessFrame;

    public static void OnInputSubmitted()
    {
        // AlertInputSuccess never goes through SetConfirmAnswer, so a suppress flag armed for it
        // would survive and eat the NEXT alert's answer announcement.
        _suppressAnswerSpeech = false;
        _inputSuccessFrame = Time.frameCount;
        _answeredFrame = Time.frameCount;
        var am = AlertManager.Instance;
        string value = am != null ? AccessibleMenuBase.StripRichText(am.GetInputValue()) : "";
        SpeechManager.Speak(value.Length > 0 ? "Submitted " + value : "Nothing entered, closed.");
    }

    public static void OnHidden()
    {
        if (!_active)
            return; // defensive HideAlert calls (scene start, aborts) for alerts never presented
        _active = false;
        _kind = Kind.None;
        _textLines.Clear();
        _edit.Abort();
        // Answers announce themselves; only a silent game-driven hide (multiplayer abort, forced
        // close) needs a note so the user knows the dialog vanished.
        if (Time.frameCount != _answeredFrame)
            SpeechManager.SpeakQueued("Alert closed.");
    }

    // ------------------------------------------------------------------ text-field edit mode

    /// <summary>The editable TMP field of the current alert shape, or null.</summary>
    private static TMPro.TMP_InputField EditField()
    {
        var am = AlertManager.Instance;
        if (am == null)
            return null;
        if (_kind == Kind.Input)
            return am.alertInput;
        if (_kind == Kind.PasteCopy)
            return am.alertInputPC;
        return null;
    }

    /// <summary>Per-frame tick from the poller, delegating to the session: undoes the game's
    /// auto-focus once, mirrors the field text while editing, and notices TMP ending the edit on
    /// its own (its native Enter/Escape handling) so the spoken state can never go stale.</summary>
    public static void Tick()
    {
        if (!Active)
        {
            _edit.Abort();
            return;
        }
        // Confirm/CopyPaste shapes have no edit field, and the manager can momentarily read as
        // null — the session's own Tick treats a null field as "screen gone" and aborts, which
        // would drop the armed auto-focus undo and leave input alerts stuck in typing mode. Only
        // tick the session while the field actually resolves.
        if (EditField() != null)
            _edit.Tick();
    }

    // ------------------------------------------------------------------ navigation (from context)

    public static void Move(int dir)
    {
        // While editing, the arrows belong to the text caret — keep the row focus still.
        if (_edit.Editing)
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;

        _index = Mathf.Clamp(_index + dir, 0, rows.Count - 1);
        SpeechManager.Speak(rows[_index].Speech);
    }

    public static void Confirm()
    {
        // Enter while editing ends the edit, keeping the typed text.
        if (_edit.Editing)
        {
            _edit.EndEdit();
            return;
        }

        // The session Tick may have noticed TMP ending the edit itself this same frame — that
        // Enter already did its job and must not also activate a row.
        if (_edit.EndedThisFrame)
            return;

        // TMP's own submit wiring may already have fired AlertInputSuccess for this same Enter.
        if (Time.frameCount == _inputSuccessFrame)
            return;

        var rows = BuildRows();
        if (rows.Count == 0)
            return;

        _index = Mathf.Clamp(_index, 0, rows.Count - 1);
        var row = rows[_index];
        if (row.Activate == null)
        {
            bool anyOption = rows.Exists(r => r.Activate != null);
            SpeechManager.Speak(anyOption ? "Down to reach the options." : "No options, waiting.");
            return;
        }

        // The text-field row starts editing — it is not an answer, so skip the answer
        // bookkeeping (the session's BeginEdit does its own announcement).
        if (row.IsEdit)
        {
            row.Activate();
            return;
        }

        // The input-accept row announces itself ("Submitted X" from the AlertInputSuccess
        // patch) and never reaches SetConfirmAnswer — arming the suppress flag here would
        // leave it set for the next alert's answer.
        if (row.IsSubmit)
        {
            row.Activate();
            return;
        }

        _suppressAnswerSpeech = true;
        _answeredFrame = Time.frameCount;
        SpeechManager.Speak(row.Speech);
        row.Activate();
    }

    public static void Cancel()
    {
        // Escape while editing only ends the edit (keeping the text) — it must not answer the
        // alert underneath.
        if (_edit.Editing)
        {
            _edit.EndEdit();
            return;
        }

        // Same race as Confirm: if TMP ended the edit first and the session Tick already ran,
        // this Escape belongs to the edit — falling through would answer/dismiss the dialog.
        if (_edit.EndedThisFrame)
            return;

        var am = AlertManager.Instance;
        if (am == null)
            return;

        bool leftVisible = am.alertTextLeftButton.transform.parent.gameObject.activeSelf;
        bool exitVisible = am.exitButton.gameObject.activeSelf;
        bool singleVisible = am.alertTextSingleButton.transform.parent.gameObject.activeSelf;

        if (leftVisible || exitVisible)
        {
            // Same path CloseAlert takes: answer "no". The SetConfirmAnswer patch announces it.
            am.SetConfirmAnswer(false);
        }
        else if (singleVisible)
        {
            // CloseAlert's single-button path fires the delegate and hides without going through
            // SetConfirmAnswer, so it would be silent — speak the dismissal ourselves.
            _answeredFrame = Time.frameCount;
            SpeechManager.Speak("Dismissed.");
            am.CloseAlert();
        }
        else
        {
            SpeechManager.Speak("No options, waiting.");
        }
    }

    // ------------------------------------------------------------------ row model

    /// <summary>
    /// Rebuilt on every read so mid-alert changes (a button hidden via <c>HideConfirmButton</c>,
    /// text typed into the input field) are always reflected. Text lines first, then the input
    /// field (when present), then one row per visible option button in visual left-to-right order.
    /// </summary>
    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();
        var am = AlertManager.Instance;
        if (am == null)
            return rows;

        foreach (var line in _textLines)
            rows.Add(new Row { Speech = line });

        if (_kind == Kind.Input)
        {
            string value = AccessibleMenuBase.StripRichText(am.alertInput.text);
            rows.Add(new Row
            {
                Speech = "Text field: " + (value.Length > 0 ? value : "empty")
                    + ". Press Enter to type",
                Activate = () => _edit.BeginEdit("Editing"),
                IsEdit = true,
            });
        }
        else if (_kind == Kind.PasteCopy)
        {
            string value = AccessibleMenuBase.StripRichText(am.alertInputPC.text);
            rows.Add(new Row
            {
                Speech = "Text field: " + (value.Length > 0 ? value : "empty")
                    + ". Press Enter to type",
                Activate = () => _edit.BeginEdit("Editing"),
                IsEdit = true,
            });
        }
        else if (_kind == Kind.CopyPaste)
        {
            string value = AccessibleMenuBase.StripRichText(am.alertInputCP.text);
            rows.Add(new Row { Speech = "Export text: " + (value.Length > 0 ? value : "empty") });
        }

        if (am.alertTextLeftButton.transform.parent.gameObject.activeSelf)
        {
            string label = AccessibleMenuBase.StripRichText(am.alertTextLeftButton.text);
            rows.Add(new Row { Speech = label + ", button", Activate = () => am.SetConfirmAnswer(false) });
        }
        if (am.alertTextRightButton.transform.parent.gameObject.activeSelf)
        {
            string label = AccessibleMenuBase.StripRichText(am.alertTextRightButton.text);
            rows.Add(new Row { Speech = label + ", button", Activate = () => am.SetConfirmAnswer(true) });
        }
        if (am.alertTextSingleButton.transform.parent.gameObject.activeSelf)
        {
            string label = AccessibleMenuBase.StripRichText(am.alertTextSingleButton.text);
            rows.Add(new Row { Speech = label + ", button", Activate = () => am.SetConfirmAnswer(true) });
        }
        if (_kind == Kind.Input && am.alertInputButtonText.transform.parent.gameObject.activeSelf)
        {
            string label = AccessibleMenuBase.StripRichText(am.alertInputButtonText.text);
            rows.Add(new Row { Speech = label + ", button", Activate = am.AlertInputSuccess, IsSubmit = true });
        }

        return rows;
    }
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertConfirm))]
public class AlertConfirmOpenPatch
{
    static void Postfix() => AlertDialogueManager.OnOpened(1);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertConfirmDouble))]
public class AlertConfirmDoubleOpenPatch
{
    static void Postfix() => AlertDialogueManager.OnOpened(2);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertInput))]
public class AlertInputOpenPatch
{
    static void Postfix() => AlertDialogueManager.OnOpened(3);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertCopyPaste))]
public class AlertCopyPasteOpenPatch
{
    static void Postfix() => AlertDialogueManager.OnOpened(4);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertPasteCopy))]
public class AlertPasteCopyOpenPatch
{
    static void Postfix() => AlertDialogueManager.OnOpened(5);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.SetConfirmAnswer))]
public class AlertAnswerPatch
{
    static void Prefix(bool status) => AlertDialogueManager.OnAnswerCaptured(status);
    static void Postfix(bool status) => AlertDialogueManager.OnAnswered(status);
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertInputSuccess))]
public class AlertInputSuccessPatch
{
    // Prefix, not postfix: the original calls HideAlert inside itself, and the frame stamps
    // must be in place before that postfix decides whether to speak "Alert closed."
    static void Prefix() => AlertDialogueManager.OnInputSubmitStarting();
    static void Postfix() => AlertDialogueManager.OnInputSubmitted();
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.HideAlert))]
public class AlertHidePatch
{
    static void Postfix() => AlertDialogueManager.OnHidden();
}
