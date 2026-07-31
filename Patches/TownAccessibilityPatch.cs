using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using static ObeliskAccess.Patches.Nav;

namespace ObeliskAccess.Patches;

/// <summary>
/// Town hub accessibility. Up/Down walk a fixed list of hub items (five buildings, town upgrades,
/// Ready, unclaimed treasures); Enter activates, replicating the guards of
/// <c>TownBuilding.OnMouseUp</c> and calling the same <c>AtOManager.Do*</c> entry points the mouse
/// path uses. Tab toggles to the party strip, mirroring the map. Confirm alerts the hub spawns
/// (treasure claims, tutorial-step warnings) are owned by the global alert dialogue, which
/// outranks the hub context. All state and speech live here; the
/// thin <c>TownInputContext</c> maps keys, and <c>TownHotkeyPoller</c> drives <see cref="Tick"/> +
/// the Alt review keys.
/// </summary>
internal static class TownScreenManager
{
    internal enum Region { Main, Party }

    private class Entry
    {
        public string Speech;
        public Func<string> Detail;
        public Action Activate;
        /// <summary>Stable identity across rebuilds — Activate anchors on this, never on the
        /// position: the MP divination invite inserts/removes itself at the TOP of the list at
        /// any moment, and a positional Enter one frame later would fire the wrong row (worst
        /// case joining a divination round the player never chose).</summary>
        public string Key;
    }

    private static readonly List<Entry> _entries = new List<Entry>();
    private static Region _region = Region.Main;
    private static int _index;
    private static int _partyIndex;
    private static string _focusedKey; // identity of the last row announced to the user

    // ---- open/close lifecycle (no patch on scene init: ShowCardCraft-style timing issues) ----
    private static bool _townPresent;
    private static bool _openAnnounced;
    private static int _openFrames;
    private static bool _subScreenOpen;

    public static Region CurrentRegion => _region;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Runs every frame from the poller, even while another context owns input.</summary>
    public static void Tick()
    {
        var tm = TownManager.Instance;
        if (tm == null)
        {
            if (_townPresent)
                Reset();
            return;
        }

        if (!_townPresent)
        {
            _townPresent = true;
            _openAnnounced = false;
            _openFrames = 0;
            _region = Region.Main;
            _index = -1; // sentinel: the first Down lands on the first hub item
            _partyIndex = 0;
            _subScreenOpen = false;
            // Row keys are stable across towns ("ready", "building:*"), so a stale key from the
            // previous visit would let a blind first Enter relocate to — and fire — that row.
            _focusedKey = null;
        }

        // Arrival announcement: give the scene ~half a second to finish laying out (treasures pop
        // in via coroutine), and let a town tutorial popup speak first.
        if (!_openAnnounced)
        {
            _openFrames++;
            if (_openFrames >= 30 && !(GameManager.Instance != null && GameManager.Instance.IsTutorialActive()))
            {
                _openAnnounced = true;
                SpeechManager.Speak(BuildOverview());
            }
        }

        // When a service screen / upgrade window / character sheet closes back to the hub, say so —
        // the player otherwise has no cue that focus returned.
        bool subOpen = CardCraftManager.Instance != null
            || (tm.townUpgradeWindow != null && tm.townUpgradeWindow.IsActive())
            || (tm.characterWindow != null && tm.characterWindow.IsActive());
        if (_subScreenOpen && !subOpen && _openAnnounced)
            SpeechManager.Speak("Town");
        _subScreenOpen = subOpen;
    }

    private static void Reset()
    {
        _townPresent = false;
        _openAnnounced = false;
        _subScreenOpen = false;
        _entries.Clear();
        _region = Region.Main;
        _index = 0;
        _focusedKey = null;
    }

    // ---------------------------------------------------------------- entry list

    private static void RebuildEntries()
    {
        _entries.Clear();
        var tm = TownManager.Instance;
        if (tm == null)
            return;

        // A pending co-op divination invite goes first: the game's panel is non-modal and has no
        // decline, so it surfaces as an ordinary hub item that exists only while the invite is up.
        if (MpSpeech.IsMp && tm.joinDivination != null && tm.joinDivination.gameObject.activeSelf)
        {
            _entries.Add(new Entry
            {
                Key = "divination-invite",
                Speech = "Join divination round, started by "
                    + MpSpeech.DisplayNick(AtOManager.Instance != null ? AtOManager.Instance.townDivinationCreator : null),
                Detail = () => tm.joinDivinationText != null
                    ? CardSpeech.CleanFlat(tm.joinDivinationText.text)
                    : "",
                Activate = () => AtOManager.Instance?.JoinCardDivination(),
            });
        }

        AddBuilding(tm.buildingForge);
        AddBuilding(tm.buildingAltar);
        AddBuilding(tm.buildingChurch);
        AddBuilding(tm.buildingCart);
        AddBuilding(tm.buildingArmory);

        if (tm.townUpgrades != null && tm.townUpgrades.gameObject.activeSelf)
        {
            _entries.Add(new Entry
            {
                Key = "upgrades",
                Speech = "Town upgrades",
                Detail = () => "Spend supply points on permanent town upgrades",
                Activate = () => tm.ShowTownUpgrades(true),
            });
        }

        string readyLabel = tm.townReady != null ? AccessibleMenuBase.StripRichText(tm.townReady.GetText()) : "";
        if (readyLabel.Length == 0)
            readyLabel = "Ready";
        _entries.Add(new Entry
        {
            Key = "ready",
            Speech = readyLabel + ", leave town",
            Detail = () => "Leave town and return to the map",
            Activate = () => tm.Ready(),
        });

        if (tm.treasureItems != null)
            foreach (var t in tm.treasureItems)
                AddTreasure(t, isCommunity: false);
        if (tm.treasureItemsCommunity != null)
            foreach (var t in tm.treasureItemsCommunity)
                AddTreasure(t, isCommunity: true);
    }

    private static void AddBuilding(TownBuilding b)
    {
        if (b == null || !b.gameObject.activeSelf)
            return;

        string title = Texts.Instance != null ? AccessibleMenuBase.StripRichText(Texts.Instance.GetText(b.idTitle)) : b.idTitle;
        bool disabled = Traverse.Create(b).Field<bool>("disabled").Value;

        _entries.Add(new Entry
        {
            Key = "building:" + b.idTitle,
            Speech = title + (disabled ? ", unavailable" : ""),
            Detail = () =>
            {
                string desc = Texts.Instance != null ? CardSpeech.CleanFlat(Texts.Instance.GetText(b.idDescription)) : "";
                return desc.Length > 0 ? title + ". " + desc : title;
            },
            Activate = () => ActivateBuilding(b, disabled),
        });
    }

    private static void AddTreasure(TreasureRun t, bool isCommunity)
    {
        if (t == null || !t.gameObject.activeSelf || t.claimed || string.IsNullOrEmpty(t.treasureId))
            return;

        string amounts = TreasureAmounts(t);
        string id = t.treasureId;
        _entries.Add(new Entry
        {
            Key = (isCommunity ? "treasure-community:" : "treasure:") + id,
            Speech = (isCommunity ? "Community treasure, " : "Treasure, ") + amounts,
            Detail = () => "Reward from a previous run: " + amounts + ". Enter to claim",
            Activate = () =>
            {
                var tm = TownManager.Instance;
                if (tm == null)
                    return;
                if (tm.AreTreasuresLocked())
                {
                    SpeechManager.Speak("Busy, try again in a moment");
                    return;
                }
                if (isCommunity)
                    tm.ClaimTreasureCommunity(id);
                else
                    tm.ClaimTreasure(id);
                // The claim raises the game's confirm alert, answered by the global alert dialogue.
            },
        });
    }

    /// <summary>"gold 340, dust 120" — from the treasure's own label, sprite icons become words.</summary>
    private static string TreasureAmounts(TreasureRun t)
    {
        if (t.qText == null)
            return "amounts unknown";
        var lines = CardSpeech.SplitLines(CardSpeech.CleanDescription(t.qText.text));
        return lines.Count > 0 ? string.Join(", ", lines.ToArray()) : "amounts unknown";
    }

    // ---------------------------------------------------------------- activation

    /// <summary>Mirrors <c>TownBuilding.OnMouseUp</c>: same guards, same <c>AtOManager</c> calls.</summary>
    private static void ActivateBuilding(TownBuilding b, bool disabled)
    {
        var tm = TownManager.Instance;
        var ato = AtOManager.Instance;
        if (tm == null || ato == null)
            return;

        if (disabled)
        {
            SpeechManager.Speak("Unavailable in this game mode");
            return;
        }
        if (tm.AreTreasuresLocked())
        {
            SpeechManager.Speak("Busy, try again in a moment");
            return;
        }

        int step = ato.TownTutorialStep;
        bool inTutorial = step > -1 && step < 3;

        switch (b.idTitle)
        {
            case "craftCards":
                if (inTutorial && step != 0) { TutorialBlockAlert(); return; }
                ato.DoCardCraft();
                break;
            case "upgradeCards":
                if (inTutorial && step != 1) { TutorialBlockAlert(); return; }
                ato.DoCardUpgrade();
                break;
            case "removeCards":
                if (inTutorial) { TutorialBlockAlert(); return; }
                ato.DoCardHealer();
                break;
            case "divinationCards":
                if (inTutorial) { TutorialBlockAlert(); return; }
                ato.DoCardDivination();
                break;
            case "buyItems":
                if (inTutorial && step != 2) { TutorialBlockAlert(); return; }
                ato.DoItemShop("");
                break;
            default:
                SpeechManager.Speak("Nothing happens");
                break;
        }
    }

    private static void TutorialBlockAlert()
    {
        // Same alert the game shows for a mouse click on the wrong building; the global
        // alert dialogue speaks and drives it.
        AlertManager.Instance?.AlertConfirm(Texts.Instance.GetText("tutorialTownNeedComplete"));
    }

    // ---------------------------------------------------------------- navigation

    public static void Move(int delta)
    {
        if (_region == Region.Party)
        {
            MoveParty(delta);
            return;
        }

        RebuildEntries();
        if (_entries.Count == 0)
        {
            SpeechManager.Speak("Nothing here");
            return;
        }
        if (_index < 0)
            _index = delta > 0 ? 0 : _entries.Count - 1;
        else
            _index = Wrap(_index + delta, _entries.Count);
        _focusedKey = _entries[_index].Key;
        SpeechManager.Speak(_entries[_index].Speech);
    }

    public static void Activate()
    {
        if (_region == Region.Party)
        {
            // Open the character sheet through the game's own portrait-click path; the
            // CharacterWindowUI.Show postfix announces the arrival.
            if (!CharWindowScreenManager.OpenHeroSheet(_partyIndex))
                SpeechManager.Speak("No character focused");
            return;
        }

        RebuildEntries();
        if (_entries.Count == 0)
            return;
        if (_index < 0)
            _index = 0;
        if (_index >= _entries.Count)
            _index = _entries.Count - 1;

        // Anchor on the row the user last HEARD, not on the position: the divination invite
        // (or a claimed treasure) shifting the list must never redirect a pending Enter.
        if (_focusedKey != null && _entries[_index].Key != _focusedKey)
        {
            int relocated = _entries.FindIndex(e => e.Key == _focusedKey);
            if (relocated >= 0)
            {
                _index = relocated;
            }
            else
            {
                if (_index >= _entries.Count)
                    _index = _entries.Count - 1;
                _focusedKey = _entries[_index].Key;
                SpeechManager.Speak("That item is gone. Now on: " + _entries[_index].Speech);
                return;
            }
        }
        _focusedKey = _entries[_index].Key;
        _entries[_index].Activate?.Invoke();
    }

    public static void ToggleRegion()
    {
        if (_region == Region.Main)
        {
            _region = Region.Party;
            FocusFirstParty();
        }
        else
        {
            _region = Region.Main;
            RebuildEntries();
            if (_entries.Count > 0)
            {
                if (_index < 0 || _index >= _entries.Count)
                    _index = 0;
                _focusedKey = _entries[_index].Key;
                SpeechManager.Speak(_entries[_index].Speech);
            }
        }
    }

    // ---------------------------------------------------------------- party strip (mirrors the map)

    private static void FocusFirstParty()
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        if (!occ.Contains(_partyIndex))
            _partyIndex = occ[0];
        AnnounceHero(_partyIndex);
    }

    private static void MoveParty(int delta)
    {
        var occ = OccupiedSlots();
        if (occ.Count == 0)
        {
            SpeechManager.Speak("No characters");
            return;
        }
        int pos = occ.IndexOf(_partyIndex);
        pos = pos < 0 ? 0 : Wrap(pos + delta, occ.Count);
        _partyIndex = occ[pos];
        AnnounceHero(_partyIndex);
    }

    public static void JumpToSlot(int slot)
    {
        _region = Region.Party;
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Slot " + (slot + 1) + " empty");
            return;
        }
        _partyIndex = slot;
        AnnounceHero(slot);
    }

    private static void AnnounceHero(int slot)
    {
        var h = GetHero(slot);
        if (h == null || h.HeroData == null)
        {
            SpeechManager.Speak("Empty slot");
            return;
        }
        SpeechManager.Speak(MapNavigator.BuildHeroLine(h));
    }

    private static List<int> OccupiedSlots()
    {
        var list = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            var h = GetHero(i);
            if (h != null && h.HeroData != null)
                list.Add(i);
        }
        return list;
    }

    private static Hero GetHero(int index)
    {
        var team = AtOManager.Instance?.team;
        return team != null ? team.GetHero(index) : null;
    }

    // ---------------------------------------------------------------- read-outs

    public static void SpeakFocusedDetail()
    {
        if (_region == Region.Party)
        {
            AnnounceHero(_partyIndex);
            return;
        }
        RebuildEntries();
        if (_entries.Count == 0 || _index >= _entries.Count || _index < 0)
        {
            SpeechManager.Speak("Nothing focused");
            return;
        }
        string detail = _entries[_index].Detail?.Invoke();
        SpeechManager.Speak(string.IsNullOrEmpty(detail) ? "No additional details" : detail);
    }

    public static void SpeakCurrencies()
    {
        var cm = AtOManager.Instance?.CurrencyManager;
        int gold = cm != null ? cm.GetPlayerGold() : 0;
        int dust = cm != null ? cm.GetPlayerDust() : 0;
        int supply = PlayerManager.Instance != null ? PlayerManager.Instance.SupplyActual : 0;
        SpeechManager.Speak("Gold " + gold + ", dust " + dust + ", supply " + supply);
    }

    public static void SpeakOverview()
    {
        SpeechManager.Speak(BuildOverview());
    }

    private static string BuildOverview()
    {
        var tm = TownManager.Instance;
        var ato = AtOManager.Instance;
        var sb = new StringBuilder();

        string zone = ZoneDisplayName(ato != null ? ato.GetTownZoneId() : "");
        sb.Append(string.IsNullOrEmpty(zone) ? "Town." : "Town of " + zone + ".");

        int treasures = 0;
        if (tm != null)
        {
            if (tm.treasureItems != null)
                foreach (var t in tm.treasureItems)
                    if (t != null && t.gameObject.activeSelf && !t.claimed && !string.IsNullOrEmpty(t.treasureId))
                        treasures++;
            if (tm.treasureItemsCommunity != null)
                foreach (var t in tm.treasureItemsCommunity)
                    if (t != null && t.gameObject.activeSelf && !t.claimed && !string.IsNullOrEmpty(t.treasureId))
                        treasures++;
        }
        if (treasures > 0)
            sb.Append(" ").Append(treasures).Append(treasures == 1 ? " treasure to claim." : " treasures to claim.");

        int step = ato != null ? ato.TownTutorialStep : -1;
        if (step > -1 && step < 3)
        {
            sb.Append(" Tutorial: ");
            sb.Append(step == 0 ? "craft a card at the Forge."
                : step == 1 ? "upgrade a card at the Altar."
                : "buy an item at the Armory.");
        }

        sb.Append(" Up and down to choose, Enter to open, Tab for party.");
        return sb.ToString();
    }

    /// <summary>Localized zone name the way the game's own node popup resolves it
    /// (ZoneDataSource[id].ZoneName through Texts, then the id as a text key); the raw zone id is
    /// an internal string and only a last resort.</summary>
    private static string ZoneDisplayName(string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId))
            return "";
        string name = "";
        if (Texts.Instance != null)
        {
            var zds = Globals.Instance?.ZoneDataSource;
            string id = zoneId.ToLower();
            name = zds != null && zds.ContainsKey(id) && zds[id] != null
                ? Texts.Instance.GetText(zds[id].ZoneName)
                : Texts.Instance.GetText(id);
        }
        return string.IsNullOrEmpty(name) ? zoneId : AccessibleMenuBase.StripRichText(name);
    }

}

/// <summary>Confirmed treasure claims: speak what was gained (the "Confirmed" answer alone says nothing).</summary>
[HarmonyPatch(typeof(TreasureRun), nameof(TreasureRun.Claim))]
public class TreasureClaimPatch
{
    static void Postfix(TreasureRun __instance)
    {
        if (TownManager.Instance == null || __instance.qText == null)
            return;
        var lines = CardSpeech.SplitLines(CardSpeech.CleanDescription(__instance.qText.text));
        if (lines.Count > 0)
            SpeechManager.SpeakQueued("Treasure claimed: " + string.Join(", ", lines.ToArray()));
    }
}

[HarmonyPatch(typeof(TreasureRun), nameof(TreasureRun.ClaimCommunity))]
public class TreasureClaimCommunityPatch
{
    static void Postfix(TreasureRun __instance)
    {
        if (TownManager.Instance == null || __instance.qText == null)
            return;
        var lines = CardSpeech.SplitLines(CardSpeech.CleanDescription(__instance.qText.text));
        if (lines.Count > 0)
            SpeechManager.SpeakQueued("Treasure claimed: " + string.Join(", ", lines.ToArray()));
    }
}

/// <summary>
/// Co-op divination invite (the MP RPC shows a non-modal town panel with no decline button).
/// The prefix snapshots the previous visibility so only the rising edge speaks; the panel text is
/// the game's own localized "{creator} started a divination round…" line.
/// </summary>
[HarmonyPatch(typeof(TownManager), nameof(TownManager.ShowJoinDivination))]
public class TownJoinDivinationAnnouncePatch
{
    static void Prefix(TownManager __instance, out bool __state)
    {
        __state = __instance.joinDivination != null && __instance.joinDivination.gameObject.activeSelf;
    }

    static void Postfix(TownManager __instance, bool state, bool __state)
    {
        if (!MpSpeech.IsMp || !state || __state)
            return; // falling edge (the local join) is silent — the divination flow takes over
        string body = __instance.joinDivinationText != null
            ? CardSpeech.CleanFlat(__instance.joinDivinationText.text)
            : "A divination round has started.";
        SpeechManager.SpeakQueued(body + " A Join divination item is at the top of the town list.");
    }
}

/// <summary>MP town Ready is a vote toggle (statusReady + SetManualReady) with no local echo of
/// its own — and the game silently UN-readies the player whenever they open a town service
/// (DisableReady, which routes through Ready). Without this a player who readied, browsed a
/// shop, and came back believes they are still ready while the whole party waits on them; the
/// ambient ready-count line stays silent on the un-ready-to-zero edge by design. SP calls take
/// the exit/tutorial path where other announcements own the outcome.</summary>
[HarmonyPatch(typeof(TownManager), nameof(TownManager.Ready))]
public class TownReadyToggleAnnouncePatch
{
    static void Prefix(TownManager __instance, out bool __state)
    {
        __state = Traverse.Create(__instance).Field<bool>("statusReady").Value;
    }

    static void Postfix(TownManager __instance, bool __state)
    {
        if (!MpSpeech.IsMp)
            return;
        bool now = Traverse.Create(__instance).Field<bool>("statusReady").Value;
        if (now == __state)
            return;
        SpeechManager.SpeakQueued(now
            ? "Ready to leave town — waiting for the other players."
            : "No longer ready to leave town.");
    }
}
