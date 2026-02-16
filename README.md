# LUCID VWAP ELITE V15 – ATAS Trading Bot

Automatisierter Trading-Bot als ATAS Chart Strategy.
Basiert auf **Auction Market Theory**, **Liquidity Sweeps** und **Orderflow-Analyse**.

Handelt Long/Short in der RTH-Session (15:30–21:00 EST) – nur dort, wo der Markt
erst Liquidität holt und sie dann ablehnt, außerhalb der Value Area.

## Voraussetzungen

- [ATAS Platform](https://atas.net/) installiert (Standard: `C:\Program Files (x86)\ATAS Platform`)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Futures-Datenfeed in ATAS (z.B. Rithmic, CQG, dxFeed)

## Build

```bash
git clone https://github.com/Dirak44/ATAS-Trading-BOT.git
cd ATAS-Trading-BOT
dotnet build src/LUCID_VWAP_Bot/
```

Die DLL wird erstellt unter:
```
src/LUCID_VWAP_Bot/bin/Debug/net8.0/LUCID_VWAP_Bot.dll
```

## Installation & Start

1. **DLL kopieren** in den ATAS Strategies-Ordner:
   ```
   C:\Users\<DeinUser>\Documents\ATAS\Strategies\
   ```
   > Den genauen Pfad findest du in ATAS unter: *Settings → General → Strategies Folder*

2. **ATAS starten** und einen Chart öffnen (z.B. ES, NQ, YM Futures)

3. **Strategy laden**: Rechtsklick auf Chart → *Strategies* → `LucidVwapEliteV14` auswählen

4. **Parameter anpassen** im Strategy-Panel (siehe unten)

5. **Start klicken** → Bot handelt automatisch während der RTH-Session

## Wie der Bot handelt

### Entry-Logik (alle Bedingungen gleichzeitig)

**Long-Entry:**
1. Preis unter VAL (Value Area Low vom Vortag) → Markt ist "billig"
2. Liquidity Sweep unter einem Swing-Low erkannt
3. Absorption bestätigt (hohes Volumen, kleine Range)
4. Delta Flip bullish (Delta dreht von negativ zu positiv)

**Short-Entry:**
1. Preis über VAH (Value Area High vom Vortag) → Markt ist "teuer"
2. Liquidity Sweep über einem Swing-High erkannt
3. Absorption bestätigt (hohes Volumen, kleine Range)
4. Delta Flip bearish (Delta dreht von positiv zu negativ)

Kein Trade wenn nur ein Teil der Bedingungen erfüllt ist.

### Filter
- RTH only (15:30–21:00 EST)
- Mindestvolumen pro Kerze
- Mindest-ATR
- Max Trades pro Tag
- Max Tagesverlust (Auto-Stop)
- News-Zeitfenster (8:00–8:45 & 14:00–14:45 EST)

## Parameter

Alle Parameter sind in der ATAS UI konfigurierbar.

### Trade Management
| Parameter | Default | Beschreibung |
|-----------|---------|-------------|
| Min Volumen/Kerze | 300 | Mindestvolumen pro Bar |
| Min ATR (Punkte) | 0.6 | Mindest-ATR für Volatilität |
| ATR Mult TP | 2.0 | Take-Profit = ATR × Multiplikator |
| ATR Mult SL | 1.0 | Stop-Loss = ATR × Multiplikator |
| Max Trades/Tag | 5 | Maximale Trades pro Session |
| Max Tagesverlust USD | 500 | Auto-Stop bei Verlust |
| Kontrakte/Trade | 1 | Positionsgrösse |
| News-Filter aktiv | true | Kein Trading um News-Zeiten |
| Min R:R Ratio | 1.5 | Minimales Risk:Reward |

### Auction Market Theory
| Parameter | Default | Beschreibung |
|-----------|---------|-------------|
| Value Area % | 70 | Prozent des Volumens für Value Area |
| Initial Balance Min | 30 | Dauer der Initial Balance in Minuten |

### Liquidity Detection
| Parameter | Default | Beschreibung |
|-----------|---------|-------------|
| Sweep Lookback (Bars) | 20 | Anzahl Bars für Swing-Punkt-Erkennung |
| Sweep Threshold (Ticks) | 4 | Min. Ticks über/unter Swing für Sweep |
| LVN Threshold (Vol %) | 20 | Low Volume Node Schwelle |

### Orderflow Trigger
| Parameter | Default | Beschreibung |
|-----------|---------|-------------|
| Absorption Min Vol | 500 | Min. Volumen für Absorption |
| Absorption Max Range (Ticks) | 3 | Max. Range in Ticks bei Absorption |
| Delta Flip Lookback (Bars) | 3 | Bars zurückschauen für Delta-Flip |

## Logging

Der Bot loggt alle Aktivitäten im ATAS Log-Fenster:

```
=== NEUER HANDELSTAG: 2026-02-16 === PrevVAH=6045.50 PrevVAL=6020.25 PrevVPOC=6032.00
VALUE AREA berechnet: VAH=6045.50 VAL=6020.25 VPOC=6032.00 (48 Levels, 125000 Vol)
INITIAL BALANCE: High=6048.00 Low=6018.50 Range=29.50
[Bar 412] SWEEP LOW erkannt: Low=6018.00 < SwingLow=6020.25, Close=6021.50 zurück darüber
[Bar 412] *** SIGNAL LONG *** Sweep+Absorption+DeltaFlip unter VAL
[Bar 412] TRADE #1 LONG | Entry=6021.50 | SL=6015.50 | TP=6033.50 | Qty=1 | R:R=2.00
FLAT – Tages-PnL: 600.00 USD | Trades heute: 1
```

## Tech-Stack

- .NET 8 (C# 12)
- ATAS SDK (`ChartStrategy`)
- Indikatoren: VWAP (Daily), EMA(20), ATR(14)

## Lizenz

Private Nutzung.
