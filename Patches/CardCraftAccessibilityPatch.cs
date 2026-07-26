using System;
using System.Collections.Generic;
using System.Text;
using Cards;
using HarmonyLib;
using UnityEngine;

namespace ObeliskAccess.Patches;

/// <summary>
/// The five town service screens — Altar (upgrade, craftType 0), Church (remove, 1), Forge (craft,
/// 2), Divination (3) and Armory (item shop, 4) — are one game prefab, <c>CardCraftManager</c>,
/// switched by <c>craftType</c>; this manager mirrors that: one focus model branching per type.
/// Up/Down walk the current list (deck rows, grid cells, tiers), Left/Right cycle pages (or the
/// A/B variant in the Altar preview), Tab cycles regions, 1–4 switch hero, Enter buys with a
/// single press. Opening is detected by polling (<c>ShowCardCraft</c> is async void — patching it
/// fires before the UI exists). Purchases announce through Hero.* postfixes because the game
/// finishes them in coroutines behind the public <c>blocked</c> flag.
/// </summary>
internal static class CardCraftScreenManager
{
    internal enum Region { List, DeckRef, Equipped, Controls }
    internal enum Phase { Browse, Preview, ConfirmRemove, FilterModal }

    private static Region _region = Region.List;
    private static Phase _phase = Phase.Browse;
    private static int _index;
    private static string _previewVariant = "A";

    // open/close lifecycle
    private static int _announcedInstanceId;
    private static int _readyFrames;

    // context for purchase postfixes
    private static string _pendingActionCard = "";
    private static string _pendingReplacedItem = "";

    // page-change announcements wait for the game to repopulate the grid
    private static bool _pendingPageAnnounce;
    private static bool _pendingFocusLast;
    private static int _pendingFrames;

    private static float _lastWaitSpeak;

    private static CardCraftManager Mgr => CardCraftManager.Instance;

    /// <summary>True for the five screens this layer owns (5–7 are challenge/corruption flows).</summary>
    private static bool Owned(CardCraftManager m) => m != null && m.craftType >= 0 && m.craftType <= 4;

    // ---------------------------------------------------------------- lifecycle

    public static void Tick()
    {
        var m = Mgr;
        if (!Owned(m) || !m.gameObject.activeSelf)
        {
            if (m == null && _announcedInstanceId != 0)
            {
                _announcedInstanceId = 0;
                _phase = Phase.Browse;
                _region = Region.List;
                _pendingPageAnnounce = false;
                _pendingMatchAnnounce = false;
            }
            return;
        }

        int id = m.GetInstanceID();
        if (id == _announcedInstanceId)
        {
            TickPendingPage(m);
            return;
        }

        // Freshly opened screen: wait until its content exists before announcing, since
        // ShowCardCraft populates the UI asynchronously.
        if (!ContentReady(m))
        {
            _readyFrames = 0;
            return;
        }
        if (++_readyFrames < 3)
            return; // let layout settle a couple of frames

        _announcedInstanceId = id;
        _region = Region.List;
        _phase = Phase.Browse;
        _index = -1; // sentinel: the first Down lands on item 1, the first Up on the last item
        _pendingPageAnnounce = false;
        _pendingActionCard = "";
        _pendingReplacedItem = "";
        SpeechManager.Speak(BuildOverview(m));
    }

    /// <summary>After a page change (ours or the game's reroll/shop-toggle), wait for the grid
    /// dictionary to repopulate, then speak "Page x of y" and the landing entry.</summary>
    private static void TickPendingPage(CardCraftManager m)
    {
        // Match-count feedback after a filter toggle, once the grid has rebuilt.
        if (_pendingMatchAnnounce && !m.blocked && ++_pendingMatchFrames >= 3)
        {
            _pendingMatchAnnounce = false;
            SpeechManager.SpeakQueued(MatchSummary(m));
        }

        if (!_pendingPageAnnounce || m.blocked)
            return;
        if (++_pendingFrames < 2)
            return;

        var entries = GridEntries(m);
        if (entries.Count == 0)
        {
            if (_pendingFrames > 120)
            {
                _pendingPageAnnounce = false;
                SpeechManager.Speak("No items on this page");
            }
            return;
        }
        _pendingPageAnnounce = false;
        _index = _pendingFocusLast ? entries.Count - 1 : 0;
        SpeechManager.Speak(PageLine(m) + " " + GridEntryLine(m, entries[_index], _index, entries.Count));
    }

    private static bool ContentReady(CardCraftManager m)
    {
        switch (m.craftType)
        {
            case 0:
            case 1:
                return DeckRows(m).Count > 0;
            case 2:
            case 4:
                var dict = GridDict(m);
                return dict != null && dict.Count > 0;
            case 3:
                return m.divinationButtonContainer != null && m.divinationButtonContainer.childCount > 0;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- shared guards

    /// <summary>Purchase coroutines hold <c>blocked</c> for up to ~5 s; keys are inert meanwhile.</summary>
    private static bool BlockedGuard()
    {
        var m = Mgr;
        if (m == null || !m.blocked)
            return false;
        if (Time.unscaledTime - _lastWaitSpeak > 1.5f)
        {
            _lastWaitSpeak = Time.unscaledTime;
            SpeechManager.Speak("Please wait");
        }
        return true;
    }

    // ---------------------------------------------------------------- navigation entry points

    public static void Move(int delta)
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;

        if (_phase == Phase.FilterModal)
        {
            MoveFilter(m, delta);
            return;
        }
        if (_phase == Phase.Preview || _phase == Phase.ConfirmRemove)
        {
            AnnouncePhase(m); // vertical keys just re-read the pending question/preview
            return;
        }

        switch (_region)
        {
            case Region.DeckRef:
                MoveDeckList(m, delta);
                return;
            case Region.Equipped:
                MoveEquipped(m, delta);
                return;
            case Region.Controls:
                MoveControls(m, delta);
                return;
        }

        switch (m.craftType)
        {
            case 0:
            case 1:
                MoveDeckList(m, delta);
                break;
            case 2:
            case 4:
                MoveGrid(m, delta);
                break;
            case 3:
                MoveDivination(m, delta);
                break;
        }
    }

    public static void MoveHorizontal(int delta)
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;

        if (_phase == Phase.Preview)
        {
            TogglePreviewVariant(m);
            return;
        }

        // Left/Right cycle pages on the shop grids; nothing elsewhere.
        if (_region == Region.List && (m.craftType == 2 || m.craftType == 4))
        {
            if (MaxPage(m) <= 1)
            {
                SpeechManager.Speak("Only one page");
                return;
            }
            RequestPage(m, delta, focusLast: false);
        }
    }

    public static void Activate()
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;

        switch (_phase)
        {
            case Phase.Preview:
                BuyPreviewedUpgrade(m);
                return;
            case Phase.ConfirmRemove:
                ConfirmRemove(m);
                return;
            case Phase.FilterModal:
                ToggleFocusedFilter(m);
                return;
        }

        switch (_region)
        {
            case Region.DeckRef:
                SpeechManager.Speak("Reference only. Tab to return to the craft list");
                return;
            case Region.Equipped:
                ActivateEquipped(m);
                return;
            case Region.Controls:
                ActivateControl(m);
                return;
        }

        switch (m.craftType)
        {
            case 0:
                BeginUpgradePreview(m);
                break;
            case 1:
                BeginRemoveConfirm(m);
                break;
            case 2:
                BuyFocusedCraft(m);
                break;
            case 3:
                BuyFocusedDivination(m);
                break;
            case 4:
                BuyFocusedItem(m);
                break;
        }
    }

    /// <summary>Escape: one level out. Returns true when consumed (always, while a screen is open).</summary>
    public static bool Cancel()
    {
        var m = Mgr;
        if (!Owned(m))
            return false;

        if (_phase == Phase.FilterModal)
        {
            CloseFilterModal(m);
            return true;
        }
        if (_phase == Phase.Preview || _phase == Phase.ConfirmRemove)
        {
            _phase = Phase.Browse;
            var rows = DeckRows(m);
            SpeechManager.Speak("Cancelled. " + (RowInRange(rows) ? DeckRowLine(m, rows[_index]) : ""));
            return true;
        }

        // MP event shops (map scene) close by READY-VOTE, not unilaterally: the game turns the
        // exit button into a Ready toggle (Ready() → SetManualReady), and only when every player
        // is ready does the master close all shops in lock-step (CheckForAllManualReady →
        // NET_CloseCardCraft → the "closevent" barrier passes at once). Calling ExitCardCraft
        // directly here destroyed the local shop early and stranded the player behind the sync
        // mask — no screen active, no speech, and the party could deadlock outright because the
        // manual-ready flag can no longer be set once the shop is gone. The game's own Escape
        // never exits a map shop at all (EscapeFunction excludes the Map scene).
        if (MpSpeech.IsMp && TownManager.Instance == null)
        {
            m.Ready(); // toggles the vote; collapses to ExitCardCraft in SP/town by game code
            bool ready = Traverse.Create(m).Field<bool>("statusReady").Value;
            SpeechManager.Speak(ready
                ? "Ready to leave. Waiting for the other players to finish shopping. Press Escape again to keep shopping."
                : "Staying in the shop.");
            return true;
        }

        m.ExitCardCraft(); // no-ops while blocked, mirroring the mouse path
        return true;
    }

    public static void CycleRegion(bool backwards)
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;
        if (_phase != Phase.Browse)
        {
            AnnouncePhase(m);
            return;
        }
        // Only the Forge and Armory have extra regions; the other screens re-announce.
        switch (m.craftType)
        {
            case 0:
            case 1:
                var rows = DeckRows(m);
                if (RowInRange(rows))
                    SpeechManager.Speak(DeckRowLine(m, rows[_index]));
                break;
            case 2:
                _region = _region == Region.List ? Region.DeckRef : Region.List;
                _index = 0;
                if (_region == Region.DeckRef)
                {
                    var refRows = DeckRows(m);
                    SpeechManager.Speak("Your deck, reference only. "
                        + (refRows.Count > 0 ? "1 of " + refRows.Count + ". " + DeckRowLine(m, refRows[0]) : "No cards"));
                }
                else
                {
                    AnnounceGridLanding(m, "Craft list. ");
                }
                break;
            case 3:
                var tiers = DivinationEntries(m);
                if (tiers.Count > 0 && _index < tiers.Count)
                    SpeechManager.Speak(DivinationLine(tiers[_index]));
                break;
            case 4:
                _region = _region == Region.List ? Region.Equipped
                    : _region == Region.Equipped ? Region.Controls : Region.List;
                _index = 0;
                if (_region == Region.Equipped)
                    SpeechManager.Speak("Equipped items. " + EquippedLine(m, 0));
                else if (_region == Region.Controls)
                {
                    var controls = BuildControls(m);
                    SpeechManager.Speak("Shop controls. "
                        + (controls.Count > 0 ? "1 of " + controls.Count + ". " + controls[0].Label : "None available"));
                }
                else
                    AnnounceGridLanding(m, "Shop list. ");
                break;
        }
    }

    private static void AnnounceGridLanding(CardCraftManager m, string prefix)
    {
        var entries = GridEntries(m);
        SpeechManager.Speak(prefix
            + (entries.Count > 0 ? PageLine(m) + " " + GridEntryLine(m, entries[0], 0, entries.Count) : "Nothing here"));
    }

    public static void SelectHeroSlot(int slot)
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;

        if (m.craftType == 3)
        {
            SpeechManager.Speak("Hero selection is not available at the divination cart");
            return;
        }
        if (_phase == Phase.FilterModal)
        {
            SpeechManager.Speak("Close the filters first");
            return;
        }

        var team = AtOManager.Instance?.team;
        var h = team != null ? team.GetHero(slot) : null;
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Slot " + (slot + 1) + " empty");
            return;
        }

        _phase = Phase.Browse;
        _region = Region.List;
        _index = -1;
        AtOManager.Instance.SideBarCharacterClicked(h);

        var sb = new StringBuilder("Now serving ").Append(h.SourceName);
        if (m.craftType <= 1 && h.Cards != null)
            sb.Append(", ").Append(h.Cards.Count).Append(h.Cards.Count == 1 ? " card" : " cards");
        SpeechManager.Speak(sb.ToString());
    }

    // ---------------------------------------------------------------- deck screens (Altar/Church)

    private static void MoveDeckList(CardCraftManager m, int delta)
    {
        var rows = DeckRows(m);
        if (rows.Count == 0)
        {
            SpeechManager.Speak("No cards");
            return;
        }
        _index = Step(_index, delta, rows.Count);
        SpeechManager.Speak((_index + 1) + " of " + rows.Count + ". " + DeckRowLine(m, rows[_index]));
    }

    /// <summary>Advance an index with wrap; a negative index is the fresh-screen sentinel, from
    /// which Down lands on the first item and Up on the last.</summary>
    private static int Step(int index, int delta, int count)
    {
        if (index < 0)
            return delta > 0 ? 0 : count - 1;
        if (index >= count)
            index = count - 1;
        return Wrap(index + delta, count);
    }

    private static string DeckRowLine(CardCraftManager m, CardVertical row)
    {
        var cd = row.cardData;
        var sb = new StringBuilder(CardSpeech.BriefLine(cd));
        if (row.IsLocked())
        {
            sb.Append(", locked");
            return sb.ToString();
        }

        if (m.craftType == 1 && cd != null)
        {
            string reason = RemoveBlockedReason(m, cd);
            if (reason != null)
                sb.Append(", ").Append(reason);
            else
                sb.Append(", removable");
        }
        else if (m.craftType == 0 && cd != null)
        {
            if (cd.CardUpgraded == Enums.CardUpgraded.No
                && (string.IsNullOrEmpty(cd.UpgradesTo1) && string.IsNullOrEmpty(cd.UpgradesTo2)))
                sb.Append(", no upgrades");
        }
        return sb.ToString();
    }

    /// <summary>Why this card can't be removed, or null if it can. Mirrors the game's min-deck rule:
    /// injuries and boons are always removable, other cards only while more than 15 remain.</summary>
    private static string RemoveBlockedReason(CardCraftManager m, CardRealtimeData cd)
    {
        if (cd.CardClass == Enums.CardClass.Injury || cd.CardClass == Enums.CardClass.Boon)
            return null;
        if (SandboxManager.Instance != null && SandboxManager.Instance.NoMinimumDecksize)
            return null;

        var hero = m.CurrentHero;
        if (hero?.Cards == null)
            return null;
        int countable = 0;
        foreach (var id in hero.Cards)
        {
            var c = Globals.Instance?.GetCardData(id, false);
            if (c != null && c.CardClass != Enums.CardClass.Injury && c.CardClass != Enums.CardClass.Boon)
                countable++;
        }
        return countable <= 15 ? "cannot remove, deck at the minimum of 15 cards" : null;
    }

    // ---- Altar: preview + buy ----

    private static void BeginUpgradePreview(CardCraftManager m)
    {
        var rows = DeckRows(m);
        if (!RowInRange(rows))
            return;
        var row = rows[_index];
        var cd = row.cardData;
        if (row.IsLocked() || cd == null)
        {
            SpeechManager.Speak("This card cannot be upgraded");
            return;
        }
        if (cd.CardUpgraded == Enums.CardUpgraded.No
            && string.IsNullOrEmpty(cd.UpgradesTo1) && string.IsNullOrEmpty(cd.UpgradesTo2))
        {
            SpeechManager.Speak("No upgrades available for this card");
            return;
        }

        SelectRow(m, row);
        // Already-upgraded cards can only transmute to the sibling path.
        _previewVariant = cd.CardUpgraded == Enums.CardUpgraded.A ? "B" : "A";
        _phase = Phase.Preview;
        AnnouncePreview(m);
    }

    private static void TogglePreviewVariant(CardCraftManager m)
    {
        var cd = SelectedCard(m);
        if (cd == null)
            return;
        if (cd.CardUpgraded != Enums.CardUpgraded.No)
        {
            AnnouncePreview(m); // transmute: only one target, just re-read it
            return;
        }
        _previewVariant = _previewVariant == "A" ? "B" : "A";
        AnnouncePreview(m);
    }

    private static void AnnouncePreview(CardCraftManager m)
    {
        var cd = SelectedCard(m);
        if (cd == null)
        {
            SpeechManager.Speak("Nothing selected");
            return;
        }

        string targetId = _previewVariant == "A" ? cd.UpgradesTo1 : cd.UpgradesTo2;
        var target = string.IsNullOrEmpty(targetId) ? null : Globals.Instance?.GetCardData(targetId, false);
        int cost = Traverse.Create(m).Field<int>(_previewVariant == "A" ? "costA" : "costB").Value;

        var sb = new StringBuilder();
        bool transmute = cd.CardUpgraded != Enums.CardUpgraded.No;
        sb.Append(transmute ? "Transmute to " : "Upgrade ").Append(_previewVariant).Append(": ");
        sb.Append(target != null ? CardSpeech.FullDetail(target) : "unknown card");
        if (cost > 0 && cost < 1000000)
            sb.Append(". Costs ").Append(cost).Append(" dust");
        if (!transmute)
            sb.Append(". Left and right to compare, Enter to buy, Escape to cancel");
        else
            sb.Append(". Enter to buy, Escape to cancel");
        SpeechManager.Speak(sb.ToString());
    }

    private static void BuyPreviewedUpgrade(CardCraftManager m)
    {
        var cd = SelectedCard(m);
        if (cd == null)
            return;
        int cost = Traverse.Create(m).Field<int>(_previewVariant == "A" ? "costA" : "costB").Value;
        int dust = AtOManager.Instance?.CurrencyManager?.GetPlayerDust() ?? 0;
        if (cost > 0 && cost < 1000000 && dust < cost)
        {
            // The game's CanBuy fails silently on the keyboard path; say why nothing happened.
            SpeechManager.Speak("Not enough dust. Costs " + cost + ", you have " + dust);
            return;
        }
        string targetId = _previewVariant == "A" ? cd.UpgradesTo1 : cd.UpgradesTo2;
        var target = string.IsNullOrEmpty(targetId) ? null : Globals.Instance?.GetCardData(targetId, false);
        _pendingActionCard = target != null ? AccessibleMenuBase.StripRichText(target.CardName) : "card";
        _phase = Phase.Browse;
        m.BuyUpgrade(_previewVariant);
        // Success is announced by the Hero.OnCardUpgraded postfix; failure paths (CanBuy) leave
        // the game silent, so give a fallback cue if nothing was charged.
    }

    // ---- Church: confirm + remove ----

    private static void BeginRemoveConfirm(CardCraftManager m)
    {
        var rows = DeckRows(m);
        if (!RowInRange(rows))
            return;
        var row = rows[_index];
        var cd = row.cardData;
        if (row.IsLocked() || cd == null)
        {
            SpeechManager.Speak("This card cannot be removed");
            return;
        }
        string reason = RemoveBlockedReason(m, cd);
        if (reason != null)
        {
            SpeechManager.Speak(AccessibleMenuBase.StripRichText(cd.CardName) + ": " + reason);
            return;
        }

        SelectRow(m, row);
        int cost = Traverse.Create(m).Field<int>("costRemove").Value;
        _phase = Phase.ConfirmRemove;
        string name = AccessibleMenuBase.StripRichText(cd.CardName);
        SpeechManager.Speak("Remove " + name + " for "
            + (cost <= 0 ? "free" : cost + " gold") + "? Enter to confirm, Escape to cancel");
    }

    private static void ConfirmRemove(CardCraftManager m)
    {
        var cd = SelectedCard(m);
        int cost = Traverse.Create(m).Field<int>("costRemove").Value;
        int gold = AtOManager.Instance?.CurrencyManager?.GetPlayerGold() ?? 0;
        if (cost > 0 && gold < cost)
        {
            SpeechManager.Speak("Not enough gold. Costs " + cost + ", you have " + gold);
            return;
        }
        _pendingActionCard = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : "card";
        _phase = Phase.Browse;
        m.RemoveCard();
        // Announced by the Hero.OnCardRemoved postfix.
    }

    // ---------------------------------------------------------------- shop grids (Forge/Armory)

    /// <summary>The live page grid, in slot order. Never cache: the game reallocates the
    /// dictionary on every page change, reroll and shop toggle.</summary>
    private static List<CardCraftItem> GridEntries(CardCraftManager m)
    {
        var list = new List<CardCraftItem>();
        var dict = GridDict(m);
        if (dict == null)
            return list;
        var keys = new List<int>(dict.Keys);
        keys.Sort();
        foreach (var k in keys)
            if (dict[k] != null)
                list.Add(dict[k]);
        return list;
    }

    private static int CurrentPage(CardCraftManager m)
        => m.craftType == 4
            ? m.CurrentItemsPageNum
            : Traverse.Create(m).Field<int>("currentCraftPageNum").Value;

    private static int MaxPage(CardCraftManager m)
    {
        int max = Traverse.Create(m).Field<int>("maxCraftPageNum").Value;
        return max < 1 ? 1 : max;
    }

    private static string PageLine(CardCraftManager m)
        => "Page " + CurrentPage(m) + " of " + MaxPage(m) + ".";

    /// <summary>The visible buy-button label — the game's own price text, discounts included.</summary>
    private static string PriceText(CardCraftManager m, CardCraftItem item)
    {
        Transform btn = m.craftType == 4 ? item.buttonItem : item.button;
        var bg = btn != null ? btn.GetComponent<BotonGeneric>() : null;
        return bg != null ? CardSpeech.CleanFlat(bg.GetText()) : "";
    }

    private static string GridEntryLine(CardCraftManager m, CardCraftItem item, int pos, int count)
    {
        var cd = Globals.Instance?.GetCardData(item.cardId, false);
        var sb = new StringBuilder();
        sb.Append(pos + 1).Append(" of ").Append(count).Append(". ");
        sb.Append(CardSpeech.BriefLine(cd));

        if (m.craftType == 4 && cd != null)
            sb.Append(", ").Append(SlotWord(cd.CardType));

        string price = PriceText(m, item);
        if (price.Length > 0)
            sb.Append(", ").Append(price);

        if (!item.Available)
        {
            sb.Append(", sold out");
            AppendBuyer(sb, m, item);
        }
        else
        {
            if (m.craftType == 2 && cd != null)
            {
                var avail = m.GetCardAvailability(cd.Id, Traverse.Create(item).Field<string>("shopId").Value);
                if (avail != null && avail.Length == 2 && avail[1] > 0)
                {
                    int remaining = avail[1] - avail[0];
                    sb.Append(", ").Append(remaining).Append(" remaining");
                }
            }
            if (!item.Enabled)
                sb.Append(", cannot buy now");
        }
        return sb.ToString();
    }

    /// <summary>Multiplayer: name who bought a sold item, as the buyer-portrait stamp does.</summary>
    private static void AppendBuyer(StringBuilder sb, CardCraftManager m, CardCraftItem item)
    {
        var shop = AtOManager.Instance?.ShopManager;
        if (shop == null || !GameManager.Instance.IsMultiplayer())
            return;
        string shopId = Traverse.Create(item).Field<string>("shopId").Value;
        int who = shop.WhoBoughtOnThisShop(shopId, item.cardId);
        if (who < 0)
            return;
        var h = AtOManager.Instance.team?.GetHero(who);
        if (h != null && h.HeroData != null)
            sb.Append(" to ").Append(h.SourceName);
    }

    private static void MoveGrid(CardCraftManager m, int delta)
    {
        var entries = GridEntries(m);
        if (entries.Count == 0)
        {
            SpeechManager.Speak(m.craftType == 2 ? "No cards match the current filters" : "No items");
            return;
        }
        if (_index >= entries.Count)
            _index = entries.Count - 1;

        int next = _index + delta;
        int maxPage = MaxPage(m);
        if (next >= entries.Count)
        {
            if (maxPage > 1)
            {
                RequestPage(m, 1, focusLast: false);
                return;
            }
            next = 0;
        }
        else if (next < 0)
        {
            if (maxPage > 1)
            {
                RequestPage(m, -1, focusLast: true);
                return;
            }
            next = entries.Count - 1;
        }
        _index = next;
        SpeechManager.Speak(GridEntryLine(m, entries[_index], _index, entries.Count));
    }

    /// <summary>Flip to the next/previous page (wrapping) and queue the landing announcement for
    /// when the grid has repopulated.</summary>
    private static void RequestPage(CardCraftManager m, int dir, bool focusLast)
    {
        int cur = CurrentPage(m);
        int max = MaxPage(m);
        int target = cur + dir;
        if (target > max)
            target = 1;
        else if (target < 1)
            target = max;
        _pendingPageAnnounce = true;
        _pendingFocusLast = focusLast;
        _pendingFrames = 0;
        m.ChangePage(target);
    }

    private static void BuyFocusedCraft(CardCraftManager m)
    {
        var entries = GridEntries(m);
        if (entries.Count == 0 || _index >= entries.Count)
            return;
        var item = entries[_index];
        var cd = Globals.Instance?.GetCardData(item.cardId, false);
        if (!item.Available)
        {
            SpeechManager.Speak("Sold out");
            return;
        }
        if (!item.Enabled)
        {
            string price = PriceText(m, item);
            SpeechManager.Speak("Cannot buy now" + (price.Length > 0 ? ". Costs " + price : ""));
            return;
        }
        _pendingActionCard = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : "card";
        m.BuyCraft(item.cardId);
        // Announced by the Hero.OnCardCrafted postfix.
    }

    private static void BuyFocusedItem(CardCraftManager m)
    {
        var entries = GridEntries(m);
        if (entries.Count == 0 || _index >= entries.Count)
            return;
        var item = entries[_index];
        var cd = Globals.Instance?.GetCardData(item.cardId, false);
        if (!item.Available)
        {
            SpeechManager.Speak("Sold out");
            return;
        }
        if (!item.Enabled)
        {
            string price = PriceText(m, item);
            SpeechManager.Speak("Cannot buy now" + (price.Length > 0 ? ". Costs " + price : ""));
            return;
        }

        _pendingActionCard = cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : "item";
        _pendingReplacedItem = "";
        var hero = m.CurrentHero;
        if (cd != null && hero != null)
        {
            string oldId = EquippedId(hero, cd.CardType);
            if (!string.IsNullOrEmpty(oldId))
            {
                var oldCd = Globals.Instance?.GetCardData(oldId, false);
                if (oldCd != null)
                    _pendingReplacedItem = AccessibleMenuBase.StripRichText(oldCd.CardName);
            }
        }
        m.BuyItem(item.cardId);
        // Announced by the Hero.AddItem postfix.
    }

    // ---- Armory: equipped items region ----

    private static readonly Enums.CardType[] _slotTypes =
    {
        Enums.CardType.Weapon, Enums.CardType.Armor, Enums.CardType.Jewelry,
        Enums.CardType.Accesory, Enums.CardType.Pet,
    };

    private static string SlotWord(Enums.CardType t)
    {
        if (t == Enums.CardType.Accesory)
            return "accessory";
        return t.ToString().ToLowerInvariant();
    }

    private static string EquippedId(Hero h, Enums.CardType t)
    {
        switch (t)
        {
            case Enums.CardType.Weapon: return h.Weapon;
            case Enums.CardType.Armor: return h.Armor;
            case Enums.CardType.Jewelry: return h.Jewelry;
            case Enums.CardType.Accesory: return h.Accesory;
            case Enums.CardType.Pet: return h.Pet;
            default: return "";
        }
    }

    private static void MoveEquipped(CardCraftManager m, int delta)
    {
        _index = Wrap(_index + delta, _slotTypes.Length);
        SpeechManager.Speak(EquippedLine(m, _index));
    }

    private static string EquippedLine(CardCraftManager m, int slot)
    {
        var hero = m.CurrentHero;
        var type = _slotTypes[slot];
        string prefix = (slot + 1) + " of " + _slotTypes.Length + ". " + SlotWord(type) + ": ";
        string id = hero != null ? EquippedId(hero, type) : "";
        if (string.IsNullOrEmpty(id))
            return prefix + "empty";
        var cd = Globals.Instance?.GetCardData(id, false);
        return prefix + (cd != null ? CardSpeech.BriefLine(cd) : id);
    }

    private static void ActivateEquipped(CardCraftManager m)
    {
        var hero = m.CurrentHero;
        var type = _slotTypes[_index < _slotTypes.Length ? _index : 0];
        string id = hero != null ? EquippedId(hero, type) : "";
        if (string.IsNullOrEmpty(id))
        {
            SpeechManager.Speak(SlotWord(type) + " slot is empty");
            return;
        }
        var cd = Globals.Instance?.GetCardData(id, false);
        SpeechManager.Speak(cd != null ? CardSpeech.FullDetail(cd) : id);
    }

    // ---- Armory: shop controls region ----

    private class ControlEntry
    {
        public string Label;
        public Action Act;
    }

    private static List<ControlEntry> BuildControls(CardCraftManager m)
    {
        var list = new List<ControlEntry>();

        if (m.petShopButton != null && m.petShopButton.gameObject.activeSelf)
            list.Add(new ControlEntry
            {
                Label = "Switch to pet shop",
                Act = () => { SwitchShop(m, pets: true); },
            });
        if (m.itemShopButton != null && m.itemShopButton.gameObject.activeSelf)
            list.Add(new ControlEntry
            {
                Label = "Switch to item shop",
                Act = () => { SwitchShop(m, pets: false); },
            });
        if (m.rerollButton != null && m.rerollButton.gameObject.activeSelf)
        {
            int cost = Globals.Instance != null ? Globals.Instance.GetCostReroll() : 0;
            list.Add(new ControlEntry
            {
                Label = "Reroll shop stock, costs " + cost + " gold",
                Act = () => { Reroll(m, cost); },
            });
        }
        if (m.shadyDealButton != null && m.shadyDealButton.gameObject.activeSelf)
        {
            var bg = m.shadyDealButton.GetComponent<BotonGeneric>();
            if (bg != null)
                list.Add(new ControlEntry
                {
                    Label = "Shady deal: " + CardSpeech.CleanFlat(bg.GetText()),
                    Act = () => bg.Clicked(),
                });
        }
        return list;
    }

    private static void SwitchShop(CardCraftManager m, bool pets)
    {
        if (pets)
            m.DoPetShop();
        else
            m.DoItemShop();
        _region = Region.List;
        _index = 0;
        _pendingPageAnnounce = true;
        _pendingFocusLast = false;
        _pendingFrames = 0;
        SpeechManager.Speak(pets ? "Pet shop" : "Item shop");
    }

    private static void Reroll(CardCraftManager m, int cost)
    {
        var cm = AtOManager.Instance?.CurrencyManager;
        int gold = cm?.GetPlayerGold() ?? 0;
        if (gold < cost)
        {
            SpeechManager.Speak("Not enough gold. Costs " + cost + ", you have " + gold);
            return;
        }
        m.RerollItems();
        _region = Region.List;
        _index = 0;
        _pendingPageAnnounce = true;
        _pendingFocusLast = false;
        _pendingFrames = 0;
        SpeechManager.Speak("Rerolled");
    }

    private static void MoveControls(CardCraftManager m, int delta)
    {
        var controls = BuildControls(m);
        if (controls.Count == 0)
        {
            SpeechManager.Speak("No shop controls");
            return;
        }
        if (_index >= controls.Count)
            _index = controls.Count - 1;
        _index = Wrap(_index + delta, controls.Count);
        SpeechManager.Speak((_index + 1) + " of " + controls.Count + ". " + controls[_index].Label);
    }

    private static void ActivateControl(CardCraftManager m)
    {
        var controls = BuildControls(m);
        if (controls.Count == 0 || _index >= controls.Count)
            return;
        controls[_index].Act();
    }

    // ---------------------------------------------------------------- Divination (type 3)

    private class DivEntry
    {
        public int Tier;
        public int Cost;
        public bool Locked;
    }

    /// <summary>Visible tier buttons first (auxInt = tier, auxString = cost), then any of tiers
    /// 0–4 the game didn't build, listed as locked so the player knows what exists.</summary>
    private static List<DivEntry> DivinationEntries(CardCraftManager m)
    {
        var list = new List<DivEntry>();
        var present = new HashSet<int>();
        var container = m.divinationButtonContainer;
        if (container != null)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                var t = container.GetChild(i);
                if (!t.gameObject.activeSelf)
                    continue;
                var bg = t.GetComponent<BotonGeneric>();
                if (bg == null || bg.auxInt < 0 || bg.auxInt > 4 || present.Contains(bg.auxInt))
                    continue;
                int.TryParse(bg.auxString, out int cost);
                list.Add(new DivEntry { Tier = bg.auxInt, Cost = cost });
                present.Add(bg.auxInt);
            }
        }
        list.Sort((a, b) => a.Tier.CompareTo(b.Tier));
        for (int tier = 0; tier <= 4; tier++)
            if (!present.Contains(tier))
                list.Add(new DivEntry { Tier = tier, Locked = true });
        return list;
    }

    private static string DivinationLine(DivEntry e)
    {
        string title = Texts.Instance != null
            ? CardSpeech.CleanFlat(Texts.Instance.GetText("divinationT" + e.Tier)) : "";
        if (title.Length == 0)
            title = "Divination tier " + e.Tier;

        if (e.Locked)
            return title + ", locked. Requires a higher town tier or cart upgrades";

        var cm = AtOManager.Instance?.CurrencyManager;
        int gold = cm?.GetPlayerGold() ?? 0;
        string line = title + ", " + (e.Cost <= 0 ? "free" : e.Cost + " gold");
        if (e.Cost > gold)
            line += ", cannot afford";
        return line;
    }

    private static void MoveDivination(CardCraftManager m, int delta)
    {
        var entries = DivinationEntries(m);
        if (entries.Count == 0)
        {
            SpeechManager.Speak("No divinations available");
            return;
        }
        _index = Step(_index, delta, entries.Count);
        SpeechManager.Speak((_index + 1) + " of " + entries.Count + ". " + DivinationLine(entries[_index]));
    }

    private static void BuyFocusedDivination(CardCraftManager m)
    {
        var entries = DivinationEntries(m);
        if (entries.Count == 0)
            return;
        if (_index < 0 || _index >= entries.Count)
            _index = 0;
        var e = entries[_index];
        if (e.Locked)
        {
            SpeechManager.Speak(DivinationLine(e));
            return;
        }
        var cm = AtOManager.Instance?.CurrencyManager;
        int goldBefore = cm?.GetPlayerGold() ?? 0;
        if (e.Cost > goldBefore)
        {
            SpeechManager.Speak("Not enough gold. Costs " + e.Cost + ", you have " + goldBefore);
            return;
        }

        m.BuyDivination(e.Tier);

        int goldAfter = cm?.GetPlayerGold() ?? goldBefore;
        if (goldAfter < goldBefore || e.Cost == 0)
            SpeechManager.SpeakQueued("Divination purchased. " + goldAfter + " gold remaining");
        // The game then opens its reward screen (handled by RewardsScreenManager) or, in
        // multiplayer, waits for the other players.
    }

    // ---------------------------------------------------------------- selection plumbing

    /// <summary>Select a deck row exactly as a click would: pass its private "id_index" key.</summary>
    private static void SelectRow(CardCraftManager m, CardVertical row)
    {
        string internalId = Traverse.Create(row).Field<string>("internalId").Value;
        if (!string.IsNullOrEmpty(internalId))
            m.SelectCard(internalId);
    }

    /// <summary>The card the game currently has selected (set by SelectCard).</summary>
    private static CardRealtimeData SelectedCard(CardCraftManager m)
        => Traverse.Create(m).Field<CardRealtimeData>("cardData").Value;

    private static List<CardVertical> DeckRows(CardCraftManager m)
    {
        var list = new List<CardVertical>();
        if (m == null || m.cardListContainer == null)
            return list;
        for (int i = 0; i < m.cardListContainer.childCount; i++)
        {
            var t = m.cardListContainer.GetChild(i);
            if (!t.gameObject.activeSelf)
                continue;
            var cv = t.GetComponent<CardVertical>();
            if (cv != null)
                list.Add(cv);
        }
        return list;
    }

    private static bool RowInRange(List<CardVertical> rows)
    {
        if (rows.Count == 0)
            return false;
        if (_index >= rows.Count)
            _index = rows.Count - 1;
        return true;
    }

    private static Dictionary<int, CardCraftItem> GridDict(CardCraftManager m)
        => Traverse.Create(m).Field<Dictionary<int, CardCraftItem>>("craftCardItemDict").Value;

    // ---------------------------------------------------------------- read-outs

    private static void AnnouncePhase(CardCraftManager m)
    {
        if (_phase == Phase.FilterModal)
        {
            var entries = BuildFilterEntries(m);
            if (entries.Count > 0 && _filterIndex < entries.Count)
                SpeechManager.Speak(FilterEntryLine(entries[_filterIndex]));
            return;
        }
        if (_phase == Phase.Preview)
            AnnouncePreview(m);
        else if (_phase == Phase.ConfirmRemove)
        {
            var cd = SelectedCard(m);
            int cost = Traverse.Create(m).Field<int>("costRemove").Value;
            SpeechManager.Speak("Remove "
                + (cd != null ? AccessibleMenuBase.StripRichText(cd.CardName) : "card") + " for "
                + (cost <= 0 ? "free" : cost + " gold") + "? Enter to confirm, Escape to cancel");
        }
    }

    public static void SpeakFocusedDetail()
    {
        var m = Mgr;
        if (!Owned(m))
            return;
        if (_phase == Phase.Preview || _phase == Phase.ConfirmRemove)
        {
            AnnouncePhase(m);
            return;
        }
        if (_region == Region.DeckRef)
        {
            var refRows = DeckRows(m);
            SpeechManager.Speak(RowInRange(refRows)
                ? CardSpeech.FullDetail(refRows[_index].cardData) : "Nothing focused");
            return;
        }
        if (_region == Region.Equipped)
        {
            ActivateEquipped(m); // same read: full detail of the focused slot
            return;
        }
        if (_region == Region.Controls)
        {
            var controls = BuildControls(m);
            SpeechManager.Speak(controls.Count > 0 && _index < controls.Count
                ? controls[_index].Label : "Nothing focused");
            return;
        }

        switch (m.craftType)
        {
            case 0:
            case 1:
                var rows = DeckRows(m);
                if (RowInRange(rows))
                    SpeechManager.Speak(CardSpeech.FullDetail(rows[_index].cardData));
                else
                    SpeechManager.Speak("Nothing focused");
                break;
            case 2:
            case 4:
                var entries = GridEntries(m);
                if (entries.Count == 0 || _index >= entries.Count)
                {
                    SpeechManager.Speak("Nothing focused");
                    break;
                }
                var item = entries[_index];
                var cd = Globals.Instance?.GetCardData(item.cardId, false);
                var detail = new StringBuilder(CardSpeech.FullDetail(cd));
                string price = PriceText(m, item);
                if (price.Length > 0)
                    detail.Append(". Costs ").Append(price);
                if (m.craftType == 4 && cd != null && m.CurrentHero != null)
                {
                    string oldId = EquippedId(m.CurrentHero, cd.CardType);
                    var oldCd = string.IsNullOrEmpty(oldId) ? null : Globals.Instance?.GetCardData(oldId, false);
                    detail.Append(". Currently equipped: ")
                        .Append(oldCd != null ? AccessibleMenuBase.StripRichText(oldCd.CardName) : "nothing");
                }
                SpeechManager.Speak(detail.ToString());
                break;
            case 3:
                var tiers = DivinationEntries(m);
                if (tiers.Count > 0 && _index < tiers.Count)
                    SpeechManager.Speak(DivinationLine(tiers[_index])
                        + ". Buys a card reward draw whose quality scales with the tier");
                else
                    SpeechManager.Speak("Nothing focused");
                break;
            default:
                SpeechManager.Speak("Nothing focused");
                break;
        }
    }

    public static void SpeakCurrencies()
    {
        TownScreenManager.SpeakCurrencies();
    }

    public static void SpeakOverview()
    {
        var m = Mgr;
        if (Owned(m))
            SpeechManager.Speak(BuildOverview(m));
    }

    public static void ToggleFilterModal()
    {
        var m = Mgr;
        if (!Owned(m) || BlockedGuard())
            return;
        if (m.craftType != 2)
        {
            SpeechManager.Speak("No filters here. Only the forge has filters");
            return;
        }
        if (_phase == Phase.FilterModal)
        {
            CloseFilterModal(m);
            return;
        }
        if (_phase != Phase.Browse)
            return;

        _phase = Phase.FilterModal;
        _region = Region.List;
        _filterIndex = 0;
        var entries = BuildFilterEntries(m);
        SpeechManager.Speak("Filters. " + entries.Count
            + " entries. Up and down to browse, Enter to toggle, Escape to close. "
            + (entries.Count > 0 ? "1 of " + entries.Count + ". " + FilterEntryLine(entries[0]) : ""));
    }

    // ---------------------------------------------------------------- filter modal (Forge)

    private static int _filterIndex;
    private static bool _pendingMatchAnnounce;
    private static int _pendingMatchFrames;

    private class FilterEntry
    {
        public string Label;
        public Func<bool> State;
        public Action Toggle;
        public Func<string> StateWord; // overrides on/off wording when set (energy single-select)
    }

    /// <summary>Rebuilt on every use — the components are stable but their state is live.</summary>
    private static List<FilterEntry> BuildFilterEntries(CardCraftManager m)
    {
        var list = new List<FilterEntry>();
        var tr = Traverse.Create(m);

        // Energy: single-select — picking a cost shows only it, picking it again shows all.
        if (m.cardCraftEnergySelectorContainer != null)
        {
            for (int i = 0; i < m.cardCraftEnergySelectorContainer.childCount; i++)
            {
                var comp = m.cardCraftEnergySelectorContainer.GetChild(i).GetComponent<CardCraftSelectorEnergy>();
                if (comp == null || comp.tText == null)
                    continue;
                string energyText = comp.tText.text;
                int energyValue = energyText == "4+" ? 4 : ParseOrZero(energyText);
                var selector = comp;
                list.Add(new FilterEntry
                {
                    Label = "Energy cost " + energyText,
                    State = () => !tr.Field<bool>("currentCraftAllCosts").Value
                        && tr.Field<int>("currentCraftCost").Value == energyValue,
                    Toggle = () => m.CraftSelectorEnergy(selector, energyText),
                    StateWord = () => tr.Field<bool>("currentCraftAllCosts").Value
                        ? "off, showing all costs" : "on, showing only this cost",
                });
            }
        }

        // Rarity: independent toggles.
        if (m.cardCraftRaritySelectorContainer != null)
        {
            for (int i = 0; i < m.cardCraftRaritySelectorContainer.childCount; i++)
            {
                var comp = m.cardCraftRaritySelectorContainer.GetChild(i).GetComponent<CardCraftSelectorRarity>();
                if (comp == null)
                    continue;
                var selector = comp;
                var rarity = comp.rarity;
                list.Add(new FilterEntry
                {
                    Label = "Rarity " + rarity.ToString().ToLowerInvariant(),
                    State = () =>
                    {
                        var dict = tr.Field<Dictionary<Enums.CardRarity, bool>>("currentCraftRarity").Value;
                        return dict != null && dict.ContainsKey(rarity) && dict[rarity];
                    },
                    Toggle = () => m.CraftSelectorRarity(selector, rarity),
                });
            }
        }

        // Damage type / aura / curse: BotonFilter components on the (hidden) filter window.
        if (m.filterWindow != null)
        {
            foreach (var bf in m.filterWindow.GetComponentsInChildren<BotonFilter>(true))
            {
                if (bf == null || string.IsNullOrEmpty(bf.id))
                    continue;
                var boton = bf;
                string category = boton.type == "dt" ? "damage type"
                    : boton.type == "aura" ? "aura" : "curse";
                string name = Texts.Instance != null
                    ? AccessibleMenuBase.StripRichText(Texts.Instance.GetText(boton.id)) : boton.id;
                list.Add(new FilterEntry
                {
                    Label = name + " " + category,
                    State = () => AppliedFilterList(boton.type).Contains(boton.id),
                    Toggle = () =>
                    {
                        bool newState = !AppliedFilterList(boton.type).Contains(boton.id);
                        SeedWorkingFilters(m);
                        m.SelectFilter(boton.type, boton.id, newState);
                        m.ApplyFilter();
                    },
                });
            }
        }

        list.Add(new FilterEntry
        {
            Label = "Affordable only",
            State = () => AtOManager.Instance != null && AtOManager.Instance.affordableCraft,
            Toggle = () => m.AffordableCraft(change: true),
        });
        list.Add(new FilterEntry
        {
            Label = "Show upgraded variants",
            State = () => AtOManager.Instance != null && AtOManager.Instance.advancedCraft,
            Toggle = () => m.AdvancedCraft(change: true),
        });
        list.Add(new FilterEntry
        {
            Label = "Reset damage type, aura and curse filters",
            State = () => false,
            Toggle = () => m.ResetFilter(),
            StateWord = () => "done",
        });

        return list;
    }

    /// <summary>The applied (authoritative) dt/aura/curse filter list on AtOManager.</summary>
    private static List<string> AppliedFilterList(string type)
    {
        var ato = AtOManager.Instance;
        List<string> list = null;
        if (ato != null)
            list = type == "dt" ? ato.craftFilterDT : type == "aura" ? ato.craftFilterAura : ato.craftFilterCurse;
        return list ?? new List<string>();
    }

    /// <summary>ShowFilter(true) normally seeds the working lists from the applied ones; since the
    /// modal never opens the visual window, seed them here before each SelectFilter.</summary>
    private static void SeedWorkingFilters(CardCraftManager m)
    {
        m.craftFilterDT = new List<string>(AppliedFilterList("dt"));
        m.craftFilterAura = new List<string>(AppliedFilterList("aura"));
        m.craftFilterCurse = new List<string>(AppliedFilterList("curse"));
    }

    private static string FilterEntryLine(FilterEntry e)
    {
        if (e.StateWord != null && e.State())
            return e.Label + ", " + e.StateWord();
        return e.Label + ", " + (e.State() ? "on" : "off");
    }

    private static void MoveFilter(CardCraftManager m, int delta)
    {
        var entries = BuildFilterEntries(m);
        if (entries.Count == 0)
        {
            SpeechManager.Speak("No filters");
            return;
        }
        if (_filterIndex >= entries.Count)
            _filterIndex = entries.Count - 1;
        _filterIndex = Wrap(_filterIndex + delta, entries.Count);
        SpeechManager.Speak((_filterIndex + 1) + " of " + entries.Count + ". " + FilterEntryLine(entries[_filterIndex]));
    }

    private static void ToggleFocusedFilter(CardCraftManager m)
    {
        var entries = BuildFilterEntries(m);
        if (entries.Count == 0 || _filterIndex >= entries.Count)
            return;
        var e = entries[_filterIndex];
        e.Toggle();
        string state = e.StateWord != null ? e.StateWord() : (e.State() ? "on" : "off");
        SpeechManager.Speak(e.Label + ", " + state);
        _pendingMatchAnnounce = true;
        _pendingMatchFrames = 0;
    }

    private static void CloseFilterModal(CardCraftManager m)
    {
        _phase = Phase.Browse;
        _index = 0;
        SpeechManager.Speak("Filters closed. " + MatchSummary(m));
        var entries = GridEntries(m);
        if (entries.Count > 0)
            SpeechManager.SpeakQueued(PageLine(m) + " " + GridEntryLine(m, entries[0], 0, entries.Count));
    }

    private static string MatchSummary(CardCraftManager m)
    {
        int max = MaxPage(m);
        int onPage = GridEntries(m).Count;
        if (onPage == 0)
            return "No cards match the current filters";
        if (max <= 1)
            return onPage + (onPage == 1 ? " card matches" : " cards match");
        return max + " pages of cards match";
    }

    private static int ParseOrZero(string s)
    {
        int v;
        return int.TryParse(s, out v) ? v : 0;
    }

    private static string BuildOverview(CardCraftManager m)
    {
        var cm = AtOManager.Instance?.CurrencyManager;
        var sb = new StringBuilder();
        var hero = m.CurrentHero;

        switch (m.craftType)
        {
            case 0:
                sb.Append("Altar. Upgrade cards");
                if (hero != null) sb.Append(" for ").Append(hero.SourceName);
                sb.Append(". ").Append(cm?.GetPlayerDust() ?? 0).Append(" dust.");
                AppendDeckCount(sb, m);
                sb.Append(" Up and down to choose, Enter to preview upgrades.");
                break;
            case 1:
                sb.Append("Church. Remove cards");
                if (hero != null) sb.Append(" for ").Append(hero.SourceName);
                sb.Append(". ").Append(cm?.GetPlayerGold() ?? 0).Append(" gold.");
                AppendDeckCount(sb, m);
                sb.Append(" Up and down to choose, Enter to remove.");
                break;
            case 2:
                sb.Append("Forge. Craft cards");
                if (hero != null) sb.Append(" for ").Append(hero.SourceName);
                sb.Append(". ").Append(cm?.GetPlayerDust() ?? 0).Append(" dust. ");
                sb.Append(PageLine(m));
                sb.Append(" Up and down to browse, left and right for pages, Enter to buy, Tab for your deck, Alt F for filters.");
                break;
            case 3:
                sb.Append("Divination. ").Append(cm?.GetPlayerGold() ?? 0).Append(" gold.");
                int available = 0;
                foreach (var e in DivinationEntries(m))
                    if (!e.Locked)
                        available++;
                sb.Append(" ").Append(available).Append(available == 1 ? " tier available." : " tiers available.");
                sb.Append(" Up and down to choose, Enter to buy.");
                break;
            case 4:
                sb.Append("Armory. Buy items");
                if (hero != null) sb.Append(" for ").Append(hero.SourceName);
                sb.Append(". ").Append(cm?.GetPlayerGold() ?? 0).Append(" gold. ");
                sb.Append(PageLine(m));
                sb.Append(" Up and down to browse, left and right for pages, Enter to buy and equip, Tab for equipment and shop controls.");
                break;
        }

        AppendUsesLeft(sb, m);
        return sb.ToString();
    }

    private static void AppendDeckCount(StringBuilder sb, CardCraftManager m)
    {
        int n = DeckRows(m).Count;
        sb.Append(" ").Append(n).Append(n == 1 ? " card." : " cards.");
    }

    /// <summary>Map-event shops are often use-limited; the label only exists when that applies.</summary>
    private static void AppendUsesLeft(StringBuilder sb, CardCraftManager m)
    {
        if (m.usesLeftText != null && m.usesLeftText.gameObject.activeSelf)
        {
            string uses = AccessibleMenuBase.StripRichText(m.usesLeftText.text);
            if (uses.Length > 0)
                sb.Append(" ").Append(uses).Append(".");
        }
    }

    // ---------------------------------------------------------------- purchase feedback

    internal static void OnPurchaseCompleted(string what)
    {
        var m = Mgr;
        if (!Owned(m))
            return;
        // A partner's craft arriving via NET_HeroCard* runs the same Hero.OnCard* methods on this
        // client; with a craft screen open here that would speak a bogus local "Crafted. N dust
        // remaining" on top of the MP echo. The echo patch (MpEchoPatches) speaks those.
        if (MpCraftEcho.ReceivingRemote)
            return;
        var cm = AtOManager.Instance?.CurrencyManager;
        var sb = new StringBuilder(what);
        if (_pendingActionCard.Length > 0)
        {
            sb.Append(" ").Append(_pendingActionCard);
            _pendingActionCard = "";
        }
        sb.Append(". ");
        // Altar and Forge charge dust; Church, Divination and Armory charge gold.
        if (m.craftType == 0 || m.craftType == 2)
            sb.Append(cm?.GetPlayerDust() ?? 0).Append(" dust remaining");
        else
            sb.Append(cm?.GetPlayerGold() ?? 0).Append(" gold remaining");
        AppendUsesLeft(sb, m);
        SpeechManager.SpeakQueued(sb.ToString());
        _phase = Phase.Browse;
    }

    /// <summary>Armory purchases: the item was bought AND auto-equipped; name the replacement.</summary>
    internal static void OnItemPurchaseCompleted()
    {
        var m = Mgr;
        if (!Owned(m) || m.craftType != 4 || _pendingActionCard.Length == 0)
            return;
        var cm = AtOManager.Instance?.CurrencyManager;
        var sb = new StringBuilder("Bought and equipped ").Append(_pendingActionCard);
        if (_pendingReplacedItem.Length > 0)
            sb.Append(", replacing ").Append(_pendingReplacedItem);
        sb.Append(". ").Append(cm?.GetPlayerGold() ?? 0).Append(" gold remaining");
        _pendingActionCard = "";
        _pendingReplacedItem = "";
        SpeechManager.SpeakQueued(sb.ToString());
    }

    private static int Wrap(int v, int count) => ((v % count) + count) % count;
}

[HarmonyPatch(typeof(Hero), nameof(Hero.AddItem))]
public class HeroAddItemPatch
{
    static void Postfix()
    {
        CardCraftScreenManager.OnItemPurchaseCompleted();
    }
}

// ---------------------------------------------------------------------- purchase postfixes
// The game finishes purchases inside coroutines gated by CardCraftManager.blocked; these Hero
// methods are called exactly once per completed transaction, making them the reliable announce
// point. All are gated on an owned CardCraftManager so reward screens and events stay silent.
// In MP the same methods also run on every OTHER client via the NET_HeroCard* receive RPCs —
// OnPurchaseCompleted skips those (MpCraftEcho.ReceivingRemote) so a partner's craft speaks only
// its MP echo, not a phantom local purchase.

[HarmonyPatch(typeof(Hero), nameof(Hero.OnCardUpgraded))]
public class HeroCardUpgradedPatch
{
    static void Postfix()
    {
        if (CardCraftManager.Instance != null && CardCraftManager.Instance.craftType == 0)
            CardCraftScreenManager.OnPurchaseCompleted("Upgraded to");
    }
}

[HarmonyPatch(typeof(Hero), nameof(Hero.OnCardRemoved))]
public class HeroCardRemovedPatch
{
    static void Postfix()
    {
        if (CardCraftManager.Instance != null && CardCraftManager.Instance.craftType == 1)
            CardCraftScreenManager.OnPurchaseCompleted("Removed");
    }
}

[HarmonyPatch(typeof(Hero), nameof(Hero.OnCardCrafted))]
public class HeroCardCraftedPatch
{
    static void Postfix()
    {
        if (CardCraftManager.Instance != null && CardCraftManager.Instance.craftType == 2)
            CardCraftScreenManager.OnPurchaseCompleted("Crafted");
    }
}
