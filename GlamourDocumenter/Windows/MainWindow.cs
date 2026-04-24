// ==========================================================================
//  Windows/MainWindow.cs
//
//  ImGui-Hauptfenster. Einzige Schicht, die ImGui importieren darf
//  (CLAUDE.md §3 Punkt 4).
//
//  Die Exporter-Registry wird hier gehalten — neue Formate erweitern
//  einfach <see cref="_exporters"/>, die UI zieht automatisch nach
//  (CLAUDE.md §3 Punkt 5, §12).
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Exporters;
using GlamourDocumenter.Models;
using GlamourDocumenter.Services;

namespace GlamourDocumenter.Windows;

/// <summary>
///     Haupt-UI. Zeigt den zuletzt gesammelten Export, erlaubt den
///     Re-Sammel-Trigger und schreibt den Export in eine Datei.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly DocumentationCollector _collector;
    private readonly DocumentationImporter _importer;
    private readonly GitAutoCommit _git;
    private readonly IObjectTable _objectTable;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    private readonly Configuration _config;

    /// <summary>
    ///     Exporter-Registry. Reihenfolge = Anzeige-Reihenfolge in der
    ///     Radio-Button-Gruppe. Neue Exporter hier unten anhängen.
    /// </summary>
    private readonly IReadOnlyList<IDocumentExporter> _exporters = new IDocumentExporter[]
    {
        new MarkdownExporter(),
        new HtmlExporter(),
        new JsonExporter(),
    };

    private int _selectedExporterIndex;
    private DocumentationExport? _lastExport;
    private string? _lastRenderedPath;
    private string _statusMessage = string.Empty;

    // Preview-Cache. Re-Render passiert nur, wenn sich Export oder
    // Exporter-Auswahl ändern — sonst würden wir bei jedem ImGui-Frame
    // (typ. 60/s) den gesamten Markdown neu bauen.
    private string _previewCache = string.Empty;
    private DocumentationExport? _previewFor;
    private int _previewExporterIndex = -1;

    // Historie-Tab: einfacher Cache über die Files im Export-Ordner,
    // wird bei jedem Tab-Öffnen bzw. per Refresh-Button neu gezogen.
    private List<FileInfo> _historyFiles = new();
    private string _historyPreview = string.Empty;
    private string? _historySelectedPath;
    // Edit-Buffer für den Export-Ordner-Pfad im Settings-Tab.
    private string _settingsExportFolderEdit = string.Empty;

    // Import-Tab-State.
    private string? _importPath;
    private string _importResult = string.Empty;
    private bool _importConfirmOpen;

    public MainWindow(
        DocumentationCollector collector,
        DocumentationImporter importer,
        GitAutoCommit git,
        IObjectTable objectTable,
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        Configuration config)
        : base("Glamour Documenter")
    {
        _collector = collector;
        _importer = importer;
        _git = git;
        _objectTable = objectTable;
        _pluginInterface = pluginInterface;
        _log = log;
        _config = config;

        // Zuletzt gewählten Exporter-Index aus der Config wiederherstellen,
        // aber defensiv auf die aktuelle Registry-Größe clampen (falls
        // Exporter-Reihenfolge geändert oder Exporter entfernt wurde).
        _selectedExporterIndex = Math.Clamp(
            _config.LastExporterIndex, 0, _exporters.Count - 1);
        _settingsExportFolderEdit = _config.ExportFolder ?? string.Empty;

        Size = new Vector2(620, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        // Tab-Bar: Export / Historie / Re-Import / Einstellungen / Info.
        // Der frühere Vergleich-Tab und der Character-Picker sind
        // bewusst entfernt: Penumbra liefert für Remote-Charaktere nur
        // die für unsere eigene Collection sichtbaren Mods, was für
        // andere Spieler keine nachvollziehbaren Ergebnisse produziert.
        // Der Export ist daher explizit auf den LocalPlayer fokussiert.
        if (ImGui.BeginTabBar("##gdoc-tabs"))
        {
            if (ImGui.BeginTabItem("Export"))
            {
                DrawExportTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Historie"))
            {
                DrawHistoryTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Re-Import"))
            {
                DrawImportTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Einstellungen"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Info"))
            {
                DrawInfoTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawExportTab()
    {
        DrawExporterSelection();
        ImGui.Separator();
        DrawActions();
        ImGui.Separator();
        DrawPreview();
    }

    private void DrawExporterSelection()
    {
        ImGui.Text("Format:");
        for (var i = 0; i < _exporters.Count; i++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(_exporters[i].DisplayName, _selectedExporterIndex == i))
            {
                _selectedExporterIndex = i;
                _config.LastExporterIndex = i;
                TrySaveConfig();
            }
        }
    }

    private void DrawActions()
    {
        if (ImGui.Button("Daten sammeln"))
            CollectSafely();

        ImGui.SameLine();

        // Export-Button ist nur aktiv, wenn Daten vorhanden sind.
        // ImGui.BeginDisabled kennt keinen bool-Rückgabewert, daher das
        // Pattern mit explizitem EndDisabled im finally-ähnlichen Stil.
        var noData = _lastExport is null;
        if (noData)
            ImGui.BeginDisabled();

        if (ImGui.Button("In Datei speichern"))
            WriteSafely();

        if (noData)
            ImGui.EndDisabled();

        // „Ordner öffnen" erst nach dem ersten erfolgreichen Schreiben
        // sichtbar — vorher gäbe es nichts zu zeigen.
        if (!string.IsNullOrEmpty(_lastRenderedPath))
        {
            ImGui.SameLine();
            if (ImGui.Button("Ordner öffnen"))
                OpenExportFolder();
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextWrapped(_statusMessage);
        }
    }

    private void DrawPreview()
    {
        if (_lastExport is null)
        {
            ImGui.TextWrapped(
                "Noch kein Export gesammelt. Klick auf „Daten sammeln“, " +
                "um den aktuellen Charakter auszulesen.");
            return;
        }

        var exporter = _exporters[_selectedExporterIndex];
        EnsurePreview(exporter);

        ImGui.TextUnformatted($"Vorschau ({exporter.DisplayName})");
        ImGui.SameLine();
        if (ImGui.Button("In Zwischenablage kopieren"))
        {
            ImGui.SetClipboardText(_previewCache);
            _statusMessage = "Vorschau in Zwischenablage kopiert.";
        }

        // InputTextMultiline mit ReadOnly-Flag statt TextUnformatted:
        // erlaubt Text-Selektion per Maus, Strg+A und Strg+C direkt in
        // ImGui. Kein Editieren, weil ReadOnly gesetzt ist.
        //
        // maxLength = Cache-Länge + 1 reicht, weil ReadOnly ohnehin keine
        // Erweiterung zulässt — der Buffer ist nur Anzeige.
        //
        // Der Callback-Parameter ist in Dalamuds Overload-Set Pflicht,
        // aber Nullable. Mit null verhält sich der Widget wie ein simpler
        // Read-Only-Text-View.
        var avail = ImGui.GetContentRegionAvail();
        // Zwei Overloads mit `?`-Callback kollidieren bei `null`. Explizit
        // auf den Delegate-Typ casten, damit Overload-Resolution eindeutig
        // ist.
        ImGui.InputTextMultiline(
            "##preview",
            ref _previewCache,
            (int)(_previewCache.Length + 1),
            new Vector2(avail.X, avail.Y),
            ImGuiInputTextFlags.ReadOnly,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
    }

    /// <summary>
    ///     Baut <see cref="_previewCache"/> neu auf, wenn sich seit dem
    ///     letzten Aufruf der Export oder die Exporter-Auswahl geändert
    ///     haben. Andernfalls NO-OP, damit jeder ImGui-Frame O(1) bleibt.
    /// </summary>
    private void EnsurePreview(IDocumentExporter exporter)
    {
        if (ReferenceEquals(_previewFor, _lastExport) &&
            _previewExporterIndex == _selectedExporterIndex)
            return;

        try
        {
            _previewCache = exporter.Render(_lastExport!);
            _previewFor = _lastExport;
            _previewExporterIndex = _selectedExporterIndex;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Preview-Render fehlgeschlagen.");
            _previewCache = $"Fehler beim Rendern: {ex.Message}";
        }
    }

    private void CollectSafely()
    {
        try
        {
            var player = _objectTable.LocalPlayer;
            if (player is null)
            {
                _lastExport = null;
                _statusMessage = "Kein LocalPlayer — Login/Charakter-Auswahl nötig.";
                return;
            }

            var exp = _collector.Collect(
                player,
                _config.OnlyNonDefaultMods,
                _config.IncludeGlamourerDesigns);
            // Config-Toggles auf den frisch gebauten Export mappen, damit
            // Exporter stateless bleiben können (Collector kennt die
            // Config nicht).
            _lastExport = exp with { IncludeStatsHeader = _config.IncludeStatsHeader };
            _statusMessage = "Daten gesammelt.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Collect fehlgeschlagen.");
            _lastExport = null;
            _statusMessage = $"Fehler beim Sammeln: {ex.Message}";
        }
    }

    private void WriteSafely()
    {
        if (_lastExport is null)
            return;

        try
        {
            var exporter = _exporters[_selectedExporterIndex];
            var rendered = exporter.Render(_lastExport);

            // Export-Ordner: Config hat Vorrang, Default = Plugin-Config-Dir
            // (CLAUDE.md §9 Punkt 4: wir schreiben nur dort, wenn der User
            // nicht explizit einen anderen Ordner konfiguriert hat).
            var targetDir = !string.IsNullOrWhiteSpace(_config.ExportFolder)
                ? _config.ExportFolder
                : _pluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(targetDir);

            var timestamp = _lastExport.ExportedAt.ToString("yyyyMMdd-HHmmss");
            var fileName = BuildExportFileName(_lastExport, timestamp, exporter.FileExtension);

            var path = Path.Combine(targetDir, fileName);
            File.WriteAllText(path, rendered);

            _lastRenderedPath = path;
            _statusMessage = $"Gespeichert: {path}";
            _log.Information("[GlamourDocumenter] Export geschrieben: {Path}", path);

            TryAutoCommit(targetDir, fileName);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Schreiben fehlgeschlagen.");
            _statusMessage = $"Fehler beim Speichern: {ex.Message}";
        }
    }

    private void TryAutoCommit(string targetDir, string fileName)
    {
        if (!_config.GitAutoCommit)
            return;
        _git.CommitAll(targetDir, $"Export: {fileName}");
    }

    /// <summary>
    ///     Baut den Dateinamen zusammen: <c>{Charakter}-{Collection}-{Zeit}</c>.
    ///     Collection nur, wenn <see cref="Configuration.IncludeCollectionInFilename"/>
    ///     gesetzt ist und Penumbra tatsächlich eine Collection geliefert hat.
    /// </summary>
    private string BuildExportFileName(DocumentationExport export, string timestamp, string extension)
    {
        var charPart = MakePathSafe(export.Character.Name);
        string name;
        if (_config.IncludeCollectionInFilename && !string.IsNullOrEmpty(export.Penumbra?.CollectionName))
        {
            var collectionPart = MakePathSafe(export.Penumbra!.CollectionName);
            name = $"{charPart}-{collectionPart}-{timestamp}{extension}";
        }
        else
        {
            name = $"{charPart}-{timestamp}{extension}";
        }
        return name;
    }

    /// <summary>
    ///     Öffnet den Explorer und markiert die zuletzt geschriebene Datei.
    /// </summary>
    private void OpenExportFolder()
    {
        if (string.IsNullOrEmpty(_lastRenderedPath))
            return;

        try
        {
            // /select,"pfad" öffnet den Explorer im Parent-Ordner und
            // hebt die Datei hervor — angenehmer als nur den Ordner.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_lastRenderedPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Explorer konnte nicht geöffnet werden.");
            _statusMessage = $"Ordner-Öffnen fehlgeschlagen: {ex.Message}";
        }
    }

    /// <summary>
    ///     Entfernt ungültige Datei-Zeichen aus einem Namen. Wir loggen
    ///     das NICHT, weil in den FFXIV-Namen üblicherweise nichts
    ///     problematisches steht — nur Defensive.
    /// </summary>
    private static string MakePathSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    ///     Persistiert die Config ohne Exception-Spam. Fehler landen im
    ///     Log, aber brechen das UI nicht ab — der User kann Setting
    ///     dann beim Reload ohnehin neu setzen.
    /// </summary>
    private void TrySaveConfig()
    {
        try
        {
            _pluginInterface.SavePluginConfig(_config);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Config-Save fehlgeschlagen.");
        }
    }

    // -------------------------------------------------------------------
    //  Historie-Tab
    // -------------------------------------------------------------------

    private void DrawHistoryTab()
    {
        var targetDir = !string.IsNullOrWhiteSpace(_config.ExportFolder)
            ? _config.ExportFolder
            : _pluginInterface.GetPluginConfigDirectory();

        ImGui.TextWrapped($"Ordner: {targetDir}");
        if (ImGui.Button("Aktualisieren"))
            RefreshHistory();
        ImGui.SameLine();
        if (ImGui.Button("Ordner öffnen"))
            OpenHistoryFolder(targetDir);

        ImGui.Separator();

        if (_historyFiles.Count == 0)
            RefreshHistory();

        if (_historyFiles.Count == 0)
        {
            ImGui.TextDisabled("Keine Exporte im Ordner.");
            return;
        }

        // Linke Spalte: Datei-Liste. Rechte Spalte: Vorschau.
        var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInner;
        if (ImGui.BeginTable("##history", 2, tableFlags))
        {
            ImGui.TableSetupColumn("Dateien", ImGuiTableColumnFlags.WidthFixed, 240);
            ImGui.TableSetupColumn("Vorschau", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawHistoryFileList();

            ImGui.TableSetColumnIndex(1);
            DrawHistoryPreview();

            ImGui.EndTable();
        }
    }

    private void DrawHistoryFileList()
    {
        if (ImGui.BeginChild("##history-files", new Vector2(0, 0), border: true))
        {
            foreach (var file in _historyFiles)
            {
                var selected = string.Equals(_historySelectedPath, file.FullName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{file.Name}##{file.FullName}", selected))
                    LoadHistoryFile(file);
            }
        }
        ImGui.EndChild();
    }

    private void DrawHistoryPreview()
    {
        if (string.IsNullOrEmpty(_historySelectedPath))
        {
            ImGui.TextDisabled("Datei auswählen, um die Vorschau zu laden.");
            return;
        }

        ImGui.TextUnformatted(Path.GetFileName(_historySelectedPath));
        ImGui.SameLine();
        if (ImGui.Button("Kopieren##history"))
            ImGui.SetClipboardText(_historyPreview);
        ImGui.SameLine();
        if (ImGui.Button("Löschen##history"))
            DeleteHistoryFile(_historySelectedPath);

        var avail = ImGui.GetContentRegionAvail();
        ImGui.InputTextMultiline(
            "##history-preview",
            ref _historyPreview,
            (int)(_historyPreview.Length + 1),
            new Vector2(avail.X, avail.Y),
            ImGuiInputTextFlags.ReadOnly,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
    }

    private void RefreshHistory()
    {
        try
        {
            var targetDir = !string.IsNullOrWhiteSpace(_config.ExportFolder)
                ? _config.ExportFolder
                : _pluginInterface.GetPluginConfigDirectory();
            if (!Directory.Exists(targetDir))
            {
                _historyFiles = new List<FileInfo>();
                return;
            }

            // Letzte 20, neueste zuerst. Wir filtern auf die uns bekannten
            // Extensions, damit Config-Files o. ä. nicht mitgelistet werden.
            var exts = _exporters.Select(e => e.FileExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _historyFiles = new DirectoryInfo(targetDir)
                .EnumerateFiles()
                .Where(f => exts.Contains(f.Extension))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] History-Refresh fehlgeschlagen.");
            _historyFiles = new List<FileInfo>();
        }
    }

    private void LoadHistoryFile(FileInfo file)
    {
        try
        {
            _historyPreview = File.ReadAllText(file.FullName);
            _historySelectedPath = file.FullName;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] History-Load fehlgeschlagen: {Path}", file.FullName);
            _historyPreview = $"Fehler beim Laden: {ex.Message}";
        }
    }

    private void DeleteHistoryFile(string path)
    {
        try
        {
            File.Delete(path);
            _historySelectedPath = null;
            _historyPreview = string.Empty;
            RefreshHistory();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] History-Delete fehlgeschlagen: {Path}", path);
        }
    }

    private void OpenHistoryFolder(string targetDir)
    {
        try
        {
            if (Directory.Exists(targetDir))
                Process.Start(new ProcessStartInfo { FileName = targetDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Ordner-Öffnen fehlgeschlagen.");
        }
    }

    // -------------------------------------------------------------------
    //  Import-Tab (Re-Import)
    // -------------------------------------------------------------------

    /// <summary>
    ///     Datei-Dropdown aus der Historie. Gemeinsam genutzt vom
    ///     Import-Tab (ggf. später auch anderen).
    /// </summary>
    private static void DrawFilePicker(
        string label, List<FileInfo> files, ref string? selected)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);

        var currentLabel = selected is not null
            ? Path.GetFileName(selected)
            : "(auswählen)";

        if (ImGui.BeginCombo($"##file-{label}", currentLabel))
        {
            foreach (var file in files)
            {
                var isSel = file.FullName.Equals(selected, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(file.Name, isSel))
                    selected = file.FullName;
            }
            ImGui.EndCombo();
        }
    }

    private void DrawImportTab()
    {
        ImGui.TextWrapped(
            "Re-Import eines JSON-Exports auf den LocalPlayer. Dry-Run zeigt " +
            "ohne Schreib-Operationen, was der Apply tun würde. Apply schreibt " +
            "in die aktuell aktive Penumbra-Collection und setzt den " +
            "Glamourer-State. Customize+ muss manuell aus dem Template-Block " +
            "importiert werden.");
        ImGui.Separator();

        if (_historyFiles.Count == 0)
            RefreshHistory();

        var jsonFiles = _historyFiles
            .Where(f => f.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (jsonFiles.Count == 0)
        {
            ImGui.TextDisabled("Keine JSON-Exports vorhanden.");
            return;
        }

        DrawFilePicker("Quelle", jsonFiles, ref _importPath);

        var target = _objectTable.LocalPlayer;
        ImGui.Text(target is null
            ? "Ziel: (kein LocalPlayer — Login nötig)"
            : $"Ziel: {target.Name.TextValue}");

        ImGui.Separator();

        var ready = !string.IsNullOrEmpty(_importPath) && target is not null;
        if (!ready) ImGui.BeginDisabled();

        if (ImGui.Button("Dry-Run"))
            RunImport(dryRun: true);

        ImGui.SameLine();
        if (ImGui.Button("Apply …"))
            _importConfirmOpen = true;

        if (!ready) ImGui.EndDisabled();

        DrawImportConfirmPopup();

        if (!string.IsNullOrEmpty(_importResult))
        {
            ImGui.SameLine();
            if (ImGui.Button("Kopieren##import"))
                ImGui.SetClipboardText(_importResult);

            var avail = ImGui.GetContentRegionAvail();
            ImGui.InputTextMultiline(
                "##import-output",
                ref _importResult,
                (int)(_importResult.Length + 1),
                new Vector2(avail.X, avail.Y),
                ImGuiInputTextFlags.ReadOnly,
                callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
        }
    }

    private void DrawImportConfirmPopup()
    {
        if (_importConfirmOpen)
        {
            ImGui.OpenPopup("##import-confirm");
            _importConfirmOpen = false;
        }

        var open = true;
        if (ImGui.BeginPopupModal("##import-confirm", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(
                "Diese Aktion schreibt in Glamourer und Penumbra. " +
                "Der aktuelle State wird überschrieben. Fortfahren?");
            ImGui.Separator();

            if (ImGui.Button("Ja, anwenden"))
            {
                RunImport(dryRun: false);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Abbrechen"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private void RunImport(bool dryRun)
    {
        if (string.IsNullOrEmpty(_importPath))
            return;

        var target = _objectTable.LocalPlayer;
        if (target is null)
        {
            _importResult = "Kein LocalPlayer — Login/Charakter-Auswahl nötig.";
            return;
        }

        try
        {
            var json = File.ReadAllText(_importPath);
            var exp = System.Text.Json.JsonSerializer.Deserialize<DocumentationExport>(
                          json,
                          new System.Text.Json.JsonSerializerOptions
                          {
                              PropertyNameCaseInsensitive = true,
                              Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                          })
                      ?? throw new InvalidOperationException("Leerer Export.");
            _importResult = dryRun ? _importer.DryRun(exp, target) : _importer.Apply(exp, target);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Re-Import fehlgeschlagen.");
            _importResult = $"Fehler: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------
    //  Settings-Tab
    // -------------------------------------------------------------------

    private void DrawSettingsTab()
    {
        ImGui.Text("Export-Verhalten");
        ImGui.Separator();

        var includeStats = _config.IncludeStatsHeader;
        if (ImGui.Checkbox("Summary-Block am Anfang des Reports", ref includeStats))
        {
            _config.IncludeStatsHeader = includeStats;
            TrySaveConfig();
            InvalidatePreview();
        }

        var includeCollection = _config.IncludeCollectionInFilename;
        if (ImGui.Checkbox("Collection-Name im Dateinamen", ref includeCollection))
        {
            _config.IncludeCollectionInFilename = includeCollection;
            TrySaveConfig();
        }

        var onlyNonDefault = _config.OnlyNonDefaultMods;
        if (ImGui.Checkbox("Nur Mods mit vom Default abweichenden Settings", ref onlyNonDefault))
        {
            _config.OnlyNonDefaultMods = onlyNonDefault;
            TrySaveConfig();
            InvalidatePreview();
        }

        var includeDesigns = _config.IncludeGlamourerDesigns;
        if (ImGui.Checkbox("Glamourer-Designs als Backup mit-exportieren", ref includeDesigns))
        {
            _config.IncludeGlamourerDesigns = includeDesigns;
            TrySaveConfig();
            InvalidatePreview();
        }
        ImGui.TextDisabled(
            "Enthält Re-Import-Blob pro Design — bläht den Report auf, wenn " +
            "viele Designs gespeichert sind.");

        ImGui.Spacing();
        ImGui.Text("Automatisierung");
        ImGui.Separator();

        var autoZone = _config.AutoExportOnZoneChange;
        if (ImGui.Checkbox("Auto-Export bei Zonen-Wechsel", ref autoZone))
        {
            _config.AutoExportOnZoneChange = autoZone;
            TrySaveConfig();
        }

        ImGui.Text("Auto-Export-Format:");
        ImGui.SameLine();
        foreach (var fmt in new[] { "md", "html", "json" })
        {
            if (ImGui.RadioButton(fmt, _config.AutoExportFormat == fmt))
            {
                _config.AutoExportFormat = fmt;
                TrySaveConfig();
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();

        var gitCommit = _config.GitAutoCommit;
        if (ImGui.Checkbox("Nach Export automatisch git add + commit", ref gitCommit))
        {
            _config.GitAutoCommit = gitCommit;
            TrySaveConfig();
        }
        ImGui.TextDisabled("Erfordert git.exe im PATH. Init des Repos passiert beim ersten Commit.");

        ImGui.Spacing();
        ImGui.Text("Export-Ordner");
        ImGui.Separator();
        ImGui.TextWrapped("Leer = Plugin-Config-Directory.");

        ImGui.SetNextItemWidth(-1);
        // InputText braucht (label, ref string, int maxLen, flags, callback)
        // bei Dalamuds Overload-Set — keine kürzere Variante. Callback
        // null, weil wir kein Input-Intercepting brauchen.
        ImGui.InputText(
            "##export-folder",
            ref _settingsExportFolderEdit,
            512,
            ImGuiInputTextFlags.None,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);

        if (ImGui.Button("Ordner speichern"))
        {
            var trimmed = _settingsExportFolderEdit.Trim();
            _config.ExportFolder = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            TrySaveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button("Zurücksetzen"))
        {
            _config.ExportFolder = null;
            _settingsExportFolderEdit = string.Empty;
            TrySaveConfig();
        }
    }

    /// <summary>Erzwingt Preview-Re-Render beim nächsten Draw.</summary>
    private void InvalidatePreview()
    {
        _previewFor = null;
        _previewExporterIndex = -1;
    }

    // -------------------------------------------------------------------
    //  Info-Tab
    // -------------------------------------------------------------------

    private void DrawInfoTab()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
                          .GetName().Version?.ToString() ?? "?";
        ImGui.Text($"Glamour Documenter v{version}");
        ImGui.TextWrapped("Exportiert Penumbra / Glamourer / Customize+ für den aktuellen Charakter.");
        ImGui.Separator();
        ImGui.Text("Commands");
        ImGui.BulletText("/glamdoc — Fenster öffnen/schließen");
        ImGui.BulletText("/glamdoc export [md|html|json] — headless exportieren");
        ImGui.Separator();
        ImGui.TextDisabled("Quellcode im Projekt-Ordner, Changelog in CHANGELOG.md.");
    }

    /// <summary>
    ///     Führt einen Export ohne Öffnen des Fensters durch — für den
    ///     Sub-Command <c>/glamdoc export [format]</c>. Nutzt immer den
    ///     lokalen Spieler (nicht die UI-Auswahl), damit der Call
    ///     reproduzierbar aus Makros etc. funktioniert.
    /// </summary>
    public void HeadlessExport(string formatToken)
    {
        var exporter = FindExporterByToken(formatToken);
        if (exporter is null)
        {
            _log.Warning(
                "[GlamourDocumenter] Unbekanntes Export-Format: '{Token}'. Verfügbar: md, html, json.",
                formatToken);
            return;
        }

        try
        {
            var local = _objectTable.LocalPlayer;
            if (local is null)
            {
                _log.Warning("[GlamourDocumenter] Headless-Export: kein LocalPlayer.");
                return;
            }
            var export = _collector.Collect(
                local,
                _config.OnlyNonDefaultMods,
                _config.IncludeGlamourerDesigns);
            var decorated = export with { IncludeStatsHeader = _config.IncludeStatsHeader };
            var rendered = exporter.Render(decorated);

            var targetDir = !string.IsNullOrWhiteSpace(_config.ExportFolder)
                ? _config.ExportFolder
                : _pluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(targetDir);

            var timestamp = decorated.ExportedAt.ToString("yyyyMMdd-HHmmss");
            var fileName = BuildExportFileName(decorated, timestamp, exporter.FileExtension);
            var path = Path.Combine(targetDir, fileName);
            File.WriteAllText(path, rendered);

            _lastExport = decorated;
            _lastRenderedPath = path;
            _log.Information("[GlamourDocumenter] Headless-Export geschrieben: {Path}", path);

            TryAutoCommit(targetDir, $"headless {fileName}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Headless-Export fehlgeschlagen.");
        }
    }

    /// <summary>
    ///     Matcht einen Format-Token (md/markdown/html/json) gegen
    ///     die Exporter-Registry. Case-insensitive, bereits gekürzt.
    /// </summary>
    private IDocumentExporter? FindExporterByToken(string token)
    {
        return token switch
        {
            "md" or "markdown" => _exporters.FirstOrDefault(e => e is MarkdownExporter),
            "html" => _exporters.FirstOrDefault(e => e is HtmlExporter),
            "json" => _exporters.FirstOrDefault(e => e is JsonExporter),
            _ => null,
        };
    }

    public void Dispose()
    {
        // Nichts zu disposen — Exporter sind zustandslos, Collector lebt
        // im Plugin-Root, Config wird von Plugin.Dispose finalisiert.
    }
}
