using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

public abstract class AccessibleMenuBase
{
    private static readonly Regex _richTextTag = new Regex(@"<[^>]*>", RegexOptions.Compiled);

    private static string StripRichText(string text)
    {
        text = _richTextTag.Replace(text, " ");
        text = text.Replace("\n", " ").Replace("\r", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    protected static string GetMenuItemText(Transform item)
    {
        string raw = null;

        var botonGameMode = item.GetComponentInChildren<BotonMenuGameMode>();
        if (botonGameMode != null && botonGameMode.optionText != null)
            raw = botonGameMode.optionText.text;

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
