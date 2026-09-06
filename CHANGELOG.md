# Changelog

Alle relevanten Änderungen am Plugin. Format lose an [Keep a Changelog]
(https://keepachangelog.com/) angelehnt, Versionen orientieren sich an
Semver.

## [0.1.2.5] — 2026-09-06

### Added

- **Re-Import-Auswahl** „Was importieren?“: Glamourer (Equipment /
  Customization getrennt), Penumbra (An/Aus-Status, Priorität, Optionen
  getrennt) sowie eine filterbare Mod-Liste mit Alle/Keine/Nur-aktive-
  Schnellwahl. Customize+-Template-Ausgabe im Report abschaltbar.
  Abgewählte Teile werden im Dry-Run/Apply-Report als übersprungen
  ausgewiesen.

## [Unreleased]

### Added

- **Dark/Light-Theme-Toggle im HTML-Export** mit LocalStorage-Persistenz.
- **Dye-Swatches** in der Equipment-Tabelle — Farb-Quadrat vor jedem
  Dye-Namen analog zu den RGB-Swatches der Advanced Customization.
- **Job-Icon** im Charakter-Block (über FFXIV-Konvention `62100 + JobId`).
- **Sub-Command** `/glamdoc export [md|html|json]` für headless-Exports
  ohne Fenster-Öffnen.
- **Tab-UI** mit Export / Historie / Re-Import / Einstellungen / Info.
- **Export-Historie** listet die letzten 20 Dateien, erlaubt Re-Open /
  Löschen / Kopieren.
- **Glamourer-Designs** werden als Backup-Sektion mit-exportiert.
- **Non-Default-Only-Filter** für Penumbra-Mods (Mods mit reinen
  Default-Settings ausblenden).
- **Icon-Warm-Up** parallelisiert Equipment-Icon-Extraktion beim
  Collect.
- **Re-Import** (JSON-Export zurück in Glamourer/Penumbra) mit
  Dry-Run-Default und Apply-Bestätigungs-Popup.
- **Auto-Export bei Zonen-Wechsel** (opt-in via Settings).
- **Git-Auto-Commit** nach jedem Export (opt-in, nutzt `git.exe` aus PATH).
- README und Changelog.

### Removed

- **Vergleich-Tab** und `ExportDiff`-Service.
- **Character-Picker** und **Multi-Char-Batch-Export**: Penumbras
  Effective-List für Remote-Charaktere liefert nur Mods, die der
  eigenen Collection bekannt sind, und gibt keine nachvollziehbaren
  Ergebnisse. UI ist wieder explizit auf den LocalPlayer fokussiert.

## [0.1.0] — Initial Dev-Release

### Added

- Drei Export-Formate: Markdown, HTML, JSON.
- Penumbra-Integration mit `GetAllModSettings` (filtert deaktivierte Mods).
- Glamourer-Integration:
  - Strukturierter State via `GetState` (JObject) statt nur Base64.
  - Customize-Felder mit UI-Labels (race-aware: „Tail Shape" für
    Miqo'te, „Horn Shape" für Au Ra, etc.).
  - Advanced Customization mit Hex + RGB + CSS-Farb-Swatches.
  - Bonus-Items, Sichtbarkeits-Toggles, vollständiger JSON-State.
  - Re-Import-Blob (Base64) bleibt für Migration erhalten.
- Customize+-Integration (aktives Profil + Profil-Liste).
- Lumina-Auflösung: Item-Namen, Stain-Namen, Race, Tribe, Glasses.
- Item-Icons im HTML-Export (inline Data-URIs, self-contained).
- Character-Picker: Dropdown über alle `IPlayerCharacter`-Objekte im
  `IObjectTable`.
- Persistente Konfiguration (Dalamud-Standard-Mechanismus).
- Preview-Box mit Text-Selektion und Copy-Button.
- „Ordner öffnen"-Button nach erfolgreichem Export.
- Summary-Zeile am Report-Anfang (Aktive Mods / Glamourer-Slots /
  Customize+-Profile).
- Collection-Name im Dateinamen (optional via Config).
