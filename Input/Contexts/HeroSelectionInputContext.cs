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
    private static HeroSelectionInputContext _instance;

    public HeroSelectionInputContext()
    {
        _instance = this;
    }

    /// <summary>Priority-correct "owns input right now" — used by the router patches to swallow
    /// the game's own Enter handling (which would double-fire as a synthetic cursor click) and
    /// the bare-Alt right-click.</summary>
    public static bool IsCurrentlyActive => InputRouter.IsActive(_instance);

    public override bool IsActive
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
