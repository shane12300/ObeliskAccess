using System.Collections.Generic;
using System.Text;
using Cards;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// The end-of-run screen (scene "FinishRun", <see cref="FinishRunManager"/>): the score
/// breakdown, rewards, per-hero progression bars and the Main Menu button become a walkable row
/// list. Every row reads its values live from the manager's TMP fields, so the animating
/// progression bars always speak current numbers. The unlocked-cards popup that can open on
/// arrival is a sub-mode: Left/Right review the cards, Enter/Escape close it. Arrival (after
/// that popup closes) and the Main Menu button becoming available are announced from the
/// poller-driven <see cref="Tick"/>. Input is mapped by
/// <see cref="ObeliskAccess.Input.Contexts.FinishRunInputContext"/>; Alt+I / Alt+T / Alt+R live
/// in <see cref="ObeliskAccess.Input.FinishRunHotkeyPoller"/>.
/// </summary>
internal static class FinishRunScreenManager
{
    private static int _index;
    private static bool _arrivalAnnounced;
    private static bool _lastButtonEnabled = true;

    // Unlocked-cards popup sub-mode.
    private static readonly List<string> _cardIds = new List<string>();
    private static int _cardIndex;
    private static bool _cardsMode;

    public static bool CardsMode
    {
        get
        {
            var frm = FinishRunManager.Instance;
            return _cardsMode && frm != null
                && frm.characterWindow != null && frm.characterWindow.IsActive();
        }
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Per-frame upkeep from the poller, outside the input gate.</summary>
    public static void Tick()
    {
        var frm = FinishRunManager.Instance;
        if (frm == null)
        {
            if (_arrivalAnnounced || _cardsMode)
            {
                _arrivalAnnounced = false;
                _cardsMode = false;
                _cardIds.Clear();
                _index = 0;
                _lastButtonEnabled = true;
            }
            return;
        }

        // The unlocked-cards popup can also be closed with the mouse — notice and drop the mode.
        bool windowUp = frm.characterWindow != null && frm.characterWindow.IsActive();
        if (_cardsMode && !windowUp)
        {
            _cardsMode = false;
            _cardIds.Clear();
        }

        // Arrival: everything is computed synchronously before the rewards block is shown, so
        // once rewardsT is active (and the card popup is out of the way) the summary is valid.
        if (!_arrivalAnnounced && !windowUp
            && frm.rewardsT != null && frm.rewardsT.gameObject.activeSelf)
        {
            _arrivalAnnounced = true;
            _index = 0;
            SpeechManager.Speak("Run over. " + Overview()
                + " Up and down to review; the Main Menu button is the last row.");
        }

        bool enabled = frm.mainMenuButton != null && frm.mainMenuButton.IsEnabled();
        if (enabled && !_lastButtonEnabled && _arrivalAnnounced)
            SpeechManager.SpeakQueued("Main menu available.");
        _lastButtonEnabled = enabled;
    }

    public static void OnUnlockedCardsShown(List<string> unlockedCards)
    {
        if (FinishRunManager.Instance == null)
            return;

        // Mirror DeckWindowUI.ShowUnlockedCards' filter: only cards that show in the tome.
        _cardIds.Clear();
        if (unlockedCards != null)
        {
            foreach (var id in unlockedCards)
            {
                var data = Globals.Instance.GetCardData(id, instantiate: false);
                if (data != null && data.ShowInTome)
                    _cardIds.Add(id);
            }
        }

        _cardIndex = 0;
        _cardsMode = true;

        var sb = new StringBuilder();
        sb.Append(_cardIds.Count).Append(_cardIds.Count == 1 ? " card" : " cards")
          .Append(" unlocked this run.");
        if (_cardIds.Count > 0)
        {
            sb.Append(" ").Append(CardSpeech.BriefLine(CardData(_cardIndex)))
              .Append(". Left and right to review, Enter to continue.");
        }
        else
        {
            sb.Append(" Enter to continue.");
        }
        SpeechManager.Speak(sb.ToString());
    }

    // ------------------------------------------------------------------ cards sub-mode

    private static CardRealtimeData CardData(int i)
        => i >= 0 && i < _cardIds.Count
            ? Globals.Instance.GetCardData(_cardIds[i], instantiate: false)
            : null;

    public static void MoveCard(int dir)
    {
        if (_cardIds.Count == 0)
            return;
        _cardIndex = Mathf.Clamp(_cardIndex + dir, 0, _cardIds.Count - 1);
        SpeechManager.Speak((_cardIndex + 1) + " of " + _cardIds.Count + ": "
            + CardSpeech.BriefLine(CardData(_cardIndex)));
    }

    public static void SpeakFocusedCardDetail()
    {
        var data = CardData(_cardIndex);
        SpeechManager.Speak(data != null ? CardSpeech.FullDetail(data) : "No card focused.");
    }

    public static void CloseCards()
    {
        var frm = FinishRunManager.Instance;
        if (frm != null && frm.characterWindow != null && frm.characterWindow.IsActive())
            frm.characterWindow.Hide();
        // Tick notices the window closing and drops the mode; the arrival announce follows.
    }

    // ------------------------------------------------------------------ rows

    private struct Row
    {
        public string Speech;
        public bool IsMainMenu;
    }

    /// <summary>Clean a TMP field's text, converting sprite icons (gold/dust) to words.</summary>
    private static string Clean(TMPro.TMP_Text t)
        => t != null ? CardSpeech.CleanFlat(t.text) : "";

    private static void AddScoreRow(List<Row> rows, string label, TMPro.TMP_Text count, TMPro.TMP_Text points)
    {
        rows.Add(new Row { Speech = label + ": " + Clean(count) + ", " + Clean(points) + " points" });
    }

    /// <summary>Rebuilt on every read so the animating progression numbers stay current.</summary>
    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();
        var frm = FinishRunManager.Instance;
        if (frm == null)
            return rows;

        rows.Add(new Row { Speech = Clean(frm.subHeader) });
        AddScoreRow(rows, "Places visited", frm.placesText, frm.placesTextGold);
        AddScoreRow(rows, "Combat expertise", frm.deathText, frm.deathTextGold);
        AddScoreRow(rows, "Hero deaths", frm.deathsText, frm.deathsTextGold);
        AddScoreRow(rows, "Experience gained", frm.experienceText, frm.experienceTextGold);
        AddScoreRow(rows, "Bosses defeated", frm.bossesText, frm.bossesTextGold);
        AddScoreRow(rows, "Corruptions completed", frm.corruptionsText, frm.corruptionsTextGold);

        if (frm.completedBlock != null && frm.completedBlock.gameObject.activeSelf)
            rows.Add(new Row { Speech = "Adventure completed: " + Clean(frm.completedTextGold) + " points" });

        var score = new StringBuilder();
        score.Append("Final score: ").Append(Clean(frm.finalScore));
        string madness = Clean(frm.finalScoreMadness);
        if (madness.Length > 0)
            score.Append(", ").Append(madness);
        if (frm.bestScore != null && frm.bestScore.gameObject.activeSelf)
            score.Append(". New best score!");
        string time = Clean(frm.playedTimeText);
        if (time.Length > 0)
            score.Append(" ").Append(time);
        rows.Add(new Row { Speech = score.ToString() });

        if (frm.rewardGroup != null && frm.rewardGroup.gameObject.activeSelf)
        {
            var reward = new StringBuilder();
            reward.Append("Run reward: ").Append(Clean(frm.gameScoreTextGold));
            string sub = Clean(frm.gameScoreSub);
            if (sub.Length > 0)
                reward.Append(", ").Append(sub);
            string mp = Clean(frm.mpBonus);
            if (mp.Length > 0)
                reward.Append(", ").Append(mp);
            rows.Add(new Row { Speech = reward.ToString() });

            if (frm.retentionTransform != null && frm.retentionTransform.gameObject.activeSelf)
                rows.Add(new Row
                {
                    Speech = "Retention " + Clean(frm.retentionText) + ": " + Clean(frm.retentionTextGold)
                });

            rows.Add(new Row { Speech = "Total reward: " + Clean(frm.totalTextGold) });
        }

        AddProgressionRow(rows, frm.fp0);
        AddProgressionRow(rows, frm.fp1);
        AddProgressionRow(rows, frm.fp2);
        AddProgressionRow(rows, frm.fp3);

        bool enabled = frm.mainMenuButton != null && frm.mainMenuButton.IsEnabled();
        rows.Add(new Row
        {
            Speech = "Main Menu, button" + (enabled ? "" : ", still tallying"),
            IsMainMenu = true,
        });

        return rows;
    }

    private static void AddProgressionRow(List<Row> rows, FinishProgression fp)
    {
        if (fp == null || !fp.IsActive())
            return;

        var sb = new StringBuilder();
        sb.Append(Clean(fp.charName)).Append(": ").Append(Clean(fp.charRank));
        sb.Append(", ").Append(Clean(fp.charProgress)).Append(" experience");
        string min = Clean(fp.charMin);
        string max = Clean(fp.charMax);
        if (min.Length > 0 && max.Length > 0)
            sb.Append(", level range ").Append(min).Append(" to ").Append(max);
        if (fp.charPoints != null && fp.charPoints.gameObject.activeSelf)
        {
            string points = Clean(fp.charPoints);
            if (points.Length > 0)
                sb.Append(", ").Append(points);
        }
        rows.Add(new Row { Speech = sb.ToString() });
    }

    // ------------------------------------------------------------------ navigation

    public static void MoveRow(int dir)
    {
        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Mathf.Clamp(_index + dir, 0, rows.Count - 1);
        SpeechManager.Speak(rows[_index].Speech);
    }

    public static void Activate()
    {
        var rows = BuildRows();
        if (rows.Count == 0)
            return;
        _index = Mathf.Clamp(_index, 0, rows.Count - 1);

        if (!rows[_index].IsMainMenu)
        {
            SpeechManager.Speak("Down to reach the Main Menu button.");
            return;
        }

        var frm = FinishRunManager.Instance;
        if (frm == null)
            return;
        if (frm.mainMenuButton == null || !frm.mainMenuButton.IsEnabled())
        {
            SpeechManager.Speak("Still tallying progression.");
            return;
        }

        // Not the warp+synthetic-click idiom: WarpCursorPosition is asynchronous, so the click's
        // raycast still saw the pre-warp cursor in the same frame — the first Enter missed (hover
        // sound only) and the second landed. The button's own click path needs no cursor at all.
        frm.mainMenuButton.Clicked();
    }

    public static string Overview()
    {
        var frm = FinishRunManager.Instance;
        if (frm == null)
            return "";

        var sb = new StringBuilder();
        sb.Append(Clean(frm.subHeader)).Append(". ");
        sb.Append("Final score ").Append(Clean(frm.finalScore)).Append(". ");
        string time = Clean(frm.playedTimeText);
        if (time.Length > 0)
            sb.Append(time).Append(". ");
        if (frm.rewardGroup != null && frm.rewardGroup.gameObject.activeSelf)
            sb.Append("Total reward ").Append(Clean(frm.totalTextGold)).Append(". ");
        bool enabled = frm.mainMenuButton != null && frm.mainMenuButton.IsEnabled();
        sb.Append(enabled ? "Main menu available." : "Progression still tallying.");
        return sb.ToString();
    }

    public static void SpeakOverview() => SpeechManager.Speak(Overview());
}

[HarmonyPatch(typeof(CharacterWindowUI), nameof(CharacterWindowUI.ShowUnlockedCards))]
public class FinishRunUnlockedCardsPatch
{
    static void Postfix(List<string> unlockedCards)
    {
        FinishRunScreenManager.OnUnlockedCardsShown(unlockedCards);
    }
}
