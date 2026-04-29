# Glamour Documenter

Dalamud-Plugin für Final Fantasy XIV. Exportiert die kombinierten
Einstellungen aus **Penumbra**, **Glamourer** und **Customize+** für den
lokalen Charakter als Markdown-, HTML- oder JSON-Dokumentation.

## Features

- **Fokus LocalPlayer:** Der Export bezieht sich immer auf den aktuell
  eingeloggten Charakter. Penumbra liefert für Remote-Charaktere keine
  nachvollziehbaren Mod-Daten, deshalb kein Multi-Char-Support.
- **Penumbra:** aktive Collection inkl. aller wirksamen Mods (filtert
  deaktivierte heraus), mit Priorität, Vererbungs-Flag und gewählten
  Optionen.
- **Glamourer:** vollständiger Actor-State inkl.
  - Customize-Felder mit UI-Labels (Race/Clan/Face/Hairstyle/… mit
    race-abhängigen Bezeichnungen wie „Tail Shape" für Miqo'te).
  - Equipment-Tabelle mit Item-Namen, Dye-Namen und **Dye-Swatches**.
  - Advanced Customization (RGB-Parameter) mit **Hex + RGB + Farb-Swatch**.
  - Bonus-Items (Facewear/Glasses).
  - Sichtbarkeits-Toggles (Hat, Viera-Ohren, Visor, Waffe).
  - Native Re-Import-Blob (Base64) + vollständiger JSON-State.
- **Customize+:** aktives Profil + Liste aller Profile.
- **Drei Export-Formate:**
  - **Markdown** — lesbar, archivierbar.
  - **HTML** — self-contained, mit **Dark/Light-Theme-Toggle** und
    **Item-Icons** inline als Data-URIs.
  - **JSON** — maschinenlesbar, vorbereitet für Re-Import.
- **Preview-Box** ist selektierbar und copyable (Strg+C oder Button).
- **Persistente Konfiguration:** zuletzt gewähltes Format, Export-Ordner
  und Toggle-Optionen werden über Dalamuds Standard-Config-Mechanismus
  gespeichert.

## Commands

| Command | Wirkung |
|---|---|
| `/glamdoc` | Öffnet/schließt das Haupt-Fenster |
| `/glamdoc export` | Speichert einen Export im gewählten Format (default: md) ohne UI |
| `/glamdoc export md` | Markdown |
| `/glamdoc export html` | HTML |
| `/glamdoc export json` | JSON |

## Installation (Dev-Plugin)

1. `dotnet build -c Release` aus dem Projektordner.
2. In Dalamud → Experimentell → „Dev Plugin Locations" die gebaute DLL
   eintragen (`bin/x64/Release/GlamourDocumenter.dll`).
3. `/xlplugins` → Installed → Glamour Documenter → Enable.
4. `/glamdoc` öffnet das Fenster.

## Kompatibilität

| Komponente | Version                                                    |
|---|------------------------------------------------------------|
| Dalamud API Level | 15 Dalamud.NET.Sdk/14.0.2                                 |
| Target Framework | `net10.0-windows`, x64                                     |
| Penumbra.Api | 5.13.1 (IPC-Major 5)                                       |
| Glamourer.Api | 2.8.0 (IPC-Major 1 — verifiziert gegen Glamourer v1.6.0.5) |
| Customize+ IPC | Major 6 (string-basiert)                                   |

## Datenfluss

```
IPlayerCharacter
    │
    ├─► PenumbraIpc  ──► GetCollectionForObject, GetAllModSettings
    ├─► GlamourerIpc ──► GetState (JObject), GetStateBase64
    └─► CustomizePlusIpc ─► GetList, GetActiveProfileIdOnCharacter, GetByUniqueId
          │
          ▼
    DocumentationCollector
          │
          ├─► GlamourerStateParser (JObject → strukturierte Records)
          ├─► LuminaResolver (Item/Stain/Race/Tribe-Namen + Icons)
          │
          ▼
    DocumentationExport
          │
          ▼
    MarkdownExporter / HtmlExporter / JsonExporter
```

## Siehe auch

- [CLAUDE.md](CLAUDE.md) — Entwickler-Leitfaden und Wissens-Silos.
- [CHANGELOG.md](CHANGELOG.md) — Versionshistorie.
