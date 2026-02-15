# ATAS-Trading-BOT – LUCID_VWAP_ELITE_V14

## Projektbeschreibung
Automatisierter Trading-Bot als ATAS Chart Strategy. Basiert auf VWAP, EMA(20) und ATR(14)
mit Order-Flow-Analyse (Delta-Effizienz). Handelt Long/Short in der RTH-Session (15:30–21:00 EST).

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
        └── LucidVwapEliteV14.cs
```

## Build
```bash
dotnet build src/LUCID_VWAP_Bot/
```
ATAS SDK DLL muss als Referenz im .csproj eingebunden sein (siehe TODO).

## Kritische Regeln

### VWAP
- **NIEMALS** `_vwap.Calculate(bar, value)` aufrufen → Fehler!
- Zugriff **nur** über `_vwap[bar]`
- VWAP ist Daily/Session-anchored (nicht kumulativ)
- Fallback: Eigene Session-VWAP Klasse falls ATAS-Probleme

### Code-Konventionen
- File-scoped namespaces verwenden
- Try-Catch um `GetCandle()`, `candle.Delta`, Indikator-Zugriffe
- Code-Struktur: Konstruktor → OnCalculate (Filter → Signal → ExecuteTrade) → OnPositionChanged

### Parameter (11 Stück)
1. MinVolume (300) – Min Volumen/Kerze
2. MinAtr (0.6) – Min ATR in Punkten
3. DeltaEff (10%) – Delta-Effizienz Schwelle
4. AtrMultTP (2.0) – ATR Multiplikator Take-Profit
5. AtrMultSL (1.0) – ATR Multiplikator Stop-Loss
6. VwapZoneMult (0.8) – VWAP Zone Multiplikator
7. MaxTradesDay (5) – Max Trades pro Tag
8. MaxDailyLossUSD (500) – Max Tagesverlust
9. MyQuantity (1) – Kontrakte pro Trade
10. UseNewsFilter (true) – News-Filter aktiv
11. MinRR (1.5) – Min Risk:Reward Ratio

### Logging-Konventionen
- **Bar:** Indikator-Werte + Filter-Status
- **Signal:** `SIGNAL LONG/SHORT` + alle Parameter
- **Trade:** `TRADE #x | Entry | SL | TP | Qty | Effizienz`
- **Flat:** `FLAT – Tages-PnL: xxx USD`
- **Error:** `LogError` in Try-Catch-Blöcken

## Workflow-Regeln
1. **Vor jeder Arbeitssession:** CLAUDE.md und TODO.md lesen und aktualisieren
2. **Vor Ratelimit:** Commit mit allen aktuellen Änderungen machen
3. **Nach Abschluss einer Aufgabe:** TODO.md aktualisieren (abhaken)

## Git
- Remote: `https://github.com/Dirak44/ATAS-Trading-BOT.git`
- Branch: `main`
