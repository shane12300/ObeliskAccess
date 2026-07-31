using HarmonyLib;
using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Owns the keyboard while a combat is in progress. Deliberately thin: plain arrow keys are left to
/// the game (its <c>ControllerMovement</c> warps the cursor; a postfix speaks the focused element),
/// and the game's native combat keys (0–9 cast, Space end-turn, Enter confirm) are left untouched.
/// This context only adds Ctrl+↑/↓ drill-in and Escape-to-exit-drill; the on-demand Alt review keys
/// live in <see cref="CombatHotkeyPoller"/>. All state/speech lives in <see cref="CombatNavigator"/>.
/// </summary>
public class CombatInputContext : InputContextBase
{
    /// <summary>
    /// True while a live combat owns input. Exposed statically so the <c>DoFirePerformed</c> Ctrl-click
    /// guard (which has no context instance) can gate on it, mirroring <see cref="MapInputContext"/>.
    /// Returns false under any combat modal that steals input (energy picker, death screen, deck/discard
    /// windows, character stats window) so those keep their own handling.
    /// </summary>
    public static bool IsCurrentlyActive
    {
        get
        {
            var m = MatchManager.Instance;
            if (m == null) return false;
            if (m.MatchIsOver) return false;
            if (m.EnergySelector != null && m.EnergySelector.IsActive()) return false;
            if (m.DeathScreen != null && m.DeathScreen.IsActive()) return false;
            if (m.DeckCardsWindow != null && m.DeckCardsWindow.IsActive()) return false;
            if (m.DiscardSelector != null && m.DiscardSelector.IsActive()) return false;
            if (m.characterWindow != null && m.characterWindow.IsActive()) return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    /// <summary>The ONLY context that keeps the game's Enter: in combat it maps to
    /// <c>BattleKeyboard.KeyboardEnter</c> — inert in plain combat, but the only working Enter
    /// for the (not yet mod-covered) energy-transfer selector — and OnConfirm deliberately rides
    /// the game's warped-cursor click path.</summary>
    public override bool SwallowsGameEnter => false;
    // Alt+R/E/S/A/W/Q review keys collide with the game's MP emote binds; bare Alt would
    // right-click the card under the stale cursor.
    public override bool UsesAltLetters => true;
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        // Plain arrows: defer to the game so its ControllerMovement runs and our postfix reads focus.
        if (!InputRouter.CtrlHeld)
            return false;

        // Ctrl+Up/Down: drill into the focused element (card lines / character info categories).
        if (direction.y > 0f)
            CombatNavigator.DrillNext(-1);
        else if (direction.y < 0f)
            CombatNavigator.DrillNext(1);
        // Ctrl+Left/Right: reserved — swallow so the game doesn't move focus mid-drill.

        return true;
    }

    // Escape cancels a pending MP ping pick first, then exits an active drill (consume);
    // otherwise defer to the game's pause menu.
    public override bool OnCancel()
        => EmotePingManager.TryCancelPick() || CombatNavigator.TryDrillExit();

    public override bool OnConfirm()
    {
        var controller = InputRouter.Controller;
        if (controller == null)
            return false;

        // A targeted MP ping (S/A) is waiting for a character pick: Enter on the focused
        // character completes it through the game's own EmoteTarget — never the click raycast,
        // which could hit either the character or its offset ping marker.
        if (EmotePingManager.TryCompletePick())
            return true;

        var m = MatchManager.Instance;

        // A card is held and focus is on a character: complete the cast through the game's own
        // ControllerExecute (what CharacterItem.fOnMouseUp calls). The normal click raycast can't
        // be trusted here — deck-effect cards (Look/discard) overlay each hero with DeckInHero
        // icons whose colliders eclipse the character, and DeckInHero ignores clicks while a card
        // is dragged, so the raycast path silently eats the Enter.
        if (m != null && m.CardDrag && m.controllerClickedCard)
        {
            var t = CombatNavigator.FocusedTransform;
            var target = t != null
                ? (t.GetComponent<CharacterItem>() ?? t.GetComponentInParent<CharacterItem>())
                : null;
            if (target != null)
            {
                m.ControllerExecute();
                return true;
            }
        }

        // Otherwise: the game's Enter only confirms modals — it never activates the focused
        // element. Fire the game's own click path (raycast under the warped cursor), which picks
        // up / plays the focused card, confirms a target, or presses End Turn. Same idiom as
        // MainMenuInputContext.
        //
        // Exception — a card in our own hand activates DIRECTLY, not via the raycast: unrelated
        // colliders can eclipse a hand card at its warped-cursor point, silently eating both the
        // click and the hover. Confirmed live by [enter-probe]: in MP the chat window's prefab
        // click-catcher ("Collider" under ChatGO) covers the bottom-left of the screen, and with a
        // full hand the LEFTMOST card sits under it until a relayout moves it — Enter dead, no
        // hover noise, while the card itself is perfectly playable. OnMouseUpController is exactly
        // what the raycast would have invoked on the card (insta-cast or pickup); restricted to
        // CardItemTable members so pile widgets and enemy intent cards keep the raycast path, and
        // to our own turn so Enter can't trigger the game's not-your-turn card ping.
        CombatNavigator.DebugLogEnterTarget();
        bool wasDragging = m != null && m.CardDrag;
        // Capture the focused card BEFORE activating: a pickup synchronously warps focus to the
        // auto-picked target character (MoveControllerToHeroes → ControllerMovement), so reading
        // FocusedTransform afterwards would lose the card on both activation paths.
        var preFocus = CombatNavigator.FocusedTransform;
        CardItem preCard = preFocus != null ? preFocus.GetComponent<CardItem>() : null;
        CardItem handCard = null;
        if (m != null && !wasDragging && m.IsYourTurn())
        {
            if (preCard != null && m.CardItemTable != null && m.CardItemTable.Contains(preCard))
                handCard = preCard;
        }
        if (handCard != null)
            handCard.OnMouseUpController();
        else
            Traverse.Create(controller).Method("DoFirePerformed").GetValue();

        // The pickup half of a two-step cast is otherwise silent — say what happened, briefly.
        if (m != null && !wasDragging && m.CardDrag)
        {
            SpeechManager.Speak(preCard != null && preCard.CardData != null
                ? AccessibleMenuBase.StripRichText(preCard.CardData.CardName) + ". Select target."
                : "Select target.");
        }
        return true;
    }
}
