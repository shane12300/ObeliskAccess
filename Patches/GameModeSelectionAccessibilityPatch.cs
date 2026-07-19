using HarmonyLib;

namespace ObeliskAccess.Patches;

[HarmonyPatch(typeof(MainMenuManager), "ShowGameModeSelection")]
public class GameModeSelectionOpenPatch : AccessibleMenuBase
{
    static void Postfix()
    {
        SpeechManager.Speak("Select game mode");
    }
}
