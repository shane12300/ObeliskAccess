using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The base (lowest-priority) context covering the whole <c>MainMenuManager</c> family: the main
/// menu, the game-mode selection panel, and the save-slot panel. They share one
/// <c>InputController</c> and one <c>controllerList</c>, differing only in what "activate the
/// focused item" means — so they are one context that branches on the live screen state rather
/// than three patches that had to guard against each other.
///
/// On the main-menu and save-slot screens, arrow navigation is left to the game's own
/// <c>controllerList</c> movement (announced by <c>MainMenuNavigationPatch</c>).
/// <see cref="OnMove"/> is overridden solely to take movement over on the game-mode screen, whose
/// spatial row walk is replaced with a linear list.
/// </summary>
public class MainMenuInputContext : InputContextBase
{
    /// <summary>
    /// The main-menu family screen is up (screen-open, like every other context — a modal above
    /// may still outrank this context in the router). The router patches swallow the game's own
    /// Enter handling while it is: with ConfigKeyboardShortcuts forced on, the game maps Enter to
    /// <c>DoFirePerformed</c> itself, and letting both it and <see cref="OnConfirm"/> fire
    /// pressed things twice — most visibly, the second fire's physics-raycast fallback hit the
    /// freshly-activated mode-selection collider under the stale cursor right after Play was
    /// pressed, skipping the mode screen straight into the save-slot window.
    /// </summary>
    public static bool IsCurrentlyActive =>
        MainMenuManager.Instance != null
        && MatchManager.Instance == null;

    public override bool IsActive => IsCurrentlyActive;

    /// <summary>
    /// The DLC popup and the Paradox document popup open over the mode screen without
    /// deactivating it, so IsGameModesActive() stays true beneath them. While one is up the
    /// game's own ControllerMovement only offers the popup's buttons — driving the mode list
    /// under it would arrow through invisible items and Enter could press a hidden mode button.
    /// </summary>
    private static bool PopupOver(MainMenuManager m) =>
        (m.DLCpopup != null && m.DLCpopup.gameObject.activeSelf)
        || (m.paradoxDocumentPopup != null && m.paradoxDocumentPopup.gameObject.activeSelf);

    /// <summary>
    /// The game-mode screen lays its items out as a horizontal row, so the game's spatial
    /// navigation walks it with ←/→ (↓ from the Main Menu button, then sideways) — unintuitive
    /// next to every other screen. Take over movement there and walk one linear list with ↑/↓
    /// (←/→ kept as synonyms), in the game's own order: Main Menu button first, then the modes.
    /// The game's controllerList/index are rewritten to match so Enter and the click path agree.
    /// All other MainMenuManager screens keep the game's spatial navigation.
    /// </summary>
    public override bool OnMove(Vector2 direction)
    {
        var mgr = MainMenuManager.Instance;
        if (mgr == null || mgr.IsSaveMenuActive() || !mgr.IsGameModesActive() || PopupOver(mgr))
            return false;

        int step;
        if (direction.y > 0 || direction.x < 0)
            step = -1;
        else if (direction.y < 0 || direction.x > 0)
            step = 1;
        else
            return false;

        var items = BuildModeScreenList(mgr);
        if (items.Count == 0)
            return true;

        var gameList = Traverse.Create(mgr).Field<List<Transform>>("controllerList").Value;
        if (gameList == null)
            return true;

        // Where are we now? The game list may still hold the previous screen's items (it is only
        // rebuilt by ControllerMovement, which we bypass here) — an unmatched item means we just
        // arrived, so the first press lands on the top (or bottom) of the list.
        int current = -1;
        int gi = mgr.controllerHorizontalIndex;
        if (gi >= 0 && gi < gameList.Count)
            current = items.IndexOf(gameList[gi]);

        int next = current < 0
            ? (step > 0 ? 0 : items.Count - 1)
            : Mathf.Clamp(current + step, 0, items.Count - 1);

        gameList.Clear();
        gameList.AddRange(items);
        mgr.controllerHorizontalIndex = next;
        // Warp the cursor like the game does: hover feedback (description panel, highlight) and
        // the click path both key off the cursor position.
        if (Mouse.current != null)
            Mouse.current.WarpCursorPosition(items[next].position);

        ObeliskAccess.Patches.AccessibleMenuBase.AnnounceItem(items[next]);
        return true;
    }

    /// <summary>
    /// The mode screen's reachable items in the game's own order (mirrors ControllerMovement's
    /// list build for this screen): the shared buttons (Main Menu, MP join), the four mode
    /// proxies, then the PDX buttons — visibility-filtered, so locked-out entries drop away.
    /// </summary>
    private static List<Transform> BuildModeScreenList(MainMenuManager mgr)
    {
        var items = new List<Transform>();
        AddVisible(items, mgr.menuController1);
        AddVisible(items, mgr.menuControllerModeSelection);
        AddVisible(items, mgr.menuControllerPDX);
        return items;
    }

    private static void AddVisible(List<Transform> items, List<Transform> source)
    {
        if (source == null)
            return;
        foreach (var t in source)
            if (Functions.TransformIsVisible(t))
                items.Add(t);
    }

    public override bool OnConfirm()
    {
        var mgr = MainMenuManager.Instance;
        var controller = InputRouter.Controller;
        if (mgr == null || controller == null)
            return false;

        var controllerList = Traverse.Create(mgr).Field<List<Transform>>("controllerList").Value;
        int index = mgr.controllerHorizontalIndex;
        Transform item = controllerList != null && index >= 0 && index < controllerList.Count
            ? controllerList[index]
            : null;

        // The save window keeps the mode-selection root active behind it, so mirror the game's
        // ControllerMovement and check the save menu before the mode screen.
        if (item != null && mgr.IsSaveMenuActive())
        {
            // Delete affordance first: it is a CHILD of MenuSaveButton, so the downward search
            // below never finds the slot from it. DeleteThis raises the game's own confirm
            // dialog, which the global alert layer owns.
            var deleteSave = ObeliskAccess.Patches.AccessibleMenuBase.ResolveDeleteButtonSlot(item);
            if (deleteSave != null)
            {
                deleteSave.DeleteThis();
                return true;
            }

            var save = item.GetComponentInChildren<MenuSaveButton>();
            if (save != null)
            {
                var button = save.GetComponent<Button>();
                if (button != null && !button.interactable)
                    SpeechManager.Speak("Save incompatible");
                else
                    save.SelectThis();
                return true;
            }
        }
        else if (item != null && mgr.IsGameModesActive() && !PopupOver(mgr))
        {
            // The controller item is a UI cursor-warp proxy (ButtonAM_UI etc.), not the real
            // world-space button — press the resolved button directly.
            var boton = ObeliskAccess.Patches.AccessibleMenuBase.ResolveGameModeButton(item);
            if (boton != null)
            {
                boton.OnMouseUp();
                return true;
            }
        }

        // Standard buttons (Play, Back, section buttons): reproduce the game's Enter click. The
        // game's own Enter → DoFirePerformed is suppressed while this context owns input (see
        // RouterDoKeyBindingPatch), so this is the only fire — exactly one activation per press.
        // Flagged so the router's bare-Ctrl suppression lets the mod's own fire through
        // (a Harmony detour catches reflection invokes too).
        RouterGuards.SelfFiring = true;
        try { Traverse.Create(controller).Method("DoFirePerformed").GetValue(); }
        finally { RouterGuards.SelfFiring = false; }
        return true;
    }
}
