# TODO – LUCID_VWAP_ELITE_V14

## Projekt-Setup
- [x] GitHub Repo erstellen (öffentlich)
- [x] Projektstruktur anlegen (.gitignore, CLAUDE.md, TODO.md)
- [x] .NET 8 Class Library .csproj erstellen
- [x] Strategy-Grundgerüst erstellen (LucidVwapEliteV14.cs)
- [x] Initial Commit & Push

## ATAS SDK Integration
- [ ] ATAS SDK DLL-Pfad ermitteln (OFT.Platform.SDK.dll etc.)
- [ ] DLL-Referenzen im .csproj einbinden
- [ ] `dotnet build` erfolgreich durchführen

## Strategy-Implementierung
- [x] Konstruktor + Indikatoren (VWAP, EMA 20, ATR 14)
- [x] 11 Parameter mit `[Parameter]`-Attributen definieren
- [x] Filter-Logik (RTH 15:30–21:00, Volumen, ATR, Position, MaxTrades, DailyLoss, News)
- [x] Signal-Logik (Long/Short: VWAP-Zone + DeltaEff + EMA + Delta)
- [x] Order-Execution (Entry Limit, SL Stop, TP Limit, R:R-Check)
- [x] OnPositionChanged (Flat-Handling, Restorders canceln)
- [x] Tages-Reset & PnL-Tracking
- [x] News-Filter Methode (8:00–8:45 & 14:00–14:45 EST)
- [x] Logging (Bar/Signal/Trade/Flat/Error)

## Erweiterungen
- [ ] VWAP Fallback-Implementierung (eigene Session-VWAP Klasse)
- [ ] Trailing-Stop Option
- [ ] Multi-Timeframe Bestätigung

## Testing
- [ ] 2 Wochen Replay-Testing in ATAS
- [ ] Log-Analyse & Parameter-Optimierung
- [ ] Live-Paper-Trading
