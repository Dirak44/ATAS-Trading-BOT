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

### DLL-Referenzen die benötigt werden
ATAS-Installation: `C:\Program Files (x86)\ATAS Platform`
```
ATAS.Strategies.dll       ← ChartStrategy Basisklasse
ATAS.Indicators.dll       ← VWAP, EMA, ATR + ParameterAttribute (VERALTET!)
ATAS.Indicators.Technical.dll ← Technische Indikatoren
ATAS.Types.dll            ← Basis-Typen
ATAS.DataFeedsCore.dll    ← Datafeed-Typen
Utils.Common.dll          ← ParameterAttribute lebt HIER (von OFT referenziert)
OFT.Attributes.dll        ← [Parameter] Attribut (RICHTIG, OFT.Attributes.ParameterAttribute)
OFT.Core.dll              ← Core-Typen
StockSharp.BusinessEntities.dll ← Security, Portfolio, Position, Order
StockSharp.Messages.dll   ← Sides, OrderTypes
StockSharp.Logging.dll    ← Logging Extensions
```

### Bekannte Build-Probleme & Lösungen

#### 1. `[Parameter]` Ambiguity
**Problem:** `CS0104: "Parameter" ist mehrdeutig zwischen OFT.Attributes.ParameterAttribute und ATAS.Indicators.ParameterAttribute`
**Lösung:** `ATAS.Indicators.ParameterAttribute` ist als `[Obsolete]` markiert mit Hinweis "Use OFT.Attributes.ParameterAttribute instead".
→ Entweder `using OFT.Attributes;` und NICHT `using ATAS.Indicators;` zusammen verwenden,
→ ODER vollqualifiziert: `[OFT.Attributes.Parameter]`
→ ODER mit `using Parameter = OFT.Attributes.ParameterAttribute;` alias

#### 2. `ValueAreaPercent` Property-Hiding (CS0108)
**Problem:** `ChartStrategy` hat bereits eine Property `ValueAreaPercent`
**Lösung:** Property umbenannt zu `VA_Percent`

#### 3. `OnPositionChanged(Position)` – CS0115
**Problem:** `override` passt nicht – Methode existiert möglicherweise nicht in der Basisklasse
**Status:** OFFEN – Signatur von ChartStrategy.OnPositionChanged muss noch ermittelt werden
- Reflection scheitert an Assembly-Abhängigkeiten
- Binär-Suche in DLL findet "OnPositionChanged" NICHT in ATAS.Strategies.dll
- **TODO:** ATAS Online-Doku/API-Referenz oder Beispiel-Strategies prüfen
- **Möglichkeit:** Event-basiert statt override (z.B. `PositionChanged += handler`)
- **Möglichkeit:** Methode heißt anders oder kommt aus einem Interface

#### 4. WindowsBase Versions-Warnung (MSB3277)
**Problem:** Konflikt WindowsBase 4.0 vs 8.0
**Status:** Nur Warning, nicht blockierend. Kann ignoriert werden.

### ATAS DLLs im Ordner (Referenz)
```
ATAS.Strategies.dll, ATAS.Indicators.dll, ATAS.Indicators.Technical.dll,
ATAS.Indicators.Other.dll, ATAS.Types.dll, ATAS.DataFeedsCore.dll,
OFT.Attributes.dll, OFT.Core.dll, Utils.Common.dll,
StockSharp.BusinessEntities.dll, StockSharp.Messages.dll, StockSharp.Logging.dll,
StockSharp.Algo.dll, StockSharp.Community.dll, StockSharp.Configuration.dll,
StockSharp.Fix.dll, StockSharp.Licensing.dll, StockSharp.Localization.dll
```

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
