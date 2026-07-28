using BepInEx.Logging;
using UnityAccessibilityLib;
using Lib = UnityAccessibilityLib.SpeechManager;

namespace ObeliskAccess;

/// <summary>
/// Single TTS integration point for the mod. Delegates to UnityAccessibilityLib, which speaks
/// through an active screen reader (NVDA/JAWS/…) via UniversalSpeech, with a Windows SAPI fallback.
///
/// UniversalSpeech.dll must sit in the game's root folder (next to the executable). If it is
/// missing, <see cref="Initialize"/> logs a warning and every call here becomes a silent no-op —
/// the mod keeps running, it just doesn't talk.
///
/// Two speaking modes matter for us:
///   • <see cref="Speak"/> interrupts whatever is being spoken (matches menu/navigation, where each
///     keypress should replace the last utterance — the old clipboard "last write wins" behaviour).
///   • <see cref="SpeakQueued"/> appends without interrupting, letting the screen reader's own queue
///     serialise a burst of messages in order (for sequential combat announcements).
/// </summary>
public static class SpeechManager
{
    private static bool _ready;

    /// <summary>
    /// Wire the library to BepInEx logging and start UniversalSpeech. Call once from Plugin.Awake.
    /// </summary>
    public static void Initialize(ManualLogSource logger)
    {
        AccessibilityLog.Logger = new BepInExLoggerAdapter(logger);

        // Everything we speak is descriptive/announcement text, so make it all eligible for
        // RepeatLast() (the library otherwise only stores Dialogue/Narrator types).
        Lib.ShouldStoreForRepeatPredicate = _ => true;

        // The library suppresses a message identical to the previous one within a 0.5s window.
        // That silently ate the second of two identical reads (e.g. arrowing quickly across two
        // copies of the same card). We do our own focus-level dedupe, so disable it.
        Lib.DuplicateWindowSeconds = 0;

        _ready = Lib.Initialize();
        if (!_ready)
        {
            logger.LogWarning(
                "Speech unavailable: UniversalSpeech.dll was not found in the game folder. "
                + "Place the 64-bit UniversalSpeech.dll next to AcrossTheObelisk.exe to enable speech.");
        }
    }

    /// <summary>
    /// Speak <paramref name="text"/>, interrupting any speech in progress. Use for navigation and
    /// single announcements where only the latest matters.
    /// </summary>
    public static void Speak(string text)
    {
        if (!_ready) return;
        var sw = StartTimingIfDebug();
        Lib.Stop();
        Lib.Announce(text);
        LogIfSlow(sw, "Speak", text);
    }

    /// <summary>
    /// Queue <paramref name="text"/> after whatever is currently being spoken, without interrupting.
    /// Use for a run of combat events that should all be heard in order.
    /// </summary>
    public static void SpeakQueued(string text)
    {
        if (!_ready) return;
        var sw = StartTimingIfDebug();
        Lib.Announce(text);
        LogIfSlow(sw, "SpeakQueued", text);
    }

    // ---- speech-latency diagnostic (debug mode) ----
    // Speech goes out through synchronous native calls (screen-reader client, or the SAPI
    // fallback when no reader is running), so a slow backend stalls the Unity main thread for the
    // duration of the call. In multiplayer that matters beyond feel: combat is lock-step, so a
    // stalled frame on this client extends the sync barriers every OTHER player is waiting on —
    // "my partner says his game gets slow" can literally be this client's speech calls. With debug
    // logging enabled, any call that blocks longer than a frame's budget is logged with its text.

    private const long SlowSpeechMs = 15;

    private static System.Diagnostics.Stopwatch StartTimingIfDebug()
        => AccessibilityOptions.DebugEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;

    private static void LogIfSlow(System.Diagnostics.Stopwatch sw, string call, string text)
    {
        if (sw == null)
            return;
        sw.Stop();
        if (sw.ElapsedMilliseconds < SlowSpeechMs)
            return;
        if (text != null && text.Length > 80)
            text = text.Substring(0, 80) + "…";
        Plugin.Logger.LogInfo(
            "[speech-timing] " + call + " blocked the main thread " + sw.ElapsedMilliseconds
            + " ms: \"" + text + "\"");
    }

    /// <summary>Re-speak the most recent message (for a "repeat last" hotkey).</summary>
    public static void RepeatLast()
    {
        if (!_ready) return;
        Lib.RepeatLast();
    }

    /// <summary>Stop all current and queued speech.</summary>
    public static void Stop()
    {
        if (!_ready) return;
        Lib.Stop();
    }
}

/// <summary>Bridges UnityAccessibilityLib's logging interface onto BepInEx's logger.</summary>
internal sealed class BepInExLoggerAdapter : IAccessibilityLogger
{
    private readonly ManualLogSource _logger;

    public BepInExLoggerAdapter(ManualLogSource logger) => _logger = logger;

    public void Msg(string message) => _logger.LogInfo(message);
    public void Warning(string message) => _logger.LogWarning(message);
    public void Error(string message) => _logger.LogError(message);
}
