using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace ObeliskAccess.Patches;

public abstract class AccessibleMenuBase
{
    private static readonly Regex _richTextTag = new Regex(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex _brTag = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string StripRichText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = _richTextTag.Replace(text, " ");
        text = text.Replace("\n", " ").Replace("\r", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Splits game rich text into spoken lines: &lt;br&gt; tags and real newlines are the line
    /// boundaries, each segment is cleaned (default <see cref="StripRichText"/> — pass a custom
    /// cleaner to pre-convert sprite tags etc.), and empty segments are dropped. Splitting must
    /// happen before cleaning because StripRichText flattens newlines.
    /// </summary>
    public static List<string> SplitLines(string raw, Func<string, string> cleaner = null)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(raw))
            return lines;

        cleaner ??= StripRichText;
        foreach (var segment in _brTag.Replace(raw, "\n").Split('\n'))
        {
            var line = cleaner(segment);
            if (!string.IsNullOrEmpty(line))
                lines.Add(line);
        }
        return lines;
    }

    protected static string GetMenuItemText(Transform item)
    {
        string raw = null;

        var botonGameMode = ResolveGameModeButton(item);
        if (botonGameMode != null)
        {
            raw = GetGameModeName(botonGameMode.gameMode);
            if (!string.IsNullOrEmpty(raw))
            {
                // The chains overlay is advisory only — the game never enforces the rank lock
                // (a mouse click on a chained mode opens its save screen too), so speak the
                // chains' own "obeliskNeedCharacter" requirement text rather than "locked",
                // which would wrongly promise that Enter refuses.
                if (IsGameModeLocked(item))
                    raw += ", " + (GetTextOrNull("obeliskNeedCharacter") ?? "locked");

                var description = GetGameModeDescription(botonGameMode.gameMode);
                if (!string.IsNullOrEmpty(description))
                    raw += ". " + description;
            }
        }

        if (raw == null)
        {
            // Save slots: the label lives across several TMP fields (slotText holds
            // "Create new game" for empty slots or "Load game" for occupied ones; the
            // description holds the run summary). Read them in a sensible order.
            var save = item.GetComponentInChildren<MenuSaveButton>();
            if (save != null)
                raw = GetSaveSlotText(save);
        }

        if (raw == null)
        {
            var boton = item.GetComponent<BotonGeneric>();
            if (boton != null && boton.text != null)
                raw = boton.text.text;
        }

        if (raw == null)
        {
            var menuButton = item.GetComponent<MenuButton>();
            if (menuButton != null && menuButton.buttonText != null)
                raw = menuButton.buttonText.text;
        }

        if (raw == null)
        {
            var tmp = item.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                raw = tmp.text;
        }

        if (raw != null)
        {
            var stripped = StripRichText(raw);
            if (stripped.Length > 0)
                return stripped;
        }

        return item.name;
    }

    /// <summary>
    /// Maps a mode-selection controller item to its <see cref="BotonMenuGameMode"/>. The
    /// controller list holds UI cursor-warp proxies (<c>ButtonAM_UI</c> etc. — bare Image, no
    /// text, no components of interest); the real buttons are world-space objects the game
    /// reaches via <c>gameModeSelectionN.GetChild(0)</c>, so resolve through those fields the
    /// same way <c>SelectGameMode</c> does. Null off the mode-selection screen.
    /// </summary>
    public static BotonMenuGameMode ResolveGameModeButton(Transform item)
    {
        var direct = item.GetComponentInChildren<BotonMenuGameMode>();
        if (direct != null)
            return direct;

        var holder = GetGameModeHolder(item);
        return holder != null && holder.childCount > 0
            ? holder.GetChild(0).GetComponent<BotonMenuGameMode>()
            : null;
    }

    private static Transform GetGameModeHolder(Transform item)
    {
        var mgr = MainMenuManager.Instance;
        if (mgr == null || mgr.menuControllerModeSelection == null)
            return null;

        return mgr.menuControllerModeSelection.IndexOf(item) switch
        {
            0 => mgr.gameModeSelection0,
            1 => mgr.gameModeSelection1,
            2 => mgr.gameModeSelection2,
            3 => mgr.gameModeSelection3,
            _ => null,
        };
    }

    /// <summary>
    /// A locked mode keeps its controller item visible but shows a chains overlay; mirror that
    /// so the user knows why Enter does nothing. Adventure (index 0) is never locked.
    /// </summary>
    private static bool IsGameModeLocked(Transform item)
    {
        var mgr = MainMenuManager.Instance;
        if (mgr == null || mgr.menuControllerModeSelection == null)
            return false;

        Transform chains = mgr.menuControllerModeSelection.IndexOf(item) switch
        {
            1 => mgr.gameModeObeliskChains,
            2 => mgr.gameModeWeeklyChains,
            3 => mgr.gameModeSingularityChains,
            _ => null,
        };
        return chains != null && chains.gameObject.activeSelf;
    }

    /// <summary>
    /// Resolves a game-mode button's display name from its <c>gameMode</c> id via localization.
    /// The values (0/2/4/6) are confirmed by <c>MainMenuManager.SelectGameMode</c>'s index pairs
    /// (0/1 Adventure … 6/7 Singularity, MP adds 1) and by the scene data on the four
    /// <c>Button{AM,OM,WM,SG}</c> objects. Returns null for unknown ids.
    /// </summary>
    protected static string GetGameModeName(int gameMode)
    {
        switch (gameMode)
        {
            case 0: return GetTextOrNull("modeAdventure");
            case 2: return GetTextOrNull("modeObelisk");
            case 4: return GetTextOrNull("modeWeekly");
            case 6: return GetTextOrNull("singularity");
            default: return null;
        }
    }

    /// <summary>
    /// The mode's descriptive blurb — the same localized text the game shows in the hover /
    /// selection description panel (<c>SelectGameMode</c> uses these exact keys).
    /// </summary>
    private static string GetGameModeDescription(int gameMode)
    {
        switch (gameMode)
        {
            case 0: return GetTextOrNull("mainMenuAdventureDescription");
            case 2: return GetTextOrNull("mainMenuObeliskDescription");
            case 4: return GetTextOrNull("mainMenuWeeklyDescription");
            case 6: return GetTextOrNull("mainMenuSingularityDescription");
            default: return null;
        }
    }

    private static string GetTextOrNull(string key)
    {
        if (Texts.Instance == null)
            return null;
        var text = Texts.Instance.GetText(key);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Builds a spoken label for a save slot from its visible TMP fields (only those currently
    /// active and non-empty), joined with commas. Empty result falls back to the caller's default.
    /// </summary>
    protected static string GetSaveSlotText(MenuSaveButton save)
    {
        var sb = new StringBuilder();
        AppendField(sb, save.slotText);
        AppendField(sb, save.descriptionText);
        AppendField(sb, save.madnessText);
        AppendField(sb, save.playersText);
        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, TMP_Text field)
    {
        if (field == null || !field.gameObject.activeInHierarchy)
            return;

        var text = StripRichText(field.text);
        if (text.Length == 0)
            return;

        if (sb.Length > 0)
            sb.Append(", ");
        sb.Append(text);
    }

    public static void AnnounceItem(Transform item)
    {
        SpeechManager.Speak(GetMenuItemText(item));
    }
}
