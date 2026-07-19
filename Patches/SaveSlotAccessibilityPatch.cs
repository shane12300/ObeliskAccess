using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowSaveGame))]
public class SaveSlotOpenPatch : AccessibleMenuBase
{
    static void Postfix(bool status)
    {
        // ShowSaveGame(false) is the "back/close" path — only announce when the screen opens.
        if (status)
            SpeechManager.Speak("Select save slot");
    }
}

[HarmonyPatch(typeof(InputController), "DoKeyBinding")]
public class SaveSlotEnterKeyPatch : AccessibleMenuBase
{
    static void Postfix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (Keyboard.current == null)
            return;

        bool isEnter = _context.control == Keyboard.current[Key.Enter]
                    || _context.control == Keyboard.current[Key.NumpadEnter];

        if (!isEnter)
            return;

        // Let the tutorial popup manager handle Enter while a tutorial is open.
        if (GameManager.Instance != null && GameManager.Instance.IsTutorialActive())
            return;

        // Defer to the settings/alert handlers while they own input.
        if (SettingsMenuManager.InputBlocked)
            return;

        if (MainMenuManager.Instance == null || !MainMenuManager.Instance.IsSaveMenuActive())
            return;

        var controllerList = Traverse.Create(MainMenuManager.Instance)
            .Field<List<Transform>>("controllerList").Value;

        if (controllerList == null || controllerList.Count == 0)
            return;

        int index = MainMenuManager.Instance.controllerHorizontalIndex;
        if (index < 0 || index >= controllerList.Count)
            return;

        // Only act when a save slot is focused. Other items on this screen (back/section
        // buttons in menuController1) are standard UI buttons handled by the game's fire path.
        var save = controllerList[index].GetComponentInChildren<MenuSaveButton>();
        if (save == null)
            return;

        // MenuSaveButton's real action is SelectThis(); the game's DoFirePerformed only invokes
        // Button.onClick on the exact raycast hit, which never reaches it. Call it directly.
        var button = save.GetComponent<Button>();
        if (button != null && !button.interactable)
        {
            SpeechManager.Speak("Save incompatible");
            return;
        }

        save.SelectThis();
    }
}
