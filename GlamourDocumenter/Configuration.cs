// ==========================================================================
//  Configuration.cs
//
//  Persistente Plugin-Konfiguration via Dalamuds Standard-Mechanismus
//  (<see cref="Dalamud.Configuration.IPluginConfiguration"/>). Wird
//  beim Start über <c>PluginInterface.GetPluginConfig()</c> geladen und
//  bei jeder Mutation über <c>PluginInterface.SavePluginConfig()</c>
//  geschrieben.
//
//  Alle Felder sind bewusst mit sicheren Defaults versehen, damit ein
//  fehlendes Config-File keinen UI-Seiteneffekt erzeugt.
// ==========================================================================

using Dalamud.Configuration;

namespace GlamourDocumenter;

/// <summary>
///     Persistent gespeicherte Plugin-Einstellungen.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>
    ///     Schema-Version der Config-Datei. Beim ersten Breaking-Change
    ///     inkrementieren und Migrations-Logik in
    ///     <see cref="Plugin"/> einbauen.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>Index des zuletzt gewählten Exporters (0-basierter Index in der Registry).</summary>
    public int LastExporterIndex { get; set; } = 0;

    /// <summary>
    ///     Zielordner für Exports. <c>null</c> bedeutet „nutze Plugin-
    ///     Config-Directory". CLAUDE.md §9 Punkt 8 erlaubt nur das
    ///     eigene ConfigDir — wer einen anderen Pfad setzt, übernimmt
    ///     Verantwortung.
    /// </summary>
    public string? ExportFolder { get; set; }

    /// <summary>
    ///     Schreibt einen Summary-Block am Anfang des Reports (aktive
    ///     Mods, Glamourer-Slots, Customize+-Profile). Default an.
    /// </summary>
    public bool IncludeStatsHeader { get; set; } = true;

    /// <summary>
    ///     Hängt den Collection-Namen an den Dateinamen
    ///     (<c>{Char}-{Collection}-{Timestamp}</c>). Default an.
    /// </summary>
    public bool IncludeCollectionInFilename { get; set; } = true;

    /// <summary>
    ///     Filtert Mods auf solche, deren Settings von den Gruppen-Defaults
    ///     abweichen. Mods mit leeren <c>Settings</c> (reine Default-
    ///     Konfiguration) werden ausgeblendet. Default aus, weil der
    ///     Standard-Report vollständig sein soll.
    /// </summary>
    public bool OnlyNonDefaultMods { get; set; } = false;

    /// <summary>
    ///     Hängt alle gespeicherten Glamourer-Designs (Name + Re-Import-
    ///     Blob) als Backup-Sektion an den Export. Default aus — der
    ///     Standard-Report bezieht sich auf den aktuellen Charakter-State,
    ///     nicht auf den globalen Design-Pool. Einschalten, wenn der
    ///     Export gleichzeitig Glamourer-Backup sein soll.
    /// </summary>
    public bool IncludeGlamourerDesigns { get; set; } = false;

    /// <summary>
    ///     Auto-Export bei Zonen-Wechsel: bei jedem <c>TerritoryChanged</c>-
    ///     Event des <see cref="Dalamud.Plugin.Services.IClientState"/>
    ///     wird für den LocalPlayer ein Snapshot geschrieben. Default aus,
    ///     weil ungefragtes Schreiben Überraschungspotenzial hat.
    /// </summary>
    public bool AutoExportOnZoneChange { get; set; } = false;

    /// <summary>
    ///     Format des Auto-Exports. Werte wie beim Sub-Command: <c>md</c>,
    ///     <c>html</c>, <c>json</c>.
    /// </summary>
    public string AutoExportFormat { get; set; } = "json";

    /// <summary>
    ///     Nach jedem Export automatisch <c>git add .</c> und
    ///     <c>git commit</c> im Export-Ordner ausführen (Init erfolgt
    ///     bei Bedarf). Erfordert <c>git.exe</c> im PATH. Default aus.
    /// </summary>
    public bool GitAutoCommit { get; set; } = false;
}
