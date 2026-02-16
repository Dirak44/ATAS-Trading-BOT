# TODO – LUCID_VWAP_ELITE_V15

## Projekt-Setup
- [x] GitHub Repo erstellen (öffentlich)
- [x] Projektstruktur anlegen (.gitignore, CLAUDE.md, TODO.md)
- [x] .NET 8 Class Library .csproj erstellen
- [x] Strategy-Grundgerüst erstellen (LucidVwapEliteV14.cs)
- [x] Initial Commit & Push

## ATAS SDK Integration
- [x] ATAS SDK DLL-Pfad ermitteln & DLL-Referenzen im .csproj einbinden
- [x] Alle Platzhalter durch echte SDK-Aufrufe ersetzt (ChartStrategy, Indikatoren, Orders, Logging)
- [x] `dotnet build` erfolgreich ✅ (0 Fehler, nur Warnings)

## Strategy-Implementierung (V14 – Basis) ✅
- [x] Konstruktor + Indikatoren (VWAP, EMA 20, ATR 14)
- [x] 11 Parameter mit `[Parameter]`-Attributen definieren
- [x] Filter-Logik (RTH 15:30–21:00, Volumen, ATR, Position, MaxTrades, DailyLoss, News)
- [x] Signal-Logik (Long/Short: VWAP-Zone + DeltaEff + EMA + Delta)
- [x] Order-Execution (Entry Limit, SL Stop, TP Limit, R:R-Check)
- [x] OnPositionChanged (Flat-Handling, Restorders canceln)
- [x] Tages-Reset & PnL-Tracking
- [x] News-Filter Methode (8:00–8:45 & 14:00–14:45 EST)
- [x] Logging (Bar/Signal/Trade/Flat/Error)

## V15 – Auction Market Theory + Liquidity + Orderflow (Kundenwunsch)

### Kernkonzept
> Bot handelt NUR dort, wo der Markt erst Liquidität holt und sie dann ablehnt,
> außerhalb von Value. Alle Bedingungen müssen gleichzeitig erfüllt sein.
> Parameter sollen auf eigenen Daten eingestellt, aber in ATAS UI änderbar sein.

### 1. Auction Market Theory (Makro-Kontext)
- [x] Value Area berechnen (VAH, VAL, VPOC) aus Vortages-Profil
- [x] Initial Balance berechnen (erste 30min RTH High/Low)
- [x] Session High/Low tracken
- [x] Filter: Trade nur AUSSERHALB Value Area (Preis > VAH oder < VAL)
- [x] Erkennung: Markt ist "teuer" (über VAH) oder "billig" (unter VAL)
- [x] Parameter: `ValueAreaPercent` (default 70%), `IB_Minutes` (default 30)

### 2. Liquidity Detection (Mikro-Analyse)
- [x] Liquidity Sweep erkennen (Preis durchbricht High/Low, kommt sofort zurück)
- [x] Stops über Highs / unter Lows identifizieren (Swing-Punkte)
- [x] Thin Books / Low Volume Nodes erkennen (LVN im Vortages-Profil, ±5 Ticks Suche)
- [x] Poor High / Poor Low erkennen (Single-Print-Extremes, Hit-Counter ≤ 1)
- [x] Parameter: `SweepLookback` (Bars), `SweepThresholdTicks`, `LVN_Threshold`

### 3. Orderflow-basierte Entry-Trigger (statt Indikator-Signal)
- [x] Liquidity Sweep als Vorbedingung (Markt holt erst Liquidität)
- [x] Absorption erkennen (hohes Volumen, geringer Preisfortschritt → Ablehnung)
- [x] Delta Flip erkennen (Delta dreht Vorzeichen nach Sweep → Richtungswechsel)
- [x] Failed Continuation erkennen (Breakout scheitert innerhalb FailedContBars)
- [x] Parameter: `AbsorptionMinVol`, `AbsorptionMaxRange`, `DeltaFlipBars`, `FailedContBars`

### 4. Kombinierte Entry-Logik (ALLE Bedingungen gleichzeitig)
- [x] Long: Preis < VAL → Sweep unter Low → Absorption → Delta Flip bullish
- [x] Short: Preis > VAH → Sweep über High → Absorption → Delta Flip bearish
- [x] Kein Trade wenn nur Teil-Bedingungen erfüllt
- [x] Alte V14 VWAP/EMA-Logik durch neue AMT+Liquidity+Orderflow-Logik ersetzen

### 5. Neue Parameter (alle in ATAS UI konfigurierbar)
- [x] AMT-Parameter: ValueAreaPercent, IB_Minutes
- [x] Liquidity-Parameter: SweepLookback, SweepThresholdTicks, LVN_Threshold
- [x] Orderflow-Parameter: AbsorptionMinVol, AbsorptionMaxRange, DeltaFlipBars
- [x] Bestehende Parameter beibehalten (SL/TP/MaxTrades/DailyLoss/Quantity etc.)

## Erweiterungen (Backlog)
- [x] Thin Books / Low Volume Nodes (LVN) im Profil erkennen ✅
- [x] Poor High / Poor Low (Single-Print-Extremes) erkennen ✅
- [x] Failed Continuation als zusätzlicher Orderflow-Trigger ✅
- [x] Trailing-Stop Option (ATR-basiert, nachziehend) ✅
- [ ] Multi-Timeframe Bestätigung

## Testing
- [ ] 2 Wochen Replay-Testing in ATAS
- [ ] Log-Analyse & Parameter-Optimierung
- [ ] Live-Paper-Trading
