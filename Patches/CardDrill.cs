using System;
using System.Collections.Generic;
using Cards;

namespace ObeliskAccess.Patches;

/// <summary>
/// The Ctrl+Up/Down card drill shared by the rewards, loot and character-sheet screens: enter
/// on a card to walk its <see cref="CardSpeech.DetailLines"/> one line per press, exit back to
/// the screen's own focus. Each screen holds its own instance. (Combat has a richer,
/// multi-mode drill of its own — deliberately not this class.)
/// </summary>
internal sealed class CardDrill
{
    private readonly List<string> _lines = new List<string>();
    private int _index;

    public bool Active { get; private set; }

    /// <summary>Enter the drill on a card; no-op (returns false) when there is none.</summary>
    public bool Begin(CardRealtimeData cd, int? liveCost = null)
    {
        if (cd == null)
            return false;
        _lines.Clear();
        _lines.AddRange(CardSpeech.DetailLines(cd, liveCost));
        Active = true;
        _index = 0;
        SpeechManager.Speak(_lines.Count > 0 ? _lines[0] : "No description");
        return true;
    }

    public void Step(int dir)
    {
        if (!Active || _lines.Count == 0)
            return;
        _index = Nav.Clamp(_index + dir, 0, _lines.Count - 1);
        SpeechManager.Speak(_lines[_index]);
    }

    /// <summary>Leave the drill and re-announce the underlying focus; false when not drilling
    /// (the caller's plain-arrow handling proceeds).</summary>
    public bool Exit(Action reannounce)
    {
        if (!Active)
            return false;
        Reset();
        reannounce?.Invoke();
        return true;
    }

    /// <summary>Silent clear (screen reset, focus moved).</summary>
    public void Reset()
    {
        Active = false;
        _lines.Clear();
        _index = 0;
    }
}
