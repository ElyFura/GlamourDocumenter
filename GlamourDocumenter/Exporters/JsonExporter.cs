// ==========================================================================
//  Exporters/JsonExporter.cs
//
//  Maschinenlesbarer Export. Ziel: zukünftiger Re-Import (Roadmap §10).
//  Das hier gerenderte JSON ist das Dokumentations-Format des Plugins,
//  nicht zu verwechseln mit den nativen Blobs (Glamourer-State-Base64,
//  Customize+-Template), die als opake Strings im JSON eingebettet sind.
// ==========================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Exporters;

/// <summary>
///     Serialisiert einen <see cref="DocumentationExport"/> als JSON.
/// </summary>
/// <remarks>
///     Wichtig: Breaking Changes am Output (Feld-Rename, Typ-Wechsel)
///     erfordern laut CLAUDE.md §9 Punkt 5 eine <c>fileVersion</c>-
///     Feld-Einführung. Solange das Schema additive Änderungen erfährt,
///     bleibt <c>fileVersion</c> weg.
/// </remarks>
public sealed class JsonExporter : IDocumentExporter
{
    public string FileExtension => ".json";
    public string DisplayName => "JSON";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Enum-Werte als String serialisieren (lesbarer im Output).
        Converters = { new JsonStringEnumConverter() },
        // null-Felder behalten wir, damit der Consumer die Struktur stabil
        // sieht (z. B. „Glamourer hatte keine Daten").
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Render(DocumentationExport export)
        => JsonSerializer.Serialize(export, Options);
}
