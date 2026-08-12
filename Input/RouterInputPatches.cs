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
/// digits itself, but Enter falls through to a synthetic cursor click outside combat, and Tab maps
/// to NextField() (an EventSystem walk that can focus input fields — see the Prefix docs), so the
/// prefix swallows both on screens the mod drives.
/// </summary>
/// <summary>
/// Shared guards for the router patches below.
/// </summary>
internal static class RouterGuards
{
    /// <summary>
    /// The game's on-screen virtual keyboard is up (opened by a synthetic click landing on an
    /// input field, or gamepad X/Square via <c>DoButtonWest</c>). While it shows, the game must
    /// own every key — arrows
    /// drive <c>KeyboardManager.ControllerMovement</c>, Enter clicks the highlighted OSK key —
    /// or the hidden mod screen beneath would act on keys the user aims at the keyboard.
    /// </summary>
    internal static bool OskActive =>
        KeyboardManager.Instance != null && KeyboardManager.Instance.IsActive();

    /// <summary>
    /// A live game text field owns the keyboard. The game guards its own handlers this way —
    /// <c>DoKeyBinding</c> bails when the EventSystem's selection carries a TMP/legacy input
    /// field, and <c>DoMovementVector</c> bails on the craft and tome search boxes — and the mod
    /// must match it, or typing in the Forge search box and pressing Enter would buy the focused
    /// card while the arrows walked the item list instead of the caret. The chat, lobby and alert
    /// edit sessions have their own gates; this covers every field the mod does not drive.
    /// </summary>
    internal static bool GameTextFieldFocused
    {
        get
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
                return false;
            // Never fire for a field the MOD is driving (alert input, lobby name/password): those
            // sessions have their own gates and their contexts expect keys to keep arriving.
            if (ObeliskAccess.Patches.TmpEditSession.AnyActive)
                return false;
            var sel = es.currentSelectedGameObject;
            if (sel == null || !sel.activeInHierarchy)
                return false;
            return sel.GetComponent<TMPro.TMP_InputField>() != null
                || sel.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }

    /// <summary>
    /// Set while the mod itself invokes the game's private <c>DoFirePerformed</c> (combat and
    /// main-menu confirm fall back to it). A Harmony detour applies to reflection invokes too, so
    /// without this the bare-Ctrl suppression below would swallow the mod's OWN synthetic click
    /// whenever the user happened to be holding Ctrl — a silent dead Enter.
    /// </summary>
    internal static bool SelfFiring;
}

[HarmonyPatch(typeof(InputController), "DoMovement")]
public class RouterDoMovementPatch
{
    static bool Prefix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (!InputRouter.IsKeyboard(_context))
            return true;

        // On-screen keyboard: the game's arrow dispatch must reach it (see RouterGuards).
        if (RouterGuards.OskActive)
            return true;

        // While the MP chat input is focused the arrows belong to the text caret: swallow the
        // game's movement (no cursor warp) and route nothing.
        if (ObeliskAccess.Patches.ChatSpeech.Typing)
            return false;

        // Any other live game text field (craft/tome search): let the game have the key, exactly
        // as its own DoMovementVector guard does. Routing here would move the mod's list instead
        // of the caret.
        if (RouterGuards.GameTextFieldFocused)
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
    /// way — Harmony runs postfixes even when a prefix skips the original). Which screens need
    /// which suppression is declared per-context via the <see cref="IInputContext"/> flags and
    /// aggregated by <see cref="InputRouter"/> over every OPEN context — an overlay (tutorial,
    /// alert, players panel) over combat must not re-expose the screen beneath, so "any open",
    /// never just the input owner:
    ///
    /// Enter (<see cref="IInputContext.SwallowsGameEnter"/>, default ON — only Combat opts out):
    /// outside combat the game maps Enter to <c>DoFirePerformed</c>, a synthetic click at the
    /// stale cursor position, so every self-activating context needs it swallowed or each Enter
    /// fires twice (historically: the mode-screen skip, and the event book's "always option 1").
    /// In combat, Enter maps to <c>BattleKeyboard.KeyboardEnter</c> — inert in plain combat but
    /// the only working Enter for the (not yet mod-covered) energy-transfer selector — and the
    /// combat context's Confirm deliberately rides the game's click path.
    ///
    /// Space (<see cref="IInputContext.SwallowsGameSpace"/>): the game maps it to end-turn
    /// (<c>BattleKeyboard.KeyboardSpace</c>); the selector windows repurpose it as confirm.
    ///
    /// Letters (<see cref="IInputContext.UsesAltLetters"/>): in combat the game binds
    /// R/E/S/A/W/Q to multiplayer emote pings, which collide with the mod's Alt review hotkeys —
    /// including over the in-combat modals (alert, death screen, sheet, tree), whose flag keeps
    /// Alt+R from broadcasting a heart emote to the whole party.
    /// </summary>
    static bool Prefix(InputAction.CallbackContext _context)
    {
        var kb = Keyboard.current;
        if (kb == null)
            return true;

        // On-screen keyboard: Enter must reach DoFirePerformed (it clicks the highlighted OSK
        // key), and Tab/letters must not be eaten either (see RouterGuards).
        if (RouterGuards.OskActive)
            return true;

        // A live game text field owns the keyboard (see RouterGuards): never swallow Tab out of
        // it, and never repurpose Enter while the user is typing.
        if (RouterGuards.GameTextFieldFocused)
            return true;

        InputControl control = _context.control;

        // Tab: the game maps Tab (unconditionally, before its keyboard-shortcuts gate) to
        // NextField(), which takes the EventSystem's current selection, finds the Selectable
        // BELOW it (FindSelectableOnDown) and selects it. Selecting a TMP input field activates
        // it — and in multiplayer the chat input sits at the bottom of every screen, so a Tab
        // pressed on a mod screen could silently focus the chat; the Typing gate then mutes the
        // whole router until Escape (the "random MP lockup"). The mod repurposes Tab on every
        // screen it owns, so swallow the game's handling whenever a context is active — the
        // postfix still routes Tab to the context.
        if (InputRouter.IsTab(control) && InputRouter.HasActiveContext)
            return false;

        if (InputRouter.IsEnter(control) && InputRouter.AnySwallowsGameEnter)
            return false;

        if (InputRouter.IsSpace(control) && InputRouter.AnySwallowsGameSpace)
            return false;

        if (!InputRouter.AltHeld)
            return true;
        if (!InputRouter.AnyUsesAltLetters)
            return true;

        return !(control == kb[Key.R] || control == kb[Key.E] || control == kb[Key.S]
              || control == kb[Key.A] || control == kb[Key.W] || control == kb[Key.Q]);
    }

    static void Postfix(InputController __instance, InputAction.CallbackContext _context)
    {
        if (Keyboard.current == null)
            return;

        // While the MP chat input is focused (or the very frame its edit ended — TMP processes
        // the submit Enter first), route nothing: without this the same Enter that sent a chat
        // message would also fire Confirm on the screen below, e.g. travel on the map.
        if (ObeliskAccess.Patches.ChatSpeech.Typing)
            return;

        // On-screen keyboard: keys belong to it, not to the context beneath (see RouterGuards).
        if (RouterGuards.OskActive)
            return;

        // A live game text field owns the keyboard (see RouterGuards).
        if (RouterGuards.GameTextFieldFocused)
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
/// cursor — in combat that pops the card-inspection window, and on the hero-selection screen it
/// right-clicks a roster portrait, opening the character window. Screens that use Alt as the mod's
/// review modifier declare <see cref="IInputContext.SuppressesBareAlt"/>; the synthetic right-click
/// is suppressed while any of them is open. The multiplayer chat keyboard's Alt-as-delete is left
/// working (the OSK check precedes the flag query).
/// </summary>
[HarmonyPatch(typeof(InputController), "DoButtonNorth")]
public class RouterDoButtonNorthPatch
{
    static bool Prefix()
    {
        if (RouterGuards.OskActive)
            return true;
        // Test AltHeld, not just the flag: the game routes BOTH bare Alt and the gamepad's
        // buttonNorth (Y) into DoButtonNorth, and suppressing the pad's inspect button on every
        // Alt-using screen would change the game for controller players.
        return !(InputRouter.AltHeld && InputRouter.AnySuppressesBareAlt);
    }
}

/// <summary>
/// The game maps a bare Ctrl press to a "click" (<c>DoFirePerformed</c>). No mod screen wants a
/// stray click at the cursor — it can press an option, buy an item, or focus a live TMP field
/// (craft search bar, lobby fields) and open the on-screen keyboard — so
/// <see cref="IInputContext.UsesCtrlModifier"/> defaults to true and the click is suppressed
/// whenever any mod screen is open and Ctrl is held. Gamepad A (no Ctrl held) and mouse clicks
/// are unaffected.
/// </summary>
[HarmonyPatch(typeof(InputController), "DoFirePerformed")]
public class RouterDoFirePerformedPatch
{
    static bool Prefix()
    {
        if (RouterGuards.SelfFiring)
            return true; // the mod's own synthetic click — never suppress it
        return !(InputRouter.CtrlHeld && InputRouter.AnyUsesCtrlModifier);
    }
}

[HarmonyPatch(typeof(InputController), "DoEscape")]
public class RouterDoEscapePatch
{
    static bool Prefix(InputController __instance)
    {
        // Escape while typing in the MP chat cancels the message — and must not also open the
        // pause menu over the chat.
        if (ObeliskAccess.Patches.ChatSpeech.Typing)
        {
            ObeliskAccess.Patches.ChatSpeech.CancelTyping();
            return false;
        }

        // The on-screen keyboard can only be closed by the game's own Escape — if a context
        // swallowed Escape here, the overlay would be stuck until a scene change. Let the game
        // handle it (EscapeFunction's first check closes the keyboard).
        if (RouterGuards.OskActive)
            return true;

        InputRouter.Controller = __instance;
        return !InputRouter.Cancel(); // swallow (return false) iff a context handled it
    }
}
