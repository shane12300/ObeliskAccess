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

        // Register input contexts in priority order (highest first). Modals sit above the base
        // menu so that while one is active, input never reaches the screen beneath it.
        InputRouter.Register(new TutorialInputContext());
        InputRouter.Register(new SettingsInputContext());
        InputRouter.Register(new CorruptionInputContext());
        var cardCraftContext = new CardCraftInputContext();
        InputRouter.Register(cardCraftContext);
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
        InputRouter.Register(new MainMenuInputContext());

        // The map's Alt+G/T/I hotkeys use letters the game leaves unbound, so a frame poller sees
        // them; it fires only while the map context owns input.
        gameObject.AddComponent<MapHotkeyPoller>().MapContext = mapContext;

        // Combat's Alt review hotkeys are likewise unbound; a poller drives them (and the combat
        // event-announcement flush) while the combat context owns input.
        gameObject.AddComponent<CombatHotkeyPoller>().CombatContext = combatContext;

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

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
    }
}
