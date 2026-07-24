using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ObeliskAccess.Input;
using ObeliskAccess.Input.Contexts;

namespace ObeliskAccess;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        // Bring up screen-reader speech before anything tries to talk.
        SpeechManager.Initialize(Logger);

        // Bind the mod's player-facing options (settings menu, Accessibility tab).
        AccessibilityOptions.Initialize(Config);

        // Register input contexts in priority order (highest first). Modals sit above the base
        // menu so that while one is active, input never reaches the screen beneath it.
        // Alerts are modal over everything, so their context is registered first.
        var alertContext = new AlertInputContext();
        InputRouter.Register(alertContext);
        InputRouter.Register(new TutorialInputContext());
        var settingsContext = new SettingsInputContext();
        InputRouter.Register(settingsContext);
        InputRouter.Register(new CorruptionInputContext());
        var cardCraftContext = new CardCraftInputContext();
        InputRouter.Register(cardCraftContext);
        // The in-combat death popup outranks the card-selection windows — it can appear
        // mid-resolution and freezes the whole combat until dismissed.
        var deathScreenContext = new DeathScreenInputContext();
        InputRouter.Register(deathScreenContext);
        // The in-combat card-selection windows (discard / look-at-deck / discover / pile viewers)
        // are modals over combat, so their context sits directly above it.
        var combatSelectorContext = new CombatSelectorInputContext();
        InputRouter.Register(combatSelectorContext);
        var combatContext = new CombatInputContext();
        InputRouter.Register(combatContext);
        var eventContext = new EventInputContext();
        InputRouter.Register(eventContext);
        var townUpgradeContext = new TownUpgradeInputContext();
        InputRouter.Register(townUpgradeContext);
        var townContext = new TownInputContext();
        InputRouter.Register(townContext);
        var mapContext = new MapInputContext();
        InputRouter.Register(mapContext);
        var rewardsContext = new RewardsInputContext();
        InputRouter.Register(rewardsContext);
        var lootContext = new LootInputContext();
        InputRouter.Register(lootContext);
        // The end-of-run screen is its own scene; it only needs to outrank the main menu.
        var finishRunContext = new FinishRunInputContext();
        InputRouter.Register(finishRunContext);
        InputRouter.Register(new MainMenuInputContext());

        // The alert dialogue's Alt+R repeat key: every per-screen poller gates on its own context,
        // so they all fall silent while the alert context owns input — this one covers it.
        gameObject.AddComponent<AlertHotkeyPoller>().AlertContext = alertContext;

        // The map's Alt+G/T/I hotkeys use letters the game leaves unbound, so a frame poller sees
        // them; it fires only while the map context owns input.
        gameObject.AddComponent<MapHotkeyPoller>().MapContext = mapContext;

        // The settings menu's Alt+T tooltip key is likewise unbound.
        gameObject.AddComponent<SettingsHotkeyPoller>().SettingsContext = settingsContext;

        // Combat's Alt review hotkeys are likewise unbound; a poller drives them (and the combat
        // event-announcement flush) while the combat context owns input. The same poller drives
        // the card-selection windows' arrival tick and their Alt+T/Alt+R keys.
        var combatPoller = gameObject.AddComponent<CombatHotkeyPoller>();
        combatPoller.CombatContext = combatContext;
        combatPoller.SelectorContext = combatSelectorContext;
        combatPoller.DeathContext = deathScreenContext;

        // The event screen's Alt+T/R hotkeys are unbound too; its poller also drives the
        // event lifecycle tick (reply watcher, deferred reward-card reads).
        gameObject.AddComponent<EventHotkeyPoller>().EventContext = eventContext;

        // The town hub's Alt review keys are unbound letters too; its poller also drives the
        // hub lifecycle tick (arrival announce, sub-screen close detection).
        var townPoller = gameObject.AddComponent<TownHotkeyPoller>();
        townPoller.TownContext = townContext;
        townPoller.TownUpgradeContext = townUpgradeContext;

        // The service screens' Alt review keys (and Alt+F filters) are unbound letters too; the
        // poller also drives screen-open detection, since ShowCardCraft is async.
        gameObject.AddComponent<CardCraftHotkeyPoller>().CardCraftContext = cardCraftContext;

        // The rewards screen's Alt+T/I/R review keys are unbound letters too; its poller also
        // drives readiness detection (the reward rows animate in over a couple of seconds).
        gameObject.AddComponent<RewardsHotkeyPoller>().RewardsContext = rewardsContext;

        // The loot screen's Alt+T/I/G/R review keys are unbound letters too; its poller also
        // drives readiness detection, the turn watch, and the finish announcement.
        gameObject.AddComponent<LootHotkeyPoller>().LootContext = lootContext;

        // The end-of-run screen's Alt+I/T/R keys; its poller also drives arrival detection and
        // the "Main menu available" announcement.
        gameObject.AddComponent<FinishRunHotkeyPoller>().FinishRunContext = finishRunContext;

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();

        // Debug mode: confirm the card-selection window patches actually attached.
        if (AccessibilityOptions.DebugEnabled)
        {
            LogPatchState(typeof(UIDeckCards), "TurnOn");
            LogPatchState(typeof(UIDiscardSelector), "TurnOn");
            LogPatchState(typeof(MatchManager), "SelectCardToDiscard");
        }
    }

    /// <summary>Troubleshooting log line, written only while the Debug mode option is on.</summary>
    internal static void LogDebug(string message)
    {
        if (AccessibilityOptions.DebugEnabled)
            Logger.LogInfo(message);
    }

    private static void LogPatchState(System.Type type, string method)
    {
        var m = AccessTools.Method(type, method);
        var info = m != null ? Harmony.GetPatchInfo(m) : null;
        int post = info?.Postfixes?.Count ?? 0;
        int pre = info?.Prefixes?.Count ?? 0;
        Logger.LogInfo($"[selector] {type.Name}.{method}: found={m != null} prefixes={pre} postfixes={post}");
    }
}
