namespace ObeliskAccess.Patches;

/// <summary>
/// Shared multiplayer speech helpers. Ownership in this game is a plain Photon nickname string on
/// each <c>Hero</c> (<c>Hero.Owner</c>); the local identity is <c>NetworkManager.GetPlayerNick()</c>
/// and the display name for any nick is <c>GetPlayerNickReal(nick)</c>. Every helper here is
/// null-safe and collapses to the single-player answer when not in a multiplayer game, so callers
/// can use them unconditionally.
/// </summary>
internal static class MpSpeech
{
    internal static bool IsMp
        => GameManager.Instance != null && GameManager.Instance.IsMultiplayer();

    /// <summary>The local player's raw Photon nick; "" outside multiplayer.</summary>
    internal static string LocalNick()
        => IsMp && NetworkManager.Instance != null ? NetworkManager.Instance.GetPlayerNick() : "";

    /// <summary>True when the local player controls the hero (always true in single player;
    /// an unset owner also counts as local).</summary>
    internal static bool LocalOwns(Hero hero)
        => hero != null
            && (!IsMp
                || string.IsNullOrEmpty(hero.Owner)
                || NetworkManager.Instance == null
                || hero.Owner == NetworkManager.Instance.GetPlayerNick());

    /// <summary>Display name for a raw owner nick, falling back to "Another player".</summary>
    internal static string DisplayNick(string rawNick)
        => DisplayNick(rawNick, "Another player");

    /// <summary>Display name for a raw owner nick with a caller-chosen fallback (e.g. a lowercase
    /// "another player" for mid-sentence reading).</summary>
    internal static string DisplayNick(string rawNick, string fallback)
    {
        if (string.IsNullOrEmpty(rawNick) || NetworkManager.Instance == null)
            return fallback;
        string real = NetworkManager.Instance.GetPlayerNickReal(rawNick);
        if (string.IsNullOrEmpty(real))
            return fallback;
        return AccessibleMenuBase.StripRichText(real);
    }

    /// <summary>Display nick of the room master, fallback "the host".</summary>
    internal static string HostNick()
    {
        var nm = NetworkManager.Instance;
        if (nm == null)
            return "the host";
        // Position 0 is the master by the game's own convention (PlayerIsMaster).
        string raw = nm.GetPlayerNickPosition(0);
        return string.IsNullOrEmpty(raw) ? "the host" : DisplayNick(raw);
    }

    /// <summary>Display name of the hero's owning player ("Another player" fallback).</summary>
    internal static string OwnerNick(Hero hero)
        => DisplayNick(hero != null ? hero.Owner : null);

    /// <summary>
    /// "{yourText}" when the local player owns the hero, else "{theirPrefix}{owner display nick}".
    /// E.g. <c>OwnershipClause(h, ", your pick", ", waiting for ")</c>.
    /// </summary>
    internal static string OwnershipClause(Hero hero, string yourText, string theirPrefix)
        => LocalOwns(hero) ? yourText : theirPrefix + OwnerNick(hero);
}
