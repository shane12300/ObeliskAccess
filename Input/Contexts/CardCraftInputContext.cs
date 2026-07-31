using ObeliskAccess.Patches;
using UnityEngine;

namespace ObeliskAccess.Input.Contexts;

/// <summary>
/// The five town service screens (Altar / Church / Forge / Divination / Armory), all one game
/// prefab (<c>CardCraftManager</c>, craftType 0–4). Types 5–7 (challenge setup, corruption
/// flows) are deliberately excluded — they have their own handling. These screens also open over
/// the map (event shops/healers), so this context sits above the map and event contexts. Confirm
/// alerts (tutorial warnings, buy failures) are owned by the global alert dialogue, which
/// outranks this context. All state and speech live in <see cref="CardCraftScreenManager"/>;
/// this class only maps keys.
/// </summary>
public class CardCraftInputContext : InputContextBase
{
    public static bool IsCurrentlyActive
    {
        get
        {
            var m = CardCraftManager.Instance;
            if (m == null || m.craftType < 0 || m.craftType > 4)
                return false;
            // The character sheet hides the craft screen's GameObject while it is open.
            if (!m.gameObject.activeSelf)
                return false;
            var townWindow = TownManager.Instance != null ? TownManager.Instance.characterWindow : null;
            if (townWindow != null && townWindow.IsActive())
                return false;
            var mapWindow = MapManager.Instance != null ? MapManager.Instance.characterWindow : null;
            if (mapWindow != null && mapWindow.IsActive())
                return false;
            // The give window opens over the Forge via the game's own gold button; it is modal
            // but registered below this context, so yield explicitly.
            var give = GiveManager.Instance;
            if (give != null && give.IsActive())
                return false;
            return true;
        }
    }

    public override bool IsActive => IsCurrentlyActive;

    // The Forge search bar is a live TMP field: a bare-Ctrl synthetic click landing on it opens
    // the game's on-screen keyboard over the mod screen. Ctrl has no mod meaning here — inert.
    public override bool UsesCtrlModifier => true;
    // Alt+T/F review keys — the bare-Alt right-click could pop a shop card's inspection window.
    public override bool SuppressesBareAlt => true;

    public override bool OnMove(Vector2 direction)
    {
        if (direction.y > 0f)
            CardCraftScreenManager.Move(-1);
        else if (direction.y < 0f)
            CardCraftScreenManager.Move(1);
        else if (direction.x < 0f)
            CardCraftScreenManager.MoveHorizontal(-1);
        else if (direction.x > 0f)
            CardCraftScreenManager.MoveHorizontal(1);
        return true;
    }

    public override bool OnConfirm()
    {
        CardCraftScreenManager.Activate();
        return true;
    }

    public override bool OnCancel()
    {
        return CardCraftScreenManager.Cancel();
    }

    public override bool OnTab(bool backwards)
    {
        CardCraftScreenManager.CycleRegion(backwards);
        return true;
    }

    public override bool OnNumber(int n)
    {
        CardCraftScreenManager.SelectHeroSlot(n - 1);
        return true;
    }
}
