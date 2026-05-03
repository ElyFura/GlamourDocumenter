// ==========================================================================
//  Plugin.cs
//
//  Entry Point. Verantwortlich für:
//    1. PluginService-Injection (Dalamud-Framework-Objekte einsammeln)
//    2. Service-Wiring (IPC-Wrapper, Collector, UI)
//    3. Command-Handler registrieren ("/glamdoc")
//    4. Dispose in umgekehrter Konstruktor-Reihenfolge (CLAUDE.md §4.6)
// ==========================================================================

using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Services;
using GlamourDocumenter.Windows;

namespace GlamourDocumenter;

/// <summary>
///     Plugin-Root. Einziger Typ, dem CLAUDE.md den Null-Forgiving-
///     Operator erlaubt — und zwar nur für <see cref="PluginServiceAttribute"/>-
///     Felder, die Dalamud garantiert vor Konstruktor-Eintritt injiziert.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Glamour Documenter";

    private const string MainCommand = "/glamdoc";

    // ---------------------------------------------------------------------
    //  Dalamud-Services (via [PluginService] injiziert).
    // ---------------------------------------------------------------------

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static ITextureReadbackProvider TextureReadback { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    // ---------------------------------------------------------------------
    //  Eigene Services. Konstruktor-Reihenfolge ist wichtig für Dispose
    //  (siehe unten).
    // ---------------------------------------------------------------------

    /// <summary>
    ///     Persistente Config. Wird beim Konstruktor einmal geladen und
    ///     von der UI mutiert; der finale Save läuft in <see cref="Dispose"/>
    ///     — zusätzlich speichert die UI bei jeder Toggle-Änderung sofort
    ///     (Shutdown-Crash-Schutz).
    /// </summary>
    public Configuration Config { get; }

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizePlusIpc _customizePlus;
    private readonly LuminaResolver _lumina;
    private readonly GlamourerStateParser _glamourerParser;
    private readonly DocumentationCollector _collector;
    private readonly DocumentationImporter _importer;
    private readonly GitAutoCommit _git;

    private readonly WindowSystem _windowSystem;
    private readonly MainWindow _mainWindow;

    public Plugin()
    {
        // Config vor allem anderen laden — wenn Services künftig Defaults
        // aus der Config wollen, sind sie damit schon da.
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Sprache ins globale Strings-Modul übernehmen, bevor irgendein
        // UI- oder Export-Code Texte rendert.
        Strings.Current = Config.Language;

        // IPC-Wrapper. Reihenfolge egal, aber Collector kommt danach.
        _penumbra = new PenumbraIpc(PluginInterface, Log);
        _glamourer = new GlamourerIpc(PluginInterface, Log);
        _customizePlus = new CustomizePlusIpc(PluginInterface, Log);

        // Lumina-Helfer und Glamourer-Parser. Beide reine Daten-Services
        // ohne IPC — keine Reihenfolge-Abhängigkeit.
        _lumina = new LuminaResolver(DataManager, TextureProvider, TextureReadback, Log);
        _glamourerParser = new GlamourerStateParser(_lumina, Log);

        _collector = new DocumentationCollector(
            ObjectTable, Log, _penumbra, _glamourer, _customizePlus, _glamourerParser, _lumina);
        _importer = new DocumentationImporter(Log, _penumbra, _glamourer);
        _git = new GitAutoCommit(Log);

        _mainWindow = new MainWindow(_collector, _importer, _git, ObjectTable, PluginInterface, Log, Config);

        // Auto-Export bei Zone-Wechsel — Subscriber wird nur aktiv,
        // wenn die Config das Flag gesetzt hat.
        ClientState.TerritoryChanged += OnTerritoryChanged;
        _windowSystem = new WindowSystem("GlamourDocumenter");
        _windowSystem.AddWindow(_mainWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Öffnet das Fenster. Sub-Commands: export [md|html|json] — schreibt Export ohne UI.",
        });

        Log.Information("[GlamourDocumenter] geladen.");
    }

    private void OpenMainUi()
    {
        _mainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        // Sub-Command-Parsing: erstes Token = Action, Rest = Argumente.
        var trimmed = args.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _mainWindow.Toggle();
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = parts[0].ToLowerInvariant();

        switch (action)
        {
            case "export":
                var format = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "md";
                _mainWindow.HeadlessExport(format);
                break;
            default:
                Log.Warning("[GlamourDocumenter] Unbekannter Sub-Command: {Action}", action);
                _mainWindow.Toggle();
                break;
        }
    }

    /// <summary>
    ///     Dispose in umgekehrter Konstruktor-Reihenfolge (CLAUDE.md §4.6):
    ///     UI → Commands → Services.
    /// </summary>
    /// <summary>
    ///     Schreibt bei Zone-Wechseln (falls per Config aktiviert) einen
    ///     Snapshot des LocalPlayers. Läuft asynchron via das MainWindow,
    ///     weil die Render-Logik ohnehin dort wohnt.
    /// </summary>
    private void OnTerritoryChanged(uint territoryId)
    {
        if (!Config.AutoExportOnZoneChange)
            return;

        try
        {
            _mainWindow.HeadlessExport(Config.AutoExportFormat);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GlamourDocumenter] Auto-Export bei Zone-Change fehlgeschlagen.");
        }
    }

    public void Dispose()
    {
        // Config beim Unload finalisieren, damit späte UI-Mutationen
        // (z. B. Fenster schließen mit unspeicherter Toggle-Änderung)
        // sicher persistiert werden.
        try
        {
            PluginInterface.SavePluginConfig(Config);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GlamourDocumenter] Config-Save beim Dispose fehlgeschlagen.");
        }

        ClientState.TerritoryChanged -= OnTerritoryChanged;
        CommandManager.RemoveHandler(MainCommand);

        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;

        _windowSystem.RemoveAllWindows();
        _mainWindow.Dispose();

        _collector.Dispose();
        _lumina.Dispose();
        _customizePlus.Dispose();
        _glamourer.Dispose();
        _penumbra.Dispose();

        Log.Information("[GlamourDocumenter] entladen.");
    }
}
