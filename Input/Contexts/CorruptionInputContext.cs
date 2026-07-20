using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// Modal context for the pre-combat corruption prompt. Registered above the map context so that
/// while the prompt is up it owns input (and the map's hotkeys are suspended). Left/Right choose a
/// reward (which accepts the corruption), Up/Down toggle acceptance, Enter confirms. Escape is left
/// to the game. The opening summary is spoken by the hotkey poller when the prompt appears.
/// </summary>
public class CorruptionInputContext : InputContextBase
{
    public override bool IsActive
        => MapManager.Instance != null && MapManager.Instance.IsCorruptionOver();

    public override bool OnMove(Vector2 direction)
    {
        if (direction.x < 0f)
            CorruptionAccessibility.ChooseA();
        else if (direction.x > 0f)
            CorruptionAccessibility.ChooseB();
        else if (direction.y != 0f)
            CorruptionAccessibility.ToggleAccept();
        return true;
    }

    public override bool OnConfirm()
    {
        CorruptionAccessibility.Confirm();
        return true;
    }
}
