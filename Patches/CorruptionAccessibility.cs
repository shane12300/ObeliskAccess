using System.Text;
using HarmonyLib;

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
    /// (DrawCorruptionCo parks on a network sync before touching any UI); acting — or reading — in
    /// that window is wrong: the children still hold the PREVIOUS corruption's labels (or the
    /// prefab's placeholder text on the first one), so heuristics like "banner active" or "label
    /// non-empty" pass on stale state and the announce speaks the wrong rewards. In single-player
    /// the coroutine body has no yields, so the window never exists. The only reliable signal is
    /// the fill itself: <c>CorruptionText</c> is called exactly twice, only from DrawCorruptionCo's
    /// label-fill block, so a postfix on it marks the labels fresh (see patches below).</summary>
    private static bool Drawn => _labelsFilled;

    /// <summary>True once DrawCorruptionCo has built the reward labels for the CURRENT draw;
    /// cleared when a new draw starts (InitCorruption locally, DrawCorruptionFromNet via RPC).</summary>
    private static bool _labelsFilled;

    /// <summary>Bumped once per completed label fill. The map poller announces when it sees a
    /// serial it hasn't spoken yet — which also re-announces a master's NextCorruption re-roll
    /// (the prompt stays active across re-rolls, so an "announced once while open" edge misses
    /// them).</summary>
    internal static int FillSerial { get; private set; }

    internal static void OnDrawStarted() => _labelsFilled = false;

    internal static void OnLabelFilled()
    {
        if (_labelsFilled)
            return; // second CorruptionText call of the same fill
        _labelsFilled = true;
        FillSerial++;
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

// ======================= draw-lifecycle patches =======================
// A new draw begins either locally (InitCorruption — master, and every SP prompt; also the
// master's NextCorruption re-roll, which calls InitCorruption again while the prompt is open) or
// from the master's RPC (DrawCorruptionFromNet on non-masters). Both start DrawCorruptionCo, which
// in MP parks on a network barrier BEFORE touching the labels — so the fresh-draw mark must be
// cleared here, at the start, and only set again by the fill itself.

[HarmonyPatch(typeof(CorruptionManager), nameof(CorruptionManager.InitCorruption))]
internal static class CorruptionInitDrawPatch
{
    static void Prefix() => CorruptionAccessibility.OnDrawStarted();
}

[HarmonyPatch(typeof(CorruptionManager), nameof(CorruptionManager.DrawCorruptionFromNet))]
internal static class CorruptionNetDrawPatch
{
    static void Prefix() => CorruptionAccessibility.OnDrawStarted();
}

/// <summary>
/// <c>CorruptionText</c> (private) is called exactly twice, only from DrawCorruptionCo's label-fill
/// block — the first reliable moment the reward labels reflect the current draw. Both calls land in
/// the same synchronous run, so by the time the poller's next Update reads the labels, both are set.
/// </summary>
[HarmonyPatch(typeof(CorruptionManager), "CorruptionText")]
internal static class CorruptionLabelsFilledPatch
{
    static void Postfix() => CorruptionAccessibility.OnLabelFilled();
}
