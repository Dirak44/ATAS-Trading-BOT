// LUCID_VWAP_ELITE_V14 – ATAS Chart Strategy
// Automatisierter Trading-Bot: VWAP + EMA(20) + ATR(14) + Delta-Effizienz
// Framework: .NET 8 / C# 12 | Plattform: ATAS ≥ 5.8.x

using System.ComponentModel;
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using ATAS.Strategies.Chart;
using OFT.Attributes;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace LUCID_VWAP_Bot;

/// <summary>
/// LUCID VWAP ELITE V14 – Order-Flow basierte VWAP-Strategy.
/// Handelt Long/Short in der RTH-Session (15:30–21:00 EST) basierend auf:
/// - VWAP-Nähe (Zone = ATR * VwapZoneMult)
/// - Delta-Effizienz (|Delta| / Volume >= Schwelle)
/// - EMA(20) Richtung relativ zum VWAP
/// - Delta-Vorzeichen (positiv = Long, negativ = Short)
/// </summary>
public class LucidVwapEliteV14 : ChartStrategy
{
    // ═══════════════════════════════════════════════════════════════
    // INDIKATOREN
    // ═══════════════════════════════════════════════════════════════

    private readonly VWAP _vwap = new() { Type = VWAPPeriodType.Daily };
    private readonly EMA _ema = new() { Period = 20 };
    private readonly ATR _atr = new() { Period = 14 };

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER (11 Stück – in ATAS UI konfigurierbar)
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("1. Min Volumen/Kerze")]
    public int MinVolume { get; set; } = 300;

    [Parameter]
    [DisplayName("2. Min ATR (Punkte)")]
    public decimal MinAtr { get; set; } = 0.6m;

    [Parameter]
    [DisplayName("3. DeltaEff Schwelle %")]
    public decimal DeltaEff { get; set; } = 10.0m;

    [Parameter]
    [DisplayName("4. ATR Mult TP")]
    public decimal AtrMultTP { get; set; } = 2.0m;

    [Parameter]
    [DisplayName("5. ATR Mult SL")]
    public decimal AtrMultSL { get; set; } = 1.0m;

    [Parameter]
    [DisplayName("6. VWAP Zone Mult")]
    public decimal VwapZoneMult { get; set; } = 0.8m;

    [Parameter]
    [DisplayName("7. Max Trades/Tag")]
    public int MaxTradesDay { get; set; } = 5;

    [Parameter]
    [DisplayName("8. Max Tagesverlust USD")]
    public decimal MaxDailyLossUSD { get; set; } = 500m;

    [Parameter]
    [DisplayName("9. Kontrakte/Trade")]
    public int MyQuantity { get; set; } = 1;

    [Parameter]
    [DisplayName("10. News-Filter aktiv")]
    public bool UseNewsFilter { get; set; } = true;

    [Parameter]
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
        Add(_vwap);
        Add(_ema);
        Add(_atr);
    }

    // ═══════════════════════════════════════════════════════════════
    // HAUPTLOGIK – OnCalculate (wird pro Bar aufgerufen)
    // ═══════════════════════════════════════════════════════════════

    protected override void OnCalculate(int bar, decimal value)
    {
        try
        {
            // Nur neueste Kerze verarbeiten
            if (bar != CurrentBar - 1) return;

            var candle = GetCandle(bar);

            // Tages-Reset
            ResetDailyIfNeeded(candle.Time);

            // ═══════════════════════════════════════
            // FILTER-KASKADE
            // ═══════════════════════════════════════

            // Filter 1: RTH (Regular Trading Hours) – 15:30 bis 21:00 EST
            if (candle.Time.TimeOfDay < new TimeSpan(15, 30, 0) ||
                candle.Time.TimeOfDay > new TimeSpan(21, 0, 0))
                return;

            // Filter 2: Mindestvolumen
            if (candle.Volume < MinVolume) return;

            // Filter 3: Mindest-ATR
            var atrValue = _atr[bar];
            if (atrValue < MinAtr) return;

            // Filter 4: Keine offene Position
            if (CurrentPosition != 0) return;

            // Filter 5: Keine ausstehende Order
            if (_orderPending) return;

            // Filter 6: Max Trades pro Tag
            if (_tradesToday >= MaxTradesDay) return;

            // Filter 7: Max Tagesverlust
            if (_dailyPnL <= -MaxDailyLossUSD)
            {
                this.LogInfo($"AUTO-STOP – Tagesverlust {_dailyPnL:F2} USD");
                Stop();
                return;
            }

            // Filter 8: News-Filter
            if (UseNewsFilter && IsNewsTime(candle.Time)) return;

            // ═══════════════════════════════════════
            // SIGNAL-LOGIK
            // ═══════════════════════════════════════

            // Delta-Effizienz berechnen
            decimal eff = candle.Volume > 0
                ? Math.Abs(candle.Delta) / candle.Volume * 100m
                : 0m;

            // VWAP-Zone berechnen
            var vwapValue = _vwap[bar];
            var emaValue = _ema[bar];
            decimal zone = atrValue * VwapZoneMult;
            bool nearVwap = Math.Abs(candle.Close - vwapValue) <= zone;

            // Long-Signal
            bool longSignal = nearVwap
                && eff >= DeltaEff
                && emaValue > vwapValue
                && candle.Delta > 0;

            // Short-Signal
            bool shortSignal = nearVwap
                && eff >= DeltaEff
                && emaValue < vwapValue
                && candle.Delta < 0;

            // Konflikt vermeiden
            if (longSignal && shortSignal) return;
            if (!longSignal && !shortSignal) return;

            // Log Indikator-Werte
            this.LogInfo($"[Bar {bar}] VWAP={vwapValue:F2} EMA={emaValue:F2} ATR={atrValue:F2} " +
                         $"Eff={eff:F1}% NearVWAP={nearVwap} Delta={candle.Delta}");

            // ═══════════════════════════════════════
            // TRADE AUSFÜHREN
            // ═══════════════════════════════════════

            if (longSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL LONG *** Eff={eff:F1}% Close={candle.Close:F2}");
                ExecuteTrade(bar, isLong: true, candle.Close, atrValue);
            }
            else if (shortSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL SHORT *** Eff={eff:F1}% Close={candle.Close:F2}");
                ExecuteTrade(bar, isLong: false, candle.Close, atrValue);
            }
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in OnCalculate: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRADE EXECUTION
    // ═══════════════════════════════════════════════════════════════

    private void ExecuteTrade(int bar, bool isLong, decimal entryPrice, decimal atrValue)
    {
        try
        {
            var tickSize = Security.MinStepSize;
            if (tickSize <= 0) tickSize = 0.25m;

            // SL-Distanz: Maximum aus (8 Ticks, ATR * Multiplikator)
            decimal slDist = Math.Max(8 * tickSize, atrValue * AtrMultSL);
            decimal tpDist = atrValue * AtrMultTP;

            // R:R Check
            if (slDist <= 0 || tpDist / slDist < MinRR)
            {
                this.LogInfo($"[Bar {bar}] SKIP – R:R {tpDist / Math.Max(slDist, 0.01m):F2} < {MinRR}");
                return;
            }

            decimal slPrice = isLong ? entryPrice - slDist : entryPrice + slDist;
            decimal tpPrice = isLong ? entryPrice + tpDist : entryPrice - tpDist;

            var direction = isLong ? Sides.Buy : Sides.Sell;
            var exitDirection = isLong ? Sides.Sell : Sides.Buy;

            // Entry Order (Limit)
            OpenOrder(new Order
            {
                Security = Security,
                Portfolio = Portfolio,
                Direction = direction,
                Type = OrderTypes.Limit,
                Price = entryPrice,
                Volume = MyQuantity
            });

            // Stop-Loss Order
            OpenOrder(new Order
            {
                Security = Security,
                Portfolio = Portfolio,
                Direction = exitDirection,
                Type = OrderTypes.Conditional,
                Price = slPrice,
                Volume = MyQuantity
            });

            // Take-Profit Order (Limit)
            OpenOrder(new Order
            {
                Security = Security,
                Portfolio = Portfolio,
                Direction = exitDirection,
                Type = OrderTypes.Limit,
                Price = tpPrice,
                Volume = MyQuantity
            });

            _orderPending = true;
            _tradesToday++;

            string dir = isLong ? "LONG" : "SHORT";
            this.LogInfo($"[Bar {bar}] TRADE #{_tradesToday} {dir} | " +
                         $"Entry={entryPrice:F2} | SL={slPrice:F2} | TP={tpPrice:F2} | " +
                         $"Qty={MyQuantity} | R:R={tpDist / slDist:F2}");
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in ExecuteTrade: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // POSITION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    protected override void OnPositionChanged(Position position)
    {
        if (Math.Abs(CurrentPosition) < 0.001m)
        {
            _orderPending = false;

            // Alle offenen Orders canceln
            foreach (var o in Orders.Where(o => o.Balance > 0))
                CancelOrder(o);

            this.LogInfo($"FLAT – Tages-PnL: {_dailyPnL:F2} USD | Trades heute: {_tradesToday}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TAGES-RESET
    // ═══════════════════════════════════════════════════════════════

    private void ResetDailyIfNeeded(DateTime candleTime)
    {
        if (candleTime.Date > _lastSessionDate.Date)
        {
            _lastSessionDate = candleTime;
            _tradesToday = 0;
            _dailyPnL = 0m;
            _orderPending = false;
            this.LogInfo($"=== NEUER HANDELSTAG: {candleTime:yyyy-MM-dd} === Reset durchgeführt");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // NEWS-FILTER
    // ═══════════════════════════════════════════════════════════════

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

    private void UpdateDailyPnL(decimal realizedPnL)
    {
        _dailyPnL += realizedPnL;
        this.LogInfo($"PnL Update: Trade={realizedPnL:F2} USD | Tages-PnL={_dailyPnL:F2} USD");

        if (_dailyPnL <= -MaxDailyLossUSD)
        {
            this.LogInfo($"!!! AUTO-STOP !!! Tagesverlust {_dailyPnL:F2} USD >= Limit {MaxDailyLossUSD} USD");
            Stop();
        }
    }
}
