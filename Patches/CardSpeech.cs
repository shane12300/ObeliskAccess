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

    /// <summary>Split markup into logical lines (kept as a wrapper for existing callers).</summary>
    public static List<string> SplitLines(string raw)
        => AccessibleMenuBase.SplitLines(raw);

    /// <summary>Sprite-tag recovery followed by a full strip: one-line clean text.</summary>
    public static string CleanFlat(string raw)
        => AccessibleMenuBase.StripRichText(CleanDescription(raw));

    /// <summary>
    /// Spoken card name from a card id OR a loot id ("cardId_N" — the game splits loot ids on '_',
    /// so plain card ids never contain one and the trim is a safe no-op for them). Falls back to
    /// the id itself for unknown cards.
    /// </summary>
    public static string CardName(string idOrLootId)
    {
        string cardId = idOrLootId;
        int cut = cardId != null ? cardId.LastIndexOf('_') : -1;
        if (cut >= 0 && int.TryParse(cardId.Substring(cut + 1), out _))
            cardId = cardId.Substring(0, cut);
        var cd = Globals.Instance != null ? Globals.Instance.GetCardData(cardId, instantiate: false) : null;
        return cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : cardId;
    }

    /// <summary>
    /// Spoken aura/curse name: stripped ACName, falling back to the id when the name is empty.
    /// Returns "" for null data (unknown aura id) so callers can supply their own fallback.
    /// </summary>
    public static string AuraName(AuraCurseData ac)
    {
        if (ac == null)
            return "";
        string name = AccessibleMenuBase.StripRichText(ac.ACName);
        return string.IsNullOrEmpty(name) ? ac.Id : name;
    }

    /// <summary>The five gear slots, in the armory's display order.</summary>
    public static readonly Enums.CardType[] EquipSlots =
    {
        Enums.CardType.Weapon, Enums.CardType.Armor, Enums.CardType.Jewelry,
        Enums.CardType.Accesory, Enums.CardType.Pet,
    };

    /// <summary>Spoken slot word for a gear card type; null for non-equipment types.</summary>
    public static string SlotWord(Enums.CardType type)
    {
        switch (type)
        {
            case Enums.CardType.Weapon: return "weapon";
            case Enums.CardType.Armor: return "armor";
            case Enums.CardType.Jewelry: return "jewelry";
            case Enums.CardType.Accesory: return "accessory";
            case Enums.CardType.Pet: return "pet";
            default: return null;
        }
    }

    /// <summary>The card id currently equipped in the hero's slot of the given type ("" if none/not gear).</summary>
    public static string EquippedId(Hero hero, Enums.CardType type)
    {
        switch (type)
        {
            case Enums.CardType.Weapon: return hero.Weapon;
            case Enums.CardType.Armor: return hero.Armor;
            case Enums.CardType.Jewelry: return hero.Jewelry;
            case Enums.CardType.Accesory: return hero.Accesory;
            case Enums.CardType.Pet: return hero.Pet;
            default: return "";
        }
    }

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
    /// clause (equipment has no cast cost). <paramref name="liveCost"/> overrides the printed cost
    /// with the in-combat final cost (reductions/exhaustion applied) when the caller has it.
    /// </summary>
    public static string BriefLine(CardRealtimeData cd, int? liveCost = null)
    {
        if (cd == null)
            return "Unknown card";

        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(cd.CardName));
        if (cd.CardClass != Enums.CardClass.Item)
            sb.Append(", ").Append(liveCost ?? cd.EnergyCost).Append(" energy");
        sb.Append(", ").Append(cd.CardRarity.ToString().ToLowerInvariant());

        string up = UpgradeWord(cd.CardUpgraded);
        if (up.Length > 0)
            sb.Append(", ").Append(up);
        return sb.ToString();
    }

    /// <summary>
    /// Card type plus aux types ("attack, ranged attack"), as the card face and hover popup show
    /// them. Empty for equipment items (the game hides the type row) and typeless cards.
    /// </summary>
    public static string TypeLine(CardRealtimeData cd)
    {
        if (cd == null || cd.CardClass == Enums.CardClass.Item || cd.CardType == Enums.CardType.None)
            return "";

        var names = new List<string> { TypeName(cd.CardType) };
        var aux = cd.CardTypeAux;
        if (aux != null)
        {
            foreach (var t in aux)
            {
                if (t != Enums.CardType.None)
                    names.Add(TypeName(t));
            }
        }
        return string.Join(", ", names.ToArray());
    }

    private static string TypeName(Enums.CardType t)
    {
        string s = Texts.Instance != null ? Texts.Instance.GetText(t.ToString()) : null;
        return string.IsNullOrEmpty(s) ? t.ToString() : AccessibleMenuBase.StripRichText(s);
    }

    /// <summary>
    /// The on-card requirement label ("Requires Stanza") the game renders in its own text slot,
    /// outside the description. Empty when the card requires nothing.
    /// </summary>
    public static string RequireLine(CardRealtimeData cd)
    {
        // The game only calls GetRequireText for hero-class cards (monster cards reuse the slot
        // for priority text; items hide it).
        if (cd == null || cd.CardClass == Enums.CardClass.Monster || cd.CardClass == Enums.CardClass.Item)
            return "";
        return CleanFlat(cd.GetRequireText());
    }

    /// <summary>
    /// Why the spoken cost differs from the printed one (mirrors the hover popup's cost block:
    /// reductions, zero-cost effects, Exhaustion). Empty outside combat or when unmodified.
    /// </summary>
    public static string CostModLine(CardRealtimeData cd)
    {
        if (cd == null || cd.CardClass == Enums.CardClass.Item)
            return "";

        var parts = new List<string>();
        if (cd.EnergyReductionToZeroPermanent)
            parts.Add("cost reduced to 0");
        else if (cd.EnergyReductionToZeroTemporal)
            parts.Add("cost reduced to 0 until discarded");

        int reduction = cd.EnergyReductionPermanent + cd.EnergyReductionTemporal;
        if (reduction > 0)
            parts.Add("cost reduced by " + reduction
                + (cd.EnergyReductionPermanent == 0 ? " until discarded" : ""));

        if (cd.ExhaustCounter > 0)
            parts.Add("exhaustion increases cost by " + cd.ExhaustCounter);

        return string.Join(", ", parts.ToArray());
    }

    /// <summary>
    /// One line per related card ("Related card: Spark, 0 energy, common. Deal 3 lightning
    /// damage") — the full-card previews sighted players get beside the hovered card.
    /// </summary>
    public static List<string> RelatedCardLines(CardRealtimeData cd)
    {
        var result = new List<string>();
        if (cd == null || !cd.HaveRelatedCards || cd.RelatedCards == null || Globals.Instance == null)
            return result;

        foreach (var id in cd.RelatedCards)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            var rc = Globals.Instance.GetCardData(id, instantiate: false);
            if (rc == null)
                continue;
            var sb = new StringBuilder("Related card: ");
            sb.Append(BriefLine(rc));
            string desc = CleanFlat(rc.DescriptionNormalized);
            if (desc.Length > 0)
                sb.Append(". ").Append(desc);
            result.Add(sb.ToString());
        }
        return result;
    }

    /// <summary>
    /// Everything the game shows on and around a hovered card, as discrete lines in reading
    /// order: brief line; type + target; description (one line per sentence, including the
    /// "X equals ..." explainer); require; cost modifications; one line per keyword; one line
    /// per related card. The combat drill walks these; <see cref="FullDetail"/> joins them.
    /// </summary>
    public static List<string> DetailLines(CardRealtimeData cd, int? liveCost = null)
    {
        var lines = new List<string>();
        if (cd == null)
        {
            lines.Add("No card details");
            return lines;
        }

        lines.Add(BriefLine(cd, liveCost));

        string type = TypeLine(cd);
        string target = AccessibleMenuBase.StripRichText(cd.Target);
        if (type.Length > 0 && target.Length > 0)
            lines.Add(type + ", " + target);
        else if (type.Length > 0)
            lines.Add(type);
        else if (!string.IsNullOrEmpty(target))
            lines.Add(target);

        lines.AddRange(SplitLines(CleanDescription(cd.DescriptionNormalized)));

        string require = RequireLine(cd);
        if (require.Length > 0)
            lines.Add(require);

        string costMod = CostModLine(cd);
        if (costMod.Length > 0)
            lines.Add(costMod);

        lines.AddRange(KeynoteLines(cd));
        lines.AddRange(RelatedCardLines(cd));
        return lines;
    }

    /// <summary>
    /// The Alt+T read: <see cref="DetailLines"/> as one interruptible utterance, clauses joined
    /// with ". ".
    /// </summary>
    public static string FullDetail(CardRealtimeData cd, int? liveCost = null)
        => string.Join(". ", DetailLines(cd, liveCost).ToArray());

    /// <summary>One glossary line per keyword: "Burn: takes damage each turn".</summary>
    public static List<string> KeynoteLines(CardRealtimeData cd)
    {
        var result = new List<string>();
        var kn = cd != null ? cd.KeyNotes : null;
        if (kn == null)
            return result;

        foreach (var k in kn)
        {
            if (k == null)
                continue;
            string n = AccessibleMenuBase.StripRichText(k.KeynoteName);
            string d = CleanFlat(k.Description);
            if (string.IsNullOrEmpty(n) && string.IsNullOrEmpty(d))
                continue;
            result.Add(string.IsNullOrEmpty(d) ? n : n + ": " + d);
        }
        return result;
    }
}
