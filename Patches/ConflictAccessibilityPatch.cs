using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// MP travel/answer conflict screen (ConflictManager) — the card-flip tie-breaker that opens when
/// players vote for different map nodes or different event replies. The screen is deterministic on
/// every client (same RNG seed), so it is narrated as a play-by-play from postfixes on the game's
/// own roll methods; the only interactive part is the three rule buttons, enabled solely for the
/// player who owns the choosing hero. ↑/↓ (or 1–3) review the rules, Enter chooses via the game's
/// public <c>MapManager.ConflictSelection</c>; everyone else just listens.
/// The instance is not a singleton — it lives at <c>MapManager.Instance.Conflict</c> between
/// <c>InitConflictResolution</c> and the destroy in <c>ResultConflictCo</c>.
/// </summary>
internal static class ConflictScreenManager
{
    private static int _focusIndex;
    private static bool _openAnnounced;
    private static bool _wasActive;
    private static bool _chooserAnnounced;
    private static bool _ruleChosen;
    private static bool _winnerAnnounced;
    private static bool _tieAnnouncedForRound;

    private static readonly string[] RuleFallback =
        { "Lowest card wins", "Closest to two wins", "Highest card wins" };

    private static ConflictManager Conflict
        => MapManager.Instance != null ? MapManager.Instance.Conflict : null;

    internal static bool IsOpen
    {
        get
        {
            var c = Conflict;
            return c != null && c.IsActive();
        }
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Runs every frame from the poller, outside the input gate, so the close of a
    /// conflict torn down under a modal (host disconnect) is still detected.</summary>
    public static void Tick()
    {
        bool active = IsOpen;
        if (_wasActive && !active)
        {
            if (_openAnnounced && !_winnerAnnounced)
                SpeechManager.SpeakQueued("Conflict closed.");
            Reset();
        }
        _wasActive = active;
    }

    private static void Reset()
    {
        _focusIndex = 0;
        _openAnnounced = false;
        _chooserAnnounced = false;
        _ruleChosen = false;
        _winnerAnnounced = false;
        _tieAnnouncedForRound = false;
    }

    // ---------------------------------------------------------------- announcements (from patches)

    public static void OnShow()
    {
        Reset();
        _openAnnounced = true;
        _wasActive = true;
        SpeechManager.Speak("Conflict. Players chose different "
            + (EventManager.Instance != null ? "answers." : "destinations.")
            + " Each hero flips a random card from their deck; the chosen rule decides the winner.");
    }

    public static void OnChooserKnown()
    {
        var c = Conflict;
        if (c == null || _chooserAnnounced)
            return;
        _chooserAnnounced = true;
        var chooser = HeroAt(c.playerChoosing);
        if (MpSpeech.LocalOwns(chooser))
            SpeechManager.Speak("You choose the rule. Up and Down review the three rules, Enter selects.");
        else
            SpeechManager.SpeakQueued(MpSpeech.OwnerNick(chooser) + " is choosing the rule.");
    }

    public static void OnRuleChosen(int option)
    {
        if (_ruleChosen || !_openAnnounced)
            return; // a slave chooser gets both its own call and the master's echo — speak once
        _ruleChosen = true;
        _focusIndex = Mathf.Clamp(option, 0, 2);
        SpeechManager.SpeakQueued("Rule chosen: " + RuleLabel(option) + ". Flipping cards.");
    }

    public static void OnCardFlipped(int heroIndex)
    {
        var c = Conflict;
        if (c == null || !_openAnnounced)
            return;
        var tr = Traverse.Create(c);
        int cost = SafeIntAt(tr, "charRollResult", heroIndex);
        int iterations = SafeIntAt(tr, "charRollIterations", heroIndex);

        var sb = new StringBuilder();
        if (iterations > 1 && !_tieAnnouncedForRound)
        {
            _tieAnnouncedForRound = true;
            sb.Append("Tie. Flipping again. ");
        }
        sb.Append(HeroLabel(heroIndex)).Append(" flips ").Append(FlippedCardName(c, heroIndex))
          .Append(", cost ").Append(cost).Append(".");
        SpeechManager.SpeakQueued(sb.ToString());
    }

    /// <summary>Fires when the RollResult coroutine is created — every DoCard of the round has
    /// already run synchronously by then, so the results are final. Eliminations and the winner
    /// are deliberately NOT announced from here (the coroutine body reaches them later);
    /// this only speaks the standings and advances the round bookkeeping.</summary>
    public static void OnRollRoundComplete()
    {
        var c = Conflict;
        if (c == null || !_openAnnounced)
            return;
        _tieAnnouncedForRound = false;

        var tr = Traverse.Create(c);
        var rolling = tr.Field<bool[]>("charRoll").Value;
        var results = tr.Field<int[]>("charRollResult").Value;
        if (rolling == null || results == null)
            return;
        var parts = new List<string>();
        for (int i = 0; i < rolling.Length && i < results.Length; i++)
            if (rolling[i])
                parts.Add(HeroLabel(i) + " " + results[i]);
        if (parts.Count > 0)
            SpeechManager.SpeakQueued("Results: " + string.Join(", ", parts.ToArray()) + ".");
    }

    public static void OnHeroEliminated(int heroIndex)
    {
        if (_openAnnounced)
            SpeechManager.SpeakQueued(HeroLabel(heroIndex) + " is out.");
    }

    public static void OnWinner(int heroWinner)
    {
        if (!_openAnnounced)
            return;
        _winnerAnnounced = true;
        SpeechManager.SpeakQueued(MpSpeech.OwnerNick(HeroAt(heroWinner)) + " wins the conflict. "
            + (EventManager.Instance != null ? "Their answer is chosen." : "Traveling to their destination."));
    }

    // ---------------------------------------------------------------- input (from the context)

    public static void MoveRule(int delta)
    {
        var c = Conflict;
        if (c == null)
            return;
        _focusIndex = ((_focusIndex + delta) % 3 + 3) % 3;
        AnnounceRule();
    }

    public static void FocusRule(int index)
    {
        if (Conflict == null || index < 0 || index > 2)
            return;
        _focusIndex = index;
        AnnounceRule();
    }

    private static void AnnounceRule()
    {
        SpeechManager.Speak(RuleLabel(_focusIndex) + ". Rule " + (_focusIndex + 1) + " of 3"
            + (_ruleChosen ? ", rule already chosen" : ""));
    }

    public static void Confirm()
    {
        var c = Conflict;
        if (c == null || MapManager.Instance == null)
            return;
        if (_ruleChosen)
        {
            SpeechManager.Speak("Rule already chosen.");
            return;
        }
        if (c.playerChoosing < 0)
        {
            SpeechManager.Speak("Choosing order still syncing.");
            return;
        }
        var boton = c.botonConflict != null && _focusIndex < c.botonConflict.Length
            ? c.botonConflict[_focusIndex] : null;
        if (boton != null && boton.IsEnabled())
        {
            SpeechManager.Speak("Choosing " + RuleLabel(_focusIndex));
            MapManager.Instance.ConflictSelection(_focusIndex);
        }
        else
        {
            SpeechManager.Speak("Waiting for " + MpSpeech.OwnerNick(HeroAt(c.playerChoosing))
                + " to choose the rule.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Hero HeroAt(int index)
    {
        if (index < 0)
            return null;
        var c = Conflict;
        var team = c != null ? Traverse.Create(c).Field<GameTeam>("team").Value : null;
        var hero = team != null ? team.GetHero(index) : null;
        if (hero == null && AtOManager.Instance != null && AtOManager.Instance.team != null)
            hero = AtOManager.Instance.team.GetHero(index);
        return hero;
    }

    /// <summary>"Magnus" for the local player's heroes, "Bob's Magnus" for a partner's.</summary>
    private static string HeroLabel(int heroIndex)
    {
        var hero = HeroAt(heroIndex);
        if (hero == null)
            return "Hero " + (heroIndex + 1);
        string name = AccessibleMenuBase.StripRichText(hero.SourceName ?? "");
        if (name.Length == 0)
            name = "Hero " + (heroIndex + 1);
        return MpSpeech.LocalOwns(hero) ? name : MpSpeech.OwnerNick(hero) + "'s " + name;
    }

    private static string RuleLabel(int n)
    {
        n = Mathf.Clamp(n, 0, 2);
        var c = Conflict;
        var boton = c != null && c.botonConflict != null && n < c.botonConflict.Length
            ? c.botonConflict[n] : null;
        string label = boton != null ? AccessibleMenuBase.StripRichText(boton.GetText() ?? "") : "";
        return label.Length > 0 ? label : RuleFallback[n];
    }

    private static string FlippedCardName(ConflictManager c, int heroIndex)
    {
        if (c.characterTcards != null && heroIndex >= 0 && heroIndex < c.characterTcards.Length)
        {
            var holder = c.characterTcards[heroIndex];
            if (holder != null && holder.childCount > 0)
            {
                var item = holder.GetChild(holder.childCount - 1).GetComponent<CardItem>();
                var cd = item != null ? item.GetCardData() : null;
                string nm = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName ?? "") : "";
                if (nm.Length > 0)
                    return nm;
            }
        }
        return "a card";
    }

    private static int SafeIntAt(Traverse tr, string field, int index)
    {
        var arr = tr.Field<int[]>(field).Value;
        return arr != null && index >= 0 && index < arr.Length ? arr[index] : 0;
    }
}

// ---------------------------------------------------------------- patches
// Conflicts only ever exist in multiplayer (NET_DoConflict is the sole spawner), so no IsMp gates
// are needed; the manager's null/_openAnnounced guards make every hook safe anyway.

/// <summary>Show runs on every client (broadcast via NET_DoConflict).</summary>
[HarmonyPatch(typeof(ConflictManager), nameof(ConflictManager.Show))]
public class ConflictShowAnnouncePatch
{
    static void Postfix() => ConflictScreenManager.OnShow();
}

/// <summary>Runs on all clients: on the master from ShowCo, on others from NET_ShareConflictOrderCo.</summary>
[HarmonyPatch(typeof(ConflictManager), "EnableButtonsForPlayerChoosing")]
public class ConflictChooserAnnouncePatch
{
    static void Postfix() => ConflictScreenManager.OnChooserKnown();
}

/// <summary>Local click/keyboard path (also covers our own ConflictSelection call).</summary>
[HarmonyPatch(typeof(ConflictManager), nameof(ConflictManager.SelectOption))]
public class ConflictRuleSelectedAnnouncePatch
{
    static void Postfix(int option) => ConflictScreenManager.OnRuleChosen(option);
}

/// <summary>All of the announce postfixes below execute INSIDE the game's deterministic roll
/// coroutines, which run in lock-step on every client — an uncaught exception would kill the
/// local roll loop and desync a screen that has no cancel. Speech is never worth that: swallow
/// and log instead.</summary>
internal static class ConflictSafe
{
    internal static void Run(System.Action body)
    {
        try
        {
            body();
        }
        catch (System.Exception e)
        {
            Plugin.Logger.LogWarning("Conflict announce failed: " + e.Message);
        }
    }
}

/// <summary>Network echo path — the master's NET_SelectConflictOption lands here on other clients.
/// The manager dedupes, so a chooser hearing both paths speaks once.</summary>
[HarmonyPatch(typeof(ConflictManager), nameof(ConflictManager.SelectOptionFromOutside))]
public class ConflictRuleEchoAnnouncePatch
{
    static void Postfix(int option) => ConflictSafe.Run(() => ConflictScreenManager.OnRuleChosen(option));
}

[HarmonyPatch(typeof(ConflictManager), "DoCard")]
public class ConflictCardFlipAnnouncePatch
{
    static void Postfix(int heroIndex) => ConflictSafe.Run(() => ConflictScreenManager.OnCardFlipped(heroIndex));
}

/// <summary>Fires at coroutine creation — see OnRollRoundComplete for why that is safe here.</summary>
[HarmonyPatch(typeof(ConflictManager), "RollResult")]
public class ConflictRollResultAnnouncePatch
{
    static void Postfix() => ConflictSafe.Run(ConflictScreenManager.OnRollRoundComplete);
}

[HarmonyPatch(typeof(ConflictManager), "TurnOffCharacter")]
public class ConflictEliminationAnnouncePatch
{
    static void Postfix(int heroIndex) => ConflictSafe.Run(() => ConflictScreenManager.OnHeroEliminated(heroIndex));
}

[HarmonyPatch(typeof(ConflictManager), "FinalResolution")]
public class ConflictWinnerAnnouncePatch
{
    static void Postfix(int heroWinner) => ConflictSafe.Run(() => ConflictScreenManager.OnWinner(heroWinner));
}
