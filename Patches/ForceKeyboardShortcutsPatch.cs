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

/// <summary>
/// The in-game "Keyboard shortcuts" toggle also routes through here. Turning it off would kill
/// the game-Enter/cursor-warp paths several contexts ride (combat Enter, DoFirePerformed) until
/// the next LoadPrefs re-force — a half-dead input state with no feedback. Re-assert the config
/// and snap the toggle back on, so the option truthfully reads as always-on (the settings row's
/// label says so). Snapping isOn re-enters SetKeyboardShortcuts(true) once via onValueChanged,
/// which re-saves the pref as true; the second pass finds isOn already true and stops.
/// </summary>
[HarmonyPatch(typeof(SettingsManager), nameof(SettingsManager.SetKeyboardShortcuts))]
public class ForceKeyboardShortcutsOnSetPatch
{
    static void Postfix(SettingsManager __instance)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ConfigKeyboardShortcuts = true;
        if (__instance != null && __instance.keyboardShortcutsToggle != null
            && !__instance.keyboardShortcutsToggle.isOn)
            __instance.keyboardShortcutsToggle.isOn = true;
    }
}
