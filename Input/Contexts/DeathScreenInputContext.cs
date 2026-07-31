using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Owns the keyboard while the in-combat death popup (<see cref="UICombatDeath"/>) is up.
/// Registered above the combat card-selector context: the popup can appear mid-resolution and is
/// the most modal thing in combat (<see cref="CombatInputContext"/> already yields to it).
/// Up/Down walk the popup lines, Enter continues (owner/host only in multiplayer). Escape is left
/// to the game — the popup is not Escape-dismissable. State and speech live in
/// <see cref="DeathScreenPopupManager"/>.
/// </summary>
public class DeathScreenInputContext : InputContextBase
{
    /// <summary>Static so the <c>RouterInputPatches</c> suppressions can gate on it.</summary>
    public static bool IsCurrentlyActive => DeathScreenPopupManager.Active;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+T reads the Death's Door card, Alt+R repeats — the game's emote letters must stay
    // quiet, and the bare-Alt right-click must not land on a card beneath the popup.
    public override bool UsesAltLetters => true;
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            DeathScreenPopupManager.Move(-1);
        else if (direction.y < 0f)
            DeathScreenPopupManager.Move(1);
        // Left/Right: nothing, but swallow — a modal focus trap.
        return true;
    }

    public override bool OnConfirm()
    {
        DeathScreenPopupManager.Confirm();
        return true;
    }
}
