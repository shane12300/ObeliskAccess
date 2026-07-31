using ObeliskAccess.Patches;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ObeliskAccess.Input;

/// <summary>
/// Frame poller for the hero-selection family's Alt review hotkeys (unbound letters the
/// InputAction system never fires for) and its three lifecycle ticks. The ticks run outside the
/// active-context gate on purpose: arrival detection, the Begin-button edge, multiplayer ready
/// changes, and mouse-close detection for the character window and perk tree must all survive a
/// modal (alert, tutorial, settings, madness/sandbox panel) owning input.
///
/// Modal contexts are checked first with early returns, mirroring <see cref="CombatHotkeyPoller"/>:
/// perk tree (Alt+T node tooltip, Alt+I points summary, Alt+R repeat), then character window
/// (Alt+T row detail, Alt+I headline, Alt+R), then the main screen (Alt+T hero sheet,
/// Alt+C character window, Alt+I overview, Alt+R).
/// </summary>
public class HeroSelectionHotkeyPoller : MonoBehaviour
{
    public IInputContext HeroSelectionContext;
    public IInputContext CharPopupContext;
    public IInputContext PerkTreeContext;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // Lifecycle ticks — deliberately outside the input gate (cheap no-ops off-scene).
        HeroSelectionScreenManager.Tick();
        CharPopupScreenManager.Tick();
        PerkTreeScreenManager.Tick();

        if (InputRouter.IsActive(PerkTreeContext))
        {
            if (!InputRouter.AltHeld)
                return;
            if (kb.tKey.wasPressedThisFrame) PerkTreeScreenManager.SpeakNodeDetail();
            else if (kb.iKey.wasPressedThisFrame) PerkTreeScreenManager.SpeakPointsSummary();
            else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
            return;
        }

        if (InputRouter.IsActive(CharPopupContext))
        {
            if (!InputRouter.AltHeld)
                return;
            if (kb.tKey.wasPressedThisFrame) CharPopupScreenManager.SpeakRowDetail();
            else if (kb.iKey.wasPressedThisFrame) CharPopupScreenManager.SpeakHeadline();
            else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
            return;
        }

        if (!InputRouter.IsActive(HeroSelectionContext))
            return;

        if (!InputRouter.AltHeld)
            return;

        if (kb.tKey.wasPressedThisFrame) HeroSelectionScreenManager.SpeakHeroSheet();
        else if (kb.cKey.wasPressedThisFrame) HeroSelectionScreenManager.OpenCharacterWindow();
        else if (kb.iKey.wasPressedThisFrame) HeroSelectionScreenManager.SpeakOverview();
        else if (kb.rKey.wasPressedThisFrame) SpeechManager.RepeatLast();
    }
}
