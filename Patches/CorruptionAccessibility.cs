using System.Text;

namespace ObeliskAccess.Patches;

/// <summary>
/// Speech + actions for the pre-combat corruption prompt (<c>CorruptionManager</c>, reached through
/// <c>MapManager.Instance.corruption</c>). The corruption UI already exposes its choices through
/// public <c>MapManager</c> wrappers, so this just reads the visible labels and drives those wrappers.
///
/// Flow: choosing a reward (Left/Right) also accepts the corruption; confirming without choosing
/// declines it. The accept box state is mirrored by <c>corruptionBoxX</c> being shown.
/// </summary>
public static class CorruptionAccessibility
{
    /// <summary>Spoken once when the prompt opens (driven by the hotkey poller's edge detection).</summary>
    public static void AnnounceSummary()
    {
        var c = MapManager.Instance?.corruption;
        if (c == null)
            return;
        var sb = new StringBuilder();
        sb.Append("Corruption offer. ");
        if (c.textDifficulty != null)
            sb.Append("Difficulty ").Append(Strip(c.textDifficulty.text)).Append(". ");
        AppendReward(sb, "Reward A: ", c.rewardBotA);
        AppendReward(sb, "Reward B: ", c.rewardBotB);
        sb.Append("Left or Right chooses a reward and accepts, Enter confirms; confirm without choosing to decline.");
        SpeechManager.Speak(sb.ToString());
    }

    public static void ChooseA()
    {
        MapManager.Instance?.CorruptionSelectReward("A");
        SpeechManager.Speak("Reward A chosen, corruption accepted: " + RewardText(MapManager.Instance?.corruption?.rewardBotA));
    }

    public static void ChooseB()
    {
        MapManager.Instance?.CorruptionSelectReward("B");
        SpeechManager.Speak("Reward B chosen, corruption accepted: " + RewardText(MapManager.Instance?.corruption?.rewardBotB));
    }

    public static void ToggleAccept()
    {
        MapManager.Instance?.CorruptionBox();
        var box = MapManager.Instance?.corruption?.corruptionBoxX;
        bool accepted = box != null && box.gameObject.activeSelf;
        SpeechManager.Speak(accepted ? "Corruption accepted" : "Corruption declined");
    }

    public static void Confirm()
    {
        SpeechManager.Speak("Confirming");
        MapManager.Instance?.CorruptionContinue();
    }

    private static void AppendReward(StringBuilder sb, string label, BotonGeneric reward)
    {
        string text = RewardText(reward);
        if (text.Length > 0)
            sb.Append(label).Append(text).Append(". ");
    }

    private static string RewardText(BotonGeneric reward)
        => reward != null && reward.text != null ? Strip(reward.text.text) : "";

    private static string Strip(string text) => AccessibleMenuBase.StripRichText(text ?? "");
}
