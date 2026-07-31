using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// MP travel/answer conflict tie-breaker (ConflictManager). A modal over both the map and the
/// event book — the game's own dispatch order is characterWindow &gt; Conflict &gt; Event &gt; Map, and
/// the mod registers it in the same slot (below CharWindow/Combat, above Event). Mostly a narrated
/// screen; the rule rows are only actionable for the player who owns the choosing hero.
/// Escape is deliberately left to the game (the modal has no cancel; the game's default opens the
/// pause menu, same as for sighted players).
/// </summary>
public class ConflictInputContext : InputContextBase
{
    /// <summary>Static so the RouterInputPatches suppressions can gate on it. The Conflict
    /// property is null except while a conflict is in progress, and permanently null in SP.</summary>
    public static bool IsCurrentlyActive => ConflictScreenManager.IsOpen;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+R review key — the game's bare-Alt right-click could pop a flipped card's inspection.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            ConflictScreenManager.MoveRule(-1);
        else if (direction.y < 0f)
            ConflictScreenManager.MoveRule(1);
        return true; // sideways swallowed too — the game's controller nav would warp the cursor
    }

    public override bool OnConfirm()
    {
        ConflictScreenManager.Confirm();
        return true;
    }

    public override bool OnNumber(int n)
    {
        if (n >= 1 && n <= 3)
            ConflictScreenManager.FocusRule(n - 1);
        return true;
    }
}
