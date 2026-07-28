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
    /// <summary>In MP only the master answers the prompt: the game disables both reward buttons
    /// and the accept box on other clients (showing an "only the host" banner) — but the public
    /// MapManager wrappers the mod drives have no such gate, so without this check a non-master
    /// would mutate purely-local state while the mod narrates false "accepted" outcomes (and a
    /// stray CorruptionContinue could even silently decline for the whole party).</summary>
    private static bool NonMasterMp
        => MpSpeech.IsMp && NetworkManager.Instance != null && !NetworkManager.Instance.IsMaster();

    /// <summary>The prompt GameObject activates BEFORE the MP draw barrier fills the labels in
    /// (DrawCorruptionCo waits on all players); acting in that window silently declines the
    /// corruption for everyone before any client has even seen it.</summary>
    private static bool Drawn
    {
        get
        {
            var c = MapManager.Instance?.corruption;
            if (c == null)
                return false;
            if (c.corruptionOnlyMaster != null && c.corruptionOnlyMaster.gameObject.activeSelf)
                return true; // non-master view: banner up = drawn
            return RewardText(c.rewardBotA).Length > 0 || RewardText(c.rewardBotB).Length > 0;
        }
    }

    /// <summary>Spoken once when the prompt opens (driven by the hotkey poller's edge detection).
    /// Returns false while the prompt exists but is not drawn yet — the poller retries.</summary>
    public static bool AnnounceSummary()
    {
        var c = MapManager.Instance?.corruption;
        if (c == null)
            return false;
        if (!Drawn)
            return false;
        var sb = new StringBuilder();
        sb.Append("Corruption offer. ");
        if (c.textDifficulty != null)
            sb.Append("Difficulty ").Append(Strip(c.textDifficulty.text)).Append(". ");
        AppendReward(sb, "Reward A: ", c.rewardBotA);
        AppendReward(sb, "Reward B: ", c.rewardBotB);
        if (NonMasterMp)
            sb.Append("Only the host chooses the corruption — waiting for their answer.");
        else
            sb.Append("Left or Right chooses a reward and accepts, Enter confirms; confirm without choosing to decline.");
        SpeechManager.Speak(sb.ToString());
        return true;
    }

    private static bool RefuseIfNotChoosable()
    {
        if (!Drawn)
        {
            SpeechManager.Speak("The corruption offer is still appearing.");
            return true;
        }
        if (NonMasterMp)
        {
            SpeechManager.Speak("Only the host chooses the corruption.");
            return true;
        }
        return false;
    }

    public static void ChooseA()
    {
        if (RefuseIfNotChoosable())
            return;
        MapManager.Instance?.CorruptionSelectReward("A");
        SpeechManager.Speak("Reward A chosen, corruption accepted: " + RewardText(MapManager.Instance?.corruption?.rewardBotA));
    }

    public static void ChooseB()
    {
        if (RefuseIfNotChoosable())
            return;
        MapManager.Instance?.CorruptionSelectReward("B");
        SpeechManager.Speak("Reward B chosen, corruption accepted: " + RewardText(MapManager.Instance?.corruption?.rewardBotB));
    }

    public static void ToggleAccept()
    {
        if (RefuseIfNotChoosable())
            return;
        MapManager.Instance?.CorruptionBox();
        var box = MapManager.Instance?.corruption?.corruptionBoxX;
        bool accepted = box != null && box.gameObject.activeSelf;
        SpeechManager.Speak(accepted ? "Corruption accepted" : "Corruption declined");
    }

    public static void Confirm()
    {
        if (RefuseIfNotChoosable())
            return;
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
