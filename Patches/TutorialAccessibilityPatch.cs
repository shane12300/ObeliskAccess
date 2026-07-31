using System.Collections.Generic;
using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// Makes the game's tutorial popups (<see cref="PopTutorialManager"/>) accessible:
/// announces the title + first line on open, lets Up/Down read the popup line-by-line
/// (with its buttons appended), Enter activates the focused button (or Continue), and
/// closure is announced. The game's default Escape behaviour is left untouched.
/// </summary>
internal static class TutorialPopupManager
{
    private struct Entry
    {
        public string Speech;
        public BotonGeneric Button;
    }

    private static readonly List<Entry> _entries = new List<Entry>();
    private static int _index;
    private static bool _active;
    private static BotonGeneric _continueButton;

    public static bool Active => _active;

    public static void OnOpened(PopTutorialManager pop)
    {
        if (pop == null || pop.popText == null)
            return;

        var lines = AccessibleMenuBase.SplitLines(pop.popText.text);

        _entries.Clear();
        _continueButton = null;

        string title = lines.Count > 0 ? lines[0] : string.Empty;

        // Entry 0 = title (the tutorial's "name"), then one entry per body line.
        for (int i = 0; i < lines.Count; i++)
            _entries.Add(new Entry { Speech = lines[i], Button = null });

        // Append every available button at the bottom of the dialogue.
        var buttons = pop.GetComponentsInChildren<BotonGeneric>(true);
        foreach (var b in buttons)
        {
            if (b == null || !b.gameObject.activeInHierarchy || !b.buttonEnabled)
                continue;

            // GetText() derefs the button's TMP label unguarded — an unlabeled BotonGeneric under
            // a tutorial prefab would throw out of the Show postfix and break the popup opening.
            string label = b.text != null
                ? AccessibleMenuBase.StripRichText(b.GetText())
                : b.gameObject.name;
            _entries.Add(new Entry { Speech = label + ", button", Button = b });

            if (b.gameObject.name == "Tutorial_Continue")
                _continueButton = b;
        }

        // Fallback: if there's no explicit Continue button, use the first button found.
        if (_continueButton == null)
        {
            foreach (var e in _entries)
            {
                if (e.Button != null)
                {
                    _continueButton = e.Button;
                    break;
                }
            }
        }

        _index = 0;
        _active = true;

        // Single clipboard write (last-write-wins), so build one utterance:
        // name + first body line. When the body is a single line, that line IS the whole body.
        string announcement = title;
        if (lines.Count > 1)
            announcement = title + ". " + lines[1];

        SpeechManager.Speak(announcement);
    }

    public static void OnClosed()
    {
        if (!_active)
            return;

        SpeechManager.Speak("Tutorial closed");

        _active = false;
        _index = 0;
        _entries.Clear();
        _continueButton = null;
    }

    public static void Move(int dir)
    {
        if (!_active || _entries.Count == 0)
            return;

        _index += dir;
        if (_index < 0)
            _index = 0;
        else if (_index >= _entries.Count)
            _index = _entries.Count - 1;

        SpeechManager.Speak(_entries[_index].Speech);
    }

    public static void Activate()
    {
        if (!_active || _entries.Count == 0)
            return;

        var button = _entries[_index].Button;
        if (button != null)
            button.Clicked();
        else
            _continueButton?.Clicked();
    }

}

[HarmonyPatch(typeof(PopTutorialManager), nameof(PopTutorialManager.Show))]
public class TutorialOpenPatch
{
    static void Postfix(PopTutorialManager __instance)
    {
        TutorialPopupManager.OnOpened(__instance);
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.HideTutorialPopup))]
public class TutorialClosePatch
{
    static void Postfix()
    {
        TutorialPopupManager.OnClosed();
    }
}
