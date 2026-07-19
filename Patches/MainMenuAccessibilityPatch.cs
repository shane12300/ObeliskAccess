using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Patches;

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ControllerMovement))]
public class MainMenuNavigationPatch : AccessibleMenuBase
{
    static void Postfix(MainMenuManager __instance)
    {
        var controllerList = Traverse.Create(__instance)
            .Field<List<Transform>>("controllerList").Value;

        if (controllerList == null || controllerList.Count == 0)
            return;

        int index = __instance.controllerHorizontalIndex;
        if (index < 0 || index >= controllerList.Count)
            return;

        AnnounceItem(controllerList[index]);
    }
}

[HarmonyPatch(typeof(InputController), "DoKeyBinding")]
public class MainMenuEnterKeyPatch : AccessibleMenuBase
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

        // Let the game handle Enter when in a match
        if (MatchManager.Instance != null)
            return;

        if (MainMenuManager.Instance == null)
            return;

        Traverse.Create(__instance).Method("DoFirePerformed").GetValue();
    }
}
