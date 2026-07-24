using BepInEx.Configuration;

namespace ObeliskAccess;

/// <summary>
/// Player-facing mod options, persisted through BepInEx config (ObeliskAccess.cfg) and exposed
/// in-game on the settings menu's Accessibility tab (a mod-owned virtual tab — see
/// <c>Patches/SettingsMenuAccessibilityPatch.cs</c>). Most are combat-narration verbosity
/// controls (each toggle ON enables a verbosity reduction, OFF restores the original full
/// narration), plus a debug-mode toggle for troubleshooting logs.
/// </summary>
public static class AccessibilityOptions
{
    /// <summary>
    /// Skip the "X's turn" line for enemies and let the "X plays [card]" cast line mark the turn.
    /// An enemy whose turn passes without a cast is still announced ("takes no action").
    /// </summary>
    public static ConfigEntry<bool> CombineEnemyTurns;

    /// <summary>
    /// Speak status changes as the new total ("Poison 7", "Poison gone") instead of the delta
    /// phrasing ("gains Poison 3, total 7" / "loses Poison 1, total 6").
    /// </summary>
    public static ConfigEntry<bool> ShortStatusPhrasing;

    /// <summary>
    /// Suppress the predictable minus-1 end-of-turn status decay. Expirations and larger losses
    /// (dispels, consumption) are still announced.
    /// </summary>
    public static ConfigEntry<bool> SkipRoutineDecay;

    /// <summary>
    /// Suppress status gain/loss announcements on enemies entirely (damage, heals, casts and deaths
    /// are still announced). Enemy status is always available on demand via focus reads and Alt+S.
    /// </summary>
    public static ConfigEntry<bool> SkipEnemyStatusChanges;

    /// <summary>
    /// Verbose troubleshooting logs in the BepInEx log (patch attachment at startup, card casts
    /// with their deck-effect fields, selection-window open/close). Off by default; normal
    /// announcement logging is unaffected either way.
    /// </summary>
    public static ConfigEntry<bool> DebugMode;

    /// <summary>True when verbose troubleshooting logging is enabled.</summary>
    public static bool DebugEnabled => DebugMode != null && DebugMode.Value;

    // The Bind descriptions double as the in-game Alt+T tooltips (and the comments in
    // ObeliskAccess.cfg), so they are written as full player-facing sentences.
    public static void Initialize(ConfigFile config)
    {
        const string section = "Combat narration";

        CombineEnemyTurns = config.Bind(section, "CombineEnemyTurns", false,
            "When on, enemy turns are not announced with a separate turn line. The card the enemy "
            + "plays marks its turn instead: for example, Goblin plays Stab, rather than Goblin's "
            + "turn followed by the card. An enemy that cannot act is announced as taking no "
            + "action. Your own turns and round changes are always announced.");

        ShortStatusPhrasing = config.Bind(section, "ShortStatusPhrasing", true,
            "When on, status changes are spoken as the effect name and its new total: for example, "
            + "Poison 7, or Poison gone when it runs out. When off, the longer phrasing is used: "
            + "gains Poison 3, total 7.");

        SkipRoutineDecay = config.Bind(section, "SkipRoutineDecay", true,
            "When on, the automatic 1 point drop of statuses at the end of each turn is not "
            + "announced. A status running out, and larger losses such as dispels, are still "
            + "announced. Check exact stacks at any time with Alt+S.");

        SkipEnemyStatusChanges = config.Bind(section, "SkipEnemyStatusChanges", false,
            "When on, status gains and losses on enemies are never announced. Damage, healing, "
            + "card plays and deaths are still announced. Check an enemy's status by focusing it "
            + "with the arrow keys, or with Alt+S.");

        DebugMode = config.Bind("Debug", "DebugMode", false,
            "When on, the mod writes extra troubleshooting detail to the BepInEx log: which "
            + "patches attached at startup, every card cast with its deck effect values, and "
            + "card selection window activity. Leave off for normal play; turn on to gather "
            + "information when reporting a problem.");
    }
}
