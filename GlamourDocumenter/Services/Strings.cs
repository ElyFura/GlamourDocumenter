// ==========================================================================
//  Services/Strings.cs
//
//  Zentrale Lokalisierung für UI und Export-Output. Alle benutzersicht-
//  baren Texte werden hier zweisprachig (Deutsch/Englisch) gepflegt; die
//  aktive Sprache wird global über <see cref="Strings.Current"/>
//  umgeschaltet (im Plugin-Start auf den Config-Wert gesetzt, vom
//  Settings-Tab live aktualisiert).
//
//  Bewusst KEIN ResourceManager + .resx — der Maintenance-Overhead lohnt
//  sich für zwei Sprachen nicht; eine Code-First-Tabelle bleibt
//  greifbarer und versionskontrollfreundlich.
//
//  Hinweis: Markenbezeichnungen (Penumbra, Glamourer, Customize+) bleiben
//  in beiden Sprachen identisch, weil HtmlExporter sie als Section-Marker
//  per exact-match findet.
// ==========================================================================

namespace GlamourDocumenter.Services;

/// <summary>
///     Statische Bilingual-String-Tabelle. Jede Property liefert den
///     Text in der momentan aktiven Sprache.
/// </summary>
/// <remarks>
///     Globaler Zustand ist vertretbar, weil das Plugin pro Prozess in
///     genau einer Sprache läuft. Sprach-Wechsel im UI invalidiert die
///     Preview, damit der gerenderte Markdown-Text neu gebaut wird.
/// </remarks>
public static class Strings
{
    /// <summary>
    ///     Aktive Sprache. Wird in <c>Plugin</c> nach Config-Load gesetzt
    ///     und vom Sprach-Picker im Settings-Tab überschrieben.
    /// </summary>
    public static Language Current { get; set; } = Language.German;

    /// <summary>Pickt die deutsche oder englische Variante.</summary>
    private static string T(string de, string en) => Current == Language.German ? de : en;

    // ====================================================================
    //  UI — Tabs
    // ====================================================================

    public static string TabExport      => T("Export", "Export");
    public static string TabHistory     => T("Historie", "History");
    public static string TabImport      => T("Re-Import", "Re-import");
    public static string TabSettings    => T("Einstellungen", "Settings");
    public static string TabInfo        => T("Info", "Info");

    // ====================================================================
    //  UI — Export-Tab
    // ====================================================================

    public static string FormatLabel       => T("Format:", "Format:");
    public static string CollectButton     => T("Daten sammeln", "Collect data");
    public static string CollectingButton  => T("Sammle…", "Collecting…");
    public static string SaveToFile        => T("In Datei speichern", "Save to file");
    public static string OpenFolder        => T("Ordner öffnen", "Open folder");
    public static string CopyToClipboard   => T("In Zwischenablage kopieren", "Copy to clipboard");
    public static string PreviewLabel(string format) => T($"Vorschau ({format})", $"Preview ({format})");
    public static string NoExportYet       => T(
        "Noch kein Export gesammelt. Klick auf „Daten sammeln“, um den aktuellen Charakter auszulesen.",
        "No export collected yet. Click \"Collect data\" to read the current character.");
    public static string PreviewCopied     => T("Vorschau in Zwischenablage kopiert.", "Preview copied to clipboard.");
    public static string DataCollected     => T("Daten gesammelt.", "Data collected.");
    public static string CollectingData    => T("Sammle Daten…", "Collecting data…");
    public static string CollectFailed     => T("Fehler beim Sammeln — siehe /xllog.", "Collect failed — see /xllog.");
    public static string NoLocalPlayer     => T(
        "Kein LocalPlayer — Login/Charakter-Auswahl nötig.",
        "No local player — please log in / pick a character.");
    public static string SavedTo(string path)         => T($"Gespeichert: {path}", $"Saved: {path}");
    public static string SaveFailed(string err)       => T($"Fehler beim Speichern: {err}", $"Save failed: {err}");
    public static string OpenFolderFailed(string err) => T($"Ordner-Öffnen fehlgeschlagen: {err}", $"Open folder failed: {err}");
    public static string RenderError(string err)      => T($"Fehler beim Rendern: {err}", $"Render error: {err}");

    // ====================================================================
    //  UI — Historie-Tab
    // ====================================================================

    public static string FolderPrefix(string dir) => T($"Ordner: {dir}", $"Folder: {dir}");
    public static string Refresh                  => T("Aktualisieren", "Refresh");
    public static string NoExportsInFolder        => T("Keine Exporte im Ordner.", "No exports in this folder.");
    public static string FilesColumn              => T("Dateien", "Files");
    public static string PreviewColumn            => T("Vorschau", "Preview");
    public static string SelectFileToPreview      => T("Datei auswählen, um die Vorschau zu laden.", "Select a file to load the preview.");
    public static string Copy                     => T("Kopieren", "Copy");
    public static string Delete                   => T("Löschen", "Delete");
    public static string LoadError(string err)    => T($"Fehler beim Laden: {err}", $"Load error: {err}");

    // ====================================================================
    //  UI — Re-Import-Tab
    // ====================================================================

    public static string ImportInfo => T(
        "Re-Import eines JSON-Exports auf den LocalPlayer. Dry-Run zeigt " +
        "ohne Schreib-Operationen, was der Apply tun würde. Apply schreibt " +
        "in die aktuell aktive Penumbra-Collection und setzt den " +
        "Glamourer-State. Customize+ muss manuell aus dem Template-Block " +
        "importiert werden.",
        "Re-import a JSON export onto the local player. Dry-run shows what " +
        "Apply would do without writing anything. Apply writes to the " +
        "currently active Penumbra collection and sets the Glamourer " +
        "state. Customize+ must be imported manually from the template " +
        "block.");
    public static string NoJsonExports         => T("Keine JSON-Exports vorhanden.", "No JSON exports available.");
    public static string SourceLabel           => T("Quelle", "Source");
    public static string TargetNoPlayer        => T("Ziel: (kein LocalPlayer — Login nötig)", "Target: (no local player — log in)");
    public static string TargetWith(string n)  => T($"Ziel: {n}", $"Target: {n}");
    public static string DryRunButton          => T("Dry-Run", "Dry run");
    public static string ApplyButton           => T("Apply …", "Apply …");
    public static string ImportConfirmText     => T(
        "Diese Aktion schreibt in Glamourer und Penumbra. Der aktuelle State wird überschrieben. Fortfahren?",
        "This action will write to Glamourer and Penumbra. The current state will be overwritten. Continue?");
    public static string YesApply              => T("Ja, anwenden", "Yes, apply");
    public static string Cancel                => T("Abbrechen", "Cancel");
    public static string SelectPlaceholder     => T("(auswählen)", "(select)");
    public static string ImportError(string err) => T($"Fehler: {err}", $"Error: {err}");
    public static string ImportEmpty           => T("Leerer Export.", "Empty export.");

    // ====================================================================
    //  UI — Settings-Tab
    // ====================================================================

    public static string SettingsExportBehavior      => T("Export-Verhalten", "Export behavior");
    public static string SettingsSummaryBlock        => T("Summary-Block am Anfang des Reports", "Summary block at the top of the report");
    public static string SettingsCollectionInFilename=> T("Collection-Name im Dateinamen", "Include collection name in filename");
    public static string SettingsOnlyNonDefault      => T("Nur Mods mit vom Default abweichenden Settings", "Only mods with non-default settings");
    public static string SettingsIncludeDesigns      => T("Glamourer-Designs als Backup mit-exportieren", "Include Glamourer designs as backup");
    public static string SettingsDesignsHint         => T(
        "Enthält Re-Import-Blob pro Design — bläht den Report auf, wenn viele Designs gespeichert sind.",
        "Includes a re-import blob per design — bloats the report when many designs are saved.");
    public static string SettingsAutomation          => T("Automatisierung", "Automation");
    public static string SettingsAutoZone            => T("Auto-Export bei Zonen-Wechsel", "Auto-export on zone change");
    public static string SettingsAutoFormat          => T("Auto-Export-Format:", "Auto-export format:");
    public static string SettingsGitCommit           => T("Nach Export automatisch git add + commit", "Auto git add + commit after export");
    public static string SettingsGitHint             => T(
        "Erfordert git.exe im PATH. Init des Repos passiert beim ersten Commit.",
        "Requires git.exe in PATH. The repo is initialized on the first commit.");
    public static string SettingsExportFolder        => T("Export-Ordner", "Export folder");
    public static string SettingsFolderHint          => T("Leer = Plugin-Config-Directory.", "Empty = plugin config directory.");
    public static string SettingsSaveFolder          => T("Ordner speichern", "Save folder");
    public static string SettingsResetFolder         => T("Zurücksetzen", "Reset");
    public static string SettingsLanguage            => T("Sprache", "Language");
    public static string SettingsLanguageGerman      => T("Deutsch", "German");
    public static string SettingsLanguageEnglish     => T("Englisch", "English");

    // ====================================================================
    //  UI — Info-Tab
    // ====================================================================

    public static string InfoTagline   => T(
        "Exportiert Penumbra / Glamourer / Customize+ für den aktuellen Charakter.",
        "Exports Penumbra / Glamourer / Customize+ for the current character.");
    public static string InfoCommands  => T("Commands", "Commands");
    public static string InfoCmdMain   => T("/glamdoc — Fenster öffnen/schließen", "/glamdoc — open/close the window");
    public static string InfoCmdExport => T(
        "/glamdoc export [md|html|json] — headless exportieren",
        "/glamdoc export [md|html|json] — headless export");
    public static string InfoChangelog => T(
        "Quellcode im Projekt-Ordner, Changelog in CHANGELOG.md.",
        "Source code in the project folder, changelog in CHANGELOG.md.");

    // ====================================================================
    //  Markdown-Export — Header & Summary
    // ====================================================================

    public static string MdHeaderTitle      => T("Glamour Documenter Export", "Glamour Documenter Export");
    public static string MdCreatedWith(string ts, string ver) => T(
        $"Erstellt am {ts} mit Plugin-Version {ver}.",
        $"Created at {ts} with plugin version {ver}.");
    public static string MdActiveMods       => T("Aktive Mods", "Active mods");
    public static string MdGlamourerSummary => T("Glamourer", "Glamourer");
    public static string MdEquipmentSlotsSuffix(int n) => T(
        $"{n} Equipment-Slots",
        $"{n} equipment slots");
    public static string MdBonusItemsSuffix(int n) => T(
        $"{n} Bonus-Items",
        $"{n} bonus items");
    public static string MdActiveProfileSummary(string name) => T(
        $"aktives Profil „{name}",
        $"active profile \"{name}");
    public static string MdNoActiveProfileSummary => T("kein aktives Profil", "no active profile");
    public static string MdProfilesAvailable(int n) => T(
        $"{n} Profile verfügbar",
        $"{n} profiles available");

    // ====================================================================
    //  Markdown-Export — Charakter
    // ====================================================================

    public static string MdCharacter => T("Charakter", "Character");
    public static string MdName      => T("Name", "Name");
    public static string MdWorld     => T("Welt", "World");
    public static string MdJob       => T("Job", "Job");
    public static string MdLevelAbbr => T("Lv", "Lv");

    // ====================================================================
    //  Markdown-Export — Penumbra
    // ====================================================================

    public static string MdPenumbraNotAvail => T(
        "Penumbra nicht verfügbar oder nicht aktiv.",
        "Penumbra not available or not active.");
    public static string MdCollectionLabel  => T("Collection", "Collection");
    public static string MdNoEffectiveMods  => T(
        "Keine wirksamen Mods in dieser Collection.",
        "No effective mods in this collection.");
    public static string MdMods             => T("Mods", "Mods");
    public static string MdDirectory        => T("Verzeichnis", "Directory");
    public static string MdStatus           => T("Status", "Status");
    public static string MdActive           => T("aktiv", "active");
    public static string MdInactive         => T("inaktiv", "inactive");
    public static string MdInherited        => T("vererbt", "inherited");
    public static string MdPriority         => T("Priorität", "Priority");
    public static string MdOptions          => T("Optionen", "Options");
    public static string MdNone             => T("(keine)", "(none)");

    // ====================================================================
    //  Markdown-Export — Glamourer
    // ====================================================================

    public static string MdGlamourerNotAvail => T(
        "Glamourer nicht verfügbar oder kein State vorhanden.",
        "Glamourer not available or no state present.");
    public static string MdReimportBlob   => T("Re-Import-Blob (Base64)", "Re-import blob (Base64)");
    public static string MdFullStateJson  => T("Vollständiger State (nativ, JSON)", "Full state (native, JSON)");
    public static string MdCustomize      => T("Customize", "Customize");
    public static string MdModelId        => T("ModelId", "ModelId");
    public static string MdForceWetness   => T("Force Wetness", "Force wetness");
    public static string MdYes            => T("ja", "yes");
    public static string MdNo             => T("nein", "no");
    public static string MdNotApplied     => T("(nicht angewendet)", "(not applied)");
    public static string MdNotAppliedShort=> T("(nicht angew.)", "(not applied)");
    public static string MdField          => T("Feld", "Field");
    public static string MdValue          => T("Wert", "Value");
    public static string MdAppliedColumn  => T("Angewendet", "Applied");
    public static string MdEquipment      => T("Equipment", "Equipment");
    public static string MdSlot           => T("Slot", "Slot");
    public static string MdItem           => T("Item", "Item");
    public static string MdCrest          => T("Crest", "Crest");
    public static string MdApply          => T("Apply", "Apply");
    public static string MdVisibilityToggles => T("Sichtbarkeits-Toggles", "Visibility toggles");
    public static string MdHatVisible        => T("Hat sichtbar", "Hat visible");
    public static string MdVieraEarsVisible  => T("Viera-Ohren sichtbar", "Viera ears visible");
    public static string MdVisorOpen         => T("Visor offen", "Visor open");
    public static string MdWeaponVisible     => T("Waffe sichtbar", "Weapon visible");
    public static string MdBonusSlots        => T("Bonus-Slots", "Bonus slots");
    public static string MdAdvancedCustomization => T("Advanced Customization", "Advanced Customization");
    public static string MdParameter         => T("Parameter", "Parameter");
    public static string MdAdvancedDyes      => T("Advanced Dyes (Materials)", "Advanced Dyes (Materials)");
    public static string MdDesigns(int n)    => T($"Designs ({n})", $"Designs ({n})");
    public static string MdDesignsHint       => T(
        "Gespeicherte Glamourer-Designs als Backup. Der Blob ist das native Re-Import-Format — in Glamourer unter „Designs“ einfügen.",
        "Saved Glamourer designs as backup. The blob is the native re-import format — paste it into Glamourer under \"Designs\".");
    public static string MdId                => T("Id", "Id");

    // ====================================================================
    //  Markdown-Export — Customize+
    // ====================================================================

    public static string MdCustomizePlusNotAvail => T(
        "Customize+ nicht verfügbar.",
        "Customize+ not available.");
    public static string MdActiveProfileSection => T("Aktives Profil", "Active profile");
    public static string MdNoActiveProfileSection => T(
        "Kein aktives Profil auf diesem Charakter.",
        "No active profile on this character.");
    public static string MdAllProfiles  => T("Alle Profile", "All profiles");
    public static string MdEnabled      => T("aktiviert", "enabled");
    public static string MdDisabled     => T("deaktiviert", "disabled");
    public static string MdUniqueId     => T("UniqueId", "UniqueId");
}
