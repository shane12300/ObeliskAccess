using System.Collections.Generic;
using System.Text;

namespace ObeliskAccess.Patches;

/// <summary>
/// The shared party-strip walker behind the map and town Tab regions: slot focus, wrapping
/// over occupied slots, and the spoken hero summary line. Each screen manager holds its own
/// instance — map and town focus must stay independent.
/// </summary>
internal sealed class PartyStrip
{
    private int _slot;

    /// <summary>The focused party slot (0–3) — what the character-sheet opener needs.</summary>
    public int Slot => _slot;

    public void Reset() => _slot = 0;

    public void FocusFirst()
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        if (!occ.Contains(_slot))
            _slot = occ[0];
        AnnounceSlot(_slot);
    }

    public void Move(int delta)
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        int pos = occ.IndexOf(_slot);
        pos = pos < 0 ? 0 : Nav.Wrap(pos + delta, occ.Count);
        _slot = occ[pos];
        AnnounceSlot(_slot);
    }

    /// <summary>1–4 jump. Empty slots refuse without moving focus; callers switch their
    /// region enum before calling (the strip is focused even when the jump refuses).</summary>
    public void JumpTo(int slot)
    {
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Slot " + (slot + 1) + " empty");
            return;
        }
        _slot = slot;
        AnnounceSlot(slot);
    }

    /// <summary>Re-announce the focused slot (Alt+T detail on the strip).</summary>
    public void Announce() => AnnounceSlot(_slot);

    private static void AnnounceSlot(int slot)
    {
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Empty slot");
            return;
        }
        SpeechManager.Speak(BuildHeroLine(h));
    }

    /// <summary>One spoken hero summary line.</summary>
    internal static string BuildHeroLine(Hero h)
    {
        var sb = new StringBuilder();
        sb.Append(h.SourceName);
        sb.Append(", ").Append(h.HpCurrent).Append(" of ").Append(h.Hp).Append(" health");
        sb.Append(", level ").Append(h.Level);
        if (h.Level < 5)
            sb.Append(", experience ").Append(h.Experience);
        CountCards(h, out int deck, out int injuries);
        sb.Append(", ").Append(deck).Append(deck == 1 ? " card" : " cards");
        if (injuries > 0)
            sb.Append(", ").Append(injuries).Append(injuries == 1 ? " injury" : " injuries");
        if (h.IsReadyForLevelUp())
            sb.Append(", ready to level up");
        return sb.ToString();
    }

    private static void CountCards(Hero h, out int deck, out int injuries)
    {
        deck = 0;
        injuries = 0;
        if (h.Cards == null)
            return;
        foreach (var id in h.Cards)
        {
            var cd = Globals.Instance?.GetCardData(id, false);
            if (cd != null && cd.CardClass == Enums.CardClass.Injury)
                injuries++;
            else
                deck++;
        }
    }

    /// <summary>Hero in the given party slot (0–3), or null. The team is a <c>GameTeam</c> on
    /// <c>AtOManager</c> in this build, not a raw array.</summary>
    internal static Hero GetHero(int index)
    {
        var team = AtOManager.Instance?.team;
        return team != null ? team.GetHero(index) : null;
    }

    internal static List<int> OccupiedSlots()
    {
        var list = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            var h = GetHero(i);
            if (h != null && h.HeroData != null)
                list.Add(i);
        }
        return list;
    }
}
