using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// MP give-gold/dust window (GiveManager — a DontDestroyOnLoad singleton whose panel opens over
/// the map or town). ←/→ cycle the receiving player (the game's own target text carries their
/// heroes), ↑/↓ adjust the amount by 1 — with Ctrl 20, Shift 100, both 1000, the game's own four
/// increments — Tab switches gold/dust (the game resets amount and target on the switch), Enter
/// sends via <c>GiveAction</c> (master applies directly; a client asks the master via RPC),
/// Escape closes. Opened with Ctrl+G from the map/town pollers or the game's own button.
///
/// Receiving a gift is announced from a balance diff around the public
/// <c>CurrencyManager.ApplyShareGoldDustDict</c> — the share RPC runs on every client (master
/// included), unlike the master-only NET_MASTERGivePlayer. A transfer is only announced on the
/// strict signature "local +N, exactly one other −N, everyone else 0", which loot splits,
/// purchases and rewards can't produce; the giver's own mirror image stays silent (their
/// GiveAction already spoke).
/// </summary>
internal static class GiveScreenManager
{
    public static bool Open
        => GiveManager.Instance != null && GiveManager.Instance.IsActive();

    private static bool GoldMode
        => GiveManager.Instance != null
            && GiveManager.Instance.bgGold != null
            && GiveManager.Instance.bgGold.gameObject.activeSelf;

    private static string Word => GoldMode ? "gold" : "dust";

    private static string TargetText()
    {
        var gm = GiveManager.Instance;
        return gm != null && gm.target != null ? CardSpeech.CleanFlat(gm.target.text) : "";
    }

    private static int Balance()
    {
        var ato = AtOManager.Instance;
        var cur = ato != null ? ato.CurrencyManager : null;
        if (cur == null)
            return 0;
        return GoldMode ? cur.GetPlayerGold() : cur.GetPlayerDust();
    }

    /// <summary>True while there is anyone to give to. After a partner disconnects mid-run this
    /// goes false — and the game's target cycling then recurses forever (NextTarget skips the
    /// local position and wraps over a 1-player list: an uncatchable StackOverflow the mouse UI
    /// avoids only by hiding its buttons). Every entry point checks this before touching the
    /// game's target logic.</summary>
    private static bool HasOtherPlayers
        => NetworkManager.Instance != null && NetworkManager.Instance.GetNumPlayers() >= 2;

    // ---------------------------------------------------------------- input (from the context)

    public static void AdjustQuantity(int dir)
    {
        var gm = GiveManager.Instance;
        if (gm == null)
            return;
        int step = 1;
        if (Input.InputRouter.CtrlHeld && Input.InputRouter.ShiftHeld)
            step = 1000;
        else if (Input.InputRouter.ShiftHeld)
            step = 100;
        else if (Input.InputRouter.CtrlHeld)
            step = 20;

        int before = gm.quantity;
        gm.Give(dir * step); // the game clamps to [0, wallet]
        int now = gm.quantity;
        string edge = "";
        if (dir > 0 && now == before)
            edge = now == 0 ? ", nothing to give" : ", maximum";
        else if (now == 0)
            edge = ", zero";
        SpeechManager.Speak("Quantity " + now + edge);
    }

    public static void CycleTarget(int dir)
    {
        var gm = GiveManager.Instance;
        if (gm == null)
            return;
        if (!HasOtherPlayers)
        {
            SpeechManager.Speak("No other players left. Closing the give window.");
            Close();
            return;
        }
        if (dir > 0)
            gm.NextTarget();
        else
            gm.PrevTarget();
        SpeechManager.Speak("To " + TargetText());
    }

    public static void SwitchCurrency()
    {
        var gm = GiveManager.Instance;
        if (gm == null)
            return;
        var next = GoldMode ? Enums.CurrencyType.DUST : Enums.CurrencyType.GOLD;
        gm.ShowGive(true, next); // the game resets quantity and target on this call
        SpeechManager.Speak("Switched to " + Word + ". Quantity reset to 0. To " + TargetText());
    }

    public static void Confirm()
    {
        var gm = GiveManager.Instance;
        if (gm == null)
            return;
        if (!HasOtherPlayers)
        {
            SpeechManager.Speak("No other players left. Closing the give window.");
            Close();
            return;
        }
        if (gm.quantity <= 0)
        {
            SpeechManager.Speak("Set a quantity first — Up and Down adjust it.");
            return;
        }
        SpeechManager.Speak("Sending " + gm.quantity + " " + Word + " to " + TargetText() + ".");
        gm.GiveAction(); // closes the window itself
    }

    public static void Close()
    {
        GiveManager.Instance?.ShowGive(false);
    }

    /// <summary>Ctrl+G from the map/town pollers.</summary>
    public static void TryOpen()
    {
        var gm = GiveManager.Instance;
        if (!MpSpeech.IsMp || gm == null || gm.IsActive())
            return;
        if (!HasOtherPlayers)
        {
            SpeechManager.Speak("No other players to give to.");
            return; // ShowGive(true) with one player would recurse in NextTarget — see above
        }
        gm.ShowGive(true); // the divination-creator refusal opens a game alert instead
    }

    // ---------------------------------------------------------------- announcements

    internal static void OnShown(bool state)
    {
        var gm = GiveManager.Instance;
        if (gm == null)
            return;
        if (state && gm.IsActive())
        {
            SpeechManager.Speak("Give window. Giving " + Word + ", you have " + Balance()
                + ". To " + TargetText() + ". Quantity " + gm.quantity
                + ". Up and Down adjust by 1 — hold Control for 20, Shift for 100, both for 1000."
                + " Left and Right change player. Tab switches to "
                + (GoldMode ? "dust" : "gold") + ". Enter sends, Escape closes.");
        }
        else if (!state)
        {
            SpeechManager.SpeakQueued("Give window closed.");
        }
    }
}

/// <summary>Open/close announcements. The divination-creator refusal path never activates the
/// panel (the game raises an alert instead), so the open branch stays silent there.</summary>
[HarmonyPatch(typeof(GiveManager), nameof(GiveManager.ShowGive))]
public class GiveShownPatch
{
    static void Prefix(GiveManager __instance, out bool __state)
    {
        __state = __instance.IsActive();
    }

    static void Postfix(bool state, bool __state)
    {
        if (state && !__state && GiveManager.Instance != null && GiveManager.Instance.IsActive())
            GiveScreenManager.OnShown(true);
        else if (!state && __state)
            GiveScreenManager.OnShown(false);
    }
}

/// <summary>Force-close the give panel when the run tears down. GiveManager is DontDestroyOnLoad
/// and nothing game-side closes its panel on the disconnect path (master reloads the save, a
/// client is sent to the Lobby) — the stale panel would keep GiveInputContext on top of the next
/// scene, swallowing every key over a team that no longer exists.</summary>
[HarmonyPatch(typeof(AtOManager), nameof(AtOManager.ClearGame))]
public class GiveClosedOnRunTeardownPatch
{
    static void Postfix()
    {
        var gm = GiveManager.Instance;
        if (gm != null && gm.IsActive())
            gm.ShowGive(false);
    }
}

/// <summary>
/// Gift receipts on every client: prefix snapshots the per-player balances of the shared
/// currency, postfix diffs them and announces only the strict player→player transfer signature.
/// </summary>
[HarmonyPatch(typeof(CurrencyManager), nameof(CurrencyManager.ApplyShareGoldDustDict))]
public class GiveReceiptAnnouncePatch
{
    static void Prefix(CurrencyManager __instance, Enums.CurrencyType type,
        out Dictionary<string, int> __state)
    {
        __state = null;
        if (!MpSpeech.IsMp)
            return;
        if (type == Enums.CurrencyType.GOLD)
            __state = __instance.GetMpPlayersGold();
        else if (type == Enums.CurrencyType.DUST)
            __state = __instance.GetMpPlayersDust();
    }

    static void Postfix(CurrencyManager __instance, Enums.CurrencyType type,
        Dictionary<string, int> __state)
    {
        if (__state == null || NetworkManager.Instance == null)
            return;
        var after = type == Enums.CurrencyType.GOLD
            ? __instance.GetMpPlayersGold()
            : __instance.GetMpPlayersDust();
        if (after == null)
            return;

        // Strict transfer signature: exactly two movers — the local player gained N and one
        // other player lost the same N. Loot splits, purchases and rewards can't produce this.
        string local = NetworkManager.Instance.GetPlayerNick();
        int localDelta = 0;
        string giver = null;
        int giverDelta = 0;
        int movers = 0;
        foreach (var kv in after)
        {
            int before = __state.TryGetValue(kv.Key, out int b) ? b : 0;
            int delta = kv.Value - before;
            if (delta == 0)
                continue;
            movers++;
            if (kv.Key == local)
            {
                localDelta = delta;
            }
            else if (giver == null)
            {
                giver = kv.Key;
                giverDelta = delta;
            }
        }
        if (movers != 2 || giver == null || localDelta <= 0 || giverDelta != -localDelta)
            return;
        string word = type == Enums.CurrencyType.GOLD ? "gold" : "dust";
        SpeechManager.SpeakQueued(MpSpeech.DisplayNick(giver) + " gave you " + localDelta + " "
            + word + ". You now have " + (after.TryGetValue(local, out int mine) ? mine : 0) + ".");
    }
}
