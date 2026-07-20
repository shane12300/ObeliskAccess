using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Cards;

namespace ObeliskAccess.Patches;

/// <summary>
/// Shared card-to-speech helpers used by every screen that reads cards aloud (combat, town
/// services). Centralises the markup handling the combat layer pioneered: game descriptions carry
/// keyword words only as <c>&lt;sprite name=X&gt;</c> icon tags, so those must be recovered as words
/// before rich-text stripping or the spoken text has holes.
/// </summary>
internal static class CardSpeech
{
    private static readonly Regex _brTag = new Regex(@"<br\s*/?>", RegexOptions.Compiled);
    private static readonly Regex _spriteTag = new Regex(@"<sprite name=([^>/ ]+)[^>]*>", RegexOptions.Compiled);

    /// <summary>Recover <c>&lt;sprite name=X&gt;</c> keyword words before tags are stripped, else they vanish.</summary>
    public static string CleanDescription(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return _spriteTag.Replace(raw, m =>
        {
            string key = m.Groups[1].Value;
            string word = Texts.Instance != null ? Texts.Instance.GetText(key) : key;
            return string.IsNullOrEmpty(word) ? key : word;
        });
    }

    /// <summary>Split markup into logical lines (mirrors the tutorial popup's line walker).</summary>
    public static List<string> SplitLines(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(raw))
            return result;

        string withBreaks = _brTag.Replace(raw, "\n");
        foreach (var segment in withBreaks.Split('\n'))
        {
            string clean = AccessibleMenuBase.StripRichText(segment);
            if (clean.Length > 0)
                result.Add(clean);
        }
        return result;
    }

    /// <summary>Sprite-tag recovery followed by a full strip: one-line clean text.</summary>
    public static string CleanFlat(string raw)
        => AccessibleMenuBase.StripRichText(CleanDescription(raw));

    public static string UpgradeWord(Enums.CardUpgraded u)
    {
        switch (u)
        {
            case Enums.CardUpgraded.A: return "upgraded A";
            case Enums.CardUpgraded.B: return "upgraded B";
            case Enums.CardUpgraded.Rare: return "rare upgrade";
            default: return "";
        }
    }

    /// <summary>
    /// One navigation line: "Fireball, 2 energy, uncommon, upgraded A". Items skip the energy
    /// clause (equipment has no cast cost).
    /// </summary>
    public static string BriefLine(CardRealtimeData cd)
    {
        if (cd == null)
            return "Unknown card";

        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(cd.CardName));
        if (cd.CardClass != Enums.CardClass.Item)
            sb.Append(", ").Append(cd.EnergyCost).Append(" energy");
        sb.Append(", ").Append(cd.CardRarity.ToString().ToLowerInvariant());

        string up = UpgradeWord(cd.CardUpgraded);
        if (up.Length > 0)
            sb.Append(", ").Append(up);
        return sb.ToString();
    }

    /// <summary>
    /// The Alt+T read: brief line, target, full description, then expanded keywords. Single string,
    /// clauses joined with ". ".
    /// </summary>
    public static string FullDetail(CardRealtimeData cd)
    {
        if (cd == null)
            return "No card details";

        var parts = new List<string> { BriefLine(cd) };

        string target = AccessibleMenuBase.StripRichText(cd.Target);
        if (!string.IsNullOrEmpty(target))
            parts.Add(target);

        var lines = SplitLines(CleanDescription(cd.DescriptionNormalized));
        if (lines.Count > 0)
            parts.Add(string.Join(". ", lines.ToArray()));

        string keynotes = KeynoteDetail(cd);
        if (keynotes.Length > 0)
            parts.Add(keynotes);

        return string.Join(". ", parts.ToArray());
    }

    /// <summary>
    /// Expanded keyword glossary: "Burn: takes damage each turn. Chill: ...". Empty string when the
    /// card has none (callers speak their own fallback if they need one).
    /// </summary>
    public static string KeynoteDetail(CardRealtimeData cd)
    {
        var kn = cd != null ? cd.KeyNotes : null;
        if (kn == null || kn.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var k in kn)
        {
            if (k == null)
                continue;
            string n = AccessibleMenuBase.StripRichText(k.KeynoteName);
            string d = CleanFlat(k.Description);
            if (string.IsNullOrEmpty(n) && string.IsNullOrEmpty(d))
                continue;
            parts.Add(string.IsNullOrEmpty(d) ? n : n + ": " + d);
        }
        return string.Join(". ", parts.ToArray());
    }
}
