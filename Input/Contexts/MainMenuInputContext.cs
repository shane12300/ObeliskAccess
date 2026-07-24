using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The base (lowest-priority) context covering the whole <c>MainMenuManager</c> family: the main
/// menu, the game-mode selection panel, and the save-slot panel. They share one
/// <c>InputController</c> and one <c>controllerList</c>, differing only in what "activate the
/// focused item" means — so they are one context that branches on the live screen state rather
/// than three patches that had to guard against each other.
///
/// Arrow navigation is left to the game's own <c>controllerList</c> movement (announced by
/// <c>MainMenuNavigationPatch</c>), so <see cref="OnMove"/> is not overridden.
/// </summary>
public class MainMenuInputContext : InputContextBase
{
    public override bool IsActive =>
        MainMenuManager.Instance != null
        && MatchManager.Instance == null;

    public override bool OnConfirm()
    {
        var mgr = MainMenuManager.Instance;
        var controller = InputRouter.Controller;
        if (mgr == null || controller == null)
            return false;

        // The game's own fire path (Button.onClick on the current raycast hit). Harmless for the
        // physics/custom buttons handled below, but it activates the standard buttons — Back and
        // section buttons — that share these screens. This mirrors the previous behaviour where the
        // main-menu Enter patch fired unconditionally alongside the screen-specific handlers.
        Traverse.Create(controller).Method("DoFirePerformed").GetValue();

        var controllerList = Traverse.Create(mgr).Field<List<Transform>>("controllerList").Value;
        int index = mgr.controllerHorizontalIndex;
        if (controllerList == null || index < 0 || index >= controllerList.Count)
            return true;

        Transform item = controllerList[index];

        if (mgr.IsGameModesActive())
        {
            // BotonMenuGameMode uses OnMouseUp (physics-based), not Button.onClick, so the fire
            // path above never reaches it. Trigger it directly.
            item.GetComponentInChildren<BotonMenuGameMode>()?.OnMouseUp();
        }
        else if (mgr.IsSaveMenuActive())
        {
            var save = item.GetComponentInChildren<MenuSaveButton>();
            if (save != null)
            {
                var button = save.GetComponent<Button>();
                if (button != null && !button.interactable)
                    SpeechManager.Speak("Save incompatible");
                else
                    // MenuSaveButton's real action is SelectThis(); DoFirePerformed never reaches it.
                    save.SelectThis();
            }
        }

        return true;
    }
}
