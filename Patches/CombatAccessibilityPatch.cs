using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BattleMatch;
using HarmonyLib;
using ObeliskAccess.Input.Contexts;
using TMPro;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// Combat accessibility: announces the focused element as the player navigates (piggybacking the
/// game's <c>MatchManager.ControllerMovement</c>), announces turn changes and combat end, narrates
/// what happens (damage/heal/status via the floating-combat-text channel), and answers on-demand
/// review hotkeys. Mirrors the map layer's shape: a stateless <see cref="CombatInputContext"/> and a
/// <see cref="Input.CombatHotkeyPoller"/> delegate everything to this static navigator, which owns all
/// state + speech; Harmony patches at the bottom of the file feed it game events.
/// </summary>
public static class CombatNavigator
{
    private enum DrillMode { None, CardLines, CharCategory }

    // ---- focus dedupe ----
    private static int _lastIndex = -1;
    private static int _lastInstanceId;

    // ---- current focus + drill-in state ----
    private static Transform _focusedTransform;
    private static DrillMode _drill = DrillMode.None;
    private static readonly List<string> _cardLines = new List<string>();
    private static int _cardLineIndex;
    private static readonly List<string> _categories = new List<string>();
    private static int _catIndex;

    // ---- combat lifecycle ----
    private static int _lastRound = -1;
    private static bool _overviewAnnounced;

    // ---- combat-event coalescing ----
    private struct CtEntry { public string Owner; public string Text; }
    private static readonly List<CtEntry> _ctBuffer = new List<CtEntry>();
    private static float _lastCtTime;
    private const float CT_FLUSH_DELAY = 0.35f;

    private static readonly Regex _brTag = new Regex(@"<br\s*/?>", RegexOptions.Compiled);
    private static readonly Regex _spriteTag = new Regex(@"<sprite name=([^>/ ]+)[^>]*>", RegexOptions.Compiled);

    // ======================= lifecycle =======================

    /// <summary>Turn change (also fires for the first turn, so it doubles as combat-ready).</summary>
    public static void OnTurnChanged()
    {
        ResetFocus();

        var mm = MatchManager.Instance;
        if (mm == null)
            return;

        // A round counter that went backwards means we're in a new combat (one the previous combat's
        // FinishCombat may not have covered, e.g. a retreat) — re-arm the one-shot overview.
        if (mm.CurrentRound < _lastRound)
        {
            _lastRound = -1;
            _overviewAnnounced = false;
        }

        Character actor = mm.IsHeroTurn
            ? (Character)mm.CurrentHero
            : Traverse.Create(mm).Field<NPC>("theNPC").Value;

        var sb = new StringBuilder();
        int round = mm.CurrentRound;
        if (round != _lastRound)
        {
            sb.Append("Round ").Append(round).Append(". ");
            _lastRound = round;
        }

        string name = actor != null ? AccessibleMenuBase.StripRichText(actor.SourceName) : "";
        sb.Append(name).Append(mm.IsHeroTurn ? ", your turn" : "'s turn");

        // Queue (not interrupt): enemy turns come back-to-back, and an interrupting turn line would
        // cut off the previous action's event announcement mid-utterance. Flush pending events first
        // so they are heard before the turn line.
        FlushPendingEvents();
        SpeechManager.SpeakQueued(sb.ToString());

        // Once per combat, follow the first turn with a full battlefield overview.
        if (!_overviewAnnounced)
        {
            _overviewAnnounced = true;
            string overview = BuildBattlefield();
            if (overview.Length > 0)
                SpeechManager.SpeakQueued(overview);
        }
    }

    public static void OnCombatEnd(bool won)
    {
        FlushPendingEvents();
        SpeechManager.SpeakQueued(won ? "Victory" : "Defeat");
        ResetFocus();
        _overviewAnnounced = false;
        _lastRound = -1;
    }

    private static void ResetFocus()
    {
        _lastIndex = -1;
        _lastInstanceId = 0;
        _drill = DrillMode.None;
        _cardLines.Clear();
    }

    // ======================= focus reading =======================

    /// <summary>
    /// Called after <c>ControllerMovement</c>. <paramref name="userInitiated"/> is true only for a real
    /// keyboard arrow press (one direction flag set, no absoluteIndex); programmatic rebuilds (turn
    /// start, after cast, card pickup) pass false and are tracked silently so later dedupe stays correct.
    /// </summary>
    public static void OnControllerMoved(bool userInitiated)
    {
        if (!CombatInputContext.IsCurrentlyActive)
            return;

        var mm = MatchManager.Instance;
        if (mm == null)
            return;

        var list = Traverse.Create(mm).Field<List<Transform>>("controllerList").Value;
        if (list == null)
            return;

        int idx = mm.controllerCurrentIndex;
        if (idx < 0 || idx >= list.Count)
            return;

        Transform t = list[idx];
        if (t == null)
            return;

        int iid = t.GetInstanceID();
        _focusedTransform = t; // track current focus for drill-in regardless of how we got here

        if (!userInitiated)
        {
            // Track focus so a subsequent real move onto the same element is correctly deduped.
            _lastIndex = idx;
            _lastInstanceId = iid;
            return;
        }

        if (idx == _lastIndex && iid == _lastInstanceId)
            return; // hit a wall / re-warped to the same element

        _lastIndex = idx;
        _lastInstanceId = iid;

        // Moving focus ends any in-progress drill.
        _drill = DrillMode.None;
        _cardLines.Clear();

        string speech = DescribeFocus(t);
        if (!string.IsNullOrEmpty(speech))
            SpeechManager.Speak(speech);
    }

    private static string DescribeFocus(Transform t)
    {
        var card = t.GetComponent<CardItem>();
        if (card != null)
            return DescribeCard(card);

        var ci = t.GetComponentInParent<CharacterItem>();
        if (ci != null && ci.Character != null)
            return DescribeCharacter(ci.Character, ci);

        var icon = t.GetComponent<ItemCombatIcon>();
        if (icon != null)
            return DescribeIcon(icon);

        return FallbackLabel(t);
    }

    private static string DescribeCard(CardItem card)
    {
        var cd = card.CardData;
        if (cd == null)
            return card.name;

        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(cd.CardName));
        sb.Append(", cost ").Append(card.GetEnergyCost());

        string target = AccessibleMenuBase.StripRichText(cd.Target);
        if (!string.IsNullOrEmpty(target))
            sb.Append(", ").Append(target);

        if (!card.IsPlayableRightNow())
            sb.Append(", unplayable");

        return sb.ToString();
    }

    /// <summary>Balanced focus read: name, HP, block, a short status hint, and (enemies) intent-known.</summary>
    private static string DescribeCharacter(Character c, CharacterItem ci)
    {
        var sb = new StringBuilder();
        sb.Append(AccessibleMenuBase.StripRichText(c.SourceName));
        sb.Append(", ").Append(c.GetHp()).Append(" of ").Append(c.GetMaxHP());

        int block = c.GetBlock();
        if (block > 0)
            sb.Append(", block ").Append(block);

        CountStatuses(c, out int buffs, out int curses);
        if (buffs > 0)
            sb.Append(", ").Append(buffs).Append(buffs == 1 ? " buff" : " buffs");
        if (curses > 0)
            sb.Append(", ").Append(curses).Append(curses == 1 ? " curse" : " curses");

        if (!ci.IsHero && HasRevealedIntent(ci as NPCItem))
            sb.Append(", intent known");

        return sb.ToString();
    }

    private static string DescribeIcon(ItemCombatIcon icon)
    {
        Character c = icon.TheHero != null ? (Character)icon.TheHero : icon.TheNPC;
        string owner = c != null ? AccessibleMenuBase.StripRichText(c.SourceName) : "";
        return owner.Length > 0 ? owner + " item" : "item";
    }

    private static string FallbackLabel(Transform t)
    {
        if (t.GetComponent<BotonEndTurn>() != null)
            return "End turn";

        var tmp = t.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            string s = AccessibleMenuBase.StripRichText(tmp.text);
            if (s.Length > 0)
                return s;
        }
        return t.name;
    }

    // ======================= shared helpers =======================

    /// <summary>Counts buffs vs curses on a character, excluding the internal "block" aura.</summary>
    private static void CountStatuses(Character c, out int buffs, out int curses)
    {
        buffs = 0;
        curses = 0;
        var list = c.AuraCurseList;
        if (list == null)
            return;

        foreach (var a in list)
        {
            if (a == null || a.ACData == null || a.AuraCharges <= 0)
                continue;
            if (string.Equals(a.ACData.Id, "block", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (a.ACData.IsAura)
                buffs++;
            else
                curses++;
        }
    }

    private static bool HasRevealedIntent(NPCItem npc)
    {
        if (npc == null || npc.cardsCI == null)
            return false;
        foreach (var c in npc.cardsCI)
        {
            if (c != null && c.IsCardRevealed())
                return true;
        }
        return false;
    }

    private static string BuildBattlefield()
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return "";

        var sb = new StringBuilder();
        sb.Append("Heroes: ");
        AppendTeam(sb, mm.GetTeamHero());
        sb.Append(". Enemies: ");
        AppendTeam(sb, mm.GetTeamNPC());
        return sb.ToString();
    }

    private static void AppendTeam(StringBuilder sb, Team team)
    {
        if (team == null)
        {
            sb.Append("none");
            return;
        }

        bool any = false;
        for (int i = 0; i < team.Count; i++)
        {
            Character c = team[i];
            if (c == null || !c.Alive)
                continue;

            if (any)
                sb.Append("; ");
            any = true;

            sb.Append(AccessibleMenuBase.StripRichText(c.SourceName));
            sb.Append(' ').Append(c.GetHp()).Append('/').Append(c.GetMaxHP());
            int block = c.GetBlock();
            if (block > 0)
                sb.Append(" block ").Append(block);
        }

        if (!any)
            sb.Append("none");
    }

    // ======================= not yet implemented (later steps) =======================

    // ======================= combat events (CombatText) =======================
    // The floating combat text ("-6", "Blocked", "Immune", status words) is exactly what a sighted
    // player sees. We buffer a burst (one card's resolution, or a round of passive ticks) and speak a
    // single coalesced line once the text stops arriving, keyed per affected character. SpeakQueued so
    // consecutive actions queue in the screen reader instead of clobbering each other.

    public static void BufferDamage(CombatText src, CastResolutionForCombatText cast)
    {
        if (cast == null)
            return;

        int dmg = 0;
        bool blocked = false, evaded = false;
        foreach (var r in cast.GetDamageResults())
        {
            if (r == null)
                continue;
            dmg += r.DamageDone;
            if (r.FullyBlocked) blocked = true;
            if (r.FullyEvaded) evaded = true;
        }

        string owner = OwnerName(src);
        if (dmg > 0)
            Buffer(owner, "takes " + dmg);
        else if (evaded)
            Buffer(owner, "evaded");
        else if (blocked)
            Buffer(owner, "blocked");

        if (cast.heal > 0)
            Buffer(owner, "heals " + cast.heal);

        string effect = AccessibleMenuBase.StripRichText(cast.effect);
        if (!string.IsNullOrEmpty(effect))
            Buffer(owner, effect);
    }

    public static void BufferText(CombatText src, string text)
    {
        string clean = AccessibleMenuBase.StripRichText(text);
        if (string.IsNullOrEmpty(clean))
            return;
        Buffer(OwnerName(src), clean);
    }

    private static void Buffer(string owner, string text)
    {
        _ctBuffer.Add(new CtEntry { Owner = owner, Text = text });
        _lastCtTime = Time.time;
    }

    public static void TickFlush()
    {
        if (_ctBuffer.Count == 0)
            return;
        if (Time.time - _lastCtTime < CT_FLUSH_DELAY)
            return;
        Flush();
    }

    /// <summary>Flush buffered events immediately, so they precede a queued turn/end announcement.</summary>
    private static void FlushPendingEvents()
    {
        if (_ctBuffer.Count > 0)
            Flush();
    }

    private static void Flush()
    {
        // Group fragments by affected character, preserving first-seen order.
        var order = new List<string>();
        var byOwner = new Dictionary<string, List<string>>();
        foreach (var e in _ctBuffer)
        {
            if (!byOwner.TryGetValue(e.Owner, out var parts))
            {
                parts = new List<string>();
                byOwner[e.Owner] = parts;
                order.Add(e.Owner);
            }
            parts.Add(e.Text);
        }
        _ctBuffer.Clear();

        var sb = new StringBuilder();
        bool first = true;
        foreach (var owner in order)
        {
            if (!first)
                sb.Append("; ");
            first = false;
            if (!string.IsNullOrEmpty(owner))
                sb.Append(owner).Append(' ');
            sb.Append(string.Join(", ", byOwner[owner].ToArray()));
        }

        SpeechManager.SpeakQueued(sb.ToString());
    }

    private static string OwnerName(CombatText src)
    {
        if (src == null)
            return "";
        var ci = Traverse.Create(src).Field<CharacterItem>("characterItem").Value;
        var c = ci != null ? ci.Character : null;
        return c != null ? AccessibleMenuBase.StripRichText(c.SourceName) : "";
    }

    // ======================= drill-in (Ctrl+Up/Down) =======================
    // On a card: walk the description line by line. On a character: cycle info categories. The first
    // press enters the drill (reads item 0); subsequent presses step. Escape exits.

    public static void DrillNext(int dir)
    {
        var t = _focusedTransform;
        if (t == null)
            return;

        if (_drill == DrillMode.None)
        {
            var card = t.GetComponent<CardItem>();
            if (card != null)
            {
                BeginCardLines(card);
                return;
            }
            var ci = t.GetComponentInParent<CharacterItem>();
            if (ci != null && ci.Character != null)
            {
                BeginCharCategories(ci);
                return;
            }
            return; // nothing to drill on this element
        }

        if (_drill == DrillMode.CardLines && _cardLines.Count > 0)
        {
            _cardLineIndex = Clamp(_cardLineIndex + dir, 0, _cardLines.Count - 1);
            SpeechManager.Speak(_cardLines[_cardLineIndex]);
        }
        else if (_drill == DrillMode.CharCategory && _categories.Count > 0)
        {
            _catIndex = Clamp(_catIndex + dir, 0, _categories.Count - 1);
            SpeechManager.Speak(_categories[_catIndex]);
        }
    }

    public static bool TryDrillExit()
    {
        if (_drill == DrillMode.None)
            return false;

        _drill = DrillMode.None;
        _cardLines.Clear();
        _categories.Clear();

        // Re-announce the focused element so the player knows where they are.
        if (_focusedTransform != null)
        {
            string s = DescribeFocus(_focusedTransform);
            if (!string.IsNullOrEmpty(s))
                SpeechManager.Speak(s);
        }
        return true;
    }

    private static void BeginCardLines(CardItem card)
    {
        _cardLines.Clear();
        var cd = card.CardData;
        if (cd != null)
        {
            foreach (var line in SplitLines(CleanDesc(cd.DescriptionNormalized)))
                _cardLines.Add(line);
        }
        _drill = DrillMode.CardLines;
        _cardLineIndex = 0;
        SpeechManager.Speak(_cardLines.Count > 0 ? _cardLines[0] : "No description");
    }

    private static void BeginCharCategories(CharacterItem ci)
    {
        _categories.Clear();
        BuildCharCategories(ci.Character, ci, _categories);
        _drill = DrillMode.CharCategory;
        _catIndex = 0;
        if (_categories.Count > 0)
            SpeechManager.Speak(_categories[0]);
    }

    private static void BuildCharCategories(Character c, CharacterItem ci, List<string> into)
    {
        into.Add("Health, " + c.GetHp() + " of " + c.GetMaxHP());

        int block = c.GetBlock();
        into.Add(block > 0 ? "Block " + block : "No block");

        into.Add(BuildStatusLine(c, buffs: true));
        into.Add(BuildStatusLine(c, buffs: false));

        if (!ci.IsHero)
            into.Add(BuildIntentLine(ci as NPCItem));

        into.Add("Resists: " + BuildResistLine(c));
    }

    private static string BuildStatusLine(Character c, bool buffs)
    {
        var names = new List<string>();
        var list = c.AuraCurseList;
        if (list != null)
        {
            foreach (var a in list)
            {
                if (a == null || a.ACData == null || a.AuraCharges <= 0)
                    continue;
                if (string.Equals(a.ACData.Id, "block", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (a.ACData.IsAura != buffs)
                    continue;
                names.Add(AuraName(a) + " " + a.AuraCharges);
            }
        }

        string label = buffs ? "Buffs" : "Curses";
        return names.Count > 0
            ? label + ": " + string.Join(", ", names.ToArray())
            : (buffs ? "No buffs" : "No curses");
    }

    private static string BuildIntentLine(NPCItem npc)
    {
        if (npc != null && npc.cardsCI != null)
        {
            var cards = new List<string>();
            foreach (var card in npc.cardsCI)
            {
                if (card != null && card.IsCardRevealed())
                    cards.Add(DescribeCard(card));
            }
            if (cards.Count > 0)
                return "Intent: " + string.Join("; ", cards.ToArray());
        }
        return "No revealed intent";
    }

    private static string AuraName(AuraCurse a)
    {
        string name = AccessibleMenuBase.StripRichText(a.ACData.ACName);
        return string.IsNullOrEmpty(name) ? a.ACData.Id : name;
    }

    /// <summary>Recover <c>&lt;sprite name=X&gt;</c> keyword words before tags are stripped, else they vanish.</summary>
    private static string CleanDesc(string raw)
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
    private static List<string> SplitLines(string raw)
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

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    // ======================= Alt quick keys =======================
    // Per-character reads act on the focused character, else the active hero.

    public static void SpeakHealth()
    {
        var c = CurrentCharacter();
        if (c == null) { SpeechManager.Speak("No character focused"); return; }
        SpeechManager.Speak(Name(c) + ", " + c.GetHp() + " of " + c.GetMaxHP() + " health");
    }

    public static void SpeakBlock()
    {
        var c = CurrentCharacter();
        if (c == null) { SpeechManager.Speak("No character focused"); return; }
        int b = c.GetBlock();
        SpeechManager.Speak(Name(c) + ", " + (b > 0 ? "block " + b : "no block"));
    }

    public static void SpeakEnergy()
    {
        var mm = MatchManager.Instance;
        var c = CurrentCharacter();
        Hero h = c as Hero ?? (mm != null ? mm.CurrentHero : null);
        if (h == null) { SpeechManager.Speak("No active hero"); return; }
        SpeechManager.Speak(Name(h) + ", energy " + h.EnergyCurrent);
    }

    public static void SpeakStatusList()
    {
        var c = CurrentCharacter();
        if (c == null) { SpeechManager.Speak("No character focused"); return; }
        SpeechManager.Speak(Name(c) + ". " + BuildStatusLine(c, buffs: true) + ". " + BuildStatusLine(c, buffs: false));
    }

    public static void SpeakBattlefield()
    {
        string s = BuildBattlefield();
        if (s.Length > 0)
            SpeechManager.Speak(s);
    }

    public static void SpeakRoundAndOrder()
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return;

        Character actor = mm.IsHeroTurn
            ? (Character)mm.CurrentHero
            : Traverse.Create(mm).Field<NPC>("theNPC").Value;

        int heroes = CountAlive(mm.GetTeamHero());
        int enemies = CountAlive(mm.GetTeamNPC());

        var sb = new StringBuilder();
        sb.Append("Round ").Append(mm.CurrentRound);
        if (actor != null)
            sb.Append(", ").Append(Name(actor)).Append(mm.IsHeroTurn ? " active" : "'s turn");
        sb.Append(". ").Append(heroes).Append(heroes == 1 ? " hero, " : " heroes, ");
        sb.Append(enemies).Append(enemies == 1 ? " enemy" : " enemies");
        SpeechManager.Speak(sb.ToString());
    }

    public static void SpeakPileCounts()
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return;
        SpeechManager.Speak(
            "Draw " + mm.CountHeroDeck()
            + ", hand " + mm.CountHeroHand()
            + ", discard " + mm.CountHeroDiscard()
            + ", exhaust " + mm.CountHeroVanish());
    }

    public static void SpeakRevealedIntent()
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return;

        var team = mm.GetTeamNPC();
        var sb = new StringBuilder();
        bool any = false;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                Character c = team[i];
                if (c == null || !c.Alive || c.NPCItem == null || c.NPCItem.cardsCI == null)
                    continue;

                var cards = new List<string>();
                foreach (var card in c.NPCItem.cardsCI)
                {
                    if (card != null && card.IsCardRevealed())
                        cards.Add(DescribeCard(card));
                }
                if (cards.Count > 0)
                {
                    if (any)
                        sb.Append(". ");
                    any = true;
                    sb.Append(Name(c)).Append(": ").Append(string.Join("; ", cards.ToArray()));
                }
            }
        }
        SpeechManager.Speak(any ? sb.ToString() : "No revealed enemy intent");
    }

    private static Character CurrentCharacter()
    {
        if (_focusedTransform != null)
        {
            var ci = _focusedTransform.GetComponentInParent<CharacterItem>();
            if (ci != null && ci.Character != null)
                return ci.Character;
        }
        var mm = MatchManager.Instance;
        if (mm != null && mm.IsHeroTurn && mm.CurrentHero != null)
            return mm.CurrentHero;
        return null;
    }

    private static string Name(Character c) => AccessibleMenuBase.StripRichText(c.SourceName);

    private static int CountAlive(Team team)
    {
        if (team == null)
            return 0;
        int n = 0;
        for (int i = 0; i < team.Count; i++)
            if (team[i] != null && team[i].Alive)
                n++;
        return n;
    }

    // ======================= Alt+T tooltip (hover detail) =======================
    // The extra information a sighted player gets on hover: a card's keyword definitions, or a
    // character's resistances/immunities.

    public static void SpeakTooltip()
    {
        var t = _focusedTransform;
        if (t == null) { SpeechManager.Speak("Nothing focused"); return; }

        var card = t.GetComponent<CardItem>();
        if (card != null) { SpeakCardKeynotes(card); return; }

        var ci = t.GetComponentInParent<CharacterItem>();
        if (ci != null && ci.Character != null) { SpeakResists(ci.Character); return; }

        SpeechManager.Speak("No additional details");
    }

    private static void SpeakCardKeynotes(CardItem card)
    {
        var cd = card.CardData;
        var kn = cd != null ? cd.KeyNotes : null;
        if (kn == null || kn.Count == 0) { SpeechManager.Speak("No keywords"); return; }

        var parts = new List<string>();
        foreach (var k in kn)
        {
            if (k == null)
                continue;
            string n = AccessibleMenuBase.StripRichText(k.KeynoteName);
            string d = AccessibleMenuBase.StripRichText(CleanDesc(k.Description));
            if (string.IsNullOrEmpty(n) && string.IsNullOrEmpty(d))
                continue;
            parts.Add(string.IsNullOrEmpty(d) ? n : n + ": " + d);
        }
        SpeechManager.Speak(parts.Count > 0 ? string.Join(". ", parts.ToArray()) : "No keywords");
    }

    private static void SpeakResists(Character c)
    {
        SpeechManager.Speak(Name(c) + ", " + BuildResistLine(c));
    }

    private static string BuildResistLine(Character c)
    {
        var sb = new StringBuilder();
        AppendResist(sb, "slashing", c.ResistSlashing, c.ImmuneSlashing);
        AppendResist(sb, "blunt", c.ResistBlunt, c.ImmuneBlunt);
        AppendResist(sb, "piercing", c.ResistPiercing, c.ImmunePiercing);
        AppendResist(sb, "fire", c.ResistFire, c.ImmuneFire);
        AppendResist(sb, "cold", c.ResistCold, c.ImmuneCold);
        AppendResist(sb, "lightning", c.ResistLightning, c.ImmuneLightning);
        AppendResist(sb, "mind", c.ResistMind, c.ImmuneMind);
        AppendResist(sb, "holy", c.ResistHoly, c.ImmuneHoly);
        AppendResist(sb, "shadow", c.ResistShadow, c.ImmuneShadow);
        return sb.Length > 0 ? sb.ToString() : "no resistances";
    }

    private static void AppendResist(StringBuilder sb, string type, int resist, bool immune)
    {
        if (immune)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append("immune ").Append(type);
        }
        else if (resist != 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(type).Append(' ').Append(resist);
        }
    }
}

// =========================== Harmony patches ===========================

/// <summary>Speaks the focused element after the game rebuilds its controller navigation.</summary>
[HarmonyPatch(typeof(MatchManager), "ControllerMovement")]
public class CombatFocusPatch
{
    static void Postfix(bool goingUp, bool goingRight, bool goingDown, bool goingLeft, int absoluteIndex)
    {
        bool anyDirection = goingUp || goingRight || goingDown || goingLeft;
        CombatNavigator.OnControllerMoved(anyDirection && absoluteIndex < 0);
    }
}

/// <summary>Announces whose turn it is (and, on the first turn, the battlefield overview).</summary>
[HarmonyPatch(typeof(MatchManager), "SetActiveCharacter")]
public class CombatTurnChangePatch
{
    static void Postfix() => CombatNavigator.OnTurnChanged();
}

/// <summary>Announces victory/defeat.</summary>
[HarmonyPatch(typeof(NewTurn), nameof(NewTurn.FinishCombat))]
public class CombatEndPatch
{
    static void Postfix(bool won) => CombatNavigator.OnCombatEnd(won);
}

/// <summary>Buffers damage/heal floating numbers for coalesced announcement.</summary>
[HarmonyPatch(typeof(CombatText), nameof(CombatText.SetDamageNew))]
public class CombatDamageTextPatch
{
    static void Postfix(CombatText __instance, CastResolutionForCombatText _cast)
        => CombatNavigator.BufferDamage(__instance, _cast);
}

/// <summary>Buffers status/effect floating words (ticks, applications, "Immune"/"Evaded").</summary>
[HarmonyPatch(typeof(CombatText), nameof(CombatText.SetText))]
public class CombatStatusTextPatch
{
    static void Postfix(CombatText __instance, string text)
        => CombatNavigator.BufferText(__instance, text);
}
