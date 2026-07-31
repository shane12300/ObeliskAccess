using System.Collections.Generic;
using System.Text;

namespace ObeliskAccess.Patches;

/// <summary>
/// Text builders shared by the hero-selection screen and the character window. Everything here
/// computes from game data (<c>SubClassData</c> + <c>PlayerManager</c>), mirroring the arithmetic
/// in <c>CharPopup.Init</c>, rather than scraping TMP fields — the popup is only populated for
/// the hero it last showed, but the roster needs these lines for any hero.
/// </summary>
internal static class HeroSpeech
{
    /// <summary>The portrait's subclass data. <c>HeroSelection.subClassData</c> is internal to
    /// the game assembly, but its public <c>Id</c> keys the same data in Globals.</summary>
    public static SubClassData ScdOf(HeroSelection hs)
        => hs == null || string.IsNullOrEmpty(hs.Id)
            ? null : Globals.Instance.GetSubClassData(hs.Id);

    // ---- focus sounds: the same clips the game plays when the mouse hovers each surface ----

    /// <summary>Roster portrait focus — the game's HeroSelection.OnMouseEnter sound.</summary>
    public static void PlayHeroFocusSound()
        => GameManager.Instance?.PlayLibraryAudio("castnpccardfast", 0.25f);

    /// <summary>Perk node focus — the game's PerkNode.OnMouseEnter sound.</summary>
    public static void PlayPerkFocusSound()
        => GameManager.Instance?.PlayLibraryAudio("ui_menu_popup_01");

    /// <summary>Button/row focus — the game's BotonGeneric.OnMouseEnter sound.</summary>
    public static void PlayButtonFocusSound()
    {
        if (GameManager.Instance != null && AudioManager.Instance != null)
            GameManager.Instance.PlayAudio(AudioManager.Instance.soundButtonHover);
    }

    /// <summary>
    /// Base stats + perk bonuses, mirroring CharPopup.Init's arithmetic (perks excluded in
    /// Obelisk mode, matching the game).
    /// </summary>
    public static string StatLine(SubClassData scd, bool obelisk)
    {
        var pm = PlayerManager.Instance;
        int hp = scd.Hp;
        int energy = scd.Energy;
        int speed = scd.Speed;
        if (!obelisk && pm != null)
        {
            hp += pm.GetPerkMaxHealth(scd.Id);
            energy += pm.GetPerkEnergyBegin(scd.Id);
            speed += pm.GetPerkSpeed(scd.Id);
        }
        return "Health " + hp + ", energy " + energy + " plus " + scd.EnergyTurn
            + " per turn, speed " + speed + ".";
    }

    /// <summary>Card row focus — same clip choice the loot/rewards screens mirror.</summary>
    public static void PlayCardFocusSound()
    {
        if (GameManager.Instance == null || AudioManager.Instance == null)
            return;
        if (GameManager.Instance.ConfigUseLegacySounds || AudioManager.Instance.soundCardHover == null)
            GameManager.Instance.PlayLibraryAudio("card_play");
        else
            GameManager.Instance.PlayAudio(AudioManager.Instance.soundCardHover, 0.1f);
    }

    /// <summary>Class membership as words, e.g. "Warrior" or "Mage and Warrior" — the visual UI
    /// shows these only as icon sprites.</summary>
    public static string ClassWords(SubClassData scd)
    {
        if (scd == null)
            return "";
        var parts = new List<string>();
        if (scd.HeroClass != Enums.HeroClass.None)
            parts.Add(ClassWord(scd.HeroClass));
        if (scd.HeroClassSecondary != Enums.HeroClass.None)
            parts.Add(ClassWord(scd.HeroClassSecondary));
        if (scd.HeroClassThird != Enums.HeroClass.None)
            parts.Add(ClassWord(scd.HeroClassThird));
        return string.Join(" and ", parts);
    }

    private static string ClassWord(Enums.HeroClass c)
        => c == Enums.HeroClass.MagicKnight ? "Magic Knight" : c.ToString();

    /// <summary>The subclass display name ("Sentinel", "Elementalist"), localized.</summary>
    public static string SubclassWord(SubClassData scd)
        => scd == null ? "" : AccessibleMenuBase.StripRichText(
            Functions.UppercaseFirst(Texts.Instance.GetText(scd.SubClassName)));

    /// <summary>A seed read out letter by letter ("B R O N N T H E H E R O" style), so character
    /// order is unambiguous to a listener.</summary>
    public static string SpellSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed))
            return "not set";
        var sb = new StringBuilder();
        foreach (char c in seed.Trim())
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>One-line roster announcement for a hero: name, subclass, classes, rank, unspent
    /// perk points, and lock / picked state.</summary>
    public static string HeroLine(HeroSelection hs)
    {
        if (hs == null || ScdOf(hs) == null)
            return "Unknown hero";
        var scd = ScdOf(hs);
        var sb = new StringBuilder();
        sb.Append(scd.CharacterName);
        string subclass = SubclassWord(scd);
        if (subclass != "")
            sb.Append(", ").Append(subclass);
        string classes = ClassWords(scd);
        if (classes != "")
            sb.Append(", ").Append(classes);
        bool obelisk = GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge();
        if (!obelisk && !hs.blocked && !hs.DlcBlocked)
        {
            sb.Append(". Rank ").Append(PlayerManager.Instance.GetPerkRank(hs.Id));
            if (!GameManager.Instance.IsLoadingGame())
            {
                int points = PlayerManager.Instance.GetPerkPointsAvailable(hs.Id);
                if (points > 0)
                    sb.Append(", ").Append(points).Append(points == 1 ? " perk point to spend" : " perk points to spend");
            }
        }
        if (hs.blocked || hs.DlcBlocked)
            sb.Append(". ").Append(LockReason(hs));
        else if (hs.HeroPicked)
            sb.Append(". ").Append(PickedLine(hs));
        return sb.ToString();
    }

    /// <summary>Why a roster hero can't be picked ("Locked — requires the … DLC").</summary>
    public static string LockReason(HeroSelection hs)
    {
        var popup = hs != null && hs.lockIcon != null ? hs.lockIcon.GetComponent<PopupText>() : null;
        if (popup != null)
        {
            if (!string.IsNullOrEmpty(popup.text))
                return "Locked — " + CardSpeech.CleanFlat(popup.text);
            if (!string.IsNullOrEmpty(popup.id))
                return "Locked — " + CardSpeech.CleanFlat(Texts.Instance.GetText(popup.id));
        }
        return "Locked — not yet unlocked";
    }

    /// <summary>"In the party, slot 2" (with the owner's name in multiplayer).</summary>
    public static string PickedLine(HeroSelection hs)
    {
        var hsm = HeroSelectionManager.Instance;
        if (hsm == null)
            return "In the party";
        for (int i = 0; i < 4; i++)
        {
            if (hsm.GetBoxHeroFromIndex(i) != hs)
                continue;
            if (MpSpeech.IsMp)
            {
                string owner = hsm.GetBoxOwnerFromIndex(i);
                if (!string.IsNullOrEmpty(owner) && owner != MpSpeech.LocalNick())
                    return "In " + MpSpeech.DisplayNick(owner) + "'s party, slot " + (i + 1);
            }
            return "In the party, slot " + (i + 1);
        }
        return "In the party";
    }

    /// <summary>The full Alt+T hero sheet: classes, description, stats with perk bonuses,
    /// non-zero resistances, traits with their unlock tiers, rank progress, unspent points.
    /// Mirrors the numbers CharPopup.Init computes.</summary>
    public static string HeroSheet(SubClassData scd)
    {
        if (scd == null)
            return "No hero data";
        var pm = PlayerManager.Instance;
        bool obelisk = GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge();
        string id = scd.Id;
        var sb = new StringBuilder();
        sb.Append(scd.CharacterName).Append(", ").Append(SubclassWord(scd));
        string classes = ClassWords(scd);
        if (classes != "")
            sb.Append(", ").Append(classes);
        sb.Append(". ");
        string description = CardSpeech.CleanFlat(Texts.Instance.GetText(id + "_description", "class"));
        if (description != "")
            sb.Append(description).Append(' ');
        string strength = CardSpeech.CleanFlat(Texts.Instance.GetText(id + "_strength", "class"));
        if (strength != "")
            sb.Append(strength).Append(' ');

        sb.Append(StatLine(scd, obelisk)).Append(' ');

        string resists = ResistLine(scd);
        sb.Append(resists).Append(' ');

        var traits = TraitLines(scd);
        if (traits.Count > 0)
            sb.Append(string.Join(". ", traits)).Append(". ");

        if (!obelisk)
        {
            int rank = pm.GetPerkRank(id);
            int progress = pm.GetProgress(id);
            int next = pm.GetPerkNextLevelPoints(id);
            sb.Append("Rank ").Append(rank);
            if (next > 0)
                sb.Append(", ").Append(progress).Append(" of ").Append(next).Append(" toward the next rank");
            sb.Append(". ");
            if (!GameManager.Instance.IsLoadingGame())
            {
                int points = pm.GetPerkPointsAvailable(id);
                if (points > 0)
                    sb.Append(points).Append(points == 1 ? " perk point to spend." : " perk points to spend.");
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Non-zero resistances with perk bonuses ("Resists: slashing 10%, fire 25%"), or
    /// "No resistances".</summary>
    public static string ResistLine(SubClassData scd)
    {
        bool obelisk = GameManager.Instance != null && GameManager.Instance.IsObeliskChallenge();
        var parts = new List<string>();
        AddResist(parts, "slashing", scd.ResistSlashing, scd.Id, Enums.DamageType.Slashing, obelisk);
        AddResist(parts, "blunt", scd.ResistBlunt, scd.Id, Enums.DamageType.Blunt, obelisk);
        AddResist(parts, "piercing", scd.ResistPiercing, scd.Id, Enums.DamageType.Piercing, obelisk);
        AddResist(parts, "fire", scd.ResistFire, scd.Id, Enums.DamageType.Fire, obelisk);
        AddResist(parts, "cold", scd.ResistCold, scd.Id, Enums.DamageType.Cold, obelisk);
        AddResist(parts, "lightning", scd.ResistLightning, scd.Id, Enums.DamageType.Lightning, obelisk);
        AddResist(parts, "mind", scd.ResistMind, scd.Id, Enums.DamageType.Mind, obelisk);
        AddResist(parts, "holy", scd.ResistHoly, scd.Id, Enums.DamageType.Holy, obelisk);
        AddResist(parts, "shadow", scd.ResistShadow, scd.Id, Enums.DamageType.Shadow, obelisk);
        return parts.Count == 0 ? "No resistances." : "Resists: " + string.Join(", ", parts) + ".";
    }

    private static void AddResist(List<string> parts, string word, int baseValue, string id,
        Enums.DamageType type, bool obelisk)
    {
        int value = baseValue;
        if (!obelisk)
            value += PlayerManager.Instance.GetPerkResistBonus(id, type);
        if (value != 0)
            parts.Add(word + " " + value + " percent");
    }

    /// <summary>One line per trait: "Trait at tier 1: Fury — locked, reached at rank tier 1".
    /// The game shows two traits per tier (A and B) from tier 1 up; tier 0 is always active.</summary>
    public static List<string> TraitLines(SubClassData scd)
    {
        var lines = new List<string>();
        int tier = PlayerManager.Instance.GetCharacterTier(scd.Id, "trait");
        AddTrait(lines, scd.Trait0, 0, tier);
        AddTrait(lines, scd.Trait1A, 1, tier);
        AddTrait(lines, scd.Trait1B, 1, tier);
        AddTrait(lines, scd.Trait2A, 2, tier);
        AddTrait(lines, scd.Trait2B, 2, tier);
        AddTrait(lines, scd.Trait3A, 3, tier);
        AddTrait(lines, scd.Trait3B, 3, tier);
        AddTrait(lines, scd.Trait4A, 4, tier);
        AddTrait(lines, scd.Trait4B, 4, tier);
        return lines;
    }

    private static void AddTrait(List<string> lines, TraitData trait, int requiredTier, int characterTier)
    {
        string name = TraitName(trait);
        if (name == "")
            return;
        string line = requiredTier == 0
            ? "Base trait: " + name
            : "Trait at tier " + requiredTier + ": " + name;
        if (requiredTier > characterTier)
            line += " — locked";
        lines.Add(line);
    }

    /// <summary>
    /// The trait's display name; card-granting traits read as the card's name — "the card X"
    /// when <paramref name="prefixCard"/> (roster/popup rows), bare for mid-sentence use
    /// (character-sheet level rows).
    /// </summary>
    public static string TraitName(TraitData trait, bool prefixCard = true)
    {
        if (trait == null)
            return "";
        trait.SetNameAndDescription();
        if (trait.TraitCard != null)
        {
            var card = Globals.Instance.GetCardData(trait.TraitCard.Id, instantiate: false);
            if (card != null)
                return (prefixCard ? "the card " : "") + CardSpeech.CleanFlat(card.CardName);
        }
        return CardSpeech.CleanFlat(trait.TraitName);
    }

    /// <summary>
    /// The trait's description text. Card-granting traits often carry an empty Description, so
    /// they read as the game's own "Adds X to the deck" line instead.
    /// </summary>
    public static string TraitDetail(TraitData trait)
    {
        if (trait == null)
            return "";
        trait.SetNameAndDescription();
        if (trait.TraitCard == null)
            return CardSpeech.CleanFlat(trait.Description);
        string cardName = TraitName(trait, prefixCard: false);
        string format = Texts.Instance != null ? Texts.Instance.GetText("traitAddCard") : "";
        return string.IsNullOrEmpty(format)
            ? "Adds " + cardName + " to the deck"
            : CardSpeech.CleanFlat(string.Format(format, cardName));
    }
}
