# TODO – LUCID_VWAP_ELITE_V14

## Projekt-Setup
- [x] GitHub Repo erstellen (öffentlich)
- [x] Projektstruktur anlegen (.gitignore, CLAUDE.md, TODO.md)
- [x] .NET 8 Class Library .csproj erstellen
- [x] Strategy-Grundgerüst erstellen (LucidVwapEliteV14.cs)
- [x] Initial Commit & Push

## ATAS SDK Integration
- [x] ATAS SDK DLL-Pfad ermitteln & DLL-Referenzen im .csproj einbinden
- [x] Alle Platzhalter durch echte SDK-Aufrufe ersetzt (ChartStrategy, Indikatoren, Orders, Logging)
- [ ] `dotnet build` erfolgreich durchführen (erfordert ATAS Installation auf Build-Maschine)

## Strategy-Implementierung (V14 – Basis)
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
- [ ] Value Area berechnen (VAH, VAL, VPOC) aus Vortages-Profil
- [ ] Initial Balance berechnen (erste 30min RTH High/Low)
- [ ] Session High/Low tracken
- [ ] Filter: Trade nur AUSSERHALB Value Area (Preis > VAH oder < VAL)
- [ ] Erkennung: Markt ist "teuer" (über VAH) oder "billig" (unter VAL)
- [ ] Parameter: `ValueAreaPercent` (default 70%), `IB_Minutes` (default 30)

### 2. Liquidity Detection (Mikro-Analyse)
- [ ] Liquidity Sweep erkennen (Preis durchbricht High/Low, kommt sofort zurück)
- [ ] Stops über Highs / unter Lows identifizieren (Swing-Punkte)
- [ ] Thin Books / Low Volume Nodes erkennen (dünne Stellen im Orderbook)
- [ ] Poor High / Poor Low erkennen (Single-Print-Extremes)
- [ ] Parameter: `SweepLookback` (Bars), `SweepThresholdTicks`, `LVN_Threshold`

### 3. Orderflow-basierte Entry-Trigger (statt Indikator-Signal)
- [ ] Liquidity Sweep als Vorbedingung (Markt holt erst Liquidität)
- [ ] Absorption erkennen (hohes Volumen, geringer Preisfortschritt → Ablehnung)
- [ ] Delta Flip erkennen (Delta dreht Vorzeichen nach Sweep → Richtungswechsel)
- [ ] Failed Continuation erkennen (Breakout-Versuch scheitert, Preis kommt zurück)
- [ ] Parameter: `AbsorptionMinVol`, `AbsorptionMaxRange`, `DeltaFlipBars`

### 4. Kombinierte Entry-Logik (ALLE Bedingungen gleichzeitig)
- [ ] Long: Preis < VAL → Sweep unter Low → Absorption → Delta Flip bullish
- [ ] Short: Preis > VAH → Sweep über High → Absorption → Delta Flip bearish
- [ ] Kein Trade wenn nur Teil-Bedingungen erfüllt
- [ ] Alte V14 VWAP/EMA-Logik durch neue AMT+Liquidity+Orderflow-Logik ersetzen

### 5. Neue Parameter (alle in ATAS UI konfigurierbar)
- [ ] AMT-Parameter: ValueAreaPercent, IB_Minutes
- [ ] Liquidity-Parameter: SweepLookback, SweepThresholdTicks, LVN_Threshold
- [ ] Orderflow-Parameter: AbsorptionMinVol, AbsorptionMaxRange, DeltaFlipBars
- [ ] Bestehende Parameter beibehalten (SL/TP/MaxTrades/DailyLoss/Quantity etc.)

## Erweiterungen (Backlog)
- [ ] VWAP Fallback-Implementierung (eigene Session-VWAP Klasse)
- [ ] Trailing-Stop Option
- [ ] Multi-Timeframe Bestätigung

## Testing
- [ ] 2 Wochen Replay-Testing in ATAS
- [ ] Log-Analyse & Parameter-Optimierung
- [ ] Live-Paper-Trading
