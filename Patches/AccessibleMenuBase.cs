using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

public abstract class AccessibleMenuBase
{
    private static readonly Regex _richTextTag = new Regex(@"<[^>]*>", RegexOptions.Compiled);

    public static string StripRichText(string text)
    {
        text = _richTextTag.Replace(text, " ");
        text = text.Replace("\n", " ").Replace("\r", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    protected static string GetMenuItemText(Transform item)
    {
        string raw = null;

        var botonGameMode = item.GetComponentInChildren<BotonMenuGameMode>();
        if (botonGameMode != null)
        {
            // The visible label (optionText) is a stylized WarpText and is usually empty/placeholder,
            // so resolve the real mode name from the localized text keyed by gameMode.
            raw = GetGameModeName(botonGameMode.gameMode);
            if (string.IsNullOrEmpty(raw) && botonGameMode.optionText != null)
                raw = botonGameMode.optionText.text;
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
    /// Resolves a game-mode button's display name from its <c>gameMode</c> id via localization.
    /// The values (0/2/4/6) are confirmed by <c>MainMenuManager.ShowSaveGame</c> lock checks and
    /// the <c>SaveManager</c> slot→mode mapping. Returns null for unknown ids.
    /// </summary>
    protected static string GetGameModeName(int gameMode)
    {
        if (Texts.Instance == null)
            return null;

        switch (gameMode)
        {
            case 0: return Texts.Instance.GetText("modeAdventure");
            case 2: return Texts.Instance.GetText("modeObelisk");
            case 4: return Texts.Instance.GetText("modeWeekly");
            case 6: return Texts.Instance.GetText("singularity");
            default: return null;
        }
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

    protected static void AnnounceItem(Transform item)
    {
        SpeechManager.Speak(GetMenuItemText(item));
    }

    protected static void InvokeItemButton(Transform item)
    {
        var button = item.GetComponent<Button>();
        button?.onClick.Invoke();
    }
}
