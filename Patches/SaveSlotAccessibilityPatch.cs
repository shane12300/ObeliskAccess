using HarmonyLib;

namespace ObeliskAccess.Patches;

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowSaveGame))]
public class SaveSlotOpenPatch
{
    static void Postfix(bool status)
    {
        // ShowSaveGame(false) is the "back/close" path — only announce when the screen opens.
        if (status)
            SpeechManager.Speak("Select save slot");
    }
}
