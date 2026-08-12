using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Owns the keyboard while an in-combat card-selection window is up: the discard selector
/// (<c>UIDiscardSelector</c>) or the deck-cards window (<c>UIDeckCards</c> — look-at-deck,
/// discover, and the plain pile viewers). Registered above <see cref="CombatInputContext"/>, which
/// deliberately reports inactive under these modals. Left/Right review cards, Enter toggles (and
/// auto-confirms mandatory single picks), Space confirms, Ctrl+Up/Down drill into the description,
/// Escape exits the drill / closes a viewer. All state and speech live in
/// <see cref="CombatSelectorManager"/>.
///
/// Digits are NOT handled here: the game's <c>BattleKeyboard.KeyboardNum</c> already toggles the
/// Nth container card while these windows are active, and the mod's SelectCardTo* postfixes
/// announce the result. Enter and Space reach this context from the <c>DoKeyBinding</c> postfix;
/// the matching prefix suppression (declared by <see cref="SwallowsGameEnter"/> and
/// <see cref="SwallowsGameSpace"/>) exists so the game does not also act on the same press.
/// </summary>
public class CombatSelectorInputContext : InputContextBase
{
    /// <summary>Static screen-open test for patches and pollers with no context instance, mirroring
    /// the other combat-family contexts. (The router's key suppressions are declared via the
    /// <see cref="IInputContext"/> flags, not this property.)</summary>
    public static bool IsCurrentlyActive
    {
        get
        {
            var m = MatchManager.Instance;
            if (m == null)
                return false;
            if (m.characterWindow != null && m.characterWindow.IsActive())
                return false;
            return (m.DiscardSelector != null && m.DiscardSelector.IsActive())
                || (m.DeckCardsWindow != null && m.DeckCardsWindow.IsActive());
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // Space is the window's confirm key (the game's Space would end the turn); the Alt review
    // keys and Ctrl drill both stay live in the selector, and bare Alt must not right-click a
    // container card under the stale cursor.
    public override bool SwallowsGameSpace => true;
    public override bool UsesAltLetters => true;
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (InputRouter.CtrlHeld)
        {
            // Ctrl+Up/Down: drill into the focused card's description lines, as in combat.
            if (direction.y > 0f)
                CombatSelectorManager.Drill(-1);
            else if (direction.y < 0f)
                CombatSelectorManager.Drill(1);
            return true;
        }
        // Always consume: MoveFocus returns false before the mode registers (the window's
        // IsActive() can lead our TurnOn patch), and a fall-through there would warp the combat
        // cursor and re-announce the board underneath the open modal.
        if (direction.x < 0f)
        {
            CombatSelectorManager.MoveFocus(-1);
            return true;
        }
        if (direction.x > 0f)
        {
            CombatSelectorManager.MoveFocus(1);
            return true;
        }
        // Swallow Up/Down too — the game's cursor navigation must not run under the modal.
        return true;
    }

    public override bool OnConfirm() => CombatSelectorManager.EnterPressed();
    public override bool OnSpace() => CombatSelectorManager.SpacePressed();
    public override bool OnCancel() => CombatSelectorManager.CancelPressed();
}
