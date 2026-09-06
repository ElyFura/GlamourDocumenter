// ==========================================================================
//  Services/ImportOptions.cs
//
//  Auswahl, welche Teile eines JSON-Exports beim Re-Import angewendet
//  werden. Wird vom Import-Tab per Checkboxen befüllt und an den
//  DocumentationImporter durchgereicht.
//
//  Bewusst eine mutable Klasse statt Record: ImGui-Checkboxen arbeiten
//  mit ref-Parametern und schreiben den Zustand Frame für Frame zurück;
//  ein immutables Record würde pro Klick eine Kopie erzwingen.
// ==========================================================================

using System;
using System.Collections.Generic;

namespace GlamourDocumenter.Services;

/// <summary>
///     Feingranulare Auswahl für den Re-Import. Default: alles an —
///     ein frisch erzeugtes Objekt verhält sich wie der Import vor
///     Einführung der Auswahl.
/// </summary>
/// <remarks>
///     Die Mod-Auswahl ist als <b>Opt-out</b> modelliert
///     (<see cref="ExcludedMods"/>): Mods, die nicht in der Menge stehen,
///     werden importiert. So bleibt ein neu gewählter Export ohne
///     weitere Klicks vollständig, und die Menge bleibt klein.
/// </remarks>
public sealed class ImportOptions
{
    // ----------------------------------------------------------- Glamourer

    /// <summary>Glamourer-State überhaupt anwenden.</summary>
    public bool Glamourer { get; set; } = true;

    /// <summary>Equipment-Teil des States anwenden (ApplyFlag.Equipment).</summary>
    public bool GlamourerEquipment { get; set; } = true;

    /// <summary>Customization-Teil des States anwenden (ApplyFlag.Customization).</summary>
    public bool GlamourerCustomization { get; set; } = true;

    /// <summary>
    ///     Ob nach Auflösung der Unter-Flags tatsächlich etwas für
    ///     Glamourer zu tun ist.
    /// </summary>
    public bool GlamourerEffective => Glamourer && (GlamourerEquipment || GlamourerCustomization);

    // ------------------------------------------------------------ Penumbra

    /// <summary>Penumbra-Mod-Einstellungen überhaupt schreiben.</summary>
    public bool Penumbra { get; set; } = true;

    /// <summary>Enabled/Disabled-Status pro Mod setzen.</summary>
    public bool PenumbraEnabledState { get; set; } = true;

    /// <summary>Priorität pro Mod setzen.</summary>
    public bool PenumbraPriority { get; set; } = true;

    /// <summary>Option-Gruppen (gewählte Optionen) pro Mod setzen.</summary>
    public bool PenumbraSettings { get; set; } = true;

    /// <summary>
    ///     Mod-Verzeichnisnamen (<c>PenumbraModEntry.ModDirectory</c>),
    ///     die beim Import übersprungen werden. Verzeichnisname statt
    ///     Anzeigename, weil nur ersterer in Penumbra eindeutig ist.
    /// </summary>
    public HashSet<string> ExcludedMods { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Ob nach Auflösung der Unter-Flags tatsächlich etwas für
    ///     Penumbra zu tun ist. Die Mod-Liste wird hier nicht geprüft —
    ///     „alle Mods abgewählt" ist ein gültiger (leerer) Import und wird
    ///     im Report als solcher ausgewiesen.
    /// </summary>
    public bool PenumbraEffective =>
        Penumbra && (PenumbraEnabledState || PenumbraPriority || PenumbraSettings);

    /// <summary>Ob ein Mod (per Verzeichnisname) importiert werden soll.</summary>
    public bool IsModSelected(string modDirectory) => !ExcludedMods.Contains(modDirectory);

    /// <summary>Wählt einen Mod an oder ab.</summary>
    public void SetModSelected(string modDirectory, bool selected)
    {
        if (selected)
            ExcludedMods.Remove(modDirectory);
        else
            ExcludedMods.Add(modDirectory);
    }

    // ---------------------------------------------------------- Customize+

    /// <summary>
    ///     Customize+-Template im Report ausgeben. Es gibt keinen
    ///     automatischen Customize+-Import (siehe DocumentationImporter),
    ///     daher steuert das Flag nur die Anzeige.
    /// </summary>
    public bool ShowCustomizePlusTemplate { get; set; } = true;

    // -------------------------------------------------------------- Helper

    /// <summary>
    ///     <c>true</c>, wenn Apply mindestens eine Schreib-Operation
    ///     auslösen würde. Die UI deaktiviert die Buttons sonst.
    /// </summary>
    public bool AnythingToApply => GlamourerEffective || PenumbraEffective;

    /// <summary>
    ///     Setzt alle Flags auf Default und leert die Mod-Ausschlussliste.
    ///     Wird beim Wechsel der Quelldatei aufgerufen, damit Ausschlüsse
    ///     eines anderen Exports nicht stillschweigend weiterwirken.
    /// </summary>
    public void Reset()
    {
        Glamourer = true;
        GlamourerEquipment = true;
        GlamourerCustomization = true;
        Penumbra = true;
        PenumbraEnabledState = true;
        PenumbraPriority = true;
        PenumbraSettings = true;
        ShowCustomizePlusTemplate = true;
        ExcludedMods.Clear();
    }
}
