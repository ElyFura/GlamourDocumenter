# CLAUDE.md — Glamour Documenter

Dieses Dokument ist die **einzige Wahrheitsquelle** für Claude Code, wenn es
an diesem Projekt arbeitet. Vor jeder nicht-trivialen Änderung: dieses Dokument
lesen, einschlägige Abschnitte beachten, nur dann implementieren.

---

## 1. Projekt-Überblick

**Name:** Glamour Documenter
**Typ:** Dalamud-Plugin für Final Fantasy XIV
**Sprache:** C# (.NET 9, `net9.0-windows`, x64)
**Zweck:** Exportiert kombinierte Einstellungen aus **Penumbra**, **Glamourer**
und **Customize+** für den lokalen Charakter als Markdown- oder JSON-Dokumentation.

**Sekundärziel (Roadmap):** Re-Import des JSON-Formats, um Character-States
zwischen Rechnern zu migrieren.

**Zielnutzer:** Ich selbst. Keine Release-Pipeline auf dem öffentlichen Dalamud-
Repo geplant, Distribution läuft über den Dev-Plugin-Loader meines XIVLaunchers.

---

## 2. Tech-Stack & Dependencies

| Komponente            | Version / Quelle                            | Zweck                                   |
|-----------------------|---------------------------------------------|-----------------------------------------|
| **Build-SDK**         | `Dalamud.NET.Sdk/14.0.2`                    | Liefert TargetFramework, Standard-Refs (Dalamud, ImGui, FFXIVClientStructs, Lumina, Newtonsoft.Json) und DalamudPackager als MSBuild-Task |
| Dalamud API           | Level 14 (lokale Dev-Installation)          | Plugin-Host                             |
| `Penumbra.Api`        | NuGet 5.13.1 (Major 5)                      | IPC zu Penumbra (extern, kein Teil der SDK) |
| `Glamourer.Api`       | NuGet 2.8.0 — IPC-Major **1** (verifiziert v1.6.0.5: (1,7)) | IPC zu Glamourer (extern)               |
| Customize+            | String-basierte IPC (kein NuGet)            | IPC zu Customize+ (Major 6)             |
| `System.Text.Json`    | BCL                                         | JSON-Export                             |

**NIE** zusätzliche NuGet-Pakete ohne Rückfrage einführen. Das Plugin soll
schlank bleiben.

Die SDK setzt automatisch `TargetFramework`, `Platform`, `OutputType`,
`Configurations`, sowie `LangVersion` und resolvt die Dalamud-DLLs gegen
`%AppData%\XIVLauncher\addon\Hooks\dev\` — ein Build benötigt also nur
diesen Ordner, keine hartkodierten `<Reference HintPath>`-Einträge mehr.
Siehe [v12-SDK-migration](https://dalamud.dev/plugin-development/how-tos/v12-SDK-migration)
und [SamplePlugin.csproj](https://github.com/goatcorp/SamplePlugin/blob/master/SamplePlugin/SamplePlugin.csproj).

# Quellen

Dalamud Github - https://github.com/goatcorp/Dalamud
Penumbra Github - https://github.com/xivdev/Penumbra
Glamourer Github - https://github.com/Ottermandias/Glamourer
CustomizePlus Github - https://github.com/Aether-Tools/CustomizePlus

Dalamud Dokumentation - https://dalamud.dev
Dalamud API - https://dalamud.dev/api/

---

## 3. Projektstruktur

```
GlamourDocumenter/
├── GlamourDocumenter.csproj       # Build + NuGet-Refs
├── GlamourDocumenter.json         # Plugin-Manifest (DalamudApiLevel etc.)
├── Plugin.cs                      # Entry Point, Service-Wiring, Dispose
├── Services/
│   ├── PenumbraIpc.cs             # Wrapper um Penumbra.Api.IpcSubscribers
│   ├── GlamourerIpc.cs            # Wrapper um Glamourer.Api.IpcSubscribers
│   ├── CustomizePlusIpc.cs        # CallGate-Subscriber (string-IPC)
│   └── DocumentationCollector.cs  # Orchestriert das Einsammeln
├── Models/
│   └── ExportData.cs              # Plugin-agnostische Records
├── Exporters/
│   ├── IDocumentExporter.cs       # Strategy-Interface
│   ├── MarkdownExporter.cs        # Menschenlesbar
│   └── JsonExporter.cs            # Maschinenlesbar, re-import-fähig
└── Windows/
    └── MainWindow.cs              # ImGui-UI
```

**Architektur-Regeln:**

1. **`Services/`** kapselt ausschließlich IPC und Datensammlung.
   Keine UI-Logik, keine Exporter-Logik hier rein.
2. **`Models/`** enthält **ausschließlich** plugin-agnostische Records.
   Keine Referenzen auf `Penumbra.Api`, `Glamourer.Api` oder Dalamud.
   Diese Schicht ist der Schnitt, der Exporter von IPC trennt.
3. **`Exporters/`** darf nur `Models/` kennen, nie `Services/` oder Dalamud.
4. **`Windows/`** darf alles benutzen, ist aber die einzige Schicht, die ImGui
   importieren darf.
5. Neue Export-Formate → neue Klasse, die `IDocumentExporter` implementiert,
   in `MainWindow._exporters` registrieren. **Keine** Änderung an existierenden
   Exportern erforderlich.

---

## 4. Kern-Konventionen (STRIKT)

### 4.1 IPC-Robustheit

**Jede** IPC-Methode muss:

1. Ihre eigene `IsAvailable()`-Prüfung am Anfang durchführen.
2. In `try/catch` gewrappt sein. **Niemals** eine IPC-Exception nach oben bubblen.
3. Im Fehlerfall einen neutralen Wert zurückgeben:
   - Sammlungen → leere Collection (`Array.Empty<T>()`, `new Dictionary<...>()`)
   - Einzelwerte → `null`
4. Fehler via `_log.Warning(ex, "[PluginName] Methode fehlgeschlagen...")` loggen
   (`Debug` für erwartbare Nicht-Success-ECs, `Warning` für Exceptions).

**Begründung:** Das Plugin darf nicht crashen, wenn Penumbra/Glamourer/Customize+
deinstalliert, deaktiviert oder auf einer inkompatiblen Version ist. Stummes
Fallen-Lassen mit leerem Export ist besser als Exception-Spam.

### 4.2 API-Versions-Pinning

Jeder IPC-Wrapper prüft hart die Major-API-Version gegen eine **Konstante
im Code** (kein Konfig, kein Bereich). Bei Breaking-Change:

1. In Git-History nachschauen, welche Signaturen sich geändert haben.
2. Konstante + Subscriber-Signaturen in **einem** Commit anpassen.
3. README-Tabelle „API-Kompatibilität" aktualisieren.

### 4.3 Thread-Safety

- IPC-Aufrufe, die `IClientState` / `IObjectTable` lesen, **müssen** auf dem
  Framework-Thread laufen. Wenn eine neue Funktion aus UI-Code (ImGui-Draw)
  heraus IPC aufruft, die State liest, gehört sie hinter
  `Plugin.Framework.RunOnFrameworkThread(...)`.
- Der `DocumentationCollector` wird aktuell aus dem UI-Thread aufgerufen und
  darf das bleiben, solange er nur `ClientState.LocalPlayer` schnappt und
  sofort fertig wird. Wenn du das änderst → Framework-Thread.

### 4.4 Nullability

- `<Nullable>enable</Nullable>` ist gesetzt. Keine `#nullable disable`.
- `null`-Returns aus IPC-Wrappern sind dokumentiert und erwartet.
- Konsumenten müssen `null` explizit behandeln, keine `!` (Null-Forgiving)
  Operatoren, außer bei `[PluginService]`-Feldern in `Plugin.cs`.

### 4.5 Namespaces

- Root-Namespace: `GlamourDocumenter`
- Ordner = Namespace. Keine Querverschachtelung.
- `using`-Direktiven **innerhalb** des Namespaces halten, wenn sie nur für
  Standard-BCL-Typen sind; verwende ansonsten voll qualifizierte Typen
  (`System.Collections.Generic.Dictionary<...>`) in Models und IPC-Wrappern
  für maximale Klarheit.

### 4.6 Dispose-Pattern

- Jeder Service implementiert `IDisposable`, auch wenn aktuell nichts zu
  disposen ist. Platzhalter-`Dispose()` dokumentiert, **warum** es leer ist
  („keine unmanaged Ressourcen, Platzhalter für spätere Subscriber").
- `Plugin.Dispose()` disposed in umgekehrter Konstruktor-Reihenfolge:
  UI → Commands → Services.

---

## 5. Dokumentation im Code

Senior-Dev-Standard, wie in den User-Preferences festgelegt:

- **XML-Doc-Kommentare** auf jeder öffentlichen Klasse, jedem Record, jeder
  öffentlichen Methode und jedem öffentlichen Property. Nicht-trivial: auch
  auf privaten Methoden.
- **Inline-Kommentare** an jeder Stelle, an der ein Reviewer fragen würde
  „warum?". Nicht „was macht dieser Code", sondern „warum so".
- Kommentare auf **Deutsch** (konsistent mit bestehender Codebase). Code-
  Identifier bleiben Englisch.
- `<remarks>`-Tags für Querverweise auf externe APIs und Repos.

Beispiel-Header für eine neue Klasse:

```csharp
/// <summary>
///     Kurze Ein-Satz-Beschreibung.
/// </summary>
/// <remarks>
///     Ausführlicher: welche IPC-Endpoints, welche Annahmen, welche
///     Fallback-Strategie. Links zu Upstream-Repos, wenn relevant.
/// </remarks>
public sealed class Foo { ... }
```

---

## 6. Kritische Wissens-Silos

Dies sind Dinge, bei denen die Dokumentation der Upstream-Plugins lückenhaft
ist und ich durch Versuch und Irrtum gelernt habe. **Claude Code muss hier
besonders vorsichtig sein.**

### 6.1 Glamourer-Base64-Format

- `GetStateBase64` gibt einen Blob zurück, dessen **erstes Byte** die
  Format-Version ist. Rest = GZip-komprimiertes UTF-8-JSON.
- Beim Decodieren: `MemoryStream(bytes, 1, bytes.Length - 1)`, dann
  `GZipStream(Decompress)`, dann `StreamReader(UTF-8)`.
- Format-Version kann sich ändern. Der Re-Import durch Glamourer selbst
  funktioniert trotzdem, weil Glamourer das Versions-Byte kennt — wir müssen
  den Blob also **nie** manipulieren, nur durchreichen.
- Decodieren ist **optional**, nur für die Klartext-Vorschau im Markdown.

### 6.2 Penumbra `GetCurrentModSettings`-Tuple

Die API-Signatur (Penumbra.Api 5.13.x) ist:

```csharp
Invoke(Guid collectionId, string modDirectory,
       string modName = "", bool ignoreInheritance = false)
  →  (PenumbraApiEc, (bool enabled, int priority,
                      Dictionary<string, List<string>> settings,
                      bool inherited)?)
```

- **4-Tuple**, nicht 5 — das `temporary`-Flag wurde herausgezogen und
  lebt nur noch in `GetCurrentModSettingsWithTemp`. Wer Temporary-Settings
  braucht, nimmt diesen Endpoint.
- Der `modName`-Parameter wird üblicherweise leer übergeben
  (`modDirectory` ist eindeutig). Er dient nur als Fallback.
- **Nicht** als Named-Tuple im Code benutzen, sondern destrukturieren.
- Tuple kann `null` sein, wenn der Mod in der Collection unbekannt ist.
- Für Massen-Abfragen (alle Mods einer Collection) **nicht** diesen Endpoint
  in einer Schleife aufrufen — dafür gibt es `GetAllModSettings` (siehe
  §6.3). Dieser Endpoint ist nur für gezielte Einzel-Abfragen gedacht.

### 6.3 Penumbra: alle wirksamen Mods einer Collection

Einen eigenen „Effective List"-Endpoint **gibt es nicht**. Stattdessen:

- `GetModList()` = alle **installierten** Mods global (Verzeichnis → Name).
- `GetAllModSettings(collectionId, ignoreInheritance, ignoreTemporary, key)`
  = alle wirksamen Mod-Settings einer Collection. Signatur:

  ```csharp
  Invoke(Guid collectionId, bool ignoreInheritance = false,
         bool ignoreTemporary = false, int key = 0)
    →  (PenumbraApiEc,
        Dictionary<string, (bool enabled, int priority,
                            Dictionary<string, List<string>> settings,
                            bool inherited, bool temporary)>?)
  ```

- Für Exports: `ignoreInheritance: false, ignoreTemporary: true` —
  vererbte Mods gehören rein, temporäre nicht (CLAUDE.md §6.2 letzter Satz).
- `GetModList()` bleibt nützlich für Anzeigenamen-Lookup (der Inner-Dict-Key
  von `GetAllModSettings` ist der Verzeichnisname, nicht der Anzeigename).
- **Nicht** `GetCurrentModSettings` in einer Schleife über `GetModList()`
  aufrufen — das ist N IPC-Calls statt einem.

### 6.4 Customize+ IPC

- Keine offiziellen NuGet-Bindings. Endpoints sind String-Konstanten:
  `CustomizePlus.General.GetApiVersion`,
  `CustomizePlus.Profile.GetList`,
  `CustomizePlus.Profile.GetActiveProfileIdOnCharacter`,
  `CustomizePlus.Profile.GetByUniqueId`.
- Rückgabewerte sind Tuples `(int ec, T? data)`. `ec == 0` = Success.
- Bei Updates von Customize+ **immer** zuerst die Endpoint-Namen im Upstream-
  Repo (`Aether-Tools/CustomizePlus`) prüfen. Es gab schon Rename-Runden.

### 6.5 `IPlayerCharacter.HomeWorld`

Zugriff auf den Welten-Namen geht über die Excel-Row:

```csharp
var world = player.HomeWorld.Value.Name.ExtractText();
```

Nicht `.ToString()` — das gibt interne Debug-Repräsentation. `.ExtractText()`
entfernt SeString-Payloads.

---

## 7. Build & Test

### 7.1 Lokaler Build

```powershell
# Aus dem Projektordner
dotnet build -c Release
```

Output: `GlamourDocumenter\bin\x64\Release\GlamourDocumenter\` mit
`GlamourDocumenter.dll`, dem Manifest und `latest.zip` (für Custom-Repo-
Install). Penumbra.Api.dll und Glamourer.Api.dll werden mit ausgepackt;
Dalamud-Standard-DLLs nicht — die liefert der Host.

**Voraussetzung:** `%AppData%\XIVLauncher\addon\Hooks\dev\` existiert und
enthält eine aktuelle Dalamud-Dev-Build. Die `Dalamud.NET.Sdk` resolvt
darauf automatisch. Wenn Claude Code den Pfad nicht auflösen kann (z. B.
weil XIVLauncher nicht installiert): **nicht raten**, nachfragen.

### 7.2 Installation in XIVLauncher

1. Dalamud-Settings → Experimentell → „Dev Plugin Locations" → neuen Pfad
   auf die gebaute DLL.
2. In-Game `/xlplugins` → Installed Plugins → Glamour Documenter → Enable.
3. `/glamdoc` im Chat öffnet das Hauptfenster.

### 7.3 Tests

Aktuell **keine** Unit-Tests. Wenn welche dazukommen:

- xUnit in separatem Projekt `GlamourDocumenter.Tests/`.
- Mock-Targets sind `IPluginLog` und die IPC-Subscriber-Interfaces (müsste
  man dafür erstmal selbst wrappen — das wäre ein Refactor, nicht ein
  Add-On, also Rückfrage vor Start).

---

## 8. Git-Konventionen

- **Conventional Commits**: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`,
  `test:`. Scope in Klammern, z. B. `feat(penumbra): support inherited mods`.
- Eine Änderung pro Commit. Wenn Claude Code mehrere Sachen gleichzeitig
  macht, split commits.
- Deutsch oder Englisch in Commit-Messages ist OK, aber pro Commit
  konsistent bleiben.
- Keine „WIP"-Commits auf `main`. Feature-Branches heißen `feat/<kurzname>`.
- **Niemals** `.env`-Dateien, lokale Pfade oder `bin/`/`obj/` committen.
  `.gitignore` muss das blocken.

---

## 9. Was Claude Code NIEMALS tun darf

1. **Keine `!`-Null-Forgiving-Operatoren** außerhalb von `[PluginService]`-Feldern.
2. **Keine `try { ... } catch { }`-Leer-Catches.** Immer loggen oder bewusst
   kommentieren, warum ignoriert.
3. **Keine Reflection auf Penumbra-/Glamourer-/Customize+-Interna**, nur
   die offiziellen IPC-Namen/-Signaturen.
4. **Keine direkten Datei-Pfade hardcoden.** Immer über
   `Plugin.PluginInterface.GetPluginConfigDirectory()` oder `GetPluginLoc()`.
5. **Keine Breaking-Changes am Export-JSON-Schema**, ohne das `fileVersion`-
   Feld im `DocumentationExport`-Record mitzuziehen. (Aktuell gibt es das
   noch nicht — beim ersten Breaking-Change einführen.)
6. **Keine neuen Export-Zielformate in bestehende Exporter mergen.**
   Immer neue Klasse.
7. **Keine synchronen `Thread.Sleep` / Busy-Loops**, nirgendwo. UI-Thread
   würde freezen.
8. **Kein Schreiben in Plugin-Verzeichnisse außer** dem eigenen ConfigDir.
   Keine Zugriffe auf Penumbra-/Glamourer-/Customize+-Config-Dateien direkt.
9. **Kein `Task.Run` für IPC**, ohne Framework-Thread-Marshal. IPC muss auf
   dem Thread laufen, für den es gedacht ist.
10. **Keine `Console.WriteLine` / `Debug.WriteLine`**, immer `Plugin.Log`.

---

## 10. Roadmap & offene Punkte

Claude Code kann diese Punkte als „mögliche nächste Aufgaben" betrachten,
aber **nicht eigenmächtig** umsetzen ohne Auftrag:

- **Re-Import:** JSON-Export zurück in Penumbra/Glamourer/Customize+ schreiben.
  Benötigt IPC-Setter, die aktuell nicht gewrappt sind.
- **Versionierung des Export-Schemas:** `fileVersion`-Feld + Migrations-Logik.
- **HTML-Exporter:** mit Syntax-Highlighting für JSON-Blöcke, ähnlich der
  Preview-Seite.
- **Collection-Diff:** zwei Exports vergleichen, Unterschiede als Markdown-
  Report ausgeben.
- **Mehrere Characters:** aktuell nur `LocalPlayer`. Für Alt-Chars wäre ein
  Character-Picker nötig (ObjectTable durchlaufen, PlayerCharacter-Objekte
  filtern).
- **Integration in Penumbra-Collection-Sharing:** automatischer Export
  beim Wechsel der Collection, Ablage in Git-Repo.
- **Unit-Tests:** sobald der IPC-Layer abstrahiert ist.

---

## 11. Umgang mit Unsicherheit

Wenn Claude Code unsicher ist:

1. **Zuerst** in diesem Dokument nachlesen.
2. **Dann** in den entsprechenden Upstream-Repos (`xivdev/Penumbra`,
   `Ottermandias/Glamourer`, `Aether-Tools/CustomizePlus`) prüfen, wie die
   API aktuell aussieht.
3. **Dann** erst fragen.
4. **Nie raten** und nie „das wird schon so funktionieren" committen.

Wenn eine Änderung mehr als zwei Dateien betrifft oder einen Service-Schnitt
verändert: **vor** der Implementation den Plan posten und bestätigen lassen.

---

## 12. Schnell-Referenz: Häufige Aufgaben

### Neuen Exporter hinzufügen

1. Neue Datei `Exporters/HtmlExporter.cs` (oder was es ist).
2. Klasse `sealed`, `IDocumentExporter` implementieren.
3. `FileExtension` und `DisplayName` setzen.
4. In `MainWindow._exporters` registrieren.
5. Fertig — UI zieht automatisch.

### Neuen IPC-Endpoint wrappen

1. Subscriber-Feld im bestehenden `*Ipc.cs` ergänzen.
2. Im Konstruktor initialisieren.
3. Öffentliche Methode mit Verfügbarkeits-Check + try/catch + Log.
4. Im `DocumentationCollector` verdrahten, falls für Export relevant.
5. `Models/ExportData.cs` erweitern, wenn neue Felder im Output.
6. Exporter anpassen, falls neue Felder gerendert werden sollen.

### Plugin-Version bumpen

1. `<Version>` in `.csproj`.
2. Manifest ggf., falls es ein `AssemblyVersion`-Feld gibt.
3. `DocumentationExport.PluginVersion` ziehen wir aus `Assembly` — automatisch.
4. Changelog-Eintrag in README unter „Changelog"-Abschnitt (wenn es den noch
   nicht gibt: anlegen).

---

**Ende CLAUDE.md.** Bei Widersprüchen zwischen diesem Dokument und anderen
Quellen: dieses Dokument gewinnt. Bei Widersprüchen zur User-Preference:
User-Preference gewinnt.
