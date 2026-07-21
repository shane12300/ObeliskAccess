using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// The mod's only patches on <c>InputController</c>. They translate raw keyboard input into the
/// semantic events (<c>Move</c> / <c>Confirm</c> / <c>Cancel</c> / <c>Tab</c>) that
/// <see cref="InputRouter"/> routes to the active <see cref="IInputContext"/>.
///
/// Movement and Escape are prefixes that swallow the key when a context consumes it (so the game's
/// own navigation/escape does not also run). Enter/Tab are handled in a postfix, matching the
/// previous behaviour where the game does not act on those keys itself.
/// </summary>
[HarmonyPatch(typeof(InputController), "DoMovement")]
public class RouterDoMovementPatch
{
    static bool Prefix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (!InputRouter.IsKeyboard(_context))
            return true;

        InputRouter.Controller = __instance;
        Vector2 direction = _context.ReadValue<Vector2>();
        return !InputRouter.Move(direction); // swallow (return false) iff a context handled it
    }
}

[HarmonyPatch(typeof(InputController), "DoKeyBinding")]
public class RouterDoKeyBindingPatch
{
    /// <summary>
    /// In combat the game binds R/E/S/A/W/Q to multiplayer emote pings. Those letters double as the
    /// mod's Alt review hotkeys (Alt+R repeat, Alt+E energy, Alt+S statuses), so while combat owns
    /// input and Alt is held, skip the game's handling — the poller still sees the key.
    /// </summary>
    static bool Prefix(InputAction.CallbackContext _context)
    {
        var kb = Keyboard.current;
        if (kb == null || !InputRouter.AltHeld)
            return true;
        if (!ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive)
            return true;

        InputControl control = _context.control;
        return !(control == kb[Key.R] || control == kb[Key.E] || control == kb[Key.S]
              || control == kb[Key.A] || control == kb[Key.W] || control == kb[Key.Q]);
    }

    static void Postfix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (Keyboard.current == null)
            return;

        InputRouter.Controller = __instance;
        InputControl control = _context.control;

        if (InputRouter.IsEnter(control))
            InputRouter.Confirm();
        else if (InputRouter.IsTab(control))
            InputRouter.Tab(InputRouter.ShiftHeld);
        else if (InputRouter.IsDigit(control, out int n))
            InputRouter.Number(n);
        // Digits are inert on the map by default (the game only acts on them during combat), so a
        // non-swallowing postfix is enough — nothing else to suppress.
    }
}

/// <summary>
/// The game maps a bare Alt press to <c>DoButtonNorth</c>, which right-clicks whatever is under the
/// cursor — and in combat the cursor sits on the focused card, so every Alt review hotkey would pop
/// the card-inspection window (stealing input until Escape). Suppress it while combat owns input;
/// Alt is the mod's review modifier there. The multiplayer chat keyboard's Alt-as-delete is left
/// working.
/// </summary>
[HarmonyPatch(typeof(InputController), "DoButtonNorth")]
public class RouterDoButtonNorthPatch
{
    static bool Prefix()
    {
        if (KeyboardManager.Instance != null && KeyboardManager.Instance.IsActive())
            return true;
        return !ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive;
    }
}

/// <summary>
/// The game maps a bare Ctrl press to a "click" (<c>DoFirePerformed</c>). The map context
/// repurposes Ctrl as its look-ahead modifier, so this prefix suppresses that click while the map
/// owns input and Ctrl is held — otherwise Ctrl+arrow would also click whatever node the cursor
/// happens to rest on. Gamepad A (no Ctrl held) and mouse clicks are unaffected.
/// </summary>
[HarmonyPatch(typeof(InputController), "DoFirePerformed")]
public class RouterDoFirePerformedPatch
{
    static bool Prefix()
    {
        // The map, combat and rewards screens repurpose Ctrl as a look-ahead / drill-in modifier,
        // so suppress the bare-Ctrl click while one of them owns input and Ctrl is held.
        bool ctrlModifierScreen =
            ObeliskAccess.Input.Contexts.MapInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.RewardsInputContext.IsCurrentlyActive;
        return !(ctrlModifierScreen && InputRouter.CtrlHeld);
    }
}

[HarmonyPatch(typeof(InputController), "DoEscape")]
public class RouterDoEscapePatch
{
    static bool Prefix(InputController __instance)
    {
        InputRouter.Controller = __instance;
        return !InputRouter.Cancel(); // swallow (return false) iff a context handled it
    }
}
