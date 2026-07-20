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
        var combatContext = new CombatInputContext();
        InputRouter.Register(combatContext);
        var mapContext = new MapInputContext();
        InputRouter.Register(mapContext);
        InputRouter.Register(new MainMenuInputContext());

        // The map's Alt+G/T/I hotkeys use letters the game leaves unbound, so a frame poller sees
        // them; it fires only while the map context owns input.
        gameObject.AddComponent<MapHotkeyPoller>().MapContext = mapContext;

        // Combat's Alt review hotkeys are likewise unbound; a poller drives them (and the combat
        // event-announcement flush) while the combat context owns input.
        gameObject.AddComponent<CombatHotkeyPoller>().CombatContext = combatContext;

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
    }
}
