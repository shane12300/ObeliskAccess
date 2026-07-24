using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

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
    }

    private static readonly List<Entry> _entries = new List<Entry>();
    private static Region _region = Region.Main;
    private static int _index;
    private static int _partyIndex;

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
    }

    // ---------------------------------------------------------------- entry list

    private static void RebuildEntries()
    {
        _entries.Clear();
        var tm = TownManager.Instance;
        if (tm == null)
            return;

        AddBuilding(tm.buildingForge);
        AddBuilding(tm.buildingAltar);
        AddBuilding(tm.buildingChurch);
        AddBuilding(tm.buildingCart);
        AddBuilding(tm.buildingArmory);

        if (tm.townUpgrades != null && tm.townUpgrades.gameObject.activeSelf)
        {
            _entries.Add(new Entry
            {
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
        SpeechManager.Speak(_entries[_index].Speech);
    }

    public static void Activate()
    {
        if (_region == Region.Party)
        {
            SpeechManager.Speak("Character sheet is not yet accessible");
            return;
        }

        RebuildEntries();
        if (_entries.Count == 0)
            return;
        if (_index < 0)
            _index = 0;
        if (_index >= _entries.Count)
            _index = _entries.Count - 1;
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

        string zone = ato != null ? ato.GetTownZoneId() : "";
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

    private static int Wrap(int v, int count) => ((v % count) + count) % count;
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
