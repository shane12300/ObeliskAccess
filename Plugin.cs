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

        // Register input contexts in priority order (highest first). Modals sit above the base
        // menu so that while one is active, input never reaches the screen beneath it.
        InputRouter.Register(new TutorialInputContext());
        InputRouter.Register(new SettingsInputContext());
        InputRouter.Register(new MainMenuInputContext());

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
    }
}
