// ==========================================================================
//  Services/ModTemplateCodec.cs
//
//  Share-Code für Vorlagen: ein kompakter Text, der sich per Chat/Discord
//  weitergeben lässt. Aufbau analog zu Glamourers Design-Blobs
//  (CLAUDE.md §6.1), aber mit lesbarem Präfix:
//
//      GDT1:<Base64( GZip( UTF-8-JSON( ModTemplateShare[] ) ) )>
//
//  „GDT" = Glamour Documenter Template, „1" = Format-Version. Ein Code
//  kann mehrere Vorlagen enthalten (z. B. alle Vorlagen eines Mods).
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Services;

/// <summary>
///     Kodiert und dekodiert Vorlagen-Share-Codes. Rein funktional, kein
///     IPC — daher statisch und ohne Logger; Fehler kommen als Text
///     zurück und werden vom Aufrufer geloggt/angezeigt.
/// </summary>
public static class ModTemplateCodec
{
    /// <summary>Präfix inkl. Format-Version. Bei Breaking-Change hochzählen.</summary>
    public const string Prefix = "GDT1:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Erzeugt einen Share-Code für eine oder mehrere Vorlagen.</summary>
    public static string Encode(IEnumerable<ModTemplate> templates)
    {
        var payload = templates.Select(t => t.ToShare()).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        using var output = new MemoryStream();
        // SmallestSize statt Optimal: Codes landen in Chat-Nachrichten
        // mit Längenlimit, die paar Mikrosekunden mehr sind egal.
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(json);

        return Prefix + Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    ///     Dekodiert einen Share-Code. Toleriert Whitespace und Zeilen-
    ///     umbrüche, wie sie beim Kopieren aus Chat-Clients entstehen.
    /// </summary>
    /// <param name="code">Roh-Text aus Eingabefeld oder Zwischenablage.</param>
    /// <param name="templates">Dekodierte Vorlagen (frische IDs), leer bei Fehler.</param>
    /// <param name="error">Menschenlesbare Fehlerursache oder leer.</param>
    public static bool TryDecode(string? code, out List<ModTemplate> templates, out string error)
    {
        templates = new List<ModTemplate>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            error = Strings.ShareCodeEmpty;
            return false;
        }

        // Whitespace komplett entfernen — Base64 enthält keins, und
        // Chat-Clients brechen lange Strings gern um.
        var compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = Strings.ShareCodeBadPrefix(Prefix);
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(compact[Prefix.Length..]);
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var json = reader.ReadToEnd();

            var shares = JsonSerializer.Deserialize<ModTemplateShare[]>(json, JsonOptions);
            if (shares is null || shares.Length == 0)
            {
                error = Strings.ShareCodeNoTemplates;
                return false;
            }

            foreach (var share in shares)
            {
                // Minimal-Validierung: ohne Mod-Verzeichnis ist die Vorlage
                // nicht anwendbar, ohne Namen nicht anzeigbar.
                if (string.IsNullOrWhiteSpace(share.ModDirectory) || string.IsNullOrWhiteSpace(share.Name))
                {
                    error = Strings.ShareCodeInvalidEntry;
                    templates.Clear();
                    return false;
                }
                templates.Add(ModTemplate.FromShare(share));
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            error = Strings.ShareCodeCorrupt(ex.Message);
            templates.Clear();
            return false;
        }
    }
}
