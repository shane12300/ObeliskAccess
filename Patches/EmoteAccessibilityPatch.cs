using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// MP combat emotes and pings. The game already binds the bare letters R/E/S/A/W/Q to emotes in
/// multiplayer combat (InputController → BattleKeyboard.KeyboardEmote, active only when the
/// emote wheel exists — BeginMatch spawns it in MP only), so sending the four untargeted emotes
/// needs no mod keys. What this file adds:
/// - a keyboard completion for the two TARGETED pings (S = ping, A = attack ping): after the
///   game shows the per-character ping markers, arrow to a character and press Enter — the
///   combat context calls the same public <c>MatchManager.EmoteTarget</c> the mouse marker
///   click would; Escape cancels via the game's own reset;
/// - spoken feedback for every emote and card ping, local and remote (the postfixes sit after
///   the game's mute filter);
/// - the cooldown refusal (the game silently ignores emote keys for 3 seconds after each);
/// - optional partner aim announcements (NET_DrawArrowNet) behind the off-by-default
///   AnnouncePartnerAim toggle, speaking only target changes.
/// </summary>
internal static class EmotePingManager
{
    private static int _pendingAction = -1;

    /// <summary>Spoken names for the six emote actions (order = EmoteSmall.SetAction's letters
    /// R/E/S/A/W/Q). The S/A ping wording is verified against the sprites in playtest.</summary>
    internal static string ActionWord(int action)
    {
        switch (action)
        {
            case 0: return "a heart";
            case 1: return "surprise";
            case 2: return "a ping";
            case 3: return "an attack ping";
            case 4: return "indifference";
            case 5: return "anger";
            default: return "an emote";
        }
    }

    /// <summary>True while a targeted ping is waiting for the local player to pick a character.
    /// Re-validated live against the game's own ping markers, so a mouse pick or the game hiding
    /// them can never strand the mode.</summary>
    public static bool Pending
    {
        get
        {
            if (_pendingAction < 0)
                return false;
            if (AnyPingMarkerShowing())
                return true;
            _pendingAction = -1;
            return false;
        }
    }

    public static void BeginPick(int action)
    {
        _pendingAction = action;
        SpeechManager.Speak("Pick a target for " + ActionWord(action)
            + ": arrow to a character and press Enter. Escape cancels.");
    }

    public static void ClearPending() => _pendingAction = -1;

    /// <summary>Enter on a focused character while a ping pick is pending (from the combat
    /// context). Returns true when consumed.</summary>
    public static bool TryCompletePick()
    {
        if (!Pending)
            return false;
        var mm = MatchManager.Instance;
        if (mm == null)
        {
            _pendingAction = -1;
            return false;
        }
        var t = CombatNavigator.FocusedTransform;
        var ci = t != null
            ? (t.GetComponent<CharacterItem>() ?? t.GetComponentInParent<CharacterItem>())
            : null;
        Character target = null;
        if (ci != null)
        {
            target = Traverse.Create(ci).Field<Hero>("_hero").Value;
            if (target == null)
                target = Traverse.Create(ci).Field<NPC>("_npc").Value;
        }
        if (target == null)
        {
            SpeechManager.Speak("Arrow to a character first.");
            return true;
        }
        int action = _pendingAction;
        _pendingAction = -1;
        // The exact call the mouse marker click makes; it resolves the sender internally,
        // sends the RPC, starts the cooldown and hides the markers.
        mm.EmoteTarget(target.Id, action);
        return true;
    }

    /// <summary>Escape while a ping pick is pending. Returns true when consumed.</summary>
    public static bool TryCancelPick()
    {
        if (!Pending)
            return false;
        _pendingAction = -1;
        var mm = MatchManager.Instance;
        if (mm != null)
            Traverse.Create(mm).Method("ResetCharactersPing").GetValue();
        SpeechManager.Speak("Ping cancelled.");
        return true;
    }

    private static bool AnyPingMarkerShowing()
    {
        var mm = MatchManager.Instance;
        var team = mm != null ? mm.GetTeamHero() : null;
        if (team == null)
            return false;
        foreach (var c in team)
        {
            if (c != null && c.Alive && c.HeroItem != null
                && c.HeroItem.emoteCharacterPing != null
                && c.HeroItem.emoteCharacterPing.gameObject.activeSelf)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- shared text helpers

    /// <summary>"You" / "Bob's Magnus" for the hero at a team index.</summary>
    internal static string SenderClause(int heroIndex, bool fromNet)
    {
        if (!fromNet)
            return "You";
        var mm = MatchManager.Instance;
        var team = mm != null ? mm.GetTeamHero() : null;
        var hero = team != null && heroIndex >= 0 && heroIndex < team.Count
            ? team[heroIndex] as Hero : null;
        if (hero == null)
            return "A partner";
        string name = AccessibleMenuBase.StripRichText(hero.SourceName ?? "");
        return MpSpeech.OwnerNick(hero) + (name.Length > 0 ? "'s " + name : "");
    }

    internal static Character FindById(string id)
    {
        // Mirror EmoteTarget's own target filter (alive + spawned item): the game silently no-ops
        // a ping at a just-died character, so matching on Id alone would announce an undelivered ping.
        var mm = MatchManager.Instance;
        if (mm == null || string.IsNullOrEmpty(id))
            return null;
        var heroes = mm.GetTeamHero();
        if (heroes != null)
            foreach (var c in heroes)
                if (c != null && c.Alive && c.HeroItem != null && c.Id == id)
                    return c;
        var npcs = mm.GetTeamNPC();
        if (npcs != null)
            foreach (var c in npcs)
                if (c != null && c.Alive && c.NPCItem != null && c.Id == id)
                    return c;
        return null;
    }

    internal static string CharacterName(Character c)
    {
        string n = c != null ? AccessibleMenuBase.StripRichText(c.SourceName ?? "") : "";
        return n.Length > 0 ? n : "a character";
    }

    // ---------------------------------------------------------------- partner aim (toggle-gated)

    private static short _aimTable = -1;
    private static bool _aimIsHero;
    private static byte _aimIndex;

    internal static void OnPartnerAim(short tablePosition, bool isHero, byte characterIndex)
    {
        if (AccessibilityOptions.AnnouncePartnerAim == null || !AccessibilityOptions.AnnouncePartnerAim.Value)
            return;
        if (tablePosition == _aimTable && isHero == _aimIsHero && characterIndex == _aimIndex)
            return; // only target CHANGES are spoken
        _aimTable = tablePosition;
        _aimIsHero = isHero;
        _aimIndex = characterIndex;

        var mm = MatchManager.Instance;
        if (mm == null)
            return;
        string card = "a card";
        var tableList = mm.CardItemTable;
        if (tableList != null && tablePosition >= 0 && tablePosition < tableList.Count
            && tableList[tablePosition] != null && tableList[tablePosition].CardData != null)
            card = AccessibleMenuBase.StripRichText(tableList[tablePosition].CardData.CardName);
        var team = isHero ? mm.GetTeamHero() : mm.GetTeamNPC();
        string target = team != null && characterIndex < team.Count
            ? CharacterName(team[characterIndex]) : "a character";
        SpeechManager.SpeakQueued("Partner aims " + card + " at " + target + ".");
    }

    internal static void ResetAim()
    {
        _aimTable = -1;
        _aimIsHero = false;
        _aimIndex = 0;
    }
}

// ---------------------------------------------------------------- patches
// Emotes only exist in MP combat (BeginMatch spawns the wheel in MP only), but every patch is
// IsMp-gated anyway.

/// <summary>Cooldown refusal (prefix — the original silently no-ops while blocked) and the
/// targeted-ping pick prompt (postfix — the markers are up once the original ran its guards).</summary>
[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.SetCharactersPing))]
public class EmoteKeyPressedPatch
{
    static void Prefix(MatchManager __instance, int _action, out bool __state)
    {
        __state = false; // whether the original will act
        if (!MpSpeech.IsMp || __instance.emoteManager == null)
            return;
        bool willAct = !__instance.waitingDeathScreen
            && !__instance.WaitingForActionScreen()
            && !__instance.emoteManager.IsBlocked()
            && __instance.emoteManager.gameObject.activeSelf;
        __state = willAct;
        if (!willAct
            && __instance.emoteManager.gameObject.activeSelf
            && __instance.emoteManager.IsBlocked()
            && ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive)
            SpeechManager.Speak("Emotes are on cooldown for a few seconds.");
    }

    static void Postfix(MatchManager __instance, int _action, bool __state)
    {
        if (__state && MpSpeech.IsMp && __instance.emoteManager != null
            && __instance.emoteManager.EmoteNeedsTarget(_action)
            && ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive)
            EmotePingManager.BeginPick(_action);
    }
}

/// <summary>Every emote/ping, local and remote — NET_EmoteTarget calls this with _fromNet after
/// the game's own mute filter, so muted players never speak. Harmony postfix parameters share
/// the original's locals, so _heroIndex is already the resolved sender index here.</summary>
[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.EmoteTarget))]
public class EmoteAnnouncePatch
{
    static void Postfix(string _id, int _action, int _heroIndex, bool _fromNet)
    {
        if (!MpSpeech.IsMp)
            return;
        if (!_fromNet)
            EmotePingManager.ClearPending();
        var target = EmotePingManager.FindById(_id);
        if (target == null)
            return; // the original also no-ops when the id resolves to nothing
        string sender = EmotePingManager.SenderClause(_heroIndex, _fromNet);
        string word = EmotePingManager.ActionWord(_action);
        if (_action == 2 || _action == 3)
            SpeechManager.SpeakQueued(sender + (_fromNet ? " sends " : " send ") + word
                + " at " + EmotePingManager.CharacterName(target) + ".");
        else
            SpeechManager.SpeakQueued(sender + (_fromNet ? " emotes " : " emote ") + word + ".");
    }
}

/// <summary>Card pings. DoEmoteCard is the shared sink for the local SendEmoteCard and the
/// NET_SendEmoteCard RPC (which mute-filters first), so one patch covers both and never speaks
/// for muted players. The prefix snapshots whether the icon was already up — the action toggles.</summary>
[HarmonyPatch(typeof(MatchManager), "DoEmoteCard")]
public class EmoteCardPingAnnouncePatch
{
    static void Prefix(MatchManager __instance, byte _tablePosition, byte _heroIndex, out bool __state)
    {
        __state = false;
        var table = __instance.CardItemTable;
        if (table != null && _tablePosition < table.Count && table[_tablePosition] != null)
            __state = table[_tablePosition].HaveEmoteIcon(_heroIndex);
    }

    static void Postfix(MatchManager __instance, byte _tablePosition, byte _heroIndex, bool __state)
    {
        if (!MpSpeech.IsMp)
            return;
        var table = __instance.CardItemTable;
        if (table == null || _tablePosition >= table.Count || table[_tablePosition] == null)
            return;
        var team = __instance.GetTeamHero();
        var hero = team != null && _heroIndex < team.Count ? team[_heroIndex] as Hero : null;
        bool local = hero != null && MpSpeech.LocalOwns(hero);
        string sender = local ? "You" : MpSpeech.OwnerNick(hero);
        string card = table[_tablePosition].CardData != null
            ? AccessibleMenuBase.StripRichText(table[_tablePosition].CardData.CardName)
            : "a card";
        SpeechManager.SpeakQueued(__state
            ? sender + (local ? " clear" : " clears") + " the card ping."
            : sender + (local ? " ping" : " pings") + " the card " + card + ".");
    }
}

/// <summary>Partner drag-arrow target (off by default — AnnouncePartnerAim toggle).</summary>
[HarmonyPatch(typeof(MatchManager), "NET_DrawArrowNet")]
public class PartnerAimAnnouncePatch
{
    static void Postfix(short _tablePosition, bool _isHero, byte _characterIndex)
    {
        if (MpSpeech.IsMp)
            EmotePingManager.OnPartnerAim(_tablePosition, _isHero, _characterIndex);
    }
}

[HarmonyPatch(typeof(MatchManager), "NET_StopArrowNet")]
public class PartnerAimStoppedPatch
{
    static void Postfix() => EmotePingManager.ResetAim();
}
