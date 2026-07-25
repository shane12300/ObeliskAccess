using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The in-run character sheet (<c>CharacterWindowUI</c>) — a modal over the map, town, combat,
/// rewards and loot screens, so this context registers above all of them (and below the true
/// modals: alerts, the death popup, the combat card selectors, and the perk tree that can open
/// over the sheet's Perks tab). Up/Down walk rows, Left/Right compare a level row's two trait
/// choices, Tab cycles tabs, 1–4 switch party member, Enter activates, Ctrl+Up/Down drill into
/// the focused card, Escape closes. The screens beneath all gate themselves on
/// <c>characterWindow.IsActive()</c> already, so input can never leak down while the sheet is
/// up. All state and speech live in <see cref="CharWindowScreenManager"/>.
/// </summary>
public class CharWindowInputContext : InputContextBase
{
    /// <summary>Exposed statically for the router patches (Enter swallow, bare-Ctrl and
    /// bare-Alt suppression), which have no context instance.</summary>
    public static bool IsCurrentlyActive => CharWindowScreenManager.IsOpen;

    public override bool IsActive => IsCurrentlyActive;

    public override bool OnMove(Vector2 direction)
    {
        if (InputRouter.CtrlHeld)
        {
            // Ctrl+Up/Down: drill into the focused card, line by line.
            if (direction.y > 0f)
                CharWindowScreenManager.DrillNext(-1);
            else if (direction.y < 0f)
                CharWindowScreenManager.DrillNext(1);
            // Ctrl+Left/Right: reserved — swallow so nothing moves mid-drill.
            return true;
        }
        if (direction.y > 0f)
            CharWindowScreenManager.MoveVertical(-1);
        else if (direction.y < 0f)
            CharWindowScreenManager.MoveVertical(1);
        else if (direction.x < 0f)
            CharWindowScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            CharWindowScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        CharWindowScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        return CharWindowScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        CharWindowScreenManager.CycleTab(backwards);
        return true;
    }

    public override bool OnNumber(int n)
    {
        CharWindowScreenManager.SelectHeroSlot(n - 1);
        return true;
    }
}
