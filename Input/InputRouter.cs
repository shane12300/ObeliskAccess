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
/// Only <see cref="RouterInputPatches"/> patches the game's <c>InputController</c>; contexts never
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

    // ---- raw-input helpers (shared by the patches so key detection lives in one place) ----

    /// <summary>True when the event came from the physical keyboard (leave gamepad to the game).</summary>
    public static bool IsKeyboard(InputAction.CallbackContext context)
        => Keyboard.current != null && context.control.device.displayName == "Keyboard";

    public static bool IsEnter(InputControl control)
        => Keyboard.current != null
        && (control == Keyboard.current[Key.Enter] || control == Keyboard.current[Key.NumpadEnter]);

    public static bool IsTab(InputControl control)
        => Keyboard.current != null && control == Keyboard.current[Key.Tab];

    public static bool ShiftHeld
        => Keyboard.current != null
        && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
}
