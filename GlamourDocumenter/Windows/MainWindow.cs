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
using System.Threading;
using System.Threading.Tasks;
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
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly DocumentationCollector _collector;
    private readonly DocumentationImporter _importer;
    // Vorlagen-Galerie (Tab „Vorlagen", siehe MainWindow.Templates.cs).
    private readonly ModTemplateStore _templates;
    private readonly ModTemplateApplier _templateApplier;
    private readonly PenumbraIpc _penumbra;
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

    /// <summary>
    ///     Läuft gerade ein Hintergrund-Collect? Verhindert Doppel-Klicks
    ///     und treibt das deaktivierte Button-Rendering.
    /// </summary>
    /// <remarks>
    ///     <c>volatile</c>-Semantik via <see cref="Volatile"/>-Reads in
    ///     der Draw-Schleife wäre overkill — der Worker-Task setzt das
    ///     Flag genau einmal beim Abschluss, und ImGui pollt jeden Frame
    ///     ohnehin neu.
    /// </remarks>
    private bool _collectInFlight;

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
    // Geparster Export der aktuell gewählten Quelle. Wird einmal beim
    // Datei-Wechsel gelesen, damit die Auswahl-Checkboxen (Mod-Liste)
    // nicht pro Frame die JSON-Datei neu parsen. _importParsedPath
    // merkt sich, für welchen Pfad der Cache gilt.
    private string? _importParsedPath;
    private DocumentationExport? _importParsed;
    private string _importParseError = string.Empty;
    private readonly ImportOptions _importOptions = new();
    private string _importModFilter = string.Empty;

    public MainWindow(
        DocumentationCollector collector,
        DocumentationImporter importer,
        ModTemplateStore templates,
        ModTemplateApplier templateApplier,
        PenumbraIpc penumbra,
        GitAutoCommit git,
        IObjectTable objectTable,
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        Configuration config)
        : base("Glamour Documenter")
    {
        _collector = collector;
        _importer = importer;
        _templates = templates;
        _templateApplier = templateApplier;
        _penumbra = penumbra;
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
        // Tab-Bar: Export / Historie / Re-Import / Vorlagen / Einstellungen / Info.
        // Der frühere Vergleich-Tab und der Character-Picker sind
        // bewusst entfernt: Penumbra liefert für Remote-Charaktere nur
        // die für unsere eigene Collection sichtbaren Mods, was für
        // andere Spieler keine nachvollziehbaren Ergebnisse produziert.
        // Der Export ist daher explizit auf den LocalPlayer fokussiert.
        if (ImGui.BeginTabBar("##gdoc-tabs"))
        {
            if (ImGui.BeginTabItem(Strings.TabExport))
            {
                DrawExportTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Strings.TabHistory))
            {
                DrawHistoryTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Strings.TabImport))
            {
                DrawImportTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Strings.TabTemplates))
            {
                DrawTemplatesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Strings.TabSettings))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Strings.TabInfo))
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
        ImGui.Text(Strings.FormatLabel);
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
        if (_collectInFlight)
            ImGui.BeginDisabled();

        if (ImGui.Button(_collectInFlight ? Strings.CollectingButton : Strings.CollectButton))
            CollectSafely();

        if (_collectInFlight)
            ImGui.EndDisabled();

        ImGui.SameLine();

        // Export-Button ist nur aktiv, wenn Daten vorhanden sind.
        // ImGui.BeginDisabled kennt keinen bool-Rückgabewert, daher das
        // Pattern mit explizitem EndDisabled im finally-ähnlichen Stil.
        var noData = _lastExport is null;
        if (noData)
            ImGui.BeginDisabled();

        if (ImGui.Button(Strings.SaveToFile))
            WriteSafely();

        if (noData)
            ImGui.EndDisabled();

        // „Ordner öffnen" erst nach dem ersten erfolgreichen Schreiben
        // sichtbar — vorher gäbe es nichts zu zeigen.
        if (!string.IsNullOrEmpty(_lastRenderedPath))
        {
            ImGui.SameLine();
            if (ImGui.Button(Strings.OpenFolder))
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
            ImGui.TextWrapped(Strings.NoExportYet);
            return;
        }

        var exporter = _exporters[_selectedExporterIndex];
        EnsurePreview(exporter);

        ImGui.TextUnformatted(Strings.PreviewLabel(exporter.DisplayName));
        ImGui.SameLine();
        if (ImGui.Button(Strings.CopyToClipboard))
        {
            ImGui.SetClipboardText(_previewCache);
            _statusMessage = Strings.PreviewCopied;
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
            _previewCache = Strings.RenderError(ex.Message);
        }
    }

    private void CollectSafely()
    {
        // Re-Entry-Schutz: kein zweiter Collect, solange der erste läuft.
        if (_collectInFlight)
            return;

        var player = _objectTable.LocalPlayer;
        if (player is null)
        {
            _lastExport = null;
            _statusMessage = Strings.NoLocalPlayer;
            return;
        }

        // Config-Snapshots, damit der Worker-Task keine Felder von außen
        // liest und damit ohne weitere Synchronisation auskommt.
        var onlyNonDefault = _config.OnlyNonDefaultMods;
        var includeDesigns = _config.IncludeGlamourerDesigns;
        var includeStatsHeader = _config.IncludeStatsHeader;

        _collectInFlight = true;
        _statusMessage = Strings.CollectingData;

        // Ausgelagert auf einen Worker-Thread, weil Icon-Fetch via
        // Dalamuds Texture-Pipeline mehrere Sekunden dauern kann. Wäre
        // die UI hier blockiert, würde der Framework-Thread nicht
        // weiterlaufen → Game-Freeze (CLAUDE.md §4.3 sagt nur, dass
        // ClientState-Reads auf dem FW-Thread *passieren* müssen; Collect
        // schnappt LocalPlayer einmalig oben und werkelt danach mit IPC,
        // Lumina-Excel und Texture-Loader — alles thread-safe genug für
        // einen kurzen Worker-Run).
        Task.Run(() =>
        {
            try
            {
                var exp = _collector.Collect(player, onlyNonDefault, includeDesigns);
                return exp with { IncludeStatsHeader = includeStatsHeader };
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[GlamourDocumenter] Collect fehlgeschlagen.");
                return null;
            }
        }).ContinueWith(t =>
        {
            // Felder-Update auf dem ImGui-Thread ist nicht zwingend nötig
            // (Draw pollt einfach im nächsten Frame), aber wir wollen die
            // Schreib-Reihenfolge sauber halten: erst Ergebnis, dann Flag.
            _lastExport = t.Result;
            _statusMessage = t.Result is null
                ? Strings.CollectFailed
                : Strings.DataCollected;
            _collectInFlight = false;
        }, TaskScheduler.Default);
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
            _statusMessage = Strings.SavedTo(path);
            _log.Information("[GlamourDocumenter] Export geschrieben: {Path}", path);

            TryAutoCommit(targetDir, fileName);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Schreiben fehlgeschlagen.");
            _statusMessage = Strings.SaveFailed(ex.Message);
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
            _statusMessage = Strings.OpenFolderFailed(ex.Message);
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

        ImGui.TextWrapped(Strings.FolderPrefix(targetDir));
        if (ImGui.Button(Strings.Refresh))
            RefreshHistory();
        ImGui.SameLine();
        if (ImGui.Button(Strings.OpenFolder))
            OpenHistoryFolder(targetDir);

        ImGui.Separator();

        if (_historyFiles.Count == 0)
            RefreshHistory();

        if (_historyFiles.Count == 0)
        {
            ImGui.TextDisabled(Strings.NoExportsInFolder);
            return;
        }

        // Linke Spalte: Datei-Liste. Rechte Spalte: Vorschau.
        var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInner;
        if (ImGui.BeginTable("##history", 2, tableFlags))
        {
            ImGui.TableSetupColumn(Strings.FilesColumn, ImGuiTableColumnFlags.WidthFixed, 240);
            ImGui.TableSetupColumn(Strings.PreviewColumn, ImGuiTableColumnFlags.WidthStretch);
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
            ImGui.TextDisabled(Strings.SelectFileToPreview);
            return;
        }

        ImGui.TextUnformatted(Path.GetFileName(_historySelectedPath));
        ImGui.SameLine();
        if (ImGui.Button($"{Strings.Copy}##history"))
            ImGui.SetClipboardText(_historyPreview);
        ImGui.SameLine();
        if (ImGui.Button($"{Strings.Delete}##history"))
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
            _historyPreview = Strings.LoadError(ex.Message);
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
            : Strings.SelectPlaceholder;

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
        ImGui.TextWrapped(Strings.ImportInfo);
        ImGui.Separator();

        if (_historyFiles.Count == 0)
            RefreshHistory();

        var jsonFiles = _historyFiles
            .Where(f => f.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (jsonFiles.Count == 0)
        {
            ImGui.TextDisabled(Strings.NoJsonExports);
            return;
        }

        DrawFilePicker(Strings.SourceLabel, jsonFiles, ref _importPath);

        // Quelle einmalig parsen, wenn sich der Pfad geändert hat. Die
        // Auswahl wird dabei zurückgesetzt, damit Ausschlüsse eines
        // anderen Exports nicht stillschweigend weiterwirken.
        if (!string.Equals(_importPath, _importParsedPath, StringComparison.OrdinalIgnoreCase))
            LoadImportSource();

        var target = _objectTable.LocalPlayer;
        ImGui.Text(target is null
            ? Strings.TargetNoPlayer
            : Strings.TargetWith(target.Name.TextValue));

        if (!string.IsNullOrEmpty(_importParseError))
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _importParseError);

        if (_importParsed is not null)
            DrawImportSelection(_importParsed);

        ImGui.Separator();

        var ready = _importParsed is not null && target is not null && _importOptions.AnythingToApply;
        if (_importParsed is not null && !_importOptions.AnythingToApply)
            ImGui.TextDisabled(Strings.ImportNothingSelected);
        if (!ready) ImGui.BeginDisabled();

        if (ImGui.Button(Strings.DryRunButton))
            RunImport(dryRun: true);

        ImGui.SameLine();
        if (ImGui.Button(Strings.ApplyButton))
            _importConfirmOpen = true;

        if (!ready) ImGui.EndDisabled();

        DrawImportConfirmPopup();

        if (!string.IsNullOrEmpty(_importResult))
        {
            ImGui.SameLine();
            if (ImGui.Button($"{Strings.Copy}##import"))
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
            ImGui.TextWrapped(Strings.ImportConfirmText);
            ImGui.Separator();

            if (ImGui.Button(Strings.YesApply))
            {
                RunImport(dryRun: false);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(Strings.Cancel))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    /// <summary>
    ///     Liest und parst die gewählte Quelldatei in den Cache
    ///     (<see cref="_importParsed"/>) und setzt die Auswahl zurück.
    ///     Parse-Fehler landen in <see cref="_importParseError"/> statt
    ///     zu bubblen — der Tab bleibt bedienbar.
    /// </summary>
    private void LoadImportSource()
    {
        _importParsedPath = _importPath;
        _importParsed = null;
        _importParseError = string.Empty;
        _importResult = string.Empty;
        _importModFilter = string.Empty;
        _importOptions.Reset();

        if (string.IsNullOrEmpty(_importPath))
            return;

        try
        {
            var json = File.ReadAllText(_importPath);
            _importParsed = System.Text.Json.JsonSerializer.Deserialize<DocumentationExport>(
                                json,
                                new System.Text.Json.JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                                })
                            ?? throw new InvalidOperationException(Strings.ImportEmpty);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Re-Import: Quelle konnte nicht geladen werden.");
            _importParseError = Strings.ImportError(ex.Message);
        }
    }

    /// <summary>
    ///     Auswahl-Block „Was importieren?": Plugin-Teile mit Unter-Flags
    ///     und eine filterbare Mod-Liste. Schreibt direkt in
    ///     <see cref="_importOptions"/>.
    /// </summary>
    private void DrawImportSelection(DocumentationExport export)
    {
        if (!ImGui.CollapsingHeader(Strings.ImportSelectionHeader, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // --- Glamourer -------------------------------------------------
        var hasGlam = export.Glamourer is not null;
        if (!hasGlam) ImGui.BeginDisabled();
        var glam = _importOptions.Glamourer && hasGlam;
        if (ImGui.Checkbox(Strings.ImportSelectGlamourer, ref glam))
            _importOptions.Glamourer = glam;
        if (!hasGlam)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.ImportNotInSource);
        }

        ImGui.Indent();
        if (!glam) ImGui.BeginDisabled();
        var equip = _importOptions.GlamourerEquipment;
        if (ImGui.Checkbox(Strings.ImportSelectEquipment, ref equip))
            _importOptions.GlamourerEquipment = equip;
        ImGui.SameLine();
        var cust = _importOptions.GlamourerCustomization;
        if (ImGui.Checkbox(Strings.ImportSelectCustomize, ref cust))
            _importOptions.GlamourerCustomization = cust;
        if (!glam) ImGui.EndDisabled();
        ImGui.Unindent();
        if (!hasGlam) ImGui.EndDisabled();

        ImGui.Spacing();

        // --- Penumbra --------------------------------------------------
        var hasPen = export.Penumbra is not null;
        if (!hasPen) ImGui.BeginDisabled();
        var pen = _importOptions.Penumbra && hasPen;
        if (ImGui.Checkbox(Strings.ImportSelectPenumbra, ref pen))
            _importOptions.Penumbra = pen;
        if (!hasPen)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.ImportNotInSource);
        }

        ImGui.Indent();
        if (!pen) ImGui.BeginDisabled();
        var en = _importOptions.PenumbraEnabledState;
        if (ImGui.Checkbox(Strings.ImportSelectEnabled, ref en))
            _importOptions.PenumbraEnabledState = en;
        ImGui.SameLine();
        var prio = _importOptions.PenumbraPriority;
        if (ImGui.Checkbox(Strings.ImportSelectPriority, ref prio))
            _importOptions.PenumbraPriority = prio;
        ImGui.SameLine();
        var sett = _importOptions.PenumbraSettings;
        if (ImGui.Checkbox(Strings.ImportSelectSettings, ref sett))
            _importOptions.PenumbraSettings = sett;

        if (export.Penumbra is { } penumbra)
            DrawImportModList(penumbra.Mods);

        if (!pen) ImGui.EndDisabled();
        ImGui.Unindent();
        if (!hasPen) ImGui.EndDisabled();

        ImGui.Spacing();

        // --- Customize+ ------------------------------------------------
        var hasCPlus = export.CustomizePlus is not null;
        if (!hasCPlus) ImGui.BeginDisabled();
        var cplus = _importOptions.ShowCustomizePlusTemplate && hasCPlus;
        if (ImGui.Checkbox(Strings.ImportSelectCPlus, ref cplus))
            _importOptions.ShowCustomizePlusTemplate = cplus;
        if (!hasCPlus)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.ImportNotInSource);
        }
        if (!hasCPlus) ImGui.EndDisabled();

        ImGui.Spacing();
    }

    /// <summary>
    ///     Filterbare Checkbox-Liste aller Mods des Exports mit
    ///     Alle/Keine/Nur-aktive-Schnellwahl. Die Liste steht in einem
    ///     Child mit fester Höhe, damit große Collections (hunderte
    ///     Mods) den Tab nicht sprengen.
    /// </summary>
    private void DrawImportModList(IReadOnlyList<PenumbraModEntry> mods)
    {
        var selectedCount = mods.Count(m => _importOptions.IsModSelected(m.ModDirectory));
        ImGui.TextUnformatted(Strings.ImportModsLabel(selectedCount, mods.Count));

        ImGui.SameLine();
        if (ImGui.SmallButton(Strings.ImportModsAll))
            _importOptions.ExcludedMods.Clear();
        ImGui.SameLine();
        if (ImGui.SmallButton(Strings.ImportModsNone))
        {
            foreach (var m in mods)
                _importOptions.ExcludedMods.Add(m.ModDirectory);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Strings.ImportModsOnlyEnabled))
        {
            // „Nur aktive": deaktivierte Mods des Exports ausschließen —
            // typischer Fall, wenn man ein Outfit übernehmen, aber die
            // Ziel-Collection nicht mit Aus-Schaltern zumüllen will.
            foreach (var m in mods)
                _importOptions.SetModSelected(m.ModDirectory, m.Enabled);
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(
            $"##import-mod-filter",
            ref _importModFilter,
            128,
            ImGuiInputTextFlags.None,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
        if (string.IsNullOrEmpty(_importModFilter) && !ImGui.IsItemActive())
        {
            // Platzhalter-Text manuell zeichnen — InputTextWithHint ist
            // im Overload-Set des Dalamud-Bindings nicht identisch
            // verfügbar, und ein eigener Overlay-Text reicht hier.
            var min = ImGui.GetItemRectMin();
            var pad = ImGui.GetStyle().FramePadding;
            ImGui.GetWindowDrawList().AddText(
                new Vector2(min.X + pad.X, min.Y + pad.Y),
                ImGui.GetColorU32(ImGuiCol.TextDisabled),
                Strings.ImportModFilterHint);
        }

        if (ImGui.BeginChild("##import-mods", new Vector2(0, 150), border: true))
        {
            foreach (var mod in mods)
            {
                if (!string.IsNullOrEmpty(_importModFilter)
                    && !mod.ModName.Contains(_importModFilter, StringComparison.OrdinalIgnoreCase)
                    && !mod.ModDirectory.Contains(_importModFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sel = _importOptions.IsModSelected(mod.ModDirectory);
                // ModDirectory als ID-Suffix, weil Anzeigenamen nicht
                // eindeutig sind (zwei Mods können gleich heißen).
                if (ImGui.Checkbox($"{mod.ModName}##{mod.ModDirectory}", ref sel))
                    _importOptions.SetModSelected(mod.ModDirectory, sel);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Strings.ImportModTooltip(mod.ModDirectory, mod.Priority, mod.Settings.Count));

                if (!mod.Enabled)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({Strings.ImportModDisabledTag})");
                }
            }
        }
        ImGui.EndChild();
    }

    private void RunImport(bool dryRun)
    {
        if (_importParsed is null)
            return;

        var target = _objectTable.LocalPlayer;
        if (target is null)
        {
            _importResult = Strings.NoLocalPlayer;
            return;
        }

        try
        {
            _importResult = dryRun
                ? _importer.DryRun(_importParsed, target, _importOptions)
                : _importer.Apply(_importParsed, target, _importOptions);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[GlamourDocumenter] Re-Import fehlgeschlagen.");
            _importResult = Strings.ImportError(ex.Message);
        }
    }

    // -------------------------------------------------------------------
    //  Settings-Tab
    // -------------------------------------------------------------------

    private void DrawSettingsTab()
    {
        // Sprache zuerst — wenn der User hier umschaltet, alle weiteren
        // Strings auf dieser Seite werden im nächsten Frame in der neuen
        // Sprache gerendert.
        DrawLanguageSelector();

        ImGui.Spacing();
        ImGui.Text(Strings.SettingsExportBehavior);
        ImGui.Separator();

        var includeStats = _config.IncludeStatsHeader;
        if (ImGui.Checkbox(Strings.SettingsSummaryBlock, ref includeStats))
        {
            _config.IncludeStatsHeader = includeStats;
            TrySaveConfig();
            InvalidatePreview();
        }

        var includeCollection = _config.IncludeCollectionInFilename;
        if (ImGui.Checkbox(Strings.SettingsCollectionInFilename, ref includeCollection))
        {
            _config.IncludeCollectionInFilename = includeCollection;
            TrySaveConfig();
        }

        var onlyNonDefault = _config.OnlyNonDefaultMods;
        if (ImGui.Checkbox(Strings.SettingsOnlyNonDefault, ref onlyNonDefault))
        {
            _config.OnlyNonDefaultMods = onlyNonDefault;
            TrySaveConfig();
            InvalidatePreview();
        }

        var includeDesigns = _config.IncludeGlamourerDesigns;
        if (ImGui.Checkbox(Strings.SettingsIncludeDesigns, ref includeDesigns))
        {
            _config.IncludeGlamourerDesigns = includeDesigns;
            TrySaveConfig();
            InvalidatePreview();
        }
        ImGui.TextDisabled(Strings.SettingsDesignsHint);

        ImGui.Spacing();
        ImGui.Text(Strings.SettingsAutomation);
        ImGui.Separator();

        var autoZone = _config.AutoExportOnZoneChange;
        if (ImGui.Checkbox(Strings.SettingsAutoZone, ref autoZone))
        {
            _config.AutoExportOnZoneChange = autoZone;
            TrySaveConfig();
        }

        ImGui.Text(Strings.SettingsAutoFormat);
        ImGui.SameLine();
        // Format-Tokens (md/html/json) bleiben Englisch — sie sind Datei-
        // Endungen / CLI-Argumente und sprachneutral.
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
        if (ImGui.Checkbox(Strings.SettingsGitCommit, ref gitCommit))
        {
            _config.GitAutoCommit = gitCommit;
            TrySaveConfig();
        }
        ImGui.TextDisabled(Strings.SettingsGitHint);

        ImGui.Spacing();
        ImGui.Text(Strings.SettingsExportFolder);
        ImGui.Separator();
        ImGui.TextWrapped(Strings.SettingsFolderHint);

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

        if (ImGui.Button(Strings.SettingsSaveFolder))
        {
            var trimmed = _settingsExportFolderEdit.Trim();
            _config.ExportFolder = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            TrySaveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button(Strings.SettingsResetFolder))
        {
            _config.ExportFolder = null;
            _settingsExportFolderEdit = string.Empty;
            TrySaveConfig();
        }
    }

    /// <summary>
    ///     Sprach-Combo am Anfang des Settings-Tabs. Wechselt
    ///     <see cref="Strings.Current"/> sofort und invalidiert die
    ///     Markdown-Preview, damit der Export-Text in der neuen Sprache
    ///     neu gerendert wird.
    /// </summary>
    private void DrawLanguageSelector()
    {
        ImGui.Text($"{Strings.SettingsLanguage}:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);

        // Reihenfolge muss zur Language-Enum-Reihenfolge passen, weil wir
        // den Index per (int)-Cast 1:1 hinten an die Config zurückschreiben.
        var labels = new[] { Strings.SettingsLanguageGerman, Strings.SettingsLanguageEnglish };
        var current = (int)_config.Language;

        if (ImGui.BeginCombo("##gdoc-lang", labels[current]))
        {
            for (var i = 0; i < labels.Length; i++)
            {
                var isSel = i == current;
                if (ImGui.Selectable(labels[i], isSel))
                {
                    var lang = (Language)i;
                    if (lang != _config.Language)
                    {
                        _config.Language = lang;
                        Strings.Current = lang;
                        TrySaveConfig();
                        // Preview neu rendern, damit der Markdown-Text
                        // in der neuen Sprache erscheint.
                        InvalidatePreview();
                    }
                }
                if (isSel)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
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
        ImGui.TextWrapped(Strings.InfoTagline);
        ImGui.Separator();
        ImGui.Text(Strings.InfoCommands);
        ImGui.BulletText(Strings.InfoCmdMain);
        ImGui.BulletText(Strings.InfoCmdExport);
        ImGui.Separator();
        ImGui.TextDisabled(Strings.InfoChangelog);
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
