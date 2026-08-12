using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// MP players panel — the second face of the always-alive AlertManager canvas (ShowPlayers swaps
/// <c>popupT</c> off / <c>playersT</c> on; the alert dialogue deliberately excludes it via its
/// popupT guard, so the two contexts can never both be active). Rows are rebuilt live — one per
/// occupied slot of <c>NetworkManager.PlayerPositionListArray()</c>, never by walking
/// <c>playerList</c> itself, which renders per position and leaves stale, deactivated hole rows
/// behind (see <c>Positions</c>). Each row's text starts from the game's own rendered nick line on
/// <c>playerList[pos]</c> (nick + master tag)
/// plus ping, platform, mute state, and mod-added enrichment the game's panel drops: ready state
/// and which heroes the player runs. Enter on a partner's row toggles mute via the row's own
/// public <c>DoMute</c>/<c>DoUnmute</c>; Escape closes through <c>HideAlert</c>. Opened by the
/// game's players button or the mod's Alt+P (ChatHotkeyPoller).
/// </summary>
internal static class PlayersPanelManager
{
    private static int _index;
    private static bool _active;
    private static int _closedByUsFrame = -1;

    public static bool PanelOpen
        => AlertManager.Instance != null
            && AlertManager.Instance.IsActive()
            && AlertManager.Instance.playersT != null
            && AlertManager.Instance.playersT.gameObject.activeSelf;

    // ---------------------------------------------------------------- open/close (patches)

    internal static void OnShown()
    {
        _active = true;
        _index = 0;
        int n = ActiveRowCount();
        SpeechManager.Speak("Players panel. " + n + (n == 1 ? " player. " : " players. ")
            + RowSpeech(0)
            + ". Up and Down to review, Enter mutes or unmutes, Escape closes.");
    }

    internal static void OnHidden()
    {
        if (!_active)
            return;
        _active = false;
        if (Time.frameCount != _closedByUsFrame)
            SpeechManager.SpeakQueued("Players panel closed.");
    }

    /// <summary>A real alert claiming the shared canvas force-closes the panel WITHOUT HideAlert
    /// (ShowAlert just swaps playersT off). Drop our open flag silently — the alert's own open
    /// announce covers the transition; without this, the alert's eventual close would speak a
    /// phantom "Players panel closed" minutes after the panel actually went away.</summary>
    internal static void OnSupersededByAlert()
    {
        if (_active && PanelOpen)
            _active = false;
    }

    // ---------------------------------------------------------------- input (from the context)

    public static void Move(int dir)
    {
        int count = ActiveRowCount();
        if (count == 0)
            return;
        _index = Mathf.Clamp(_index, 0, count - 1);
        _index = ((_index + dir) % count + count) % count;
        SpeechManager.Speak(RowSpeech(_index) + ", " + (_index + 1) + " of " + count);
    }

    public static void Confirm()
    {
        var am = AlertManager.Instance;
        var nm = NetworkManager.Instance;
        if (am == null || nm == null || am.playerList == null)
            return;
        var positions = Positions();
        int count = positions.Count;
        if (count == 0)
            return;
        _index = Mathf.Clamp(_index, 0, count - 1);
        int pos = positions[_index];

        string nick = nm.GetPlayerNickPosition(pos);
        if (string.IsNullOrEmpty(nick))
            return;
        if (nick == nm.GetPlayerNick())
        {
            SpeechManager.Speak("That's you.");
            return;
        }
        var row = pos < am.playerList.Count ? am.playerList[pos] : null;
        if (row == null)
            return;
        if (nm.IsPlayerMutedBySlot(pos))
        {
            row.DoUnmute();
            SpeechManager.Speak(MpSpeech.DisplayNick(nick) + " unmuted.");
        }
        else
        {
            row.DoMute();
            SpeechManager.Speak(MpSpeech.DisplayNick(nick)
                + " muted. Their chat messages and pings are hidden.");
        }
    }

    public static void Close()
    {
        var am = AlertManager.Instance;
        if (am == null)
            return;
        _closedByUsFrame = Time.frameCount;
        SpeechManager.Speak("Players panel closed.");
        am.HideAlert();
    }

    // ---------------------------------------------------------------- rows

    /// <summary>Row → position mapping. A mid-run leave blanks a PlayerPositionList entry without
    /// compacting (normalization only happens at launch), and the game's SetPlayers renders per
    /// POSITION — hole rows are deactivated with stale text. Walking only the occupied positions
    /// keeps every player reachable and never reads a hidden row.</summary>
    private static System.Collections.Generic.List<int> Positions()
    {
        var list = new System.Collections.Generic.List<int>();
        var nm = NetworkManager.Instance;
        var arr = nm != null ? nm.PlayerPositionListArray() : null;
        if (arr != null)
            for (int i = 0; i < arr.Length; i++)
                if (!string.IsNullOrEmpty(arr[i]))
                    list.Add(i);
        return list;
    }

    private static int ActiveRowCount() => Positions().Count;

    private static string RowSpeech(int i)
    {
        var am = AlertManager.Instance;
        var nm = NetworkManager.Instance;
        if (am == null || nm == null)
            return "";
        var positions = Positions();
        if (i < 0 || i >= positions.Count)
            return "";
        int pos = positions[i];
        string nick = nm.GetPlayerNickPosition(pos);
        var row = am.playerList != null && pos < am.playerList.Count ? am.playerList[pos] : null;

        var sb = new StringBuilder();
        // The game's own rendered line already carries the display nick + "(master)".
        string nameLine = row != null && row.playerName != null
            ? CardSpeech.CleanFlat(row.playerName.text) : "";
        sb.Append(nameLine.Length > 0 ? nameLine : MpSpeech.DisplayNick(nick));

        if (!string.IsNullOrEmpty(nick))
        {
            if (nick == nm.GetPlayerNick())
                sb.Append(", that's you");
            string platform = nm.GetPlatformString(nick);
            if (!string.IsNullOrEmpty(platform))
                sb.Append(", ").Append(platform);
            if (nm.PlayerManualReady != null
                && nm.PlayerManualReady.TryGetValue(nick, out bool ready) && ready)
                sb.Append(", ready");
            if (nm.IsPlayerMutedBySlot(pos))
                sb.Append(", muted");
            AppendHeroes(sb, nick);
        }

        string ping = row != null && row.playerDescription != null
            ? CardSpeech.CleanFlat(row.playerDescription.text) : "";
        if (ping.Length > 0)
            sb.Append(", ").Append(ping);
        return sb.ToString();
    }

    /// <summary>", playing Magnus and Evelyn" — the heroes owned by that player. Null-safe for
    /// the lobby, where no team exists yet.</summary>
    private static void AppendHeroes(StringBuilder sb, string nick)
    {
        var ato = AtOManager.Instance;
        var team = ato != null ? ato.team : null;
        if (team == null)
            return;
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 4; i++)
        {
            var hero = team.GetHero(i);
            if (hero != null && hero.Owner == nick)
            {
                string n = AccessibleMenuBase.StripRichText(hero.SourceName ?? "");
                if (n.Length > 0)
                    names.Add(n);
            }
        }
        if (names.Count > 0)
            sb.Append(", playing ").Append(string.Join(" and ", names.ToArray()));
    }
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.ShowPlayers))]
public class PlayersPanelShownPatch
{
    static void Postfix() => PlayersPanelManager.OnShown();
}

/// <summary>HideAlert closes both alert faces; the alert dialogue's own hide patch is guarded by
/// its _active flag, this one by the panel's — they never both speak.</summary>
[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.HideAlert))]
public class PlayersPanelHiddenPatch
{
    static void Postfix() => PlayersPanelManager.OnHidden();
}

/// <summary>A real alert replacing the panel on the shared canvas (no HideAlert on that path).</summary>
[HarmonyPatch(typeof(AlertManager), "ShowAlert")]
public class PlayersPanelSupersededPatch
{
    static void Prefix() => PlayersPanelManager.OnSupersededByAlert();
}
