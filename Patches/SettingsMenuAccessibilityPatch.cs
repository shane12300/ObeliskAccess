using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

/// <summary>
/// Screen-reader navigation for the (otherwise mouse-only) settings menu.
///   Tab / Shift+Tab — switch between the Graphics / Audio / Gameplay / Accessibility tabs.
///                     (Accessibility is a mod-owned virtual tab holding ObeliskAccess options —
///                     it has no game UI panel behind it, so the visual panel keeps showing the
///                     previously selected game tab while it is focused.)
///   Up / Down       — move between the options in the current tab.
///   Left / Right    — adjust the focused slider.
///   Enter           — flip a toggle, press a button, or open a dropdown.
///   Alt+T           — tooltip for the focused option (currently the mod's own Accessibility
///                     options; game controls answer "No description available").
///   Escape          — game default (cancels an open dropdown; otherwise closes the panel).
/// The reset controls (Reset Tutorial / Reset Saved Data) open an AlertManager confirm dialog,
/// handled by the global alert dialogue (AlertInputContext outranks Settings).
/// </summary>
internal static class SettingsMenuManager
{
    private class Entry
    {
        public string Name;
        public Toggle Toggle;
        public Slider Slider;
        public TMP_Dropdown Dropdown;
        // Set for controls that behave as buttons (Reset Tutorial / Reset Saved Data): activating
        // them invokes this action instead of flipping a toggle or opening a dropdown.
        public Action Action;
        // Set for the mod's own options (Accessibility tab): a virtual toggle with no UI control
        // behind it, read and flipped through a BepInEx config entry.
        public ConfigEntry<bool> Option;
    }

    private static readonly string[] TabNames = { "Graphics", "Audio", "Gameplay", "Accessibility" };

    private static readonly List<Entry> _entries = new List<Entry>();
    private static int _tabIndex;
    private static int _index;
    private static bool _active;

    // Dropdown sub-mode: Enter opens a dropdown, Up/Down preview options, Enter applies, Escape cancels.
    private static bool _dropdownOpen;
    private static TMP_Dropdown _openDropdown;
    private static int _pendingOption;

    // The frame on which the panel opened. The single Enter that opens Settings reaches two game
    // input actions: DoFire opens the panel, then DoKeyBinding (routed by us as Confirm) arrives
    // when Settings is already active and would otherwise activate entry 0 — opening the resolution
    // dropdown. We ignore that leaked Enter by dropping Confirm on the frame we opened.
    private static int _openedFrame = -1;

    public static bool Active => _active;
    public static bool DropdownOpen => _dropdownOpen;

    public static void OnSettingsToggled(bool state)
    {
        if (state)
        {
            _active = true;
            _dropdownOpen = false;
            _openDropdown = null;

            // Remember the open frame so the leaked opening Enter (see _openedFrame) is ignored.
            _openedFrame = Time.frameCount;

            // ShowSettings always forces the Graphics tab visible but doesn't touch the private
            // actualTabIndex, so resync it before we drive SelectTab.
            Traverse.Create(SettingsManager.Instance).Field<int>("actualTabIndex").Value = 0;
            _tabIndex = 0;
            BuildControls();
            _index = 0;

            SpeechManager.Speak("Settings, " + TabNames[_tabIndex] + " tab. " + CurrentAnnouncement());
        }
        else if (_active)
        {
            _active = false;
            _dropdownOpen = false;
            _openDropdown = null;
            _openedFrame = -1;
            _entries.Clear();
            SpeechManager.Speak("Settings closed");
        }
    }

    public static void HandleArrow(Vector2 v)
    {
        if (_dropdownOpen)
        {
            if (v.y > 0f) PreviewOption(-1);
            else if (v.y < 0f) PreviewOption(1);
            return;
        }

        if (v.y > 0f) Move(-1);
        else if (v.y < 0f) Move(1);
        else if (v.x < 0f) AdjustSlider(-1);
        else if (v.x > 0f) AdjustSlider(1);
    }

    public static void HandleEnter()
    {
        // Ignore the Enter that opened Settings: it leaks in as a Confirm on the open frame and
        // would otherwise activate entry 0 (opening the resolution dropdown). A deliberate Enter
        // arrives on a later frame.
        if (Time.frameCount == _openedFrame)
            return;

        if (_dropdownOpen)
        {
            ApplyDropdown();
            return;
        }

        Activate();
    }

    public static void HandleTab(bool backwards)
    {
        if (_dropdownOpen)
            return;
        if (_entries == null)
            return;

        _tabIndex = (_tabIndex + (backwards ? TabNames.Length - 1 : 1)) % TabNames.Length;
        // The Accessibility tab is ours alone — the game only knows tabs 0-2, so don't SelectTab it.
        if (_tabIndex < 3)
            SettingsManager.Instance.SelectTab(_tabIndex);
        BuildControls();
        _index = 0;
        SpeechManager.Speak(TabNames[_tabIndex] + " tab. " + CurrentAnnouncement());
    }

    /// <summary>
    /// Alt+T: speaks the focused entry's tooltip. The mod's own options carry theirs in the
    /// BepInEx config description (single source with the .cfg comment); game controls have none.
    /// </summary>
    public static void SpeakTooltip()
    {
        if (!_active || _index < 0 || _index >= _entries.Count)
            return;

        var e = _entries[_index];
        string desc = e.Option != null ? e.Option.Description.Description : null;
        SpeechManager.Speak(string.IsNullOrEmpty(desc) ? "No description available" : desc);
    }

    /// <summary>Cancels an open dropdown (used by the Escape prefix). Returns true if it consumed.</summary>
    public static bool CancelDropdown()
    {
        if (!_dropdownOpen)
            return false;

        CloseDropdown();
        SpeechManager.Speak("Cancelled");
        return true;
    }

    // ---- internals ----

    private static void Move(int dir)
    {
        if (_entries.Count == 0)
            return;

        _index += dir;
        if (_index < 0) _index = 0;
        else if (_index >= _entries.Count) _index = _entries.Count - 1;

        SpeechManager.Speak(CurrentAnnouncement());
    }

    private static void Activate()
    {
        if (_index < 0 || _index >= _entries.Count)
            return;

        var e = _entries[_index];

        if (e.Action != null)
        {
            // Button-style controls (Reset Tutorial / Reset Saved Data) open an AlertConfirmDouble
            // dialog, which the global alert dialogue announces and drives — nothing to say here.
            e.Action();
        }
        else if (e.Toggle != null)
        {
            e.Toggle.isOn = !e.Toggle.isOn;
            SpeechManager.Speak(e.Name + ", " + (e.Toggle.isOn ? "on" : "off"));
        }
        else if (e.Option != null)
        {
            e.Option.Value = !e.Option.Value; // BepInEx saves the config file on set
            SpeechManager.Speak(e.Name + ", " + (e.Option.Value ? "on" : "off"));
        }
        else if (e.Dropdown != null)
        {
            OpenDropdown(e);
        }
        // Sliders are adjusted with Left/Right; Enter does nothing for them.
    }

    private static void AdjustSlider(int dir)
    {
        if (_index < 0 || _index >= _entries.Count)
            return;

        var e = _entries[_index];
        if (e.Slider == null)
            return;

        var s = e.Slider;
        float step = s.wholeNumbers ? 1f : (s.maxValue - s.minValue) / 20f;
        s.value = Mathf.Clamp(s.value + dir * step, s.minValue, s.maxValue);
        SpeechManager.Speak(e.Name + ", " + SliderText(s));
    }

    private static void OpenDropdown(Entry e)
    {
        _dropdownOpen = true;
        _openDropdown = e.Dropdown;
        _pendingOption = e.Dropdown.value;
        e.Dropdown.Show();
        SpeechManager.Speak(e.Name + ", " + OptionText() + ". Up and down to change, Enter to select.");
    }

    private static void PreviewOption(int dir)
    {
        if (_openDropdown == null || _openDropdown.options.Count == 0)
            return;

        _pendingOption += dir;
        if (_pendingOption < 0) _pendingOption = 0;
        else if (_pendingOption >= _openDropdown.options.Count) _pendingOption = _openDropdown.options.Count - 1;

        SpeechManager.Speak(OptionText());
    }

    private static void ApplyDropdown()
    {
        var dd = _openDropdown;
        string option = OptionText();
        if (dd != null)
        {
            dd.value = _pendingOption;
            dd.RefreshShownValue();
        }
        CloseDropdown();
        SpeechManager.Speak("Selected " + option);
    }

    private static void CloseDropdown()
    {
        _openDropdown?.Hide();
        _dropdownOpen = false;
        _openDropdown = null;
    }

    private static string OptionText()
    {
        if (_openDropdown == null || _pendingOption < 0 || _pendingOption >= _openDropdown.options.Count)
            return "";
        return AccessibleMenuBase.StripRichText(_openDropdown.options[_pendingOption].text)
             + ", " + (_pendingOption + 1) + " of " + _openDropdown.options.Count;
    }

    private static string CurrentAnnouncement()
    {
        if (_index < 0 || _index >= _entries.Count)
            return "";

        var e = _entries[_index];
        if (e.Action != null)
            return e.Name + ", button";
        if (e.Toggle != null)
            return e.Name + ", " + (e.Toggle.isOn ? "on" : "off");
        if (e.Option != null)
            return e.Name + ", " + (e.Option.Value ? "on" : "off");
        if (e.Slider != null)
            return e.Name + ", " + SliderText(e.Slider);
        if (e.Dropdown != null)
            return e.Name + ", " + CurrentDropdownOption(e.Dropdown);
        return e.Name;
    }

    private static string SliderText(Slider s)
    {
        int percent = Mathf.RoundToInt(Mathf.InverseLerp(s.minValue, s.maxValue, s.value) * 100f);
        return percent + " percent";
    }

    private static string CurrentDropdownOption(TMP_Dropdown dd)
    {
        if (dd.value < 0 || dd.value >= dd.options.Count)
            return "";
        return AccessibleMenuBase.StripRichText(dd.options[dd.value].text);
    }

    private static void BuildControls()
    {
        _entries.Clear();
        var s = SettingsManager.Instance;
        if (s == null)
            return;

        switch (_tabIndex)
        {
            case 0: // Graphics
                AddDropdown(s.resolutionDropdown, "Resolution");
                AddDropdown(s.languageDropdown, "Language");
                AddToggle(s.vsyncToggle, "V-Sync");
                AddToggle(s.fullscreenToggle, "Fullscreen");
                break;
            case 1: // Audio
                AddSlider(s.masterVolumeSlider, "Master volume");
                AddSlider(s.effectsVolumeSlider, "Effects volume");
                AddSlider(s.bsoVolumeSlider, "Music volume");
                AddSlider(s.ambienceVolumeSlider, "Ambience volume");
                AddToggle(s.backgroundMuteToggle, "Mute when in background");
                AddToggle(s.legacySoundsToggle, "Legacy sounds");
                AddToggle(s.legacySoundsSheepOwlToggle, "Legacy sheep and owl sounds");
                break;
            case 2: // Gameplay
                AddToggle(s.fastModeToggle, "Fast mode");
                AddToggle(s.autoEndToggle, "Auto end turn");
                AddToggle(s.showEffectsToggle, "Show effects");
                AddToggle(s.acbackgroundEffectsToggle, "Background effects");
                AddToggle(s.restartCombatOptionToggle, "Restart combat option");
                AddToggle(s.screenShakeOptionToggle, "Screen shake");
                AddToggle(s.keyboardShortcutsToggle, "Keyboard shortcuts");
                AddToggle(s.followingTheLeaderToggle, "Following the leader");
                AddToggle(s.extendedDescriptionsToggle, "Extended descriptions");

                // Reset Tutorial / Reset Saved Data are wired in-game as toggles, but they act as
                // buttons: flipping one opens an AlertConfirmDouble dialog and immediately snaps the
                // toggle back to off. Expose them as buttons that call the manager action directly,
                // and DON'T gate them on activeInHierarchy — the game leaves these controls' Game
                // objects inactive until needed, which was hiding Reset Tutorial entirely.
                if (s.resetTutorialToggle != null)
                {
                    s.resetTutorialToggle.gameObject.SetActive(true);
                    _entries.Add(new Entry { Name = "Reset tutorial", Action = s.ResetTutorial });
                }

                // Reset Saved Data lives in a container (resetSavedT) the game only enables at the
                // main menu; keep that rule (you can't wipe saves mid-run). Ensure the container is
                // live before exposing it.
                if (s.resetSavedToggle != null && SceneStatic.GetSceneName() == "MainMenu")
                {
                    if (s.resetSavedT != null)
                        s.resetSavedT.gameObject.SetActive(true);
                    _entries.Add(new Entry { Name = "Reset saved data", Action = s.ResetSavedData });
                }
                break;
            case 3: // Accessibility — the mod's own combat-narration options (virtual, no game UI)
                AddOption(AccessibilityOptions.CombineEnemyTurns, "Combine enemy turn announcements");
                AddOption(AccessibilityOptions.ShortStatusPhrasing, "Short status phrasing");
                AddOption(AccessibilityOptions.SkipRoutineDecay, "Skip routine status decay");
                AddOption(AccessibilityOptions.SkipEnemyStatusChanges, "Skip enemy status changes");
                AddOption(AccessibilityOptions.SpeakChat, "Speak chat messages");
                AddOption(AccessibilityOptions.DebugMode, "Debug mode");
                break;
        }
    }

    private static void AddOption(ConfigEntry<bool> option, string name)
    {
        if (option != null)
            _entries.Add(new Entry { Name = name, Option = option });
    }

    private static void AddToggle(Toggle t, string name)
    {
        if (t != null && t.gameObject.activeInHierarchy)
            _entries.Add(new Entry { Name = name, Toggle = t });
    }

    private static void AddSlider(Slider s, string name)
    {
        if (s != null && s.gameObject.activeInHierarchy)
            _entries.Add(new Entry { Name = name, Slider = s });
    }

    private static void AddDropdown(TMP_Dropdown d, string name)
    {
        if (d != null && d.gameObject.activeInHierarchy)
            _entries.Add(new Entry { Name = name, Dropdown = d });
    }
}

[HarmonyPatch(typeof(SettingsManager), nameof(SettingsManager.ShowSettings))]
public class SettingsOpenPatch
{
    static void Postfix(bool _state)
    {
        SettingsMenuManager.OnSettingsToggled(_state);
    }
}

