using UnityEngine;

namespace ObeliskAccess.Input;

/// <summary>
/// A single accessible screen's response to semantic input events. The <see cref="InputRouter"/>
/// dispatches each event to exactly one context — the highest-priority one whose
/// <see cref="IsActive"/> is currently true — so input can never fall through from one screen to
/// another. Handlers return <c>true</c> when they consume the event; for movement and cancel
/// (Escape) that also swallows the raw key so the game's own handling does not run.
/// </summary>
public interface IInputContext
{
    /// <summary>Whether this context currently owns input. Queried live on every event, so it can
    /// be derived from game state rather than tracked — a missed open/close never strands input.</summary>
    bool IsActive { get; }

    /// <summary>Handle an arrow-key movement. Return true to consume (and swallow) the key.</summary>
    bool OnMove(Vector2 direction);

    /// <summary>Handle Enter / Numpad Enter.</summary>
    bool OnConfirm();

    /// <summary>Handle Escape. Return true to consume (and swallow) it.</summary>
    bool OnCancel();

    /// <summary>Handle Tab / Shift+Tab.</summary>
    bool OnTab(bool backwards);
}

/// <summary>No-op base so contexts only override the events they actually use.</summary>
public abstract class InputContextBase : IInputContext
{
    public abstract bool IsActive { get; }
    public virtual bool OnMove(Vector2 direction) => false;
    public virtual bool OnConfirm() => false;
    public virtual bool OnCancel() => false;
    public virtual bool OnTab(bool backwards) => false;
}
