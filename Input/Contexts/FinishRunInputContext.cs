using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The end-of-run screen (scene "FinishRun"). Table mode: Up/Down walk the score, reward and
/// per-hero progression rows, with the Main Menu button as the last row (Enter activates it).
/// While the unlocked-cards popup is up (a sub-mode): Left/Right review the cards, Enter or
/// Escape close it. All state and speech live in <see cref="FinishRunScreenManager"/>.
/// </summary>
public class FinishRunInputContext : InputContextBase
{
    public static bool IsCurrentlyActive => FinishRunManager.Instance != null;

    public override bool IsActive => IsCurrentlyActive;

    // Alt+T/I/R review keys — the game's bare-Alt right-click could pop an unlocked card's window.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (FinishRunScreenManager.CardsMode)
        {
            if (direction.x < 0f)
                FinishRunScreenManager.MoveCard(-1);
            else if (direction.x > 0f)
                FinishRunScreenManager.MoveCard(1);
            // Up/Down: nothing in the card strip; swallow to keep the modal a focus trap.
            return true;
        }

        if (direction.y > 0f)
            FinishRunScreenManager.MoveRow(-1);
        else if (direction.y < 0f)
            FinishRunScreenManager.MoveRow(1);
        // Left/Right: nothing on the table, but swallow so the game's controller nav (which only
        // knows the Main Menu button) doesn't warp the cursor underneath us.
        return true;
    }

    public override bool OnConfirm()
    {
        if (FinishRunScreenManager.CardsMode)
            FinishRunScreenManager.CloseCards();
        else
            FinishRunScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        if (FinishRunScreenManager.CardsMode)
        {
            FinishRunScreenManager.CloseCards();
            return true;
        }
        return false; // table mode: leave Escape to the game
    }
}
