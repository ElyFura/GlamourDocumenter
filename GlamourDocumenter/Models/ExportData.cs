// ==========================================================================
//  Models/ExportData.cs
//
//  Plugin-agnostische Datenschicht. Siehe CLAUDE.md §3 (Architektur-Regeln):
//  Diese Datei darf KEINE Referenzen auf Penumbra.Api, Glamourer.Api, Dalamud
//  oder Customize+ enthalten. Sie ist der saubere Schnitt zwischen IPC-Layer
//  (Services/) und Rendering-Layer (Exporters/).
// ==========================================================================

using System;
using System.Collections.Generic;

namespace GlamourDocumenter.Models;

/// <summary>
///     Top-Level-Container eines Exports. Aggregiert alle Teildaten, die von
///     den drei Plugins (Penumbra, Glamourer, Customize+) gesammelt wurden.
/// </summary>
/// <remarks>
///     Schema-Versionierung (<c>fileVersion</c>) wird bewusst noch NICHT
///     eingeführt. Erst beim ersten Breaking-Change am Schema wird das Feld
///     ergänzt und eine Migrations-Logik gebaut. Siehe CLAUDE.md §9 Punkt 5
///     und §10 (Roadmap).
/// </remarks>
public sealed record DocumentationExport
{
    /// <summary>Erstellungszeitpunkt in lokaler Zeit (ISO-8601).</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>Version des Plugins, das den Export erzeugt hat.</summary>
    public required string PluginVersion { get; init; }

    /// <summary>
    ///     Ob der Report mit einer Summary-Zeile (aktive Mods, Glamourer-
    ///     Slots, Customize+-Profile) beginnen soll. Wird von der UI aus
    ///     der Konfiguration gesetzt; Default <c>true</c> für headless-
    ///     Aufrufe (z. B. Tests).
    /// </summary>
    public bool IncludeStatsHeader { get; init; } = true;

    /// <summary>Charakter-Block (Name, Welt, Job, ...).</summary>
    public required CharacterInfo Character { get; init; }

    /// <summary>Penumbra-Daten oder <c>null</c>, wenn Penumbra nicht verfügbar.</summary>
    public PenumbraExport? Penumbra { get; init; }

    /// <summary>Glamourer-Daten oder <c>null</c>, wenn Glamourer nicht verfügbar.</summary>
    public GlamourerExport? Glamourer { get; init; }

    /// <summary>Customize+-Daten oder <c>null</c>, wenn Customize+ nicht verfügbar.</summary>
    public CustomizePlusExport? CustomizePlus { get; init; }
}

/// <summary>
///     Charakter-Identitätsdaten. Welt-Name muss via
///     <c>HomeWorld.Value.Name.ExtractText()</c> geholt werden — siehe
///     CLAUDE.md §6.5, <c>.ToString()</c> gibt Debug-Payload zurück.
/// </summary>
public sealed record CharacterInfo
{
    public required string Name { get; init; }
    public required string HomeWorld { get; init; }
    public required string Job { get; init; }
    public required uint Level { get; init; }

    /// <summary>
    ///     Optionales Job-Icon als Data-URI. Vom HTML-Exporter inline
    ///     neben dem Job-Namen gerendert.
    /// </summary>
    public string? JobIconDataUri { get; init; }
}

// -------------------------------------------------------------------------
//  Penumbra
// -------------------------------------------------------------------------

/// <summary>
///     Penumbra-Teil des Exports. Bezieht sich auf die aktuell aktive
///     Collection des Charakters.
/// </summary>
public sealed record PenumbraExport
{
    /// <summary>Name der aktiven Collection (für den Charakter).</summary>
    public required string CollectionName { get; init; }

    /// <summary>GUID der Collection als String (Penumbra-interne ID).</summary>
    public required string CollectionId { get; init; }

    /// <summary>
    ///     Alle Mods, die in der Collection tatsächlich wirken (inkl.
    ///     vererbter). Siehe CLAUDE.md §6.3 zur Unterscheidung zwischen
    ///     „Effective List" und „Mod List".
    /// </summary>
    public required IReadOnlyList<PenumbraModEntry> Mods { get; init; }
}

/// <summary>
///     Ein einzelner Mod mit seinen effektiven Einstellungen innerhalb der
///     Collection.
/// </summary>
public sealed record PenumbraModEntry
{
    /// <summary>Ordner-Name des Mods (Penumbra-interne ID).</summary>
    public required string ModDirectory { get; init; }

    /// <summary>Anzeigename des Mods.</summary>
    public required string ModName { get; init; }

    /// <summary>Ob der Mod in der Collection aktiviert ist.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Prioritätswert (höher = überschreibt niedrigere).</summary>
    public required int Priority { get; init; }

    /// <summary>Ob die Einstellung vererbt wurde (nicht direkt gesetzt).</summary>
    public required bool Inherited { get; init; }

    /// <summary>
    ///     Gewählte Optionen pro Option-Gruppe des Mods. Key = Gruppenname,
    ///     Value = gewählte Optionen (Multi-Select möglich).
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Settings { get; init; }
}

// -------------------------------------------------------------------------
//  Glamourer
// -------------------------------------------------------------------------

/// <summary>
///     Glamourer-Teil des Exports. Kombiniert strukturierte Felder
///     (Customize, Equipment, Bonus, Meta-Flags) mit dem nativen
///     Re-Import-Blob.
/// </summary>
/// <remarks>
///     Der <see cref="StateBase64"/>-Blob ist das native Re-Import-Format
///     von Glamourer (siehe CLAUDE.md §6.1). Die strukturierten Felder
///     kommen aus dem parallel abgeholten <c>GetState</c>-JObject und
///     werden vom Collector in plugin-agnostische Records übersetzt.
/// </remarks>
public sealed record GlamourerExport
{
    /// <summary>Re-Import-Blob (Version-Byte + GZip(JSON)).</summary>
    public required string StateBase64 { get; init; }

    /// <summary>
    ///     Pretty-printed vollständiger State (native Glamourer-JObject
    ///     als String). Enthält alle Felder — auch solche, die wir nicht
    ///     strukturiert ausgeben.
    /// </summary>
    public string? StateJson { get; init; }

    /// <summary>Customize-Block (Race, Gender, Face, Farben, Form-IDs).</summary>
    public GlamourerCustomize? Customize { get; init; }

    /// <summary>Equipment-Slots inkl. Item-Namen und Dye-Namen.</summary>
    public IReadOnlyList<GlamourerEquipmentSlot>? Equipment { get; init; }

    /// <summary>Bonus-Items (aktuell Facewear/Glasses, mögliche weitere).</summary>
    public IReadOnlyList<GlamourerBonusItem>? Bonus { get; init; }

    /// <summary>
    ///     Advanced-Customize-Parameters (Muscle Tone, Lip-Highlight
    ///     usw.). Roh übernommen — Bezeichnung = Glamourer-Feldname,
    ///     Wert formatiert als String (Zahl oder RGB/RGBA).
    /// </summary>
    public IReadOnlyList<GlamourerParameter>? Parameters { get; init; }

    /// <summary>Meta-Toggle-Flags: Hat/VieraEars/Visor/Weapon sichtbar.</summary>
    public GlamourerMetaFlags? MetaFlags { get; init; }

    /// <summary>
    ///     Advanced-Dye-Layer (Material-Overrides). Nur als roher
    ///     Schlüssel→Dict-Eintrag exportiert — die Key-Bedeutung
    ///     (<c>MaterialValueIndex</c>) liegt bei Glamourer selbst.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Materials { get; init; }

    /// <summary>
    ///     Liste aller gespeicherten Glamourer-Designs (Name + Re-Import-
    ///     Blob). Wird als Backup-Sektion im Report gerendert, damit der
    ///     Export nicht nur den Actor-State, sondern auch den User-
    ///     Design-Pool enthält.
    /// </summary>
    public IReadOnlyList<GlamourerDesign>? Designs { get; init; }
}

/// <summary>Eines der gespeicherten Glamourer-Designs.</summary>
public sealed record GlamourerDesign
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Base64 { get; init; }
}

/// <summary>Eine Ausprägung einer Customize-Eigenschaft.</summary>
/// <param name="Name">Feldname, wie Glamourer ihn benennt (z. B. „Race").</param>
/// <param name="RawValue">Roh-Wert aus dem JObject (meist Byte/Int).</param>
/// <param name="DisplayValue">
///     Menschenlesbarer Wert — für Race/Clan/Gender aus Lumina aufgelöst,
///     sonst identisch zu <see cref="RawValue"/>.
/// </param>
/// <param name="Apply">Ob dieses Feld beim Re-Import angewendet wird.</param>
public sealed record CustomizeField(
    string Name,
    string RawValue,
    string DisplayValue,
    bool Apply);

/// <summary>
///     Customize-Block. <see cref="Fields"/> ist die Reihenfolge, in der
///     Glamourer die Felder liefert (stabil pro API-Version).
/// </summary>
public sealed record GlamourerCustomize
{
    public required uint ModelId { get; init; }
    public required IReadOnlyList<CustomizeField> Fields { get; init; }
    public bool? Wetness { get; init; }
    public bool? WetnessApply { get; init; }
}

/// <summary>Ein Equipment-Slot inkl. aufgelöster Item- und Dye-Namen.</summary>
public sealed record GlamourerEquipmentSlot
{
    /// <summary>„Head", „Body", „MainHand", …</summary>
    public required string SlotName { get; init; }
    public required ulong ItemId { get; init; }
    /// <summary>Aus Lumina aufgelöst — fällt auf „Nothing" / „Unknown" zurück.</summary>
    public required string ItemName { get; init; }
    public required byte Stain1 { get; init; }
    public required byte Stain2 { get; init; }
    public string? Stain1Name { get; init; }
    public string? Stain2Name { get; init; }
    /// <summary>RGB-Hex der Stain 1 (ohne <c>#</c>) — nur gesetzt wenn bekannt.</summary>
    public string? Stain1Hex { get; init; }
    /// <summary>RGB-Hex der Stain 2.</summary>
    public string? Stain2Hex { get; init; }
    public required bool Crest { get; init; }
    public required bool Apply { get; init; }
    public required bool ApplyStain { get; init; }
    public required bool ApplyCrest { get; init; }

    /// <summary>
    ///     Optionales Item-Icon als Data-URI (<c>data:image/png;base64,…</c>).
    ///     Vom HTML-Exporter inline gerendert; Markdown-Exporter packt es
    ///     in einen stummen HTML-Kommentar (den der MD-Viewer ignoriert).
    ///     Null wenn kein Icon auflösbar (Nothing-Slot, Custom-Glamour etc.).
    /// </summary>
    public string? IconDataUri { get; init; }
}

/// <summary>Bonus-Slot (Facewear, Glasses, …).</summary>
public sealed record GlamourerBonusItem
{
    public required string SlotName { get; init; }
    public required ulong BonusId { get; init; }
    public required string ItemName { get; init; }
    public required bool Apply { get; init; }
}

/// <summary>Advanced-Parameter (RGB, Value, Percentage).</summary>
public sealed record GlamourerParameter
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required bool Apply { get; init; }
}

/// <summary>
///     Meta-Toggle-Flags. Alle nullable, weil sie je nach Charaktertyp
///     (human/non-human) nicht alle vorhanden sind.
/// </summary>
public sealed record GlamourerMetaFlags
{
    public bool? HatVisible { get; init; }
    public bool? HatApply { get; init; }
    public bool? VieraEarsVisible { get; init; }
    public bool? VieraEarsApply { get; init; }
    public bool? VisorToggled { get; init; }
    public bool? VisorApply { get; init; }
    public bool? WeaponVisible { get; init; }
    public bool? WeaponApply { get; init; }
}

// -------------------------------------------------------------------------
//  Customize+
// -------------------------------------------------------------------------

/// <summary>
///     Customize+-Teil des Exports. Enthält das aktuell aktive Profil des
///     Charakters (falls vorhanden) und die Liste aller Profile.
/// </summary>
public sealed record CustomizePlusExport
{
    /// <summary>Aktuell aktives Profil des Charakters, oder <c>null</c>.</summary>
    public CustomizePlusProfile? ActiveProfile { get; init; }

    /// <summary>Alle in Customize+ installierten Profile.</summary>
    public required IReadOnlyList<CustomizePlusProfileSummary> AllProfiles { get; init; }
}

/// <summary>Kurzform eines Profils für die Listen-Ansicht.</summary>
public sealed record CustomizePlusProfileSummary
{
    public required string UniqueId { get; init; }
    public required string Name { get; init; }
    public required bool IsEnabled { get; init; }
}

/// <summary>
///     Vollständige Profil-Daten des aktiven Customize+-Profils. Das
///     <see cref="Template"/>-Feld hält das native JSON des Profils und
///     wird unverändert durchgereicht (für Re-Import).
/// </summary>
public sealed record CustomizePlusProfile
{
    public required string UniqueId { get; init; }
    public required string Name { get; init; }
    public required string Template { get; init; }
}
