using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The hero-selection screen (scene "HeroSelection") — party building and run options. The
/// character window (<see cref="CharPopupInputContext"/>) and perk tree
/// (<see cref="PerkTreeInputContext"/>) are registered above this context, so it never sees
/// input while either is open. While the madness or sandbox panel is open, all three contexts
/// go inert so the game's own controller navigation drives those (not-yet-accessible) panels.
/// All state and speech live in <see cref="HeroSelectionScreenManager"/>.
/// </summary>
public class HeroSelectionInputContext : InputContextBase
{
    /// <summary>Screen-open: the hero-selection scene is up and neither deferred panel (madness/
    /// sandbox) has taken over the game's own navigation. Like every context this is raw screen
    /// state — the character window or perk tree above may still outrank it in the router.</summary>
    public static bool IsCurrentlyActive
    {
        get
        {
            if (HeroSelectionManager.Instance == null)
                return false;
            if (MadnessManager.Instance != null && MadnessManager.Instance.IsActive())
                return false;
            if (SandboxManager.Instance != null && SandboxManager.Instance.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            HeroSelectionScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            HeroSelectionScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            HeroSelectionScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            HeroSelectionScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        HeroSelectionScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        return HeroSelectionScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        HeroSelectionScreenManager.CycleRegion(backwards);
        return true;
    }

    public override bool OnNumber(int n)
    {
        HeroSelectionScreenManager.HandleNumber(n);
        return true;
    }
}
