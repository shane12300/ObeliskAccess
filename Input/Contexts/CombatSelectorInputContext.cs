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
/// announce the result. Enter and Space reach this context because <see cref="RouterInputPatches"/>
/// suppresses the game's own handling of both keys while this context is active.
/// </summary>
public class CombatSelectorInputContext : InputContextBase
{
    /// <summary>Exposed statically so the router's key-suppression prefixes (which have no context
    /// instance) can gate on it, mirroring the other combat-family contexts.</summary>
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
        if (direction.x < 0f)
            return CombatSelectorManager.MoveFocus(-1);
        if (direction.x > 0f)
            return CombatSelectorManager.MoveFocus(1);
        // Swallow Up/Down too — the game's cursor navigation must not run under the modal.
        return true;
    }

    public override bool OnConfirm() => CombatSelectorManager.EnterPressed();
    public override bool OnSpace() => CombatSelectorManager.SpacePressed();
    public override bool OnCancel() => CombatSelectorManager.CancelPressed();
}
