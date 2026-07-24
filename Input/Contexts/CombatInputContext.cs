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

    // Escape exits an active drill (consume); otherwise defer to the game's pause menu.
    public override bool OnCancel() => CombatNavigator.TryDrillExit();

    public override bool OnConfirm()
    {
        var controller = InputRouter.Controller;
        if (controller == null)
            return false;

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
        bool wasDragging = m != null && m.CardDrag;
        Traverse.Create(controller).Method("DoFirePerformed").GetValue();

        // The pickup half of a two-step cast is otherwise silent — say what happened.
        if (m != null && !wasDragging && m.CardDrag)
        {
            var card = CombatNavigator.FocusedTransform != null
                ? CombatNavigator.FocusedTransform.GetComponent<CardItem>()
                : null;
            string name = card != null && card.CardData != null
                ? AccessibleMenuBase.StripRichText(card.CardData.CardName)
                : "Card";
            SpeechManager.Speak(name + " picked up. Move to a target and press Enter.");
        }
        return true;
    }
}
