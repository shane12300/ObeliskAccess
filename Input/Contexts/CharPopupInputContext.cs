using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The character window (<c>CharPopup</c>) on the hero-selection screen — a modal over
/// <see cref="HeroSelectionInputContext"/>. The perk tree sits above this context, so while the
/// tree is open this one never sees input; the card-backs popup, by contrast, is part of this
/// window's own Card Backs tab. All state and speech live in
/// <see cref="CharPopupScreenManager"/>.
/// </summary>
public class CharPopupInputContext : InputContextBase
{
    private static CharPopupInputContext _instance;

    public CharPopupInputContext()
    {
        _instance = this;
    }

    public static bool IsCurrentlyActive => InputRouter.IsActive(_instance);

    public override bool IsActive
    {
        get
        {
            if (!CharPopupScreenManager.IsOpen)
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
            CharPopupScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            CharPopupScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            CharPopupScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            CharPopupScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        CharPopupScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        return CharPopupScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        CharPopupScreenManager.CycleTab(backwards);
        return true;
    }

    // Digits are swallowed so a stray 1–4 can't reach the hero-selection screen underneath.
    public override bool OnNumber(int n) => true;
}
