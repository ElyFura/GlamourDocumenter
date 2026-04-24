// ==========================================================================
//  Exporters/IDocumentExporter.cs
//
//  Strategy-Interface für alle Export-Formate (CLAUDE.md §3: Neue Formate
//  → neue Klasse, kein Eingriff in existierende Exporter).
// ==========================================================================

using GlamourDocumenter.Models;

namespace GlamourDocumenter.Exporters;

/// <summary>
///     Vertrag für einen Export-Renderer. Bekommt einen fertigen
///     <see cref="DocumentationExport"/> und liefert die serialisierte
///     Form als String zurück. Persistenz (Datei schreiben) liegt beim
///     Aufrufer — die Exporter sind zustandslos und IO-frei.
/// </summary>
public interface IDocumentExporter
{
    /// <summary>
    ///     Dateiendung inklusive Punkt (z. B. <c>.md</c>, <c>.json</c>).
    ///     Wird vom UI im Save-Dialog verwendet.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    ///     Name für die UI (Radio-Button-Label, Dropdown-Text).
    /// </summary>
    string DisplayName { get; }

    /// <summary>Serialisiert den Export in die Ziel-Form.</summary>
    string Render(DocumentationExport export);
}
