using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ObeliskAccess.Patches;

public abstract class AccessibleMenuBase
{
    protected static string GetMenuItemText(Transform item)
    {
        var boton = item.GetComponent<BotonGeneric>();
        if (boton != null && boton.text != null)
            return boton.text.text;

        var menuButton = item.GetComponent<MenuButton>();
        if (menuButton != null && menuButton.buttonText != null)
            return menuButton.buttonText.text;

        var tmp = item.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
            return tmp.text;

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
