using System.Collections.Generic;
using System.Text;
using BattleMatch;
using Cards;
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

    /// <summary>Board region the focused element belongs to; announced when crossing into it.</summary>
    private enum Section { None, Hand, Heroes, Enemies }

    /// <summary>
    /// One drill step; AuraData+Charges (health/block/buff/curse entries) or Trait is set so Alt+T
    /// can read that entry's detail.
    /// </summary>
    private struct DrillEntry
    {
        public string Speech;
        public AuraCurseData AuraData;
        public int Charges;
        public TraitData Trait;
    }

    // ---- focus dedupe ----
    private static int _lastIndex = -1;
    private static int _lastInstanceId;
    private static Section _lastSection = Section.None;

    // ---- current focus + drill-in state ----
    private static Transform _focusedTransform;

    /// <summary>The element combat navigation last focused. The combat input context uses it to
    /// resolve the intended target of an Enter press without depending on the game's click raycast
    /// (which deck-effect cards break — see <c>CombatInputContext.OnConfirm</c>).</summary>
    public static Transform FocusedTransform => _focusedTransform;

    private static DrillMode _drill = DrillMode.None;
    private static readonly List<string> _cardLines = new List<string>();
    private static int _cardLineIndex;
    private static readonly List<DrillEntry> _charEntries = new List<DrillEntry>();
    private static int _catIndex;
    private static Character _drillChar; // owner of _charEntries, for charge-dependent descriptions

    // ---- combat lifecycle ----
    private static int _lastRound = -1;
    private static bool _overviewAnnounced;

    // With CombineEnemyTurns on, an NPC's turn line is withheld and the "plays [card]" cast line
    // marks the turn instead. This holds the NPC's name until a cast arrives; if its turn ends
    // (next turn change) with no cast — stun, freeze — "takes no action" is spoken so the turn
    // doesn't pass silently.
    private static string _pendingNpcTurnName;

    // ---- combat-event coalescing ----
    private struct CtEntry { public string Owner; public string Text; }
    private static readonly List<CtEntry> _ctBuffer = new List<CtEntry>();
    private static float _lastCtTime;
    private const float CT_FLUSH_DELAY = 0.35f;

    // ---- aura-loss watching ----
    // Charge decreases (end-of-turn tick-down, consumption) are direct field writes with no floating
    // text — the only visible sign is the icon counter. A per-frame snapshot diff catches them all.
    private struct AuraSnap { public int Charges; public string Name; }
    private static readonly Dictionary<Character, Dictionary<string, AuraSnap>> _auraWatch
        = new Dictionary<Character, Dictionary<string, AuraSnap>>();
    private static readonly Dictionary<string, float> _recentNegated = new Dictionary<string, float>();
    private const float NEGATED_DEDUPE_WINDOW = 1.5f;


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

        // IsHeroTurn is stale here (SetActiveCharacter sets heroActive/npcActive but heroTurn only
        // flips later, during the draw) — resolve the actor from the indices it *did* just set.
        int heroIdx = Traverse.Create(mm).Field<int>("heroActive").Value;
        int npcIdx = Traverse.Create(mm).Field<int>("npcActive").Value;
        bool isHero = heroIdx >= 0;
        var team = isHero ? mm.GetTeamHero() : mm.GetTeamNPC();
        int idx = isHero ? heroIdx : npcIdx;
        Character actor = (team != null && idx >= 0 && idx < team.Count) ? team[idx] : null;

        // A merged NPC turn that produced no cast line ended just now — say so before moving on.
        if (_pendingNpcTurnName != null)
        {
            Buffer(_pendingNpcTurnName, "takes no action");
            _pendingNpcTurnName = null;
        }

        var sb = new StringBuilder();
        int round = mm.CurrentRound;
        if (round != _lastRound)
        {
            sb.Append("Round ").Append(round).Append(". ");
            _lastRound = round;
        }

        string name = actor != null ? AccessibleMenuBase.StripRichText(actor.SourceName) : "";
        if (name.Length == 0)
        {
            // Don't emit a dangling "'s turn" with no name; speak the round change if there was one.
            if (sb.Length > 0)
            {
                FlushPendingEvents();
                SpeechManager.SpeakQueued(sb.ToString());
            }
            return;
        }
        if (!isHero && AccessibilityOptions.CombineEnemyTurns.Value)
        {
            // Withhold the enemy turn line; the coming "plays [card]" line marks the turn.
            _pendingNpcTurnName = name;
            if (sb.Length > 0)
            {
                FlushPendingEvents();
                SpeechManager.SpeakQueued(sb.ToString()); // round change still spoken
            }
        }
        else
        {
            sb.Append(name).Append(isHero ? ", your turn" : "'s turn");

            // Queue (not interrupt): enemy turns come back-to-back, and an interrupting turn line
            // would cut off the previous action's event announcement mid-utterance. Flush pending
            // events first so they are heard before the turn line.
            FlushPendingEvents();
            SpeechManager.SpeakQueued(sb.ToString());
        }

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
        _pendingNpcTurnName = null; // combat ended mid-turn; no "takes no action" chatter
        FlushPendingEvents();
        SpeechManager.SpeakQueued(won ? "Victory" : "Defeat");
        ResetFocus();
        _overviewAnnounced = false;
        _lastRound = -1;
        _auraWatch.Clear();
        _recentNegated.Clear();
    }

    private static void ResetFocus()
    {
        _lastIndex = -1;
        _lastInstanceId = 0;
        _lastSection = Section.None;
        _drill = DrillMode.None;
        _cardLines.Clear();
        _charEntries.Clear();
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
        if (string.IsNullOrEmpty(speech))
            return;

        // Announce the board region when crossing into it ("Hand", "Heroes", …).
        Section section = GetSection(t);
        if (section != _lastSection)
        {
            _lastSection = section;
            string label = SectionLabel(section);
            if (label != null)
                speech = label + ". " + speech;
        }

        SpeechManager.Speak(speech);
    }

    private static Section GetSection(Transform t)
    {
        if (IsDiscardPileEntry(t) || IsDrawPileEntry(t))
            return Section.None; // the pile descriptions name themselves

        var card = t.GetComponent<CardItem>();
        if (card != null)
        {
            if (t.GetComponentInParent<NPCItem>() != null)
                return Section.Enemies; // an enemy's intent card floats over its portrait
            return Section.Hand;
        }

        var ci = t.GetComponentInParent<CharacterItem>();
        if (ci != null && ci.Character != null)
            return ci.IsHero ? Section.Heroes : Section.Enemies;

        return Section.None;
    }

    private static string SectionLabel(Section s) => s switch
    {
        Section.Hand => "Hand",
        Section.Heroes => "Heroes",
        Section.Enemies => "Enemies",
        _ => null,
    };

    // The game navigates two pile widgets alongside the hand: the draw-deck object, and ONE entry
    // inside the discard-pile stack (played/discarded cards leave the hand immediately — what looks
    // like "a played card on the left" is the discard pile itself, whose focused child happens to be
    // the first card discarded). Describe them as piles, not as cards.

    private static bool IsDiscardPileEntry(Transform t)
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return false;
        var pile = Traverse.Create(mm).Field<GameObject>("GO_DiscardPile").Value;
        return pile != null && t != pile.transform && t.IsChildOf(pile.transform);
    }

    private static bool IsDrawPileEntry(Transform t)
    {
        var mm = MatchManager.Instance;
        if (mm == null)
            return false;
        var decks = Traverse.Create(mm).Field<GameObject>("GO_DecksObject").Value;
        return decks != null && t == decks.transform;
    }

    /// <summary>The visually-topmost card of the active hero's discard pile (most recently discarded).</summary>
    private static CardRealtimeData TopDiscardCard()
    {
        var mm = MatchManager.Instance;
        var hero = mm != null ? mm.CurrentHero : null;
        var pile = hero != null && hero.BattleCards != null
            ? hero.BattleCards.GetPile(DeckType.DeckDiscard)
            : null;
        if (pile == null || pile.Count == 0)
            return null;
        return mm.GetCardData(pile[pile.Count - 1], createInDict: false);
    }

    private static string DescribeDrawPile()
    {
        int n = MatchManager.Instance.CountHeroDeck();
        return "Draw pile, " + n + (n == 1 ? " card" : " cards");
    }

    private static string DescribeDiscardPile()
    {
        int n = MatchManager.Instance.CountHeroDiscard();
        var sb = new StringBuilder();
        sb.Append("Discard pile, ").Append(n).Append(n == 1 ? " card" : " cards");
        var top = TopDiscardCard();
        if (top != null)
            sb.Append(". Top card, ").Append(AccessibleMenuBase.StripRichText(top.CardName));
        return sb.ToString();
    }

    private enum CardSuffix { None, Hand }

    private static string DescribeFocus(Transform t)
    {
        if (IsDiscardPileEntry(t))
            return DescribeDiscardPile();
        if (IsDrawPileEntry(t))
            return DescribeDrawPile();

        var card = t.GetComponent<CardItem>();
        if (card != null)
        {
            // Enemy intent cards get no playability chatter.
            CardSuffix suffix = t.GetComponentInParent<NPCItem>() == null ? CardSuffix.Hand : CardSuffix.None;
            return DescribeCard(card, suffix);
        }

        var ci = t.GetComponentInParent<CharacterItem>();
        if (ci != null && ci.Character != null)
            return DescribeCharacter(ci.Character, ci);

        var icon = t.GetComponent<ItemCombatIcon>();
        if (icon != null)
            return DescribeIcon(icon);

        var ip = t.GetComponent<InitiativePortrait>();
        if (ip != null)
            return DescribeInitiativePortrait(ip);

        return FallbackLabel(t);
    }

    /// <summary>The turn-order strip at the top of the screen: portrait + speed number per character.</summary>
    private static string DescribeInitiativePortrait(InitiativePortrait ip)
    {
        Character c = ip.Hero != null ? (Character)ip.Hero : (ip.npcItem != null ? ip.npcItem.NPC : null);

        var sb = new StringBuilder("Initiative, ");
        sb.Append(c != null ? Name(c) : "unknown");

        string speed = ip.speedTM != null ? AccessibleMenuBase.StripRichText(ip.speedTM.text) : "";
        if (speed.Length > 0)
            sb.Append(", speed ").Append(speed);

        int pos = ip.GetPos();
        if (pos >= 0)
        {
            sb.Append(", position ").Append(pos + 1);
            if (ip.portraitElements > 0)
                sb.Append(" of ").Append(ip.portraitElements);
        }
        return sb.ToString();
    }

    private static string DescribeCard(CardItem card, CardSuffix suffix = CardSuffix.None)
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

        if (suffix == CardSuffix.Hand && !card.IsPlayableRightNow())
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

        string type = icon.itemType;
        if (string.IsNullOrEmpty(type))
            type = icon.gameObject.name.ToLower(); // enchantment icons carry no itemType
        if (type == "accesory")
            type = "accessory";

        var sb = new StringBuilder();
        if (owner.Length > 0)
            sb.Append(owner).Append(", ");
        sb.Append(type);

        var cd = IconCard(icon);
        if (cd != null)
            sb.Append(", ").Append(AccessibleMenuBase.StripRichText(cd.CardName));
        return sb.ToString();
    }

    /// <summary>The item card an ItemCombatIcon represents (shown enlarged on hover for sighted players).</summary>
    private static CardRealtimeData IconCard(ItemCombatIcon icon)
        => Traverse.Create(icon).Field<CardRealtimeData>("cardData").Value;

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

        int dmg = 0, blocked = 0;
        bool evaded = false;
        foreach (var r in cast.GetDamageResults())
        {
            if (r == null)
                continue;
            dmg += r.DamageDone;
            blocked += r.DamageBlocked;
            if (r.FullyEvaded) evaded = true;
        }

        string owner = OwnerName(src);
        if (dmg > 0)
            Buffer(owner, blocked > 0 ? "takes " + dmg + ", blocked " + blocked : "takes " + dmg);
        else if (evaded)
            Buffer(owner, "evaded");
        else if (blocked > 0)
            Buffer(owner, "blocked " + blocked);

        if (cast.heal > 0)
            Buffer(owner, "heals " + cast.heal);

        string effect = AccessibleMenuBase.StripRichText(cast.effect);
        if (!string.IsNullOrEmpty(effect))
            Buffer(owner, effect);
    }

    public static void BufferText(CombatText src, string text, Enums.CombatScrollEffectType type)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Crossed-out text (<s>…</s>) is how the game shows an effect being removed OR failing to
        // apply (immunity) — "negated" covers both readings.
        bool negated = text.Contains("<s>");

        string clean = AccessibleMenuBase.StripRichText(text).Replace("\n", ", ").Trim(' ', ',');
        if (string.IsNullOrEmpty(clean))
            return;

        string owner = OwnerName(src);

        // Block expiry floats as "-Block".
        if (type == Enums.CombatScrollEffectType.Block && clean.StartsWith("-"))
        {
            Buffer(owner, clean.Substring(1).Trim() + " removed");
            return;
        }

        if (negated)
        {
            // Remember the names so the aura-loss watcher doesn't announce the same removal again.
            foreach (var n in clean.Split(','))
            {
                string trimmed = n.Trim();
                if (trimmed.Length > 0)
                    _recentNegated[owner.ToLower() + "|" + trimmed.ToLower()] = Time.time;
            }
            Buffer(owner, clean + " negated");
            return;
        }

        // Aura/curse gains are announced (with exact amounts) from the SetAuraCurse patch below —
        // buffering the name-only floating text too would double-announce them.
        if (type == Enums.CombatScrollEffectType.Aura || type == Enums.CombatScrollEffectType.Curse)
            return;

        Buffer(owner, clean);
    }

    /// <summary>
    /// Announces every successful effect application with the amount actually gained (the floating
    /// text has no numbers, and trait/item applications produce no floating text at all). The gain
    /// is a delta — "gains Block 5, total 10" — because charges stack across applications.
    /// </summary>
    public static void OnAuraApplied(Character target, AuraCurseData acData, bool fromTrait,
        Character.AuraSetResult result, int chargesBefore)
    {
        if (target == null || acData == null)
            return;
        if (result != Character.AuraSetResult.Added && result != Character.AuraSetResult.Updated)
            return;
        if (AccessibilityOptions.SkipEnemyStatusChanges.Value && !(target is Hero))
            return;

        var mm = MatchManager.Instance;
        if (mm == null || mm.MatchIsOver)
            return;

        int total = target.EffectCharges(acData.Id);
        int gained = total - chargesBefore;
        if (gained <= 0)
            return;

        string name = AccessibleMenuBase.StripRichText(acData.ACName);
        if (string.IsNullOrEmpty(name))
            name = acData.Id;

        string msg;
        if (AccessibilityOptions.ShortStatusPhrasing.Value)
        {
            // The new total is the actionable number; the source was just named by the cast line.
            msg = name + " " + total;
        }
        else
        {
            msg = "gains " + name + " " + gained;
            if (total != gained)
                msg += ", total " + total;
        }
        Buffer(Name(target), msg);
    }

    /// <summary>Deaths and resurrections, from the combat-log channel both kill paths feed.</summary>
    public static void OnLogEvent(Character hero, Character npc, Enums.EventActivation ev)
    {
        if (ev != Enums.EventActivation.Killed && ev != Enums.EventActivation.Resurrect)
            return;
        Character c = hero ?? npc;
        if (c == null)
            return;
        Buffer(Name(c), ev == Enums.EventActivation.Killed ? "killed" : "resurrected");
    }

    /// <summary>Enemy (and automatic hero/pet/item) casts: name the card being played.</summary>
    public static void OnAutomaticCast(CardRealtimeData card, Transform from)
    {
        if (card == null)
            return;

        // Any cast during a merged enemy turn means the turn was not skipped. (An item or pet proc
        // clearing it instead of the enemy's own card is a harmless rare miss of "takes no action".)
        _pendingNpcTurnName = null;

        string caster = "";
        if (from != null)
        {
            var hi = from.GetComponent<HeroItem>();
            var ni = from.GetComponent<NPCItem>();
            Character c = hi != null ? (Character)hi.Hero : (ni != null ? ni.NPC : null);
            if (c != null)
                caster = Name(c);
        }
        Buffer(caster, "plays " + AccessibleMenuBase.StripRichText(card.CardName));
    }

    private static void Buffer(string owner, string text)
    {
        _ctBuffer.Add(new CtEntry { Owner = owner, Text = text });
        _lastCtTime = Time.time;
    }

    public static void TickFlush()
    {
        TickAuraWatch();

        if (_ctBuffer.Count == 0)
            return;
        if (Time.time - _lastCtTime < CT_FLUSH_DELAY)
            return;
        Flush();
    }

    private static void TickAuraWatch()
    {
        var mm = MatchManager.Instance;
        if (mm == null || mm.MatchIsOver)
        {
            if (_auraWatch.Count > 0)
            {
                _auraWatch.Clear();
                _recentNegated.Clear();
            }
            return;
        }
        WatchTeam(mm.GetTeamHero(), announce: true);
        // Snapshots keep updating even when enemy announcements are off, so toggling the option
        // mid-combat doesn't replay a backlog of stale diffs.
        WatchTeam(mm.GetTeamNPC(), announce: !AccessibilityOptions.SkipEnemyStatusChanges.Value);
    }

    private static void WatchTeam(Team team, bool announce)
    {
        if (team == null)
            return;

        for (int i = 0; i < team.Count; i++)
        {
            Character c = team[i];
            if (c == null)
                continue;
            if (!c.Alive)
            {
                _auraWatch.Remove(c); // no loss chatter after "X killed"
                continue;
            }

            var current = new Dictionary<string, AuraSnap>();
            var list = c.AuraCurseList;
            if (list != null)
            {
                foreach (var a in list)
                {
                    if (a == null || a.ACData == null || a.AuraCharges <= 0)
                        continue;
                    // Block losses have their own channels (damage "blocked N", expiry "-Block").
                    if (string.Equals(a.ACData.Id, "block", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    current[a.ACData.Id] = new AuraSnap { Charges = a.AuraCharges, Name = AuraName(a) };
                }
            }

            if (announce && _auraWatch.TryGetValue(c, out var previous))
            {
                foreach (var kv in previous)
                {
                    int now = current.TryGetValue(kv.Key, out var snap) ? snap.Charges : 0;
                    int lost = kv.Value.Charges - now;
                    if (lost <= 0)
                        continue; // gains announce from the SetAuraCurse patch

                    // Routine minus-1 decay is predictable by rule; only expiries and bigger
                    // losses (dispels, consumption) are worth words.
                    bool expired = now == 0;
                    if (AccessibilityOptions.SkipRoutineDecay.Value && lost == 1 && !expired)
                        continue;

                    // A dispel/cleanse already announced this one as "negated".
                    string dedupeKey = Name(c).ToLower() + "|" + kv.Value.Name.ToLower();
                    if (_recentNegated.TryGetValue(dedupeKey, out float t)
                        && Time.time - t < NEGATED_DEDUPE_WINDOW)
                        continue;

                    string msg;
                    if (AccessibilityOptions.ShortStatusPhrasing.Value)
                        msg = expired ? kv.Value.Name + " gone" : kv.Value.Name + " " + now;
                    else
                    {
                        msg = "loses " + kv.Value.Name + " " + lost;
                        if (now > 0)
                            msg += ", total " + now;
                    }
                    Buffer(Name(c), msg);
                }
            }
            _auraWatch[c] = current;
        }
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

    private static Character OwnerCharacter(CombatText src)
    {
        if (src == null)
            return null;
        var ci = Traverse.Create(src).Field<CharacterItem>("characterItem").Value;
        return ci != null ? ci.Character : null;
    }

    private static string OwnerName(CombatText src)
    {
        var c = OwnerCharacter(src);
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
            if (IsDiscardPileEntry(t))
            {
                // Drill the pile's visually-topmost card, matching what was announced.
                BeginCardLines(TopDiscardCard());
                return;
            }
            var card = t.GetComponent<CardItem>();
            if (card != null)
            {
                BeginCardLines(card.CardData, card.GetEnergyCost());
                return;
            }
            var icon = t.GetComponent<ItemCombatIcon>();
            if (icon != null)
            {
                BeginCardLines(IconCard(icon)); // walk the item card's detail lines
                return;
            }
            var ip = t.GetComponent<InitiativePortrait>();
            if (ip != null)
            {
                // Drill an initiative portrait as its character.
                CharacterItem ipItem = ip.heroItem != null ? (CharacterItem)ip.heroItem : ip.npcItem;
                if (ipItem != null && ipItem.Character != null)
                    BeginCharCategories(ipItem);
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
        else if (_drill == DrillMode.CharCategory && _charEntries.Count > 0)
        {
            _catIndex = Clamp(_catIndex + dir, 0, _charEntries.Count - 1);
            SpeechManager.Speak(_charEntries[_catIndex].Speech);
        }
    }

    public static bool TryDrillExit()
    {
        if (_drill == DrillMode.None)
            return false;

        _drill = DrillMode.None;
        _cardLines.Clear();
        _charEntries.Clear();

        // Re-announce the focused element so the player knows where they are.
        if (_focusedTransform != null)
        {
            string s = DescribeFocus(_focusedTransform);
            if (!string.IsNullOrEmpty(s))
                SpeechManager.Speak(s);
        }
        return true;
    }

    private static void BeginCardLines(CardRealtimeData cd, int? liveCost = null)
    {
        _cardLines.Clear();
        _cardLines.AddRange(CardSpeech.DetailLines(cd, liveCost));
        _drill = DrillMode.CardLines;
        _cardLineIndex = 0;
        SpeechManager.Speak(_cardLines.Count > 0 ? _cardLines[0] : "No description");
    }

    private static void BeginCharCategories(CharacterItem ci)
    {
        _charEntries.Clear();
        _drillChar = ci.Character;
        BuildCharEntries(ci.Character, ci, _charEntries);
        _drill = DrillMode.CharCategory;
        _catIndex = 0;
        if (_charEntries.Count > 0)
            SpeechManager.Speak(_charEntries[0].Speech);
    }

    private static void BuildCharEntries(Character c, CharacterItem ci, List<DrillEntry> into)
    {
        into.Add(new DrillEntry { Speech = "Health, " + c.GetHp() + " of " + c.GetMaxHP() });

        int block = c.GetBlock();
        var blockData = Globals.Instance != null ? Globals.Instance.GetAuraCurseData("block") : null;
        into.Add(new DrillEntry
        {
            Speech = block > 0 ? "Block " + block : "No block",
            AuraData = blockData,
            Charges = block,
        });

        // One entry per buff, then per curse, so Alt+T can read the focused effect's description.
        AddAuraEntries(c, buffs: true, into);
        AddAuraEntries(c, buffs: false, into);

        if (!ci.IsHero)
            into.Add(new DrillEntry { Speech = BuildIntentLine(ci as NPCItem) });

        into.Add(new DrillEntry { Speech = "Resists: " + BuildResistLine(c) });

        AddTraitEntries(c, into);
    }

    /// <summary>
    /// Traits live outside the buff/curse list; a sighted player sees them in the trait-info line
    /// ("Well trained 1/1") and on hover. One entry per trait, with the usage counter the game shows.
    /// </summary>
    private static void AddTraitEntries(Character c, List<DrillEntry> into)
    {
        var traits = c.Traits;
        if (traits == null || Globals.Instance == null)
            return;

        var mm = MatchManager.Instance;
        foreach (var id in traits)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            var td = Globals.Instance.GetTraitData(id, c);
            if (td == null)
                continue;

            string name = AccessibleMenuBase.StripRichText(td.TraitName);
            if (string.IsNullOrEmpty(name))
                name = td.Id;

            var sb = new StringBuilder("Trait, ").Append(name);

            int max = td.TimesPerTurn > 0 ? td.TimesPerTurn : td.TimesPerRound;
            if (td.Id == "welltrained")
                max = Trait.GetWellTrainedTimesPerTurn(c); // perks can raise it above the base value
            if (max > 0 && !td.HideTimesPerTurnText && mm != null)
            {
                string key = Globals.Instance.GetCanonicalTraitId(id, c);
                int used;
                if (!mm.activatedTraits.TryGetValue(key, out used))
                    mm.activatedTraitsRound.TryGetValue(key, out used);
                sb.Append(", used ").Append(used).Append(" of ").Append(max);
            }

            into.Add(new DrillEntry { Speech = sb.ToString(), Trait = td });
        }
    }

    private static void AddAuraEntries(Character c, bool buffs, List<DrillEntry> into)
    {
        int added = 0;
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

                into.Add(new DrillEntry
                {
                    Speech = (buffs ? "Buff, " : "Curse, ") + AuraName(a) + ", " + a.AuraCharges,
                    AuraData = a.ACData,
                    Charges = a.AuraCharges,
                });
                added++;
            }
        }

        if (added == 0)
            into.Add(new DrillEntry { Speech = buffs ? "No buffs" : "No curses" });
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

    /// <summary>
    /// A status description with its numeric placeholders (&lt;ChargesMultiplier&gt; and friends)
    /// filled in, using the game's own popup substitution machinery — otherwise StripRichText
    /// deletes the tokens and the spoken text has holes ("Damage done and Heal received + .").
    /// </summary>
    private static string AuraDescription(AuraCurseData acData, int charges, Character owner)
    {
        string desc = acData != null ? acData.Description : null;
        if (string.IsNullOrEmpty(desc))
            return "";

        var pm = PopupManager.Instance;
        if (pm != null && owner != null)
        {
            try
            {
                var repl = new Dictionary<string, string>();
                var tr = Traverse.Create(pm);
                tr.Method("UpdateReplacements", repl, charges, -1000, acData, owner).GetValue();
                desc = tr.Method("ApplyReplacements", desc, repl).GetValue<string>();
            }
            catch
            {
                // fall back to the raw description; leftover tokens are stripped below
            }
        }
        return AccessibleMenuBase.StripRichText(CleanDesc(desc));
    }

    // Markup handling lives in the shared CardSpeech helper (also used by the town screens);
    // these wrappers keep the many combat call sites unchanged.
    private static string CleanDesc(string raw) => CardSpeech.CleanDescription(raw);

    private static List<string> SplitLines(string raw) => CardSpeech.SplitLines(raw);

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

        var sb = new StringBuilder();
        sb.Append("Round ").Append(mm.CurrentRound);

        // Full initiative order, exactly as the on-screen strip shows it: the active character first
        // (it is pinned to the top of the game's sorted order), upcoming actors by speed, and those
        // who already acted this round at the end.
        var im = mm.InitiativesManager;
        var order = im != null
            ? Traverse.Create(im).Field<List<InitiativesManager.CharacterForOrderNew>>("CharOrder").Value
            : null;

        if (order != null && order.Count > 0)
        {
            sb.Append(". Turn order: ");
            bool first = true;
            foreach (var entry in order)
            {
                var c = entry != null ? entry.character : null;
                if (c == null || !c.Alive)
                    continue;

                if (!first)
                    sb.Append("; ");
                sb.Append(Name(c));
                if (first)
                    sb.Append(", active");
                else if (c.RoundMoved >= mm.CurrentRound)
                    sb.Append(", acted");
                first = false;
            }
        }

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

    // ======================= Alt+T tooltip (hover detail) =======================
    // The extra information a sighted player gets on hover: a card's keyword definitions, or a
    // character's resistances/immunities.

    public static void SpeakTooltip()
    {
        // Drilled onto a specific buff/curse/block/trait → read that entry's full description.
        if (_drill == DrillMode.CharCategory && _catIndex >= 0 && _catIndex < _charEntries.Count)
        {
            var entry = _charEntries[_catIndex];
            if (entry.AuraData != null)
            {
                string name = AccessibleMenuBase.StripRichText(entry.AuraData.ACName);
                if (string.IsNullOrEmpty(name))
                    name = entry.AuraData.Id;
                string desc = AuraDescription(entry.AuraData, entry.Charges, _drillChar);
                SpeechManager.Speak(
                    name + ", " + entry.Charges
                    + (desc.Length > 0 ? ". " + desc : ". No description"));
                return;
            }
            if (entry.Trait != null)
            {
                string name = AccessibleMenuBase.StripRichText(entry.Trait.TraitName);
                if (string.IsNullOrEmpty(name))
                    name = entry.Trait.Id;
                string desc = AccessibleMenuBase.StripRichText(CleanDesc(entry.Trait.Description));
                SpeechManager.Speak(name + (desc.Length > 0 ? ". " + desc : ". No description"));
                return;
            }
        }

        var t = _focusedTransform;
        if (t == null) { SpeechManager.Speak("Nothing focused"); return; }

        if (IsDiscardPileEntry(t)) { SpeakCardDetail(TopDiscardCard()); return; }

        var card = t.GetComponent<CardItem>();
        if (card != null) { SpeakCardDetail(card.CardData, card.GetEnergyCost()); return; }

        var icon = t.GetComponent<ItemCombatIcon>();
        if (icon != null) { SpeakCardDetail(IconCard(icon)); return; }

        var ip = t.GetComponent<InitiativePortrait>();
        if (ip != null)
        {
            Character ipChar = ip.Hero != null ? (Character)ip.Hero : (ip.npcItem != null ? ip.npcItem.NPC : null);
            if (ipChar != null) { SpeakResists(ipChar); return; }
        }

        var ci = t.GetComponentInParent<CharacterItem>();
        if (ci != null && ci.Character != null) { SpeakResists(ci.Character); return; }

        SpeechManager.Speak("No additional details");
    }

    private static void SpeakCardDetail(CardRealtimeData cd, int? liveCost = null)
    {
        SpeechManager.Speak(CardSpeech.FullDetail(cd, liveCost));
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

// The CombatText patches target the private Launch* methods, NOT the public SetDamageNew/SetText:
// the public methods queue-and-replay themselves (SetText re-enters with forceIt=true when a queued
// message dequeues), so a postfix there fires twice per message. Launch* runs exactly once per text
// actually displayed — and also inherits the game's own duplicate suppression.

/// <summary>Buffers damage/heal floating numbers for coalesced announcement.</summary>
[HarmonyPatch(typeof(CombatText), "LaunchInstance")]
public class CombatDamageTextPatch
{
    static void Postfix(CombatText __instance, CastResolutionForCombatText _cast)
        => CombatNavigator.BufferDamage(__instance, _cast);
}

/// <summary>Buffers status/effect floating words (ticks, applications, "Immune"/"Evaded").</summary>
[HarmonyPatch(typeof(CombatText), "LaunchInstanceText")]
public class CombatStatusTextPatch
{
    static void Postfix(CombatText __instance, string text, Enums.CombatScrollEffectType type)
        => CombatNavigator.BufferText(__instance, text, type);
}

/// <summary>Announces character deaths and resurrections.</summary>
[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.CreateLogEntry))]
public class CombatLogEventPatch
{
    static void Postfix(Character _theHero, Character _theNPC, Enums.EventActivation _event)
        => CombatNavigator.OnLogEvent(_theHero, _theNPC, _event);
}

/// <summary>Announces which card an enemy (or an automatic hero effect) plays.</summary>
[HarmonyPatch(typeof(MatchManager), nameof(MatchManager.CastAutomatic))]
public class CombatAutomaticCastPatch
{
    static void Postfix(CardRealtimeData theCardRealtimeData, Transform from)
        => CombatNavigator.OnAutomaticCast(theCardRealtimeData, from);
}

/// <summary>
/// Announces effect applications with exact amounts. The prefix snapshots the pre-application
/// charge count so the postfix can speak the delta actually gained.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.SetAuraCurse))]
public class CombatAuraAppliedPatch
{
    static void Prefix(Character __instance, AuraCurseData _acData, out int __state)
        => __state = _acData != null ? __instance.EffectCharges(_acData.Id) : 0;

    static void Postfix(Character __instance, AuraCurseData _acData, bool fromTrait,
        Character.AuraSetResult __result, int __state)
        => CombatNavigator.OnAuraApplied(__instance, _acData, fromTrait, __result, __state);
}
