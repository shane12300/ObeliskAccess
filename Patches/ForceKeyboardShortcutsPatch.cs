using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// Forces ConfigKeyboardShortcuts = true so that keyboard arrow-key navigation
/// is always active, regardless of the in-game setting.
/// Without this flag, InputController.DoMovement ignores keyboard input entirely.
/// </summary>
[HarmonyPatch(typeof(SettingsManager), "LoadPrefs")]
public class ForceKeyboardShortcutsPatch
{
    static void Postfix()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ConfigKeyboardShortcuts = true;
    }
}
