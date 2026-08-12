using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ObeliskAccess.Input;
using ObeliskAccess.Input.Contexts;

namespace ObeliskAccess;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        // Bring up screen-reader speech before anything tries to talk.
        SpeechManager.Initialize(Logger);

        // Bind the mod's player-facing options (settings menu, Accessibility tab).
        AccessibilityOptions.Initialize(Config);

        // Register input contexts in priority order (highest first). Modals sit above the base
        // menu so that while one is active, input never reaches the screen beneath it.
        // Alerts are modal over everything, so their context is registered first.
        var alertContext = new AlertInputContext();
        InputRouter.Register(alertContext);
        // The MP players panel is the second face of the same AlertManager canvas (popupT vs
        // playersT — mutually exclusive by game code), so it sits directly under the alert.
        var playersPanelContext = new PlayersPanelInputContext();
        InputRouter.Register(playersPanelContext);
        InputRouter.Register(new TutorialInputContext());
        var settingsContext = new SettingsInputContext();
        InputRouter.Register(settingsContext);
        InputRouter.Register(new CorruptionInputContext());
        var cardCraftContext = new CardCraftInputContext();
        InputRouter.Register(cardCraftContext);
        // The in-combat death popup outranks the card-selection windows — it can appear
        // mid-resolution and freezes the whole combat until dismissed.
        var deathScreenContext = new DeathScreenInputContext();
        InputRouter.Register(deathScreenContext);
        // The in-combat card-selection windows (discard / look-at-deck / discover / pile viewers)
        // are modals over combat, so their context sits above it (with the perk tree and the
        // character sheet registered in between).
        var combatSelectorContext = new CombatSelectorInputContext();
        InputRouter.Register(combatSelectorContext);
        // The perk tree overlay opens over the in-run character sheet (its Perks tab) and over
        // the hero-selection family, so it sits above both — it gates itself on the tree being
        // visible, so it never steals input from the screens below while closed.
        var perkTreeContext = new PerkTreeInputContext();
        InputRouter.Register(perkTreeContext);
        // The in-run character sheet is a modal over combat, the map, town, rewards and loot;
        // all of those already go inert while it is open (characterWindow.IsActive checks).
        var charWindowContext = new CharWindowInputContext();
        InputRouter.Register(charWindowContext);
        var combatContext = new CombatInputContext();
        InputRouter.Register(combatContext);
        // The MP travel/answer conflict is a modal over both the event book and the map — the
        // game's own dispatch order is characterWindow > Conflict > Event > Map. It never
        // coexists with combat (different scenes), so sitting just under Combat is free.
        var conflictContext = new ConflictInputContext();
        InputRouter.Register(conflictContext);
        // The MP give-gold/dust window opens over the map or town (the game's own order puts
        // Give above Event/Town/Map, and it force-closes the upgrade/character windows).
        var giveContext = new GiveInputContext();
        InputRouter.Register(giveContext);
        var eventContext = new EventInputContext();
        InputRouter.Register(eventContext);
        var townUpgradeContext = new TownUpgradeInputContext();
        InputRouter.Register(townUpgradeContext);
        var townContext = new TownInputContext();
        InputRouter.Register(townContext);
        var mapContext = new MapInputContext();
        InputRouter.Register(mapContext);
        var rewardsContext = new RewardsInputContext();
        InputRouter.Register(rewardsContext);
        var lootContext = new LootInputContext();
        InputRouter.Register(lootContext);
        // The end-of-run screen is its own scene; it only needs to outrank the main menu.
        var finishRunContext = new FinishRunInputContext();
        InputRouter.Register(finishRunContext);
        // The act-transition screen ("IntroNewGame") is also its own scene, so exact priority
        // barely matters — it just sits below Alert (MP disconnect dialogs pop over it).
        var introContext = new IntroInputContext();
        InputRouter.Register(introContext);
        // The multiplayer lobby is its own scene too; below Alert (kick/password/version/exit
        // dialogs pop over it), matching the game's … Lobby > HeroSelection/CharPopup … order.
        var lobbyContext = new LobbyInputContext();
        InputRouter.Register(lobbyContext);
        // The hero-selection family (scene "HeroSelection"): the character window sits over the
        // base screen; the perk tree (registered far above) is the topmost modal of the family —
        // matching the game's own modality order (PerkTree > HeroSelection/CharPopup).
        var charPopupContext = new CharPopupInputContext();
        InputRouter.Register(charPopupContext);
        var heroSelectionContext = new HeroSelectionInputContext();
        InputRouter.Register(heroSelectionContext);
        InputRouter.Register(new MainMenuInputContext());

        // The alert dialogue's Alt+R repeat key: every per-screen poller gates on its own context,
        // so they all fall silent while the alert context owns input — this one covers it (and
        // the players panel, the other face of the same canvas).
        var alertPoller = gameObject.AddComponent<AlertHotkeyPoller>();
        alertPoller.AlertContext = alertContext;
        alertPoller.PlayersContext = playersPanelContext;

        // MP chat is a global overlay, not a screen — its poller is context-free and gates on
        // game state (Alt+Y send, Alt+M history, Alt+P players panel, plus the edit-session tick).
        gameObject.AddComponent<ChatHotkeyPoller>();

        // The map's Alt+G/T/I hotkeys use letters the game leaves unbound, so a frame poller sees
        // them; it fires only while the map context owns input.
        gameObject.AddComponent<MapHotkeyPoller>().MapContext = mapContext;

        // The settings menu's Alt+T tooltip key is likewise unbound.
        gameObject.AddComponent<SettingsHotkeyPoller>().SettingsContext = settingsContext;

        // Combat's Alt review hotkeys are likewise unbound; a poller drives them (and the combat
        // event-announcement flush) while the combat context owns input. The same poller drives
        // the card-selection windows' arrival tick and their Alt+T/Alt+R keys.
        var combatPoller = gameObject.AddComponent<CombatHotkeyPoller>();
        combatPoller.CombatContext = combatContext;
        combatPoller.SelectorContext = combatSelectorContext;
        combatPoller.DeathContext = deathScreenContext;

        // The event screen's Alt+T/R hotkeys are unbound too; its poller also drives the
        // event lifecycle tick (reply watcher, deferred reward-card reads).
        gameObject.AddComponent<EventHotkeyPoller>().EventContext = eventContext;

        // The MP conflict screen's Alt+R, plus its lifecycle tick (close detection must survive
        // an alert owning input, e.g. a host-disconnect dialog over the conflict).
        gameObject.AddComponent<ConflictHotkeyPoller>().ConflictContext = conflictContext;

        // The MP lobby's Alt+R/Alt+I, plus its lifecycle tick (panel arrival, room-list and
        // slot edges, and the room-name/password edit-session mirrors).
        gameObject.AddComponent<LobbyHotkeyPoller>().LobbyContext = lobbyContext;

        // The town hub's Alt review keys are unbound letters too; its poller also drives the
        // hub lifecycle tick (arrival announce, sub-screen close detection).
        var townPoller = gameObject.AddComponent<TownHotkeyPoller>();
        townPoller.TownContext = townContext;
        townPoller.TownUpgradeContext = townUpgradeContext;

        // The service screens' Alt review keys (and Alt+F filters) are unbound letters too; the
        // poller also drives screen-open detection, since ShowCardCraft is async.
        gameObject.AddComponent<CardCraftHotkeyPoller>().CardCraftContext = cardCraftContext;

        // The rewards screen's Alt+T/I/R review keys are unbound letters too; its poller also
        // drives readiness detection (the reward rows animate in over a couple of seconds).
        gameObject.AddComponent<RewardsHotkeyPoller>().RewardsContext = rewardsContext;

        // The loot screen's Alt+T/I/G/R review keys are unbound letters too; its poller also
        // drives readiness detection, the turn watch, and the finish announcement.
        gameObject.AddComponent<LootHotkeyPoller>().LootContext = lootContext;

        // The end-of-run screen's Alt+I/T/R keys; its poller also drives arrival detection and
        // the "Main menu available" announcement.
        gameObject.AddComponent<FinishRunHotkeyPoller>().FinishRunContext = finishRunContext;

        // The act-transition screen's Alt+R repeat key.
        gameObject.AddComponent<IntroHotkeyPoller>().IntroContext = introContext;

        // The in-run character sheet's Alt+T/I/R keys; its poller also drives the manager's
        // scene-change safety tick.
        gameObject.AddComponent<CharWindowHotkeyPoller>().CharWindowContext = charWindowContext;

        // The hero-selection screen's Alt+T/C/I/R keys (and the character window's / perk
        // tree's Alt reads); the poller also drives all three lifecycle ticks — arrival
        // overview, Begin-button edge, ready changes, and mouse-close detection.
        var heroSelPoller = gameObject.AddComponent<HeroSelectionHotkeyPoller>();
        heroSelPoller.HeroSelectionContext = heroSelectionContext;
        heroSelPoller.CharPopupContext = charPopupContext;
        heroSelPoller.PerkTreeContext = perkTreeContext;

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();

        // Debug mode: confirm the card-selection window patches actually attached.
        if (AccessibilityOptions.DebugEnabled)
        {
            LogPatchState(typeof(UIDeckCards), "TurnOn");
            LogPatchState(typeof(UIDiscardSelector), "TurnOn");
            LogPatchState(typeof(MatchManager), "SelectCardToDiscard");
        }
    }

    /// <summary>Troubleshooting log line, written only while the Debug mode option is on.</summary>
    internal static void LogDebug(string message)
    {
        if (AccessibilityOptions.DebugEnabled)
            Logger.LogInfo(message);
    }

    private static void LogPatchState(System.Type type, string method)
    {
        var m = AccessTools.Method(type, method);
        var info = m != null ? Harmony.GetPatchInfo(m) : null;
        int post = info?.Postfixes?.Count ?? 0;
        int pre = info?.Prefixes?.Count ?? 0;
        Logger.LogInfo($"[selector] {type.Name}.{method}: found={m != null} prefixes={pre} postfixes={post}");
    }
}
