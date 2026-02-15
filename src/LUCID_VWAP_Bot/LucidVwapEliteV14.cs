// LUCID_VWAP_ELITE_V14 – ATAS Chart Strategy
// Automatisierter Trading-Bot: VWAP + EMA(20) + ATR(14) + Delta-Effizienz
// Framework: .NET 8 / C# 12 | Plattform: ATAS ≥ 5.8.x

using System.ComponentModel;

namespace LUCID_VWAP_Bot;

// TODO: Sobald ATAS SDK referenziert ist, diese Usings aktivieren:
// using ATAS.Indicators;
// using OFT.Attributes;
// using OFT.Trading;

/// <summary>
/// LUCID VWAP ELITE V14 – Order-Flow basierte VWAP-Strategy.
/// Handelt Long/Short in der RTH-Session (15:30–21:00 EST) basierend auf:
/// - VWAP-Nähe (Zone = ATR * VwapZoneMult)
/// - Delta-Effizienz (|Delta| / Volume >= Schwelle)
/// - EMA(20) Richtung relativ zum VWAP
/// - Delta-Vorzeichen (positiv = Long, negativ = Short)
/// </summary>
public class LucidVwapEliteV14 // : ChartStrategy  // TODO: ChartStrategy aktivieren nach SDK-Einbindung
{
    // ═══════════════════════════════════════════════════════════════
    // INDIKATOREN
    // ═══════════════════════════════════════════════════════════════

    // TODO: Typen durch ATAS SDK Typen ersetzen nach Einbindung
    // private readonly VWAP _vwap = new VWAP();
    // private readonly EMA  _ema  = new EMA { Period = 20 };
    // private readonly ATR  _atr  = new ATR { Period = 14 };

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER (11 Stück – in ATAS UI konfigurierbar)
    // ═══════════════════════════════════════════════════════════════

    // TODO: [OFT.Attributes.Parameter] Attribut aktivieren nach SDK-Einbindung

    [DisplayName("1. Min Volumen/Kerze")]
    public int MinVolume { get; set; } = 300;

    [DisplayName("2. Min ATR (Punkte)")]
    public decimal MinAtr { get; set; } = 0.6m;

    [DisplayName("3. DeltaEff Schwelle %")]
    public decimal DeltaEff { get; set; } = 10.0m;

    [DisplayName("4. ATR Mult TP")]
    public decimal AtrMultTP { get; set; } = 2.0m;

    [DisplayName("5. ATR Mult SL")]
    public decimal AtrMultSL { get; set; } = 1.0m;

    [DisplayName("6. VWAP Zone Mult")]
    public decimal VwapZoneMult { get; set; } = 0.8m;

    [DisplayName("7. Max Trades/Tag")]
    public int MaxTradesDay { get; set; } = 5;

    [DisplayName("8. Max Tagesverlust USD")]
    public decimal MaxDailyLossUSD { get; set; } = 500m;

    [DisplayName("9. Kontrakte/Trade")]
    public int MyQuantity { get; set; } = 1;

    [DisplayName("10. News-Filter aktiv")]
    public bool UseNewsFilter { get; set; } = true;

    [DisplayName("11. Min R:R Ratio")]
    public decimal MinRR { get; set; } = 1.5m;

    // ═══════════════════════════════════════════════════════════════
    // INTERNE FELDER
    // ═══════════════════════════════════════════════════════════════

    private bool _orderPending;
    private decimal _dailyPnL;
    private DateTime _lastSessionDate = DateTime.MinValue;
    private int _tradesToday;

    // ═══════════════════════════════════════════════════════════════
    // KONSTRUKTOR
    // ═══════════════════════════════════════════════════════════════

    public LucidVwapEliteV14()
    {
        // WICHTIG: ATAS berechnet Indikatoren automatisch – KEIN manuelles .Calculate()!
        // Add(_vwap);
        // Add(_ema);
        // Add(_atr);
    }

    // ═══════════════════════════════════════════════════════════════
    // HAUPTLOGIK – OnCalculate (wird pro Bar aufgerufen)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Wird von ATAS für jede Bar aufgerufen.
    /// Ablauf: Filter prüfen → Signal erkennen → Trade ausführen.
    /// </summary>
    protected void OnCalculate(int bar, decimal currentPosition, int currentBar)
    {
        try
        {
            // --- Nur neueste Kerze verarbeiten ---
            if (bar != currentBar - 1) return;

            // TODO: candle via GetCandle(bar) holen nach SDK-Einbindung
            // var candle = GetCandle(bar);

            // === PLATZHALTER für Candle-Daten (wird durch SDK ersetzt) ===
            var candleTime = DateTime.Now;       // candle.Time
            var candleVolume = 0m;               // candle.Volume
            var candleClose = 0m;                // candle.Close
            var candleDelta = 0m;                // candle.Delta

            // --- Tages-Reset ---
            ResetDailyIfNeeded(candleTime);

            // ═══════════════════════════════════════
            // FILTER-KASKADE
            // ═══════════════════════════════════════

            // Filter 1: RTH (Regular Trading Hours) – 15:30 bis 21:00 EST
            if (candleTime.TimeOfDay < new TimeSpan(15, 30, 0) ||
                candleTime.TimeOfDay > new TimeSpan(21, 0, 0))
            {
                LogInfo($"[Bar {bar}] SKIP – Außerhalb RTH: {candleTime.TimeOfDay}");
                return;
            }

            // Filter 2: Mindestvolumen
            if (candleVolume < MinVolume)
            {
                LogInfo($"[Bar {bar}] SKIP – Volumen {candleVolume} < {MinVolume}");
                return;
            }

            // Filter 3: Mindest-ATR
            decimal atrValue = 0m; // _atr[bar]
            if (atrValue < MinAtr)
            {
                LogInfo($"[Bar {bar}] SKIP – ATR {atrValue:F2} < {MinAtr}");
                return;
            }

            // Filter 4: Keine offene Position
            if (currentPosition != 0)
            {
                LogInfo($"[Bar {bar}] SKIP – Position offen: {currentPosition}");
                return;
            }

            // Filter 5: Keine ausstehende Order
            if (_orderPending)
            {
                LogInfo($"[Bar {bar}] SKIP – Order ausstehend");
                return;
            }

            // Filter 6: Max Trades pro Tag
            if (_tradesToday >= MaxTradesDay)
            {
                LogInfo($"[Bar {bar}] SKIP – MaxTrades erreicht: {_tradesToday}/{MaxTradesDay}");
                return;
            }

            // Filter 7: Max Tagesverlust
            if (_dailyPnL <= -MaxDailyLossUSD)
            {
                LogInfo($"[Bar {bar}] AUTO-STOP – Tagesverlust {_dailyPnL:F2} USD <= -{MaxDailyLossUSD}");
                // Enabled = false;  // TODO: Aktivieren nach SDK-Einbindung
                return;
            }

            // Filter 8: News-Filter
            if (UseNewsFilter && IsNewsTime(candleTime))
            {
                LogInfo($"[Bar {bar}] SKIP – News-Zeitfenster");
                return;
            }

            // ═══════════════════════════════════════
            // SIGNAL-LOGIK
            // ═══════════════════════════════════════

            // Delta-Effizienz berechnen
            decimal eff = candleVolume > 0
                ? Math.Abs(candleDelta) / candleVolume * 100m
                : 0m;

            // VWAP-Zone berechnen
            decimal vwapValue = 0m; // _vwap[bar]
            decimal emaValue = 0m;  // _ema[bar]
            decimal zone = atrValue * VwapZoneMult;
            bool nearVwap = Math.Abs(candleClose - vwapValue) <= zone;

            // Long-Signal: Preis nahe VWAP + hohe Effizienz + EMA > VWAP + positives Delta
            bool longSignal = nearVwap
                && eff >= DeltaEff
                && emaValue > vwapValue
                && candleDelta > 0;

            // Short-Signal: Preis nahe VWAP + hohe Effizienz + EMA < VWAP + negatives Delta
            bool shortSignal = nearVwap
                && eff >= DeltaEff
                && emaValue < vwapValue
                && candleDelta < 0;

            // Konflikt vermeiden – kein Trade wenn beide Signale aktiv
            if (longSignal && shortSignal) return;

            // Log Indikator-Werte
            LogInfo($"[Bar {bar}] VWAP={vwapValue:F2} EMA={emaValue:F2} ATR={atrValue:F2} " +
                    $"Eff={eff:F1}% NearVWAP={nearVwap} Delta={candleDelta}");

            // ═══════════════════════════════════════
            // TRADE AUSFÜHREN
            // ═══════════════════════════════════════

            if (longSignal)
            {
                LogInfo($"[Bar {bar}] *** SIGNAL LONG *** Eff={eff:F1}% Close={candleClose:F2}");
                ExecuteTrade(bar, isLong: true, candleClose, atrValue);
            }
            else if (shortSignal)
            {
                LogInfo($"[Bar {bar}] *** SIGNAL SHORT *** Eff={eff:F1}% Close={candleClose:F2}");
                ExecuteTrade(bar, isLong: false, candleClose, atrValue);
            }
        }
        catch (Exception ex)
        {
            LogError($"[Bar {bar}] FEHLER in OnCalculate: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRADE EXECUTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Platziert Entry-, Stop-Loss- und Take-Profit-Orders.
    /// </summary>
    private void ExecuteTrade(int bar, bool isLong, decimal entryPrice, decimal atrValue)
    {
        try
        {
            // SL-Distanz: Maximum aus (8 Ticks, ATR * Multiplikator)
            decimal tickSize = 0.25m; // Security.TickSize – TODO: aus SDK holen
            decimal slDist = Math.Max(8 * tickSize, atrValue * AtrMultSL);
            decimal tpDist = atrValue * AtrMultTP;

            // R:R Check
            if (slDist <= 0 || tpDist / slDist < MinRR)
            {
                LogInfo($"[Bar {bar}] SKIP – R:R {tpDist / Math.Max(slDist, 0.01m):F2} < {MinRR}");
                return;
            }

            decimal slPrice = isLong ? entryPrice - slDist : entryPrice + slDist;
            decimal tpPrice = isLong ? entryPrice + tpDist : entryPrice - tpDist;

            // --- Entry Order ---
            // TODO: OpenOrder nach SDK-Einbindung aktivieren
            /*
            this.OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = isLong ? OrderDirections.Buy : OrderDirections.Sell,
                Type = OrderTypes.Limit,
                Price = entryPrice,
                QuantityToFill = MyQuantity
            });
            */

            // --- Stop-Loss Order ---
            /*
            this.OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = isLong ? OrderDirections.Sell : OrderDirections.Buy,
                Type = OrderTypes.Stop,
                TriggerPrice = slPrice,
                QuantityToFill = MyQuantity
            });
            */

            // --- Take-Profit Order ---
            /*
            this.OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = isLong ? OrderDirections.Sell : OrderDirections.Buy,
                Type = OrderTypes.Limit,
                Price = tpPrice,
                QuantityToFill = MyQuantity
            });
            */

            _orderPending = true;
            _tradesToday++;

            string direction = isLong ? "LONG" : "SHORT";
            LogInfo($"[Bar {bar}] TRADE #{_tradesToday} {direction} | " +
                    $"Entry={entryPrice:F2} | SL={slPrice:F2} | TP={tpPrice:F2} | " +
                    $"Qty={MyQuantity} | R:R={tpDist / slDist:F2}");
        }
        catch (Exception ex)
        {
            LogError($"[Bar {bar}] FEHLER in ExecuteTrade: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // POSITION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Wird aufgerufen wenn sich die Position ändert.
    /// Bei Flat: Ausstehende Orders canceln.
    /// </summary>
    protected void OnPositionChanged(decimal currentPosition)
    {
        // TODO: Override von ChartStrategy.OnPositionChanged(Position position) nach SDK-Einbindung
        if (Math.Abs(currentPosition) < 0.001m)
        {
            _orderPending = false;

            // Alle offenen Orders canceln
            // TODO: Nach SDK-Einbindung aktivieren:
            // foreach (var o in Orders.Where(o => o.Unfilled > 0))
            //     CancelOrder(o);

            LogInfo($"FLAT – Tages-PnL: {_dailyPnL:F2} USD | Trades heute: {_tradesToday}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TAGES-RESET
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Setzt Tages-Zähler zurück wenn ein neuer Handelstag beginnt.
    /// </summary>
    private void ResetDailyIfNeeded(DateTime candleTime)
    {
        if (candleTime.Date > _lastSessionDate.Date)
        {
            _lastSessionDate = candleTime;
            _tradesToday = 0;
            _dailyPnL = 0m;
            _orderPending = false;
            LogInfo($"=== NEUER HANDELSTAG: {candleTime:yyyy-MM-dd} === Reset durchgeführt");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // NEWS-FILTER
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob die aktuelle Zeit in einem News-Zeitfenster liegt.
    /// Fenster: 8:00–8:45 EST und 14:00–14:45 EST.
    /// </summary>
    private static bool IsNewsTime(DateTime time)
    {
        var tod = time.TimeOfDay;
        // Vor-Markt News (z.B. Arbeitsmarktdaten, CPI)
        if (tod >= new TimeSpan(8, 0, 0) && tod <= new TimeSpan(8, 45, 0))
            return true;
        // Nachmittags News (z.B. FOMC, Fed-Reden)
        if (tod >= new TimeSpan(14, 0, 0) && tod <= new TimeSpan(14, 45, 0))
            return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // PNL TRACKING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktualisiert den Tages-PnL nach einem abgeschlossenen Trade.
    /// Wird aus OnTrade / OnOrderFilled aufgerufen.
    /// </summary>
    private void UpdateDailyPnL(decimal realizedPnL)
    {
        _dailyPnL += realizedPnL;
        LogInfo($"PnL Update: Trade={realizedPnL:F2} USD | Tages-PnL={_dailyPnL:F2} USD");

        // Auto-Deaktivierung bei Max-Verlust
        if (_dailyPnL <= -MaxDailyLossUSD)
        {
            LogInfo($"!!! AUTO-STOP !!! Tagesverlust {_dailyPnL:F2} USD >= Limit {MaxDailyLossUSD} USD");
            // Enabled = false;  // TODO: Aktivieren nach SDK-Einbindung
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LOGGING HELPER
    // ═══════════════════════════════════════════════════════════════

    private static void LogInfo(string message)
    {
        // TODO: ATAS LogInfo verwenden nach SDK-Einbindung
        // this.LogInfo(message);
        Console.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} | {message}");
    }

    private static void LogError(string message)
    {
        // TODO: ATAS LogError verwenden nach SDK-Einbindung
        // this.LogError(message);
        Console.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} | {message}");
    }
}
