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
        return !(ObeliskAccess.Input.Contexts.MapInputContext.IsCurrentlyActive && InputRouter.CtrlHeld);
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
