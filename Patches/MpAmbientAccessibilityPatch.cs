using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Photon.Realtime;

namespace ObeliskAccess.Patches;

/// <summary>
/// Ambient multiplayer state announcements — no screen of their own, just spoken awareness of what
/// the rest of the party is doing: ready counts on the shared sync screens, desync reloads, the
/// resign/leave/reload alert icons, players joining or leaving the room, and cinematic skip votes.
/// Everything here is gated on <see cref="MpSpeech.IsMp"/> (or fires only from MP-only RPCs), so
/// single-player behaviour is untouched.
/// </summary>

/// <summary>
/// "Players ready: N of M" — DoReadyStatus is the game's single fan-out point for the manual-ready
/// text on town / challenge-selection / card-craft / event screens, and it runs on every client
/// (master RPC handler and share handler both call it). Hero selection is excluded: that screen
/// already announces its own ready edges from its poller.
/// </summary>
[HarmonyPatch(typeof(NetworkManager), "DoReadyStatus")]
internal static class MpReadyStatusAnnouncePatch
{
    private static int _lastReady = -1;
    private static int _lastTotal = -1;

    static void Postfix(NetworkManager __instance)
    {
        if (!MpSpeech.IsMp)
            return;
        var dict = __instance.PlayerManualReady;
        if (dict == null || dict.Count == 0)
        {
            _lastReady = -1;
            _lastTotal = -1;
            return;
        }
        if (HeroSelectionManager.Instance != null)
            return; // hero selection speaks its own ready edges

        int total = dict.Count;
        int ready = 0;
        foreach (var kv in dict)
            if (kv.Value)
                ready++;

        if (ready == _lastReady && total == _lastTotal)
            return;
        _lastReady = ready;
        _lastTotal = total;
        if (ready == 0)
            return; // between-screens reset state; the game shows nothing here either

        var sb = new StringBuilder();
        sb.Append("Players ready: ").Append(ready).Append(" of ").Append(total).Append(".");
        if (ready < total)
        {
            var waiting = new List<string>();
            foreach (var kv in dict)
                if (!kv.Value)
                    waiting.Add(MpSpeech.DisplayNick(kv.Key));
            sb.Append(" Waiting for ").Append(string.Join(", ", waiting.ToArray())).Append(".");
        }
        SpeechManager.SpeakQueued(sb.ToString());
    }
}

/// <summary>Client side of a combat desync: the whole combat is torn down and replayed from the
/// host — interrupt, the player's current activity no longer exists.</summary>
[HarmonyPatch(typeof(MatchManager), "NET_ReloadCombat")]
internal static class MpCombatReloadAnnouncePatch
{
    static void Postfix()
    {
        if (MpSpeech.IsMp)
            SpeechManager.Speak("Combat out of sync. Reloading from the host.");
    }
}

/// <summary>Master side: a client reported a desync and the master is about to resync everyone.</summary>
[HarmonyPatch(typeof(MatchManager), "NET_MasterReloadCombat")]
internal static class MpCombatMasterReloadAnnouncePatch
{
    static void Postfix()
    {
        if (MpSpeech.IsMp)
            SpeechManager.SpeakQueued("A player reported a desync. Resyncing combat.");
    }
}

/// <summary>
/// The three alert context icons are visual-only decorations on dialogs whose text the global
/// alert dialogue already speaks — these lines add the icon's meaning. The door and reload icons
/// also appear on single-player dialogs, hence the MP gate.
/// </summary>
[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.ShowReloadIcon))]
internal static class MpReloadIconAnnouncePatch
{
    static void Postfix()
    {
        if (MpSpeech.IsMp)
            SpeechManager.SpeakQueued("Reload pending.");
    }
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.ShowResignIcon))]
internal static class MpResignIconAnnouncePatch
{
    static void Postfix()
    {
        if (MpSpeech.IsMp)
            SpeechManager.SpeakQueued("Resign vote in progress.");
    }
}

[HarmonyPatch(typeof(AlertManager), nameof(AlertManager.ShowDoorIcon))]
internal static class MpDoorIconAnnouncePatch
{
    static void Postfix()
    {
        if (MpSpeech.IsMp)
            SpeechManager.SpeakQueued("A player is leaving the game.");
    }
}

/// <summary>A player joined the current Photon room (fires on every client already in it).</summary>
[HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom))]
internal static class MpPlayerJoinedAnnouncePatch
{
    static void Postfix(Player other)
    {
        if (other != null)
            SpeechManager.SpeakQueued(MpSpeech.DisplayNick(other.NickName) + " joined the room.");
    }
}

/// <summary>
/// A master switch in this game always means the session is about to be torn down (no host
/// migration) — say only that the host is gone; the consequence is spoken by the
/// OnPlayerLeftRoom patch that immediately follows.
/// </summary>
[HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnMasterClientSwitched))]
internal static class MpMasterSwitchedAnnouncePatch
{
    static void Postfix()
    {
        SpeechManager.SpeakQueued("The host has disconnected.");
    }
}

/// <summary>
/// A player left the room. The original tears the session down (LeaveRoom/ClearGame/scene load)
/// before a postfix could read anything, so the prefix snapshots every fact the announcement
/// needs — including the private masterClientLeftRoom flag, which the method zeroes immediately —
/// and the postfix reconstructs which branch ran.
/// </summary>
[HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom))]
internal static class MpPlayerLeftAnnouncePatch
{
    // A class, not a struct: the Harmony003 analyzer misreads struct-field reads on __state as
    // modifications (same false positive as RouterInputPatches) — a reference type avoids the noise.
    internal sealed class Snap
    {
        public string Nick;
        public bool MasterLeft;
        public string Scene;
        public bool LobbyPresent;
        public bool GameIdEmpty;
        public bool IsMasterClient;
        public bool LoadingGame;
        public int TownTier;
    }

    static void Prefix(NetworkManager __instance, Player other, out Snap __state)
    {
        __state = new Snap
        {
            Nick = MpSpeech.DisplayNick(other != null ? other.NickName : null),
            MasterLeft = Traverse.Create(__instance).Field<bool>("masterClientLeftRoom").Value,
            Scene = SceneStatic.GetSceneName(),
            LobbyPresent = LobbyManager.Instance != null,
            GameIdEmpty = AtOManager.Instance == null || AtOManager.Instance.GetGameId() == "",
            IsMasterClient = Photon.Pun.PhotonNetwork.IsMasterClient,
            LoadingGame = GameManager.Instance != null && GameManager.Instance.IsLoadingGame(),
            TownTier = AtOManager.Instance != null ? AtOManager.Instance.GetTownTier() : -1,
        };
    }

    static void Postfix(Snap __state)
    {
        // Mirror the original's branch order exactly (NetworkManager.OnPlayerLeftRoom).
        if (__state.Scene == "FinishRun" || (__state.Scene == "IntroNewGame" && __state.TownTier == 3))
        {
            SpeechManager.SpeakQueued(__state.Nick + " left.");
            return;
        }
        if (__state.MasterLeft)
        {
            SpeechManager.Speak("The host left the game."
                + (__state.LobbyPresent ? " Room closed." : " Returning to the lobby."));
            return;
        }
        if (__state.LobbyPresent || (__state.GameIdEmpty && __state.Scene != "HeroSelection"))
        {
            SpeechManager.SpeakQueued(__state.Nick + " left the room.");
            return;
        }
        // In-run teardown branch.
        string consequence;
        if (__state.IsMasterClient)
        {
            bool backToLobby = (__state.Scene == "HeroSelection" || __state.Scene == "ChallengeSelection")
                && !__state.LoadingGame;
            consequence = backToLobby ? "Returning to the lobby." : "Reloading the last save.";
        }
        else
        {
            consequence = "Returning to the lobby.";
        }
        SpeechManager.Speak(__state.Nick + " left the game. " + consequence);
    }
}

/// <summary>Cinematic skip votes — only reachable via NET_SkipCinematic (single player skips
/// directly), and the counter only ever increments, so no edge gate is needed.</summary>
[HarmonyPatch(typeof(CinematicManager), "SetTotalPlayersReady")]
internal static class MpCinematicSkipAnnouncePatch
{
    static void Postfix(CinematicManager __instance)
    {
        if (!MpSpeech.IsMp || NetworkManager.Instance == null)
            return;
        int ready = Traverse.Create(__instance).Field<int>("totalPlayersReady").Value;
        int total = NetworkManager.Instance.GetNumPlayers();
        SpeechManager.SpeakQueued(ready >= total
            ? "All players ready. Skipping cinematic."
            : "Skip votes: " + ready + " of " + total + ".");
    }
}
