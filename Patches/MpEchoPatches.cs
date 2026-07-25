using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// Remaining partner-action echoes — one-line announcements for MP events no screen owns:
/// hero control handovers, shop purchase denials (the master arbitrates client purchases), and
/// partners crafting/upgrading/removing cards at the Forge/Altar. Cosmetic hero-selection echoes
/// (skins, card backs, classic variants) are deliberately NOT covered — pure noise.
/// </summary>

/// <summary>Control handover — the private GiveControlToPlayer is the shared sink for the local
/// master call and the NET_GiveControlToPlayer receive, so one patch covers every client.</summary>
[HarmonyPatch(typeof(AtOManager), "GiveControlToPlayer")]
public class MpControlHandoverAnnouncePatch
{
    static void Postfix(AtOManager __instance, byte heroPosition, byte playerIndex)
    {
        if (!MpSpeech.IsMp || NetworkManager.Instance == null)
            return;
        var team = __instance.team;
        var hero = team != null ? team.GetHero(heroPosition) : null;
        if (hero == null)
            return;
        string heroName = AccessibleMenuBase.StripRichText(hero.SourceName ?? "");
        if (heroName.Length == 0)
            heroName = "A hero";
        string nick = NetworkManager.Instance.GetPlayerNickPosition(playerIndex);
        bool mine = !string.IsNullOrEmpty(nick) && nick == NetworkManager.Instance.GetPlayerNick();
        SpeechManager.SpeakQueued(heroName + " is now controlled by "
            + (mine ? "you." : MpSpeech.DisplayNick(nick) + "."));
    }
}

/// <summary>Client purchase denial — the RPC is targeted at the buyer alone, and an approved
/// purchase already flows into the existing craft-screen announcements; only the refusal needs a
/// voice. Interrupting Speak: it answers the buyer's own pending action.</summary>
[HarmonyPatch(typeof(AtOManager), nameof(AtOManager.NET_BuyItemResult))]
public class MpBuyDeniedAnnouncePatch
{
    static void Postfix(string cardId, bool approved)
    {
        if (!MpSpeech.IsMp || approved)
            return;
        var cd = Globals.Instance != null ? Globals.Instance.GetCardData(cardId) : null;
        string name = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName ?? "") : "";
        SpeechManager.Speak("Purchase failed" + (name.Length > 0 ? ": " + name : "")
            + " — already sold or not enough gold.");
    }
}

/// <summary>Partner craft activity. These RPCs are receive-only (the acting client never runs
/// them, and the Hero methods they call are pure statistics counters), so there is no overlap
/// with the local craft announcements.</summary>
[HarmonyPatch(typeof(AtOManager), nameof(AtOManager.NET_HeroCardCrafted))]
public class MpPartnerCraftedAnnouncePatch
{
    static void Postfix(AtOManager __instance, int heroIndex)
        => MpCraftEcho.Announce(__instance, heroIndex, "crafted a card at the Forge");
}

[HarmonyPatch(typeof(AtOManager), nameof(AtOManager.NET_HeroCardUpgraded))]
public class MpPartnerUpgradedAnnouncePatch
{
    static void Postfix(AtOManager __instance, int heroIndex)
        => MpCraftEcho.Announce(__instance, heroIndex, "upgraded a card at the Altar");
}

[HarmonyPatch(typeof(AtOManager), nameof(AtOManager.NET_HeroCardRemoved))]
public class MpPartnerRemovedAnnouncePatch
{
    static void Postfix(AtOManager __instance, int heroIndex)
        => MpCraftEcho.Announce(__instance, heroIndex, "removed a card");
}

internal static class MpCraftEcho
{
    internal static void Announce(AtOManager ato, int heroIndex, string action)
    {
        if (!MpSpeech.IsMp)
            return;
        var team = ato != null ? ato.team : null;
        var hero = team != null ? team.GetHero(heroIndex) : null;
        if (hero == null || MpSpeech.LocalOwns(hero))
            return; // own crafts are announced by the craft screen itself
        string heroName = AccessibleMenuBase.StripRichText(hero.SourceName ?? "");
        SpeechManager.SpeakQueued(MpSpeech.OwnerNick(hero)
            + (heroName.Length > 0 ? "'s " + heroName : "") + " " + action + ".");
    }
}
