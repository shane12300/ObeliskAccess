using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

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
