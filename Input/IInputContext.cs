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
    /// <summary>Whether this context's screen is open. Queried live on every event, so it can
    /// be derived from game state rather than tracked — a missed open/close never strands input.
    /// IsActive / a context's static IsCurrentlyActive mean <b>screen open</b>, not "owns input";
    /// a higher-priority context may sit above. For owns-input, use
    /// <see cref="InputRouter.IsActive(IInputContext)"/>.</summary>
    bool IsActive { get; }

    /// <summary>Handle an arrow-key movement. Return true to consume (and swallow) the key.</summary>
    bool OnMove(Vector2 direction);

    /// <summary>Handle Enter / Numpad Enter.</summary>
    bool OnConfirm();

    /// <summary>Handle Escape. Return true to consume (and swallow) it.</summary>
    bool OnCancel();

    /// <summary>Handle Tab / Shift+Tab.</summary>
    bool OnTab(bool backwards);

    /// <summary>Handle a number key 1–4 (n is 1-based). Return true to consume it.</summary>
    bool OnNumber(int n);

    /// <summary>Handle Space. Routed non-swallowing (like Enter/Tab); a context that repurposes
    /// Space declares <see cref="SwallowsGameSpace"/> so the game's own handling is suppressed.</summary>
    bool OnSpace();

    // ---- key-suppression flags -------------------------------------------------------------
    // The router patches suppress the game's own handling of a key whenever ANY OPEN context
    // (screen open — not just the input owner; overlays like the tutorial or an alert must not
    // re-expose the screen beneath) declares the matching flag. Declaring a flag here replaces
    // the old hand-maintained context lists in RouterInputPatches — a new context is safe by
    // default and only opts in/out of what it actually repurposes.

    /// <summary>The game's Enter (→ DoFirePerformed, a synthetic click at the stale cursor) is
    /// swallowed while this screen is open. Default TRUE: every self-activating context needs it
    /// or each Enter fires twice. Only a context whose Confirm deliberately rides the game's own
    /// Enter path (combat) opts out.</summary>
    bool SwallowsGameEnter { get; }

    /// <summary>The game's Space (→ BattleKeyboard end-turn) is swallowed while this screen is
    /// open, because the context repurposes Space (e.g. the selector's confirm).</summary>
    bool SwallowsGameSpace { get; }

    /// <summary>This screen uses Alt+R/E/S/A/W/Q review keys, which collide with the game's MP
    /// emote binds in combat: suppress the game's letter handling while Alt is held.</summary>
    bool UsesAltLetters { get; }

    /// <summary>This screen uses Alt as a modifier, so the game's bare-Alt synthetic right-click
    /// (DoButtonNorth) must be suppressed — it would right-click whatever sits under the stale
    /// cursor (cards, portraits) and pop an inaccessible window.</summary>
    bool SuppressesBareAlt { get; }

    /// <summary>The game's bare-Ctrl synthetic click (DoFirePerformed) is suppressed while this
    /// screen is open and Ctrl is held — default TRUE: no mod screen wants a stray click at the
    /// cursor (it can press an option, buy an item, or focus a TMP field and open the on-screen
    /// keyboard). Enter-triggered clicks (no Ctrl held) are unaffected.</summary>
    bool UsesCtrlModifier { get; }
}

/// <summary>No-op base so contexts only override the events they actually use.</summary>
public abstract class InputContextBase : IInputContext
{
    public abstract bool IsActive { get; }
    public virtual bool OnMove(Vector2 direction) => false;
    public virtual bool OnConfirm() => false;
    public virtual bool OnCancel() => false;
    public virtual bool OnTab(bool backwards) => false;
    public virtual bool OnNumber(int n) => false;
    public virtual bool OnSpace() => false;

    // Suppression-flag defaults (see IInputContext): Enter-swallow and Ctrl-click suppression are
    // opt-OUT — forgetting them on a new context was a repeat bug source (mode-screen skip, event
    // "always option 1", stray Ctrl clicks on the main menu) — the rest are opt-in.
    public virtual bool SwallowsGameEnter => true;
    public virtual bool SwallowsGameSpace => false;
    public virtual bool UsesAltLetters => false;
    public virtual bool SuppressesBareAlt => false;
    public virtual bool UsesCtrlModifier => true;
}
