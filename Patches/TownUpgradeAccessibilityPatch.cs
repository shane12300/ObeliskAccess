using System.Text;
using HarmonyLib;

namespace ObeliskAccess.Patches;

/// <summary>
/// Town upgrades window (the 5x6 supply tree). Left/Right change building column, Up/Down walk the
/// column's upgrade chain; Tab cycles grid, Sell Supply and Exit; Enter buys through the game's own
/// confirm-alert flow (replicating <c>BotonSupply.OnMouseUp</c>), answered by the global alert
/// dialogue. The sell-supply panel is a sub-mode: Up/Down adjust quantity, Enter sells, Escape
/// closes. State and speech live here; <c>TownUpgradeInputContext</c> maps keys and
/// <c>TownHotkeyPoller</c> drives <see cref="Tick"/> + Alt keys.
/// </summary>
internal static class TownUpgradeScreenManager
{
    private enum Focus { Grid, SellButton, ExitButton }

    private static Focus _focus = Focus.Grid;
    private static int _column; // 0..4
    private static int _row;    // 0..5
    private static bool _inSellPanel;
    private static bool _wasOpen;

    /// <summary>Building name keys per column (column 1..5 of the tree).</summary>
    private static readonly string[] _columnTitleKeys =
        { "craftCards", "upgradeCards", "removeCards", "divinationCards", "buyItems" };

    private static TownUpgradeWindow Window =>
        TownManager.Instance != null ? TownManager.Instance.townUpgradeWindow : null;

    public static bool InSellPanel => _inSellPanel;

    // ---------------------------------------------------------------- lifecycle

    public static void Tick()
    {
        var w = Window;
        bool open = w != null && w.IsActive();
        if (open && !_wasOpen)
        {
            _focus = Focus.Grid;
            _column = 0;
            _row = -1; // sentinel: the first Down lands on row 1
            _inSellPanel = false;
            SpeechManager.Speak(BuildOverview());
        }
        else if (!open && _wasOpen)
        {
            _inSellPanel = false;
        }
        _wasOpen = open;
    }

    // ---------------------------------------------------------------- navigation

    public static void MoveVertical(int delta)
    {
        if (_inSellPanel)
        {
            // Up = more, Down = fewer; the game clamps to 0..SupplyActual.
            Window?.ModifySupplyQuantity(-delta);
            AnnounceSellPanel();
            return;
        }
        if (_focus != Focus.Grid)
        {
            AnnounceFocus();
            return;
        }
        if (_row < 0)
            _row = delta > 0 ? 0 : 5;
        else
            _row = Wrap(_row + delta, 6);
        AnnounceUpgrade();
    }

    public static void MoveHorizontal(int delta)
    {
        if (_inSellPanel || _focus != Focus.Grid)
        {
            AnnounceFocus();
            return;
        }
        if (_row < 0)
            _row = 0;
        _column = Wrap(_column + delta, 5);
        SpeechManager.Speak(ColumnTitle(_column) + " column. " + UpgradeLine(_column, _row));
    }

    public static void CycleFocus()
    {
        if (_inSellPanel)
        {
            AnnounceSellPanel();
            return;
        }
        var w = Window;
        for (int i = 0; i < 3; i++)
        {
            _focus = (Focus)(((int)_focus + 1) % 3);
            if (_focus == Focus.SellButton
                && (w == null || w.sellSupplyButton == null || !w.sellSupplyButton.gameObject.activeSelf))
                continue; // sell supply is a late-game unlock; skip while hidden
            break;
        }
        AnnounceFocus();
    }

    public static void Activate()
    {
        var w = Window;
        if (w == null)
            return;

        if (_inSellPanel)
        {
            int qty = CurrentSellQuantity(w);
            int have = PlayerManager.Instance != null ? PlayerManager.Instance.SupplyActual : 0;
            if (qty <= 0 || qty > have)
            {
                // CurrencyManager.SellSupply silently refuses these; don't claim a sale.
                w.ShowSellSupply(false);
                _inSellPanel = false;
                SpeechManager.Speak("Nothing sold.");
                return;
            }
            w.SellSupplyAction();
            _inSellPanel = false;
            if (MpSpeech.IsMp)
            {
                // In MP the gold/dust credit routes through a master RPC (CurrencyManager.SellSupply
                // -> AskForGold/AskForDust), so local balances are still pre-sale this frame; the
                // 100-per-supply rate is hardcoded in SellSupply.
                SpeechManager.Speak("Sold " + qty + " supply for " + (qty * 100) + " gold and "
                    + (qty * 100) + " dust. Supply " + (PlayerManager.Instance?.SupplyActual ?? 0)
                    + ". Balances update shortly.");
            }
            else
            {
                var cm = AtOManager.Instance?.CurrencyManager;
                SpeechManager.Speak("Sold " + qty + " supply. Gold " + (cm?.GetPlayerGold() ?? 0)
                    + ", dust " + (cm?.GetPlayerDust() ?? 0)
                    + ", supply " + (PlayerManager.Instance?.SupplyActual ?? 0));
            }
            return;
        }

        switch (_focus)
        {
            case Focus.SellButton:
                w.ShowSellSupply(true);
                _inSellPanel = true;
                SpeechManager.Speak("Sell supply. Quantity 0. Up and down to adjust, Enter to sell, Escape to cancel");
                break;
            case Focus.ExitButton:
                Close();
                break;
            default:
                if (_row < 0)
                    _row = 0;
                BuyFocusedUpgrade();
                break;
        }
    }

    /// <summary>Escape: sell panel first, then the window itself. Returns true if consumed.</summary>
    public static bool Cancel()
    {
        var w = Window;
        if (w == null)
            return false;
        if (_inSellPanel)
        {
            w.ShowSellSupply(false);
            _inSellPanel = false;
            SpeechManager.Speak("Sell cancelled");
            return true;
        }
        Close();
        return true;
    }

    private static void Close()
    {
        TownManager.Instance?.ShowTownUpgrades(false);
        // The hub manager notices the window closing and announces "Town".
    }

    // ---------------------------------------------------------------- buying

    /// <summary>Mirrors <c>BotonSupply.OnMouseUp</c>: hand the game our button's BuySupply as the
    /// alert delegate, then raise the same double-confirm warning.</summary>
    private static void BuyFocusedUpgrade()
    {
        var boton = FindBoton(_column, _row);
        if (boton == null)
        {
            SpeechManager.Speak("Nothing here");
            return;
        }
        if (!boton.available)
        {
            SpeechManager.Speak(StateLine(boton));
            return;
        }
        AlertManager.buttonClickDelegate = boton.BuySupply;
        AlertManager.Instance.AlertConfirmDouble(Texts.Instance.GetText("townAssignWarning"));
        // The global alert dialogue announces the warning and drives the answer.
    }

    // ---------------------------------------------------------------- speech

    private static void AnnounceFocus()
    {
        switch (_focus)
        {
            case Focus.SellButton:
                SpeechManager.Speak("Sell supply button. Converts supply to gold and dust");
                break;
            case Focus.ExitButton:
                SpeechManager.Speak("Exit town upgrades");
                break;
            default:
                AnnounceUpgrade();
                break;
        }
    }

    private static void AnnounceUpgrade()
    {
        SpeechManager.Speak(UpgradeLine(_column, _row));
    }

    private static void AnnounceSellPanel()
    {
        var w = Window;
        if (w == null)
            return;
        string qty = w.supplySellQuantity != null ? CardSpeech.CleanFlat(w.supplySellQuantity.text) : "";
        string result = w.supplySellResult != null ? CardSpeech.CleanFlat(w.supplySellResult.text) : "";
        SpeechManager.Speak(qty + (result.Length > 0 ? ", for " + result : ""));
    }

    private static string UpgradeLine(int column, int row)
    {
        var boton = FindBoton(column, row);
        if (boton == null)
            return "Nothing here";

        string name = Texts.Instance != null
            ? CardSpeech.CleanFlat(Texts.Instance.GetText(boton.supplyId))
            : boton.supplyId;

        return name + ", row " + (row + 1) + " of 6. " + StateLine(boton);
    }

    private static string StateLine(BotonSupply boton)
    {
        var pm = PlayerManager.Instance;
        if (pm == null)
            return "";
        if (pm.PlayerHaveSupply(boton.supplyId))
            return "Owned";

        int cost = pm.PointsRequiredForSupply(boton.supplyId);
        int have = pm.GetPlayerSupplyActual();

        if (boton.available)
            return "Available, costs " + cost + (cost == 1 ? " supply point" : " supply points")
                + ", you have " + have;

        // Locked: name the concrete gate, most specific first.
        string prereq = pm.SupplyRequiredForSupply(boton.supplyId);
        if (!string.IsNullOrEmpty(prereq) && !pm.PlayerHaveSupply(prereq))
        {
            string prereqName = Texts.Instance != null
                ? CardSpeech.CleanFlat(Texts.Instance.GetText(prereq)) : prereq;
            return "Locked, requires " + prereqName;
        }
        int spent = pm.TotalPointsSpentInSupplys();
        if (boton.row > 3 && spent < 30)
            return "Locked, requires 30 total points spent, you have spent " + spent;
        if (cost > have)
            return "Locked, costs " + cost + " supply points, you have " + have;
        return "Locked";
    }

    public static void SpeakFocusedDetail()
    {
        if (_inSellPanel)
        {
            AnnounceSellPanel();
            return;
        }
        if (_focus != Focus.Grid)
        {
            AnnounceFocus();
            return;
        }
        SpeechManager.Speak(ColumnTitle(_column) + " column. " + UpgradeLine(_column, _row < 0 ? 0 : _row));
    }

    public static void SpeakOverview()
    {
        SpeechManager.Speak(BuildOverview());
    }

    private static string BuildOverview()
    {
        var pm = PlayerManager.Instance;
        var sb = new StringBuilder("Town upgrades. Five building columns, six upgrades each.");
        if (pm != null)
        {
            sb.Append(" Supply available ").Append(pm.GetPlayerSupplyActual()).Append(".");
            int spent = pm.TotalPointsSpentInSupplys();
            sb.Append(" Points spent ").Append(spent).Append(".");
            if (spent < 30)
                sb.Append(" Rows four and up unlock after 30 points spent.");
        }
        sb.Append(" Left and right for building, up and down for upgrade.");
        return sb.ToString();
    }

    private static string ColumnTitle(int column)
    {
        string key = _columnTitleKeys[column];
        string title = Texts.Instance != null ? AccessibleMenuBase.StripRichText(Texts.Instance.GetText(key)) : "";
        return title.Length > 0 ? title : "Column " + (column + 1);
    }

    // ---------------------------------------------------------------- helpers

    private static BotonSupply FindBoton(int column, int row)
    {
        var w = Window;
        if (w == null || w.botonSupply == null)
            return null;
        foreach (var b in w.botonSupply)
            if (b != null && b.column == column + 1 && b.row == row + 1)
                return b;
        return null;
    }

    private static int CurrentSellQuantity(TownUpgradeWindow w)
    {
        if (w.supplySellQuantity == null)
            return 0;
        string first = CardSpeech.CleanFlat(w.supplySellQuantity.text).Split(' ')[0];
        return int.TryParse(first, out int q) ? q : 0;
    }

    private static int Wrap(int v, int count) => ((v % count) + count) % count;
}

/// <summary>Speak a completed supply purchase. PlayerBuySupply validates internally and can bail
/// out silently, so compare the supply balance around the call and only announce a real spend.</summary>
[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.PlayerBuySupply))]
public class PlayerBuySupplyPatch
{
    static void Prefix(PlayerManager __instance, out int __state)
    {
        __state = __instance.GetPlayerSupplyActual();
    }

    static void Postfix(PlayerManager __instance, string supplyId, int __state)
    {
        if (TownManager.Instance == null)
            return;
        int now = __instance.GetPlayerSupplyActual();
        if (now == __state)
            return; // validation bailed; nothing was bought
        string name = Texts.Instance != null
            ? CardSpeech.CleanFlat(Texts.Instance.GetText(supplyId)) : supplyId;
        SpeechManager.SpeakQueued("Purchased " + name + ". " + now
            + (now == 1 ? " supply point" : " supply points") + " remaining");
    }
}
