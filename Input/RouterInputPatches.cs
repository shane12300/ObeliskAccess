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
/// own navigation/escape does not also run). Enter/Tab are handled in a postfix: the game ignores
/// Tab and digits itself, but Enter falls through to a synthetic cursor click outside combat, so
/// the prefix swallows the game's Enter on screens where that click would land on a stale cursor
/// position (see Prefix docs).
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
    /// Suppressions of the game's own key handling (our postfix still routes the key either
    /// way — Harmony runs postfixes even when a prefix skips the original):
    ///
    /// Enter: outside combat the game maps Enter to <c>DoFirePerformed</c>, a synthetic click at
    /// the current mouse-cursor position. The loot and rewards screens swallow all arrows and
    /// never warp the cursor, so that click always lands on a stale position — it can take an
    /// item, grab the gold, switch the loot picker, or open the (inaccessible) deck window under
    /// our feet. Swallow the game's Enter while either screen owns input. In the combat card
    /// selector windows the game instead maps Enter to "confirm the window"
    /// (<c>BattleKeyboard.KeyboardEnter</c>); the mod repurposes Enter as select-focused-card, so
    /// swallow it there too. Real mouse clicks and gamepad A don't go through this path.
    ///
    /// Space: the game maps it to end-turn (<c>BattleKeyboard.KeyboardSpace</c>). While a combat
    /// card selector window is up the mod repurposes Space as the confirm key, so swallow the
    /// game's handling — the postfix routes it to the context.
    ///
    /// Letters: in combat the game binds R/E/S/A/W/Q to multiplayer emote pings. Those letters
    /// double as the mod's Alt review hotkeys (Alt+R repeat, Alt+E energy, Alt+S statuses), so
    /// while combat or a selector window owns input and Alt is held, skip the game's handling —
    /// the poller still sees the key.
    /// </summary>
    static bool Prefix(InputAction.CallbackContext _context)
    {
        var kb = Keyboard.current;
        if (kb == null)
            return true;

        InputControl control = _context.control;
        bool selectorActive = ObeliskAccess.Input.Contexts.CombatSelectorInputContext.IsCurrentlyActive;

        // The alert dialogue repurposes Enter as activate-focused-row; the game's Enter would
        // synthetically click whatever the (possibly warped) cursor rests on — an alert button.
        if (InputRouter.IsEnter(control)
            && (selectorActive
                || ObeliskAccess.Input.Contexts.LootInputContext.IsCurrentlyActive
                || ObeliskAccess.Input.Contexts.RewardsInputContext.IsCurrentlyActive
                || ObeliskAccess.Input.Contexts.AlertInputContext.IsCurrentlyActive
                // The game's own Enter (BattleKeyboard.KeyboardEnter) also dismisses the death
                // screen — swallow it so the dismissal goes through our context alone.
                || ObeliskAccess.Input.Contexts.DeathScreenInputContext.IsCurrentlyActive
                // On the main-menu screens the game's Enter → DoFirePerformed would fire alongside
                // OnConfirm's, pressing things twice — the second fire's physics fallback could hit
                // a just-activated mode-selection collider and skip straight to the save window.
                // OnConfirm reproduces the single click itself.
                || ObeliskAccess.Input.Contexts.MainMenuInputContext.IsCurrentlyActive))
            return false;

        if (selectorActive && InputRouter.IsSpace(control))
            return false;

        if (!InputRouter.AltHeld)
            return true;
        // Alt review keys must also stay quiet over the in-combat modals that sit above the combat
        // context (an alert such as the retry dialog), or Alt+R would fire an emote ping.
        if (!ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive && !selectorActive
            && !ObeliskAccess.Input.Contexts.AlertInputContext.IsCurrentlyActive
            && !ObeliskAccess.Input.Contexts.DeathScreenInputContext.IsCurrentlyActive)
            return true;

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
        else if (InputRouter.IsSpace(control))
            InputRouter.Space();
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
        return !(ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive
              || ObeliskAccess.Input.Contexts.CombatSelectorInputContext.IsCurrentlyActive);
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
        // The map, combat, rewards and loot screens repurpose Ctrl as a look-ahead / drill-in
        // modifier, so suppress the bare-Ctrl click while one of them owns input and Ctrl is held.
        bool ctrlModifierScreen =
            ObeliskAccess.Input.Contexts.MapInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.CombatInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.CombatSelectorInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.RewardsInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.LootInputContext.IsCurrentlyActive
            || ObeliskAccess.Input.Contexts.AlertInputContext.IsCurrentlyActive;
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
