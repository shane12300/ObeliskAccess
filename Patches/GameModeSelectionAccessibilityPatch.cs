using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Patches;

[HarmonyPatch(typeof(MainMenuManager), "ShowGameModeSelection")]
public class GameModeSelectionOpenPatch : AccessibleMenuBase
{
    static void Postfix()
    {
        SpeechManager.Speak("Select game mode");
    }
}

[HarmonyPatch(typeof(InputController), "DoKeyBinding")]
public class GameModeSelectionEnterKeyPatch : AccessibleMenuBase
{
    static void Postfix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (Keyboard.current == null)
            return;

        bool isEnter = _context.control == Keyboard.current[Key.Enter]
                    || _context.control == Keyboard.current[Key.NumpadEnter];

        if (!isEnter)
            return;

        if (MainMenuManager.Instance == null || !MainMenuManager.Instance.IsGameModesActive())
            return;

        var controllerList = Traverse.Create(MainMenuManager.Instance)
            .Field<List<Transform>>("controllerList").Value;

        if (controllerList == null || controllerList.Count == 0)
            return;

        int index = MainMenuManager.Instance.controllerHorizontalIndex;
        if (index < 0 || index >= controllerList.Count)
            return;

        // BotonMenuGameMode uses OnMouseUp (physics-based), not Button.onClick,
        // so DoFirePerformed won't hit it. Search the item and its children.
        var boton = controllerList[index].GetComponentInChildren<BotonMenuGameMode>();
        boton?.OnMouseUp();
    }
}
