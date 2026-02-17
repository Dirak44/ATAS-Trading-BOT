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
    private readonly EMA _emaSlow = new() { Period = 50 };
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
    // PARAMETER – Trailing Stop
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Trailing-Stop aktiv")]
    public bool UseTrailingStop { get; set; } = false;

    [Parameter]
    [DisplayName("Trailing-Stop ATR Mult")]
    public decimal TrailingStopAtrMult { get; set; } = 1.5m;

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Failed Continuation
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Failed Continuation aktiv")]
    public bool UseFailedContinuation { get; set; } = true;

    [Parameter]
    [DisplayName("Failed Cont. Lookback (Bars)")]
    public int FailedContBars { get; set; } = 3;

    // ═══════════════════════════════════════════════════════════════
    // PARAMETER – Multi-Timeframe Bestätigung
    // ═══════════════════════════════════════════════════════════════

    [Parameter]
    [DisplayName("Multi-TF Trend-Filter aktiv")]
    public bool UseMultiTF { get; set; } = true;

    [Parameter]
    [DisplayName("Slow EMA Periode")]
    public int SlowEmaPeriod { get; set; } = 50;

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
    // INTERNE FELDER – Poor High/Low (Single-Print-Extremes)
    // ═══════════════════════════════════════════════════════════════

    private int _sessionHighHitCount;
    private int _sessionLowHitCount;

    // ═══════════════════════════════════════════════════════════════
    // INTERNE FELDER – Trailing-Stop
    // ═══════════════════════════════════════════════════════════════

    private decimal _trailingStopPrice;
    private bool _isLongPosition;
    private decimal _entryPrice;

    // ═══════════════════════════════════════════════════════════════
    // KONSTRUKTOR
    // ═══════════════════════════════════════════════════════════════

    public LucidVwapEliteV14()
    {
        Add(_vwap);
        Add(_ema);
        Add(_emaSlow);
        Add(_atr);
    }

    // ═══════════════════════════════════════════════════════════════
    // HAUPTLOGIK – OnCalculate
    // ═══════════════════════════════════════════════════════════════

    /// Hauptlogik: wird pro Bar aufgerufen – baut Profil/IB/Swings auf, prüft Filter-Kaskade und löst bei V15-Signal einen Trade aus.
    protected override void OnCalculate(int bar, decimal value)
    {
        try
        {
            var candle = GetCandle(bar);

            // Slow EMA Periode synchronisieren (falls User in UI ändert)
            if (_emaSlow.Period != SlowEmaPeriod)
                _emaSlow.Period = SlowEmaPeriod;

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
            // TRAILING-STOP MANAGEMENT (bei offener Position)
            // ═══════════════════════════════════════

            if (UseTrailingStop && CurrentPosition != 0 && _trailingStopPrice > 0)
            {
                UpdateTrailingStop(bar, candle);
            }

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
                _ = StopAsync();
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

            // --- 4. Zusätzliche Bestätigungen (optional, nicht blockierend) ---

            // LVN: Preis liegt nahe einem Low Volume Node → erhöht Signalqualität
            bool nearLVN = IsNearLVN(candle.Close, tickSize);

            // Poor High/Low: Session-Extreme mit nur 1 Touch → unvollständige Auktion
            bool poorHigh = IsPoorHigh();
            bool poorLow = IsPoorLow();

            // Failed Continuation: Breakout-Versuch der gescheitert ist
            bool failedContBullish = DetectFailedContinuation(bar, checkBullish: true);
            bool failedContBearish = DetectFailedContinuation(bar, checkBullish: false);

            // Multi-TF Trend-Filter: EMA Slow Richtung bestimmt übergeordneten Trend
            bool multiTfBullish = true;
            bool multiTfBearish = true;
            if (UseMultiTF && bar >= SlowEmaPeriod + 1)
            {
                decimal emaSlowCurr = _emaSlow[bar];
                decimal emaSlowPrev = _emaSlow[bar - 1];
                multiTfBullish = emaSlowCurr > emaSlowPrev; // EMA steigend → bullish
                multiTfBearish = emaSlowCurr < emaSlowPrev; // EMA fallend → bearish
            }

            // ═══════════════════════════════════════
            // KOMBINIERTE ENTRY-LOGIK
            // ═══════════════════════════════════════

            // Basis-Signal: AMT + Sweep + Absorption + Delta Flip (alle Pflicht)
            // Multi-TF: blockiert Trades gegen den übergeordneten Trend (wenn aktiv)
            bool longSignal = belowVAL && sweepLow && absorption && deltaFlipBullish && multiTfBullish;
            bool shortSignal = aboveVAH && sweepHigh && absorption && deltaFlipBearish && multiTfBearish;

            // Bonus-Bestätigungen: LVN, Poor High/Low, Failed Continuation
            // stärken das Signal (für Logging/Analyse), blockieren es aber nicht
            int longConfirmations = (nearLVN ? 1 : 0) + (poorLow ? 1 : 0) + (failedContBullish ? 1 : 0);
            int shortConfirmations = (nearLVN ? 1 : 0) + (poorHigh ? 1 : 0) + (failedContBearish ? 1 : 0);

            if (longSignal && shortSignal) return;
            if (!longSignal && !shortSignal) return;

            this.LogInfo($"[Bar {bar}] AMT: VAH={_prevVAH:F2} VAL={_prevVAL:F2} VPOC={_prevVPOC:F2} | " +
                         $"Close={candle.Close:F2} aboveVAH={aboveVAH} belowVAL={belowVAL}");
            this.LogInfo($"[Bar {bar}] LIQ: SweepHigh={sweepHigh} SweepLow={sweepLow} LVN={nearLVN} | " +
                         $"PoorHigh={poorHigh} PoorLow={poorLow}");
            this.LogInfo($"[Bar {bar}] OF: Absorption={absorption} DeltaFlipBull={deltaFlipBullish} " +
                         $"DeltaFlipBear={deltaFlipBearish} FailedContBull={failedContBullish} " +
                         $"FailedContBear={failedContBearish}");
            this.LogInfo($"[Bar {bar}] MTF: MultiTF={UseMultiTF} Bullish={multiTfBullish} Bearish={multiTfBearish} " +
                         $"EMA{SlowEmaPeriod}={_emaSlow[bar]:F2}");

            if (longSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL LONG *** Sweep+Absorption+DeltaFlip unter VAL " +
                             $"(+{longConfirmations} Bonus: LVN={nearLVN} PoorLow={poorLow} FailedCont={failedContBullish}" +
                             $" | MTF=bullish)");
                ExecuteTrade(bar, isLong: true, candle.Close, atrValue);
            }
            else if (shortSignal)
            {
                this.LogInfo($"[Bar {bar}] *** SIGNAL SHORT *** Sweep+Absorption+DeltaFlip über VAH " +
                             $"(+{shortConfirmations} Bonus: LVN={nearLVN} PoorHigh={poorHigh} FailedCont={failedContBearish}" +
                             $" | MTF=bearish)");
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

    /// Akkumuliert Volumen pro Tick-Level im heutigen Profil (Basis für Value Area Berechnung).
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

    /// Berechnet VAH, VAL und VPOC aus dem Vortages-Profil nach dem CME-Algorithmus (VA_Percent vom Volumen um den VPOC).
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

    /// Trackt Session High/Low innerhalb der RTH-Session und zählt wie oft ein Extreme berührt wird (für Poor High/Low).
    private void UpdateSessionHighLow(IndicatorCandle candle)
    {
        var tod = candle.Time.TimeOfDay;
        if (tod < new TimeSpan(15, 30, 0) || tod > new TimeSpan(21, 0, 0))
            return;

        var tickSize = Security.TickSize;
        if (tickSize <= 0) tickSize = 0.25m;

        if (candle.High > _sessionHigh)
        {
            _sessionHigh = candle.High;
            _sessionHighHitCount = 1;
        }
        else if (Math.Abs(candle.High - _sessionHigh) <= tickSize)
        {
            _sessionHighHitCount++;
        }

        if (candle.Low < _sessionLow)
        {
            _sessionLow = candle.Low;
            _sessionLowHitCount = 1;
        }
        else if (Math.Abs(candle.Low - _sessionLow) <= tickSize)
        {
            _sessionLowHitCount++;
        }
    }

    /// Berechnet die Initial Balance (High/Low der ersten IB_Minutes nach RTH-Open 15:30).
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

    /// Erkennt Swing-Highs und Swing-Lows (3-Bar-Pivot) und speichert sie für die Liquidity-Sweep-Erkennung.
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
    // LIQUIDITY – LOW VOLUME NODES (LVN) / THIN BOOKS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der aktuelle Preis nahe einem Low Volume Node (LVN) liegt.
    /// LVN = Preis-Level im Vortages-Profil mit weniger als LVN_Threshold %
    /// des Durchschnittsvolumens. Diese "dünnen Stellen" im Profil sind Bereiche
    /// wo Preis schnell durchlaufen kann → gute Reversal-Zonen.
    /// </summary>
    private bool IsNearLVN(decimal price, decimal tickSize)
    {
        if (_prevProfile.Count == 0) return false;
        if (tickSize <= 0) tickSize = 0.25m;

        decimal avgVol = _prevProfile.Values.Average();
        decimal lvnThreshold = avgVol * (LVN_Threshold / 100m);

        // Suche im Umkreis von ±5 Ticks um den aktuellen Preis
        for (int i = -5; i <= 5; i++)
        {
            decimal level = Math.Round(price / tickSize) * tickSize + i * tickSize;

            if (_prevProfile.TryGetValue(level, out var vol))
            {
                if (vol < lvnThreshold)
                {
                    this.LogInfo($"LVN erkannt bei {level:F2} (Vol={vol:F0} < Threshold={lvnThreshold:F0})");
                    return true;
                }
            }
            else
            {
                // Level existiert nicht im Profil → definitiv dünn / kein Volumen
                this.LogInfo($"LVN erkannt bei {level:F2} (kein Volumen im Profil)");
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // LIQUIDITY – POOR HIGH / POOR LOW (SINGLE-PRINT-EXTREMES)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Ein "Poor High" entsteht wenn das Session-High nur 1x berührt wurde
    /// (Single Print) → unvollständige Auktion, Markt wird dieses Level
    /// wahrscheinlich nochmal testen. Gleiche Logik für Poor Low.
    /// </summary>
    private bool IsPoorHigh()
    {
        return _sessionHighHitCount <= 1 && _sessionHigh != decimal.MinValue;
    }

    private bool IsPoorLow()
    {
        return _sessionLowHitCount <= 1 && _sessionLow != decimal.MaxValue;
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDERFLOW – FAILED CONTINUATION ERKENNUNG
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Failed Continuation = Breakout-Versuch der scheitert.
    /// Preis bricht über ein Swing-High (oder unter Swing-Low) aus,
    /// aber innerhalb von FailedContBars kehrt er zurück und schliesst
    /// innerhalb der vorherigen Range → Breakout ist gescheitert.
    /// Starkes Reversal-Signal.
    /// </summary>
    private bool DetectFailedContinuation(int bar, bool checkBullish)
    {
        if (!UseFailedContinuation) return false;
        if (bar < FailedContBars + 2) return false;

        try
        {
            var current = GetCandle(bar);

            if (checkBullish)
            {
                // Failed Continuation nach unten → bullisches Signal
                // Preis bricht unter Swing-Low, kommt aber zurück
                for (int i = 1; i <= FailedContBars; i++)
                {
                    var breakoutBar = GetCandle(bar - i);
                    foreach (var swingLow in _recentLows)
                    {
                        // Bar hat unter Swing-Low geschlossen (Breakout-Versuch)
                        if (breakoutBar.Close < swingLow && current.Close > swingLow)
                        {
                            this.LogInfo($"[Bar {bar}] FAILED CONTINUATION BULLISH: " +
                                         $"Bar-{i} Close={breakoutBar.Close:F2} < SwingLow={swingLow:F2}, " +
                                         $"jetzt Close={current.Close:F2} zurück darüber");
                            return true;
                        }
                    }
                }
            }
            else
            {
                // Failed Continuation nach oben → bärisches Signal
                // Preis bricht über Swing-High, kommt aber zurück
                for (int i = 1; i <= FailedContBars; i++)
                {
                    var breakoutBar = GetCandle(bar - i);
                    foreach (var swingHigh in _recentHighs)
                    {
                        // Bar hat über Swing-High geschlossen (Breakout-Versuch)
                        if (breakoutBar.Close > swingHigh && current.Close < swingHigh)
                        {
                            this.LogInfo($"[Bar {bar}] FAILED CONTINUATION BEARISH: " +
                                         $"Bar-{i} Close={breakoutBar.Close:F2} > SwingHigh={swingHigh:F2}, " +
                                         $"jetzt Close={current.Close:F2} zurück darunter");
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in DetectFailedContinuation", ex);
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDERFLOW – ABSORPTION ERKENNUNG
    // ═══════════════════════════════════════════════════════════════

    /// Erkennt Absorption: hohes Volumen bei kleiner Preisrange → Markt lehnt Level ab.
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

    /// Erkennt Delta Flip: Delta dreht Vorzeichen (neg→pos = bullish, pos→neg = bearish) innerhalb DeltaFlipBars.
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

    /// Sendet Entry-Limit, Stop-Loss und Take-Profit Orders an ATAS und initialisiert Trailing-Stop falls aktiv.
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
            _isLongPosition = isLong;
            _entryPrice = entryPrice;

            // Trailing-Stop initialisieren (falls aktiv)
            if (UseTrailingStop)
            {
                decimal trailDist = atrValue * TrailingStopAtrMult;
                _trailingStopPrice = isLong
                    ? entryPrice - trailDist
                    : entryPrice + trailDist;
                this.LogInfo($"[Bar {bar}] TRAILING-STOP initialisiert: {_trailingStopPrice:F2} " +
                             $"(Abstand={trailDist:F2}, ATR*{TrailingStopAtrMult})");
            }

            string dir = isLong ? "LONG" : "SHORT";
            this.LogInfo($"[Bar {bar}] TRADE #{_tradesToday} {dir} | " +
                         $"Entry={entryPrice:F2} | SL={slPrice:F2} | TP={tpPrice:F2} | " +
                         $"Qty={MyQuantity} | R:R={tpDist / slDist:F2}" +
                         (UseTrailingStop ? $" | TrailStop={_trailingStopPrice:F2}" : ""));
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in ExecuteTrade", ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // POSITION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    /// Wird bei Positionsänderung aufgerufen – berechnet PnL beim Flat-Werden und cancelt alle offenen Orders.
    protected override void OnCurrentPositionChanged()
    {
        if (Math.Abs(CurrentPosition) < 0.001m)
        {
            // PnL schätzen basierend auf Entry-Preis und letztem Bar-Close
            if (_entryPrice > 0)
            {
                try
                {
                    var lastBar = CurrentBar - 1;
                    if (lastBar >= 0)
                    {
                        var candle = GetCandle(lastBar);
                        var tickSize = Security.TickSize;
                        if (tickSize <= 0) tickSize = 0.25m;

                        // Tick-Wert: z.B. ES = $12.50/Tick, NQ = $5.00/Tick
                        // Approximation: 1 Punkt = (1/tickSize) Ticks * $12.50 für ES
                        // Da wir den exakten Tick-Wert nicht kennen, berechnen wir
                        // PnL in Punkten und loggen beides
                        decimal priceDiff = _isLongPosition
                            ? candle.Close - _entryPrice
                            : _entryPrice - candle.Close;
                        decimal pnlTicks = priceDiff / tickSize;

                        // Tick-Wert Approximation (konservativ: $12.50 für ES Micro)
                        decimal tickValue = 12.50m;
                        decimal estimatedPnL = pnlTicks * tickValue * MyQuantity;

                        UpdateDailyPnL(estimatedPnL);
                    }
                }
                catch (Exception ex)
                {
                    this.LogError("[PnL] FEHLER bei PnL-Berechnung", ex);
                }

                _entryPrice = 0;
            }

            _orderPending = false;
            _trailingStopPrice = 0;
            CancelAllActiveOrders();
            this.LogInfo($"FLAT – Tages-PnL: {_dailyPnL:F2} USD | Trades heute: {_tradesToday}");
        }
    }

    /// Wird beim Stoppen der Strategy aufgerufen – cancelt alle noch aktiven Orders.
    protected override void OnStopping()
    {
        CancelAllActiveOrders();
    }

    /// Iteriert über alle Orders und cancelt jede die noch State=Active hat.
    private void CancelAllActiveOrders()
    {
        foreach (var o in Orders)
        {
            if (o.State == OrderStates.Active)
                CancelOrder(o);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TRAILING-STOP LOGIK
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Trailing-Stop: Zieht den Stop-Loss nach, wenn sich der Preis
    /// in die gewünschte Richtung bewegt. Nutzt ATR-basierten Abstand.
    /// Bei Long: Stop wird nach oben nachgezogen wenn neues High.
    /// Bei Short: Stop wird nach unten nachgezogen wenn neues Low.
    /// </summary>
    private void UpdateTrailingStop(int bar, IndicatorCandle candle)
    {
        try
        {
            var atrValue = _atr[bar];
            decimal trailDist = atrValue * TrailingStopAtrMult;

            if (_isLongPosition)
            {
                // Long: Trail-Stop nachziehen wenn Preis steigt
                decimal newTrailStop = candle.High - trailDist;
                if (newTrailStop > _trailingStopPrice)
                {
                    decimal oldStop = _trailingStopPrice;
                    _trailingStopPrice = newTrailStop;
                    this.LogInfo($"[Bar {bar}] TRAILING-STOP LONG nachgezogen: " +
                                 $"{oldStop:F2} → {_trailingStopPrice:F2} (High={candle.High:F2})");
                }

                // Prüfe ob Trailing-Stop ausgelöst wurde
                if (candle.Low <= _trailingStopPrice)
                {
                    this.LogInfo($"[Bar {bar}] TRAILING-STOP LONG AUSGELÖST bei {_trailingStopPrice:F2} " +
                                 $"(Low={candle.Low:F2})");
                    ClosePositionByTrailingStop();
                }
            }
            else
            {
                // Short: Trail-Stop nachziehen wenn Preis fällt
                decimal newTrailStop = candle.Low + trailDist;
                if (newTrailStop < _trailingStopPrice)
                {
                    decimal oldStop = _trailingStopPrice;
                    _trailingStopPrice = newTrailStop;
                    this.LogInfo($"[Bar {bar}] TRAILING-STOP SHORT nachgezogen: " +
                                 $"{oldStop:F2} → {_trailingStopPrice:F2} (Low={candle.Low:F2})");
                }

                // Prüfe ob Trailing-Stop ausgelöst wurde
                if (candle.High >= _trailingStopPrice)
                {
                    this.LogInfo($"[Bar {bar}] TRAILING-STOP SHORT AUSGELÖST bei {_trailingStopPrice:F2} " +
                                 $"(High={candle.High:F2})");
                    ClosePositionByTrailingStop();
                }
            }
        }
        catch (Exception ex)
        {
            this.LogError($"[Bar {bar}] FEHLER in UpdateTrailingStop", ex);
        }
    }

    /// <summary>
    /// Schliesst die Position wenn Trailing-Stop ausgelöst wird.
    /// Cancelt alle bestehenden Orders und sendet Market-Exit.
    /// </summary>
    private void ClosePositionByTrailingStop()
    {
        CancelAllActiveOrders();

        var exitDirection = _isLongPosition ? OrderDirections.Sell : OrderDirections.Buy;
        int qty = (int)Math.Abs(CurrentPosition);
        if (qty <= 0) qty = MyQuantity;

        OpenOrder(new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = exitDirection,
            Type = OrderTypes.Market,
            QuantityToFill = qty
        });

        _trailingStopPrice = 0;
        this.LogInfo($"TRAILING-STOP EXIT: {(exitDirection == OrderDirections.Sell ? "SELL" : "BUY")} " +
                     $"Qty={qty} Market");
    }

    // ═══════════════════════════════════════════════════════════════
    // TAGES-RESET (inkl. Value Area Rotation)
    // ═══════════════════════════════════════════════════════════════

    /// Tages-Reset: rotiert Vortages-Profil → Value Area, setzt PnL/Trades/Session-Daten auf Null.
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
            _sessionHighHitCount = 0;
            _sessionLowHitCount = 0;
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

    /// Prüft ob gerade News-Fenster ist (08:00–08:45 oder 14:00–14:45 EST) → kein Trading.
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

    /// Addiert realisierten Trade-PnL zum Tages-PnL und stoppt die Strategy bei Überschreitung des MaxDailyLossUSD.
    private void UpdateDailyPnL(decimal realizedPnL)
    {
        _dailyPnL += realizedPnL;
        this.LogInfo($"PnL Update: Trade={realizedPnL:F2} USD | Tages-PnL={_dailyPnL:F2} USD");

        if (_dailyPnL <= -MaxDailyLossUSD)
        {
            this.LogInfo($"!!! AUTO-STOP !!! Tagesverlust {_dailyPnL:F2} USD >= Limit {MaxDailyLossUSD} USD");
            _ = StopAsync();
        }
    }
}
