using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Makes the act-transition screen (scene "IntroNewGame", <see cref="IntroNewGameManager"/>)
/// accessible. The screen shows an act (or sub-dungeon) title, the act's intro story text, and a
/// Continue button; the whole thing is announced on arrival, Up/Down walk it row by row, and Enter
/// continues from any row. Variants covered by the same rows: the sub-dungeon intros (Hatch,
/// Spider Lair, Black Forge, Frozen Sewers, Pyramid, Dreadnought) have no body text and
/// auto-continue after four seconds, and the adventure-complete farewell ("Congratulations")
/// leads to the end-of-run screen instead of the map. The run-start path never shows this screen
/// (it launches the intro cinematic), and a mid-act load redirects straight to the map — neither
/// calls <c>SceneLoaded</c> on this scene, so both stay silent here.
/// </summary>
internal static class IntroScreenManager
{
    private static readonly List<string> _rows = new List<string>();
    private static int _index;
    private static bool _active;

    public static bool Active => _active && IntroNewGameManager.Instance != null;

    public static void OnSceneReady()
    {
        var mgr = IntroNewGameManager.Instance;
        if (mgr == null || mgr.title == null)
            return;

        string title = string.Join(". ", AccessibleMenuBase.SplitLines(mgr.title.text).ToArray());
        if (title.Length == 0)
            return;

        _rows.Clear();
        _rows.Add(title);

        // The body string is complete as soon as the scene is ready — the game's TextFade only
        // animates the glyphs' alpha, so speech can read the whole story immediately.
        var bodyLines = AccessibleMenuBase.SplitLines(mgr.body != null ? mgr.body.text : null);
        _rows.AddRange(bodyLines);
        bool autoAdvances = bodyLines.Count == 0;

        _rows.Add("Continue, button");

        _index = 0;
        _active = true;

        SpeechManager.Speak(title);
        foreach (var line in bodyLines)
            SpeechManager.SpeakQueued(line);
        SpeechManager.SpeakQueued(autoAdvances
            ? "Continuing automatically. Press Enter to continue now."
            : "Press Enter to continue.");
    }

    public static void OnClosed()
    {
        _active = false;
        _index = 0;
        _rows.Clear();
    }

    public static void Move(int dir)
    {
        if (!Active || _rows.Count == 0)
            return;

        _index = Mathf.Clamp(_index + dir, 0, _rows.Count - 1);
        SpeechManager.Speak(_rows[_index]);
    }

    /// <summary>
    /// Continue is the screen's only action and is harmless, so Enter works from any row (same as
    /// the death popup). <c>SkipIntro</c> is exactly what the Continue button's click runs; it
    /// picks the right destination itself (map, or end-of-run when the adventure is complete).
    /// </summary>
    public static void Activate()
    {
        if (!Active)
            return;

        IntroNewGameManager.Instance.SkipIntro();
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.SceneLoaded))]
public class IntroSceneReadyPatch
{
    static void Postfix()
    {
        // SceneLoaded is the game's generic scene-ready hook; on this scene both DoIntro and
        // DoFinishGame call it only after the title/body text is fully set.
        if (SceneStatic.GetSceneName() == "IntroNewGame")
            IntroScreenManager.OnSceneReady();
    }
}

[HarmonyPatch(typeof(IntroNewGameManager), nameof(IntroNewGameManager.SkipIntro))]
public class IntroClosedPatch
{
    static void Postfix()
    {
        // Covers every exit: our Enter, the button, the game's Escape, and the four-second
        // auto-fade. No close announcement — the map / end-of-run arrival overview takes over.
        IntroScreenManager.OnClosed();
    }
}
