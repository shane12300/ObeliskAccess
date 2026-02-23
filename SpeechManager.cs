using UnityEngine;

namespace ObeliskAccess;

public static class SpeechManager
{
    public static void Speak(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }
}
