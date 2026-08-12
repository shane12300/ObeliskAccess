using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Central keyboard-input dispatcher for the mod. All accessibility screens register an
/// <see cref="IInputContext"/> here in priority order (highest first); every keyboard event is
/// routed to the single highest-priority context that is currently active. This is the one place
/// that decides who owns input, replacing the per-screen guards that each menu patch used to carry.
///
/// Only the patches in <c>RouterInputPatches.cs</c> touch the game's <c>InputController</c>; contexts never
/// patch it directly, so adding a new screen means registering one context — no existing code changes.
/// </summary>
public static class InputRouter
{
    // Ordered highest-priority first. A modal (tutorial/settings) sits above the base menu, so
    // while it is active the menu context is never reached — no fall-through, no cross-guards.
    private static readonly List<IInputContext> _contexts = new List<IInputContext>();

    /// <summary>The <c>InputController</c> instance for the event currently being dispatched.
    /// Set by the patches before each dispatch; contexts read it when they need to call back into
    /// the game (e.g. its fire path).</summary>
    public static InputController Controller { get; internal set; }

    /// <summary>Registers a context. Call order defines priority: earlier = higher priority.</summary>
    public static void Register(IInputContext context)
    {
        if (context != null && !_contexts.Contains(context))
            _contexts.Add(context);
    }

    /// <summary>The highest-priority context that currently owns input, or null if none.</summary>
    private static IInputContext ActiveContext()
    {
        for (int i = 0; i < _contexts.Count; i++)
        {
            if (_contexts[i].IsActive)
                return _contexts[i];
        }
        return null;
    }

    public static bool Move(Vector2 direction) => ActiveContext()?.OnMove(direction) ?? false;
    public static bool Confirm() => ActiveContext()?.OnConfirm() ?? false;
    public static bool Cancel() => ActiveContext()?.OnCancel() ?? false;
    public static bool Tab(bool backwards) => ActiveContext()?.OnTab(backwards) ?? false;
    public static bool Number(int n) => ActiveContext()?.OnNumber(n) ?? false;
    public static bool Space() => ActiveContext()?.OnSpace() ?? false;

    /// <summary>True when the given context is the one that currently owns input. Lets a component
    /// (e.g. the map hotkey poller) act only while its screen actually has focus, respecting priority.</summary>
    public static bool IsActive(IInputContext context) => context != null && ActiveContext() == context;

    /// <summary>True when any registered context currently owns input — i.e. the mod is driving
    /// this screen. Used by the patches to suppress game key handling the mod repurposes globally
    /// (e.g. the game's Tab → NextField form navigation).</summary>
    public static bool HasActiveContext => ActiveContext() != null;

    // ---- key-suppression flag mask ---------------------------------------------------------
    // The router patches ask "does ANY OPEN context declare this flag?" (screen open, not just
    // the input owner: an overlay like the tutorial or an alert over combat must not re-expose
    // the game's emote letters / bare-Alt click of the screen beneath). All five bits are
    // computed in one walk, reading each context's IsActive at most once, and memoised per
    // frame — the old per-list OR-chains re-evaluated up to ~24 predicates per keypress.

    private const int FlagEnter = 1, FlagSpace = 2, FlagAltLetters = 4, FlagBareAlt = 8, FlagCtrl = 16;
    private const int AllFlags = FlagEnter | FlagSpace | FlagAltLetters | FlagBareAlt | FlagCtrl;
    private static int _maskFrame = -1;
    private static int _mask;
    private static bool _flagsLogged;

    public static bool AnySwallowsGameEnter => (OpenFlagsMask() & FlagEnter) != 0;
    public static bool AnySwallowsGameSpace => (OpenFlagsMask() & FlagSpace) != 0;
    public static bool AnyUsesAltLetters => (OpenFlagsMask() & FlagAltLetters) != 0;
    public static bool AnySuppressesBareAlt => (OpenFlagsMask() & FlagBareAlt) != 0;
    public static bool AnyUsesCtrlModifier => (OpenFlagsMask() & FlagCtrl) != 0;

    private static int DeclaredFlags(IInputContext c) =>
        (c.SwallowsGameEnter ? FlagEnter : 0)
        | (c.SwallowsGameSpace ? FlagSpace : 0)
        | (c.UsesAltLetters ? FlagAltLetters : 0)
        | (c.SuppressesBareAlt ? FlagBareAlt : 0)
        | (c.UsesCtrlModifier ? FlagCtrl : 0);

    private static int OpenFlagsMask()
    {
        int frame = Time.frameCount;
        if (frame == _maskFrame)
            return _mask;

        if (!_flagsLogged)
        {
            _flagsLogged = true;
            int enterOptOuts = 0, space = 0, altLetters = 0, bareAlt = 0, ctrl = 0;
            for (int i = 0; i < _contexts.Count; i++)
            {
                if (!_contexts[i].SwallowsGameEnter) enterOptOuts++;
                if (_contexts[i].SwallowsGameSpace) space++;
                if (_contexts[i].UsesAltLetters) altLetters++;
                if (_contexts[i].SuppressesBareAlt) bareAlt++;
                if (_contexts[i].UsesCtrlModifier) ctrl++;
            }
            Plugin.LogDebug($"Router flags: {_contexts.Count} contexts; Enter opt-outs {enterOptOuts}, "
                + $"Space {space}, AltLetters {altLetters}, BareAlt {bareAlt}, Ctrl {ctrl}");
        }

        int mask = 0;
        for (int i = 0; i < _contexts.Count && mask != AllFlags; i++)
        {
            int declared = DeclaredFlags(_contexts[i]);
            if ((declared & ~mask) == 0)
                continue; // this context can't add a new bit — skip its (game-state) IsActive read
            if (_contexts[i].IsActive)
                mask |= declared;
        }
        _maskFrame = frame;
        _mask = mask;
        return mask;
    }

    // ---- raw-input helpers (shared by the patches so key detection lives in one place) ----

    /// <summary>True when the event came from the physical keyboard (leave gamepad to the game).</summary>
    public static bool IsKeyboard(InputAction.CallbackContext context)
        => Keyboard.current != null && context.control.device.displayName == "Keyboard";

    public static bool IsEnter(InputControl control)
        => Keyboard.current != null
        && (control == Keyboard.current[Key.Enter] || control == Keyboard.current[Key.NumpadEnter]);

    public static bool IsTab(InputControl control)
        => Keyboard.current != null && control == Keyboard.current[Key.Tab];

    public static bool IsSpace(InputControl control)
        => Keyboard.current != null && control == Keyboard.current[Key.Space];

    public static bool ShiftHeld
        => Keyboard.current != null
        && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

    public static bool CtrlHeld
        => Keyboard.current != null
        && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

    public static bool AltHeld
        => Keyboard.current != null
        && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);

    /// <summary>Recognises the number keys 1–4 (top row or numpad). Outputs the 1-based digit.</summary>
    public static bool IsDigit(InputControl control, out int n)
    {
        n = 0;
        var kb = Keyboard.current;
        if (kb == null)
            return false;
        if (control == kb[Key.Digit1] || control == kb[Key.Numpad1]) { n = 1; return true; }
        if (control == kb[Key.Digit2] || control == kb[Key.Numpad2]) { n = 2; return true; }
        if (control == kb[Key.Digit3] || control == kb[Key.Numpad3]) { n = 3; return true; }
        if (control == kb[Key.Digit4] || control == kb[Key.Numpad4]) { n = 4; return true; }
        return false;
    }

    private static readonly HashSet<string> _tickFailuresLogged = new HashSet<string>();

    /// <summary>
    /// Run one manager's per-frame tick in isolation. Pollers chain several ticks in a single
    /// Update, and Unity only catches the exception at the Update boundary — so without this a
    /// throw in the first tick permanently starves every later one in the same method (e.g. a
    /// combat-flush failure silently killing selector-window arrival announcements, or a
    /// hero-selection failure killing the popup/tree mouse-close detection, which strands their
    /// contexts open). Logged once per tick name so a per-frame fault cannot flood the log.
    /// </summary>
    internal static void SafeTick(Action tick, string name)
    {
        try
        {
            tick();
        }
        catch (Exception ex)
        {
            if (_tickFailuresLogged.Add(name))
                Plugin.Logger?.LogError($"[tick] {name} threw (further failures suppressed): {ex}");
        }
    }
}
