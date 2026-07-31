using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Reads the in-combat death popup (<see cref="UICombatDeath"/>, shown when a hero dies but the
/// party survives). Presented like the tutorial popup: the title, the body lines and a Death's
/// Door note are walkable with Up/Down, the Continue button is the last row, Enter dismisses.
/// The opening announcement is queued — the popup appears mid-turn-narration and must not
/// interrupt it; the player reads it when the speech queue gets there. In multiplayer the
/// Continue button belongs only to the dead hero's owner (and the host auto-dismisses after 30s),
/// so its row and Enter track live button visibility. Input is mapped by
/// <see cref="ObeliskAccess.Input.Contexts.DeathScreenInputContext"/>; Alt+T (Death's Door card
/// detail) and Alt+R live in <see cref="ObeliskAccess.Input.CombatHotkeyPoller"/>.
/// </summary>
internal static class DeathScreenPopupManager
{
    private static readonly List<string> _lines = new List<string>();
    private static int _index;
    private static bool _active;
    private static string _owner = "";

    public static void OnOpened(Hero hero)
    {
        var death = MatchManager.Instance != null ? MatchManager.Instance.DeathScreen : null;
        if (death == null || hero == null)
            return;

        // A second death while the popup is already up: the game skips SetCharacter, so the TMPs
        // still show the FIRST hero — a full re-announce would mix the two. Note the extra death
        // and keep the existing rows/owner.
        if (_active)
        {
            SpeechManager.SpeakQueued(AccessibleMenuBase.StripRichText(hero.SourceName)
                + " also fell. Death's Door added to their deck.");
            return;
        }

        _lines.Clear();
        string title = AccessibleMenuBase.StripRichText(death.textCharDeath.text);
        if (title.Length > 0)
            _lines.Add(title);
        _lines.AddRange(AccessibleMenuBase.SplitLines(death.textInstructions.text));
        _lines.Add("Death's Door curse added to "
            + AccessibleMenuBase.StripRichText(hero.SourceName) + "'s deck.");
        _index = 0;
        _active = true;
        _owner = string.IsNullOrEmpty(hero.Owner) ? "the host" : MpSpeech.DisplayNick(hero.Owner);

        // Queued on purpose: the death lands mid combat narration (the killing blow, triggered
        // auras) and must not clobber it — the screen reader reaches this when the queue does.
        string prompt = ButtonVisible()
            ? "Press Enter to continue."
            : "Waiting for " + _owner + " to continue.";
        // With an empty title the first body line sits at index 0, not 1.
        int first = title.Length > 0 ? 1 : 0;
        string firstLine = _lines.Count > first ? " " + _lines[first] : "";
        string lead = title.Length > 0 ? title + "." : "";
        SpeechManager.SpeakQueued(lead + firstLine + " " + prompt + " Up and down to read.");
    }

    public static void OnClosed()
    {
        // TurnOff is also called defensively (combat start, resets) — only announce a real close.
        if (!_active)
            return;
        _active = false;
        _lines.Clear();
        SpeechManager.SpeakQueued("Death screen closed.");
    }

    public static bool Active
    {
        get
        {
            var m = MatchManager.Instance;
            return _active && m != null && m.DeathScreen != null && m.DeathScreen.IsActive();
        }
    }

    /// <summary>Row count: text lines plus the Continue row when its button is visible.</summary>
    private static int RowCount() => _lines.Count + (ButtonVisible() ? 1 : 0);

    private static bool ButtonVisible()
    {
        var death = MatchManager.Instance != null ? MatchManager.Instance.DeathScreen : null;
        return death != null && death.button != null && death.button.gameObject.activeSelf;
    }

    public static void Move(int dir)
    {
        int count = RowCount();
        if (count == 0)
            return;

        _index = Mathf.Clamp(_index + dir, 0, count - 1);
        if (_index < _lines.Count)
            SpeechManager.Speak(_lines[_index]);
        else
            SpeechManager.Speak("Continue, button");
    }

    public static void Confirm()
    {
        // Enter works from any row — Continue is the popup's only button (tutorial convention).
        if (ButtonVisible())
            MatchManager.Instance.DeathScreen.TurnOffFromButton();
        else
            SpeechManager.Speak("Waiting for " + _owner + " to continue.");
    }

    /// <summary>Alt+T: full detail of the Death's Door curse the death just added.</summary>
    public static void SpeakDeathsDoorDetail()
    {
        var data = Globals.Instance != null
            ? Globals.Instance.GetCardData("DeathsDoor", instantiate: false)
            : null;
        SpeechManager.Speak(data != null ? CardSpeech.FullDetail(data) : "No card data.");
    }
}

[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.ShowDeathScreen))]
public class DeathScreenOpenPatch
{
    static void Postfix(Hero _hero)
    {
        DeathScreenPopupManager.OnOpened(_hero);
    }
}

[HarmonyPatch(typeof(UICombatDeath), nameof(UICombatDeath.TurnOff))]
public class DeathScreenClosePatch
{
    static void Postfix()
    {
        DeathScreenPopupManager.OnClosed();
    }
}
