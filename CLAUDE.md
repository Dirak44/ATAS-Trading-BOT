# ATAS-Trading-BOT – LUCID_VWAP_ELITE_V15

## Projektbeschreibung
Automatisierter Trading-Bot als ATAS Chart Strategy.
**V15**: Auction Market Theory + Liquidity Sweep + Orderflow-basierte Entries.
Handelt Long/Short in der RTH-Session (15:30–21:00 EST).
Kernkonzept: Handelt NUR dort, wo der Markt erst Liquidität holt und sie dann
ablehnt, außerhalb der Value Area.

## Tech-Stack
- **.NET 8** (C# 12) – `<TargetFramework>net8.0</TargetFramework>`
- **ATAS SDK** – Klasse erbt von `ChartStrategy`
- File-scoped namespaces, Nullable enabled
- Namespace: `LUCID_VWAP_Bot`

## Projektstruktur
```
ATAS-Trading-BOT/
├── CLAUDE.md              ← Diese Datei
├── TODO.md                ← Aufgaben-Tracking
├── .gitignore
└── src/
    └── LUCID_VWAP_Bot/
        ├── LUCID_VWAP_Bot.csproj
        └── LucidVwapEliteV14.cs   ← Klasse: LucidVwapEliteV14
```

## Build & Deployment
```bash
dotnet build src/LUCID_VWAP_Bot/
```
ATAS SDK DLLs werden aus `C:\Program Files (x86)\ATAS Platform` referenziert.

### Bot in ATAS starten
1. `dotnet build` → erzeugt `LUCID_VWAP_Bot.dll`
2. DLL kopieren nach: `C:\Users\<User>\Documents\ATAS\Strategies\`
3. ATAS öffnen → Chart öffnen (z.B. ES/NQ Futures)
4. Rechtsklick → "Strategies" → `LucidVwapEliteV14` auswählen
5. Parameter im Strategy-Panel anpassen
6. "Start" klicken → Bot handelt automatisch in der RTH-Session

## ATAS SDK Build-Wissen (ermittelt 16.02.2026)

### BUILD ERFOLGREICH ✅
`dotnet build src/LUCID_VWAP_Bot/` → `LUCID_VWAP_Bot.dll`
Nur Warnings (WindowsBase Versionskonflikt + Stop() deprecated), keine Fehler.

### DLL-Referenzen (alle im csproj)
ATAS-Installation: `C:\Program Files (x86)\ATAS Platform`
```
ATAS.Strategies.dll          ← ChartStrategy Basisklasse
ATAS.Indicators.dll          ← VWAP, EMA, ATR Indikatoren
ATAS.Indicators.Technical.dll ← Technische Indikatoren
ATAS.Types.dll               ← Basis-Typen
ATAS.DataFeedsCore.dll       ← Order, Security, Portfolio, OrderDirections, OrderTypes
Utils.Common.dll             ← Logging (Utils.Common.Logging)
OFT.Attributes.dll           ← [Parameter] Attribut
OFT.Core.dll                 ← Core-Typen
StockSharp.BusinessEntities.dll ← Transitive Abhängigkeit
StockSharp.Messages.dll      ← Transitive Abhängigkeit
StockSharp.Logging.dll       ← Transitive Abhängigkeit
StockSharp.Localization.dll  ← Transitive Abhängigkeit
Ecng.Common.dll              ← Transitive Abhängigkeit
Ecng.ComponentModel.dll      ← Transitive Abhängigkeit
Ecng.Collections.dll         ← Transitive Abhängigkeit
Ecng.Serialization.dll       ← Transitive Abhängigkeit
```

### Richtige API-Typen & Namespaces
| Konzept | Korrekt | FALSCH |
|---------|---------|--------|
| Order-Typen | `ATAS.DataFeedsCore.Order` | ~~StockSharp.BusinessEntities.Order~~ |
| Security | `ATAS.DataFeedsCore.Security` | ~~StockSharp.BusinessEntities.Security~~ |
| Portfolio | `ATAS.DataFeedsCore.Portfolio` | ~~StockSharp.BusinessEntities.Portfolio~~ |
| Richtung | `OrderDirections.Buy/Sell` | ~~Sides.Buy/Sell~~ |
| Order-Menge | `QuantityToFill` | ~~Volume~~ |
| Aktive Order | `order.State == OrderStates.Active` | ~~order.Balance > 0~~ |
| Tick-Size | `Security.TickSize` | ~~Security.MinStepSize~~ |
| Stop-Order | `OrderTypes.Stop` | ~~OrderTypes.Conditional~~ |
| Parameter | `using Parameter = OFT.Attributes.ParameterAttribute;` | ~~using OFT.Attributes + using ATAS.Indicators~~ |
| Logging | `using Utils.Common.Logging;` → `this.LogInfo()` | ~~using StockSharp.Logging~~ |
| LogError | `this.LogError("msg", ex)` | ~~this.LogError($"msg: {ex.Message}")~~ |
| Position | `OnCurrentPositionChanged()` (kein Parameter) | ~~OnPositionChanged(Position)~~ |
| VWAP | `new VWAP()` (Default = Daily) | ~~Type = VWAPPeriodType.Daily~~ |
| Stop | `StopAsync()` | ~~Stop()~~ (deprecated) |

### Bekannte Warnings (ignorierbar)
1. **WindowsBase** Versionskonflikt 4.0 vs 8.0 – nur Warning

## V15 Signal-Logik (ALLE Bedingungen gleichzeitig)

### Long-Entry (Pflicht-Bedingungen)
1. Preis < VAL (Value Area Low vom Vortag) → Markt ist "billig"
2. Liquidity Sweep unter Swing-Low erkannt (Wick durch, Close zurück)
3. Absorption (hohes Vol, kleine Range) → Markt lehnt Level ab
4. Delta Flip bullish (Delta dreht von negativ zu positiv)
5. Multi-TF: EMA Slow steigend (wenn UseMultiTF aktiv)

### Short-Entry (Pflicht-Bedingungen)
1. Preis > VAH (Value Area High vom Vortag) → Markt ist "teuer"
2. Liquidity Sweep über Swing-High erkannt (Wick durch, Close zurück)
3. Absorption (hohes Vol, kleine Range) → Markt lehnt Level ab
4. Delta Flip bearish (Delta dreht von positiv zu negativ)
5. Multi-TF: EMA Slow fallend (wenn UseMultiTF aktiv)

### Bonus-Bestätigungen (stärken Signal, blockieren nicht)
- **LVN (Low Volume Node):** Preis nahe dünn gehandeltem Level im Vortages-Profil (< LVN_Threshold % Durchschnittsvol)
- **Poor High/Low:** Session-Extreme mit nur 1 Touch (Single Print) → unvollständige Auktion
- **Failed Continuation:** Breakout-Versuch scheitert innerhalb FailedContBars → Preis kommt zurück

### Trailing-Stop (optional)
- ATR-basiert mit `TrailingStopAtrMult` Abstand
- Wird nachgezogen wenn Preis sich in Trade-Richtung bewegt
- Market-Exit wenn Trailing-Stop ausgelöst wird

## Kritische Regeln

### VWAP
- **NIEMALS** `_vwap.Calculate(bar, value)` aufrufen → Fehler!
- Zugriff **nur** über `_vwap[bar]`

### Code-Konventionen
- File-scoped namespaces verwenden
- Try-Catch um `GetCandle()`, `candle.Delta`, Indikator-Zugriffe
- Code-Struktur: Konstruktor → OnCalculate (Profil/IB/Swings → Filter → Signal → ExecuteTrade) → OnPositionChanged

### Parameter (23 Stück – alle in ATAS UI konfigurierbar)
**Trade Management:**
1. MinVolume (300) – Min Volumen/Kerze
2. MinAtr (0.6) – Min ATR in Punkten
3. AtrMultTP (2.0) – ATR Multiplikator Take-Profit
4. AtrMultSL (1.0) – ATR Multiplikator Stop-Loss
5. MaxTradesDay (5) – Max Trades pro Tag
6. MaxDailyLossUSD (500) – Max Tagesverlust
7. MyQuantity (1) – Kontrakte pro Trade
8. UseNewsFilter (true) – News-Filter aktiv
9. MinRR (1.5) – Min Risk:Reward Ratio

**Auction Market Theory:**
10. VA_Percent (70%) – Value Area Prozent (umbenannt wegen Basis-Property)
11. IB_Minutes (30) – Initial Balance Dauer

**Liquidity Detection:**
12. SweepLookback (20) – Swing-Punkt Lookback Bars
13. SweepThresholdTicks (4) – Min Ticks über/unter Swing für Sweep
14. LVN_Threshold (20%) – Low Volume Node Schwelle

**Orderflow Trigger:**
15. AbsorptionMinVol (500) – Min Volumen für Absorption
16. AbsorptionMaxRange (3) – Max Range in Ticks für Absorption
17. DeltaFlipBars (3) – Lookback Bars für Delta Flip

**Trailing-Stop:**
18. UseTrailingStop (false) – Trailing-Stop aktivieren
19. TrailingStopAtrMult (1.5) – ATR Multiplikator für Trailing-Abstand

**Failed Continuation:**
20. UseFailedContinuation (true) – Failed Continuation Erkennung aktiv
21. FailedContBars (3) – Lookback Bars für Failed Continuation

**Multi-Timeframe:**
22. UseMultiTF (true) – Multi-TF Trend-Filter aktiv
23. SlowEmaPeriod (50) – Periode der Slow EMA für Trendrichtung

### Logging-Konventionen
- **ValueArea:** `VALUE AREA berechnet: VAH=x VAL=x VPOC=x`
- **IB:** `INITIAL BALANCE: High=x Low=x Range=x`
- **Sweep:** `SWEEP HIGH/LOW erkannt: ...`
- **LVN:** `LVN erkannt bei x (Vol=y < Threshold=z)`
- **FailedCont:** `FAILED CONTINUATION BULLISH/BEARISH: ...`
- **MultiTF:** `MTF: MultiTF=true/false Bullish=... Bearish=... EMA50=x`
- **Signal:** `SIGNAL LONG/SHORT` + AMT/LIQ/OF Bedingungen + Bonus-Count + MTF
- **Trade:** `TRADE #x | Entry | SL | TP | Qty | R:R | TrailStop`
- **TrailingStop:** `TRAILING-STOP nachgezogen/AUSGELÖST: ...`
- **Flat:** `FLAT – Tages-PnL: xxx USD`
- **Error:** `LogError` in Try-Catch-Blöcken

## Workflow-Regeln
1. **Vor jeder Arbeitssession:** CLAUDE.md und TODO.md lesen und aktualisieren
2. **Vor Ratelimit:** Commit mit allen aktuellen Änderungen machen
3. **Nach Abschluss einer Aufgabe:** TODO.md aktualisieren (abhaken)

## Git
- Remote: `https://github.com/Dirak44/ATAS-Trading-BOT.git`
- Branch: `main`
