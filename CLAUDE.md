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

## V15 Signal-Logik (ALLE Bedingungen gleichzeitig)

### Long-Entry
1. Preis < VAL (Value Area Low vom Vortag) → Markt ist "billig"
2. Liquidity Sweep unter Swing-Low erkannt (Wick durch, Close zurück)
3. Absorption (hohes Vol, kleine Range) → Markt lehnt Level ab
4. Delta Flip bullish (Delta dreht von negativ zu positiv)

### Short-Entry
1. Preis > VAH (Value Area High vom Vortag) → Markt ist "teuer"
2. Liquidity Sweep über Swing-High erkannt (Wick durch, Close zurück)
3. Absorption (hohes Vol, kleine Range) → Markt lehnt Level ab
4. Delta Flip bearish (Delta dreht von positiv zu negativ)

## Kritische Regeln

### VWAP
- **NIEMALS** `_vwap.Calculate(bar, value)` aufrufen → Fehler!
- Zugriff **nur** über `_vwap[bar]`

### Code-Konventionen
- File-scoped namespaces verwenden
- Try-Catch um `GetCandle()`, `candle.Delta`, Indikator-Zugriffe
- Code-Struktur: Konstruktor → OnCalculate (Profil/IB/Swings → Filter → Signal → ExecuteTrade) → OnPositionChanged

### Parameter (17 Stück – alle in ATAS UI konfigurierbar)
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
10. ValueAreaPercent (70%) – Value Area Prozent
11. IB_Minutes (30) – Initial Balance Dauer

**Liquidity Detection:**
12. SweepLookback (20) – Swing-Punkt Lookback Bars
13. SweepThresholdTicks (4) – Min Ticks über/unter Swing für Sweep
14. LVN_Threshold (20%) – Low Volume Node Schwelle

**Orderflow Trigger:**
15. AbsorptionMinVol (500) – Min Volumen für Absorption
16. AbsorptionMaxRange (3) – Max Range in Ticks für Absorption
17. DeltaFlipBars (3) – Lookback Bars für Delta Flip

### Logging-Konventionen
- **ValueArea:** `VALUE AREA berechnet: VAH=x VAL=x VPOC=x`
- **IB:** `INITIAL BALANCE: High=x Low=x Range=x`
- **Sweep:** `SWEEP HIGH/LOW erkannt: ...`
- **Signal:** `SIGNAL LONG/SHORT` + AMT/LIQ/OF Bedingungen
- **Trade:** `TRADE #x | Entry | SL | TP | Qty | R:R`
- **Flat:** `FLAT – Tages-PnL: xxx USD`
- **Error:** `LogError` in Try-Catch-Blöcken

## Workflow-Regeln
1. **Vor jeder Arbeitssession:** CLAUDE.md und TODO.md lesen und aktualisieren
2. **Vor Ratelimit:** Commit mit allen aktuellen Änderungen machen
3. **Nach Abschluss einer Aufgabe:** TODO.md aktualisieren (abhaken)

## Git
- Remote: `https://github.com/Dirak44/ATAS-Trading-BOT.git`
- Branch: `main`
