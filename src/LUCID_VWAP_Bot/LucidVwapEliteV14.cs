// LUCID_VWAP_ELITE_V15 – ATAS Chart Strategy
// Auction Market Theory + Liquidity Sweep + Orderflow Trigger
// Framework: .NET 8 / C# 12 | Plattform: ATAS ≥ 5.8.x

using System.ComponentModel;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using ATAS.Strategies.Chart;
using OFT.Attributes;
using Utils.Common.Logging;

// Alias um Ambiguity zwischen OFT.Attributes.Parameter und ATAS.Indicators.Parameter aufzulösen
using Parameter = OFT.Attributes.ParameterAttribute;

namespace LUCID_VWAP_Bot;

/// <summary>
/// LUCID VWAP ELITE V15 – Orderflow-basierte AMT-Strategy.
/// Handelt Long/Short in der RTH-Session (15:30–21:00 EST).
///
/// Kernkonzept: Handelt NUR dort, wo der Markt erst Liquidität holt
/// und sie dann ablehnt, außerhalb der Value Area.
///
/// Alle Bedingungen müssen gleichzeitig erfüllt sein:
/// 1. AMT (Makro):  Preis außerhalb Value Area (VAH/VAL)
/// 2. Liquidity:    Sweep über/unter Swing High/Low erkannt
/// 3. Orderflow:    Absorption + Delta Flip bestätigen Ablehnung
/// </summary>
public class LucidVwapEliteV14 : ChartStrategy
{
    // ═══════════════════════════════════════════════════════════════
    // INDIKATOREN
    // ═══════════════════════════════════════════════════════════════

    private readonly VWAP _vwap = new();
    private readonly EMA _ema = new() { Period = 20 };
    private readonly ATR _atr = new() { Period = 14 };

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Trade Management (in ATAS UI konfigurierbar)
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Min Volumen/Kerze")]
    public int MinVolume { get; set; } = 300;

    [Parameter]
    [DisplayName("Min ATR (Punkte)")]
    public decimal MinAtr { get; set; } = 0.6m;

    [Parameter]
    [DisplayName("ATR Mult TP")]
    public decimal AtrMultTP { get; set; } = 2.0m;

    [Parameter]
    [DisplayName("ATR Mult SL")]
    public decimal AtrMultSL { get; set; } = 1.0m;

    [Parameter]
    [DisplayName("Max Trades/Tag")]
    public int MaxTradesDay { get; set; } = 5;

    [Parameter]
    [DisplayName("Max Tagesverlust USD")]
    public decimal MaxDailyLossUSD { get; set; } = 500m;

    [Parameter]
    [DisplayName("Kontrakte/Trade")]
    public int MyQuantity { get; set; } = 1;

    [Parameter]
    [DisplayName("News-Filter aktiv")]
    public bool UseNewsFilter { get; set; } = true;

    [Parameter]
    [DisplayName("Min R:R Ratio")]
    public decimal MinRR { get; set; } = 1.5m;

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Auction Market Theory
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Value Area %")]
    public decimal VA_Percent { get; set; } = 70m;

    [Parameter]
    [DisplayName("Initial Balance Minuten")]
    public int IB_Minutes { get; set; } = 30;

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Liquidity Detection
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Sweep Lookback (Bars)")]
    public int SweepLookback { get; set; } = 20;

    [Parameter]
    [DisplayName("Sweep Threshold (Ticks)")]
    public int SweepThresholdTicks { get; set; } = 4;

    [Parameter]
    [DisplayName("LVN Threshold (Vol %)")]
    public decimal LVN_Threshold { get; set; } = 20m;

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Orderflow Trigger
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Absorption Min Volumen")]
    public int AbsorptionMinVol { get; set; } = 500;

    [Parameter]
    [DisplayName("Absorption Max Range (Ticks)")]
    public int AbsorptionMaxRange { get; set; } = 3;

    [Parameter]
    [DisplayName("Delta Flip Lookback (Bars)")]
    public int DeltaFlipBars { get; set; } = 3;

    // ═══════════════════════════════════════════════════════════════
    // INTERNE FELDER – Trade Management
    // ═══════════════════════════════════════════════════════════════

    private bool _orderPending;
    private decimal _dailyPnL;
    private DateTime _lastSessionDate = DateTime.MinValue;
    private int _tradesToday;

    // ═══════════════════════════════════════════════════════════════
    // INTERNE FELDER – AMT (Vortages-Value Area)
    // ═══════════════════════════════════════════════════════════════

    private decimal _prevVAH;
    private decimal _prevVAL;
    private decimal _prevVPOC;
    private decimal _sessionHigh = decimal.MinValue;
    private decimal _sessionLow = decimal.MaxValue;
    private decimal _ibHigh = decimal.MinValue;
    private decimal _ibLow = decimal.MaxValue;
    private bool _ibComplete;
    private TimeSpan _ibEndTime;

    // Vortages-Profil: Preis → Volumen
    private readonly Dictionary<decimal, decimal> _todayProfile = new();
    private readonly Dictionary<decimal, decimal> _prevProfile = new();
    private decimal _todayTotalVolume;

    // ═══════════════════════════════════════════════════════════════
    // INTERNE FELDER – Liquidity (Swing-Punkte)
    // ═══════════════════════════════════════════════════════════════

    private readonly List<decimal> _recentHighs = new();
    private readonly List<decimal> _recentLows = new();

    // ═══════════════════════════════════════════════════════════════
    // KONSTRUKTOR
    // ═══════════════════════════════════════════════════════════════

    public LucidVwapEliteV14()
    {
        Add(_vwap);
        Add(_ema);
        Add(_atr);
    }

    // ═══════════════════════════════════════════════════════════════
    // HAUPTLOGIK – OnCalculate
    // ═══════════════════════════════════════════════════════════════

    protected override void OnCalculate(int bar, decimal value)
    {
        try
        {
            var candle = GetCandle(bar);

            // Tages-Reset (inkl. Value Area Rotation)
            ResetDailyIfNeeded(candle.Time);

            // Volumen-Profil aufbauen (für jeden Bar, nicht nur neueste)
            UpdateVolumeProfile(candle);

            // Session High/Low tracken
            UpdateSessionHighLow(candle);

            // Initial Balance tracken
            UpdateInitialBalance(candle);

            // Swing-Punkte für Liquidity tracken
            if (bar >= 2)
                UpdateSwingPoints(bar);

            // Ab hier: nur neueste Kerze verarbeiten
            if (bar != CurrentBar - 1) return;

            // ═══════════════════════════════════════
            // FILTER-KASKADE
            // ═══════════════════════════════════════

            if (candle.Time.TimeOfDay < new TimeSpan(15, 30, 0) ||
                candle.Time.TimeOfDay > new TimeSpan(21, 0, 0))
                return;

            if (candle.Volume < MinVolume) return;

            var atrValue = _atr[bar];
            if (atrValue < MinAtr) return;

            if (CurrentPosition != 0) return;
            if (_orderPending) return;
            if (_tradesToday >= MaxTradesDay) return;

            if (_dailyPnL <= -MaxDailyLossUSD)
            {
                this.LogInfo($"AUTO-STOP – Tagesverlust {_dailyPnL:F2} USD");
                Stop();
                return;
            }

            if (UseNewsFilter && IsNewsTime(candle.Time)) return;

            // ═══════════════════════════════════════
            // V15 SIGNAL-LOGIK: AMT + Liquidity + Orderflow
            // Alle 3 Module müssen gleichzeitig bestätigen!
            // ═══════════════════════════════════════

            // --- 1. AMT: Preis außerhalb Value Area? ---
            bool aboveVAH = _prevVAH > 0 && candle.Close > _prevVAH;
            bool belowVAL = _prevVAL > 0 && candle.Close < _prevVAL;

            if (!aboveVAH && !belowVAL) return;

            // --- 2. Liquidity Sweep erkennen ---
            var tickSize = Security.TickSize;
            if (tickSize <= 0) tickSize = 0.25m;
            decimal sweepThreshold = SweepThresholdTicks * tickSize;

            bool sweepHigh = false;
            bool sweepLow = false;

            if (aboveVAH)
            {
                foreach (var swingHigh in _recentHighs)
                {
                    if (candle.High > swingHigh + sweepThreshold &&
                        candle.Close < swingHigh)
                    {
                        sweepHigh = true;
                        this.LogInfo($"[Bar {bar}] SWEEP HIGH erkannt: High={candle.High:F2} > SwingHigh={swingHigh:F2}, Close={candle.Close:F2} zurück darunter");
                        break;
                    }
                }
            }

            if (belowVAL)
            {
                foreach (var swingLow in _recentLows)
                {
                    if (candle.Low < swingLow - sweepThreshold &&
                        candle.Close > swingLow)
                    {
                        sweepLow = true;
                        this.LogInfo($"[Bar {bar}] SWEEP LOW erkannt: Low={candle.Low:F2} < SwingLow={swingLow:F2}, Close={candle.Close:F2} zurück darüber");
                        break;
                    }
                }
            }

            if (!sweepHigh && !sweepLow) return;

            // --- 3. Orderflow: Absorption + Delta Flip ---
            bool absorption = DetectAbsorption(candle, tickSize);
            if (!absorption) return;

            DetectDeltaFlip(bar, out bool deltaFlipBullish, out bool deltaFlipBearish);

            // ═══════════════════════════════════════
            // KOMBINIERTE ENTRY-LOGIK
            // ═══════════════════════════════════════

            bool longSignal = belowVAL && sweepLow && absorption && deltaFlipBullish;
            bool shortSignal = aboveVAH && sweepHigh && absorption && deltaFlipBearish;

            if (longSignal && shortSignal) return;
            if (!longSignal && !shortSignal) return;

            this.LogInfo($"[Bar {bar}] AMT: VAH={_prevVAH:F2} VAL={_prevVAL:F2} VPOC={_prevVPOC:F2} | " +
                         $"Close={candle.Close:F2} aboveVAH={aboveVAH} belowVAL={belowVAL}");
            this.LogInfo($"[Bar {bar}] LIQ: SweepHigh={sweepHigh} SweepLow={sweepLow} | " +
                         $"OF: Absorption={absorption} DeltaFlipBull={deltaFlipBullish} DeltaFlipBear={deltaFlipBearish}");

            if (longSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL LONG *** Sweep+Absorption+DeltaFlip unter VAL");
                ExecuteTrade(bar, isLong: true, candle.Close, atrValue);
            }
            else if (shortSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL SHORT *** Sweep+Absorption+DeltaFlip über VAH");
                ExecuteTrade(bar, isLong: false, candle.Close, atrValue);
            }
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in OnCalculate", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // AMT – VOLUME PROFILE & VALUE AREA
    // ═══════════════════════════════════════════════════════════════

    private void UpdateVolumeProfile(IndicatorCandle candle)
    {
        var tickSize = Security.TickSize;
        if (tickSize <= 0) tickSize = 0.25m;

        decimal priceLevel = Math.Round(candle.Close / tickSize) * tickSize;

        if (_todayProfile.TryGetValue(priceLevel, out var existing))
            _todayProfile[priceLevel] = existing + candle.Volume;
        else
            _todayProfile[priceLevel] = candle.Volume;

        _todayTotalVolume += candle.Volume;
    }

    private void CalculateValueArea()
    {
        if (_prevProfile.Count == 0) return;

        decimal totalVol = _prevProfile.Values.Sum();
        if (totalVol <= 0) return;

        _prevVPOC = _prevProfile.MaxBy(kv => kv.Value).Key;

        decimal targetVol = totalVol * (VA_Percent / 100m);
        decimal accumulatedVol = _prevProfile[_prevVPOC];

        var sortedLevels = _prevProfile.Keys.OrderBy(p => p).ToList();
        int vpocIdx = sortedLevels.IndexOf(_prevVPOC);

        int upper = vpocIdx;
        int lower = vpocIdx;

        while (accumulatedVol < targetVol && (upper < sortedLevels.Count - 1 || lower > 0))
        {
            decimal volAbove = (upper < sortedLevels.Count - 1)
                ? _prevProfile[sortedLevels[upper + 1]]
                : 0;
            decimal volBelow = (lower > 0)
                ? _prevProfile[sortedLevels[lower - 1]]
                : 0;

            if (volAbove >= volBelow && upper < sortedLevels.Count - 1)
            {
                upper++;
                accumulatedVol += _prevProfile[sortedLevels[upper]];
            }
            else if (lower > 0)
            {
                lower--;
                accumulatedVol += _prevProfile[sortedLevels[lower]];
            }
            else
            {
                upper++;
                accumulatedVol += _prevProfile[sortedLevels[upper]];
            }
        }

        _prevVAH = sortedLevels[upper];
        _prevVAL = sortedLevels[lower];

        this.LogInfo($"VALUE AREA berechnet: VAH={_prevVAH:F2} VAL={_prevVAL:F2} VPOC={_prevVPOC:F2} " +
                     $"({_prevProfile.Count} Levels, {totalVol:F0} Vol)");
    }

    // ═══════════════════════════════════════════════════════════════
    // AMT – SESSION HIGH/LOW & INITIAL BALANCE
    // ═══════════════════════════════════════════════════════════════

    private void UpdateSessionHighLow(IndicatorCandle candle)
    {
        var tod = candle.Time.TimeOfDay;
        if (tod < new TimeSpan(15, 30, 0) || tod > new TimeSpan(21, 0, 0))
            return;

        if (candle.High > _sessionHigh) _sessionHigh = candle.High;
        if (candle.Low < _sessionLow) _sessionLow = candle.Low;
    }

    private void UpdateInitialBalance(IndicatorCandle candle)
    {
        if (_ibComplete) return;

        var tod = candle.Time.TimeOfDay;
        if (tod < new TimeSpan(15, 30, 0)) return;

        if (_ibEndTime == TimeSpan.Zero)
            _ibEndTime = new TimeSpan(15, 30, 0).Add(TimeSpan.FromMinutes(IB_Minutes));

        if (tod <= _ibEndTime)
        {
            if (candle.High > _ibHigh) _ibHigh = candle.High;
            if (candle.Low < _ibLow) _ibLow = candle.Low;
        }
        else if (!_ibComplete)
        {
            _ibComplete = true;
            this.LogInfo($"INITIAL BALANCE: High={_ibHigh:F2} Low={_ibLow:F2} Range={_ibHigh - _ibLow:F2}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LIQUIDITY – SWING-PUNKTE & SWEEP-ERKENNUNG
    // ═══════════════════════════════════════════════════════════════

    private void UpdateSwingPoints(int bar)
    {
        try
        {
            var prev = GetCandle(bar - 1);
            var prevPrev = GetCandle(bar - 2);
            var curr = GetCandle(bar);

            if (prev.High > prevPrev.High && prev.High > curr.High)
            {
                _recentHighs.Add(prev.High);
                if (_recentHighs.Count > SweepLookback)
                    _recentHighs.RemoveAt(0);
            }

            if (prev.Low < prevPrev.Low && prev.Low < curr.Low)
            {
                _recentLows.Add(prev.Low);
                if (_recentLows.Count > SweepLookback)
                    _recentLows.RemoveAt(0);
            }
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in UpdateSwingPoints", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDERFLOW – ABSORPTION ERKENNUNG
    // ═══════════════════════════════════════════════════════════════

    private bool DetectAbsorption(IndicatorCandle candle, decimal tickSize)
    {
        if (tickSize <= 0) tickSize = 0.25m;

        decimal range = candle.High - candle.Low;
        decimal rangeInTicks = range / tickSize;

        return candle.Volume >= AbsorptionMinVol && rangeInTicks <= AbsorptionMaxRange;
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDERFLOW – DELTA FLIP ERKENNUNG
    // ═══════════════════════════════════════════════════════════════

    private void DetectDeltaFlip(int bar, out bool bullish, out bool bearish)
    {
        bullish = false;
        bearish = false;

        if (bar < DeltaFlipBars) return;

        try
        {
            var current = GetCandle(bar);

            bool wasBearish = false;
            bool wasBullish = false;

            for (int i = 1; i <= DeltaFlipBars; i++)
            {
                var prev = GetCandle(bar - i);
                if (prev.Delta < 0) wasBearish = true;
                if (prev.Delta > 0) wasBullish = true;
            }

            if (wasBearish && current.Delta > 0)
                bullish = true;

            if (wasBullish && current.Delta < 0)
                bearish = true;
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in DetectDeltaFlip", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRADE EXECUTION (ATAS DataFeedsCore API)
    // ═══════════════════════════════════════════════════════════════

    private void ExecuteTrade(int bar, bool isLong, decimal entryPrice, decimal atrValue)
    {
        try
        {
            var tickSize = Security.TickSize;
            if (tickSize <= 0) tickSize = 0.25m;

            decimal slDist = Math.Max(8 * tickSize, atrValue * AtrMultSL);
            decimal tpDist = atrValue * AtrMultTP;

            if (slDist <= 0 || tpDist / slDist < MinRR)
            {
                this.LogInfo($"[Bar {bar}] SKIP – R:R {tpDist / Math.Max(slDist, 0.01m):F2} < {MinRR}");
                return;
            }

            decimal slPrice = isLong ? entryPrice - slDist : entryPrice + slDist;
            decimal tpPrice = isLong ? entryPrice + tpDist : entryPrice - tpDist;

            var direction = isLong ? OrderDirections.Buy : OrderDirections.Sell;
            var exitDirection = isLong ? OrderDirections.Sell : OrderDirections.Buy;

            // Entry Order (Limit)
            OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = direction,
                Type = OrderTypes.Limit,
                Price = entryPrice,
                QuantityToFill = MyQuantity
            });

            // Stop-Loss Order
            OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = exitDirection,
                Type = OrderTypes.Stop,
                Price = slPrice,
                QuantityToFill = MyQuantity
            });

            // Take-Profit Order (Limit)
            OpenOrder(new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = exitDirection,
                Type = OrderTypes.Limit,
                Price = tpPrice,
                QuantityToFill = MyQuantity
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
            this.LogError($"[Bar {bar}] FEHLER in ExecuteTrade", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // POSITION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    protected override void OnCurrentPositionChanged()
    {
        if (Math.Abs(CurrentPosition) < 0.001m)
        {
            _orderPending = false;
            CancelAllActiveOrders();
            this.LogInfo($"FLAT – Tages-PnL: {_dailyPnL:F2} USD | Trades heute: {_tradesToday}");
        }
    }

    protected override void OnStopping()
    {
        CancelAllActiveOrders();
    }

    private void CancelAllActiveOrders()
    {
        foreach (var o in Orders)
        {
            if (o.State == OrderStates.Active)
                CancelOrder(o);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TAGES-RESET (inkl. Value Area Rotation)
    // ═══════════════════════════════════════════════════════════════

    private void ResetDailyIfNeeded(DateTime candleTime)
    {
        if (candleTime.Date > _lastSessionDate.Date)
        {
            if (_todayProfile.Count > 0)
            {
                _prevProfile.Clear();
                foreach (var kv in _todayProfile)
                    _prevProfile[kv.Key] = kv.Value;

                CalculateValueArea();
            }

            _lastSessionDate = candleTime;
            _tradesToday = 0;
            _dailyPnL = 0m;
            _orderPending = false;

            _sessionHigh = decimal.MinValue;
            _sessionLow = decimal.MaxValue;
            _ibHigh = decimal.MinValue;
            _ibLow = decimal.MaxValue;
            _ibComplete = false;
            _ibEndTime = TimeSpan.Zero;

            _todayProfile.Clear();
            _todayTotalVolume = 0;

            _recentHighs.Clear();
            _recentLows.Clear();

            this.LogInfo($"=== NEUER HANDELSTAG: {candleTime:yyyy-MM-dd} === " +
                         $"PrevVAH={_prevVAH:F2} PrevVAL={_prevVAL:F2} PrevVPOC={_prevVPOC:F2}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // NEWS-FILTER
    // ═══════════════════════════════════════════════════════════════

    private static bool IsNewsTime(DateTime time)
    {
        var tod = time.TimeOfDay;
        if (tod >= new TimeSpan(8, 0, 0) && tod <= new TimeSpan(8, 45, 0))
            return true;
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
