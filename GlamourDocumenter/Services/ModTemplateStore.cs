// ==========================================================================
//  Services/ModTemplateStore.cs
//
//  Persistenz der Vorlagen-Galerie. Eine JSON-Datei im Plugin-ConfigDir
//  (CLAUDE.md §9 Punkt 8 — nur dort darf geschrieben werden). Bewusst
//  getrennt von der Dalamud-Configuration: die Galerie kann groß werden
//  und soll die eigentliche Plugin-Config nicht aufblähen.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Services;

/// <summary>
///     Lädt, hält und speichert die Vorlagen-Galerie. Jede Mutation
///     schreibt sofort auf Platte, damit ein Plugin-Crash keine
///     Vorlagen verliert.
/// </summary>
public sealed class ModTemplateStore : IDisposable
{
    private const string FileName = "mod-templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPluginLog _log;
    private readonly string _filePath;
    private ModTemplateLibrary _library = new();

    public ModTemplateStore(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;
        _filePath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), FileName);
        Load();
    }

    /// <summary>Alle Vorlagen, stabil sortiert nach Mod-Name, dann Vorlagen-Name.</summary>
    public IReadOnlyList<ModTemplate> Templates => _library.Templates;

    /// <summary>Vorlagen eines Mods (per Verzeichnisname).</summary>
    public IEnumerable<ModTemplate> ForMod(string modDirectory)
        => _library.Templates.Where(t => string.Equals(t.ModDirectory, modDirectory, StringComparison.Ordinal));

    /// <summary>Sucht eine Vorlage per ID.</summary>
    public ModTemplate? Find(Guid id)
        => _library.Templates.FirstOrDefault(t => t.Id == id);

    /// <summary>Fügt eine neue Vorlage hinzu und speichert.</summary>
    public void Add(ModTemplate template)
    {
        _library.Templates.Add(template);
        SortInPlace();
        Save();
    }

    /// <summary>
    ///     Markiert eine (bereits in der Liste enthaltene, in-place
    ///     mutierte) Vorlage als geändert und speichert.
    /// </summary>
    public void Update(ModTemplate template)
    {
        template.UpdatedAt = DateTimeOffset.Now;
        SortInPlace();
        Save();
    }

    /// <summary>
    ///     Übernimmt importierte Vorlagen (z. B. aus einem Share-Code).
    ///     Namenskollisionen innerhalb desselben Mods werden mit einem
    ///     Zähler-Suffix aufgelöst, damit nichts stillschweigend
    ///     überschrieben wird. Speichert einmal am Ende.
    /// </summary>
    /// <returns>Anzahl übernommener Vorlagen.</returns>
    public int Import(IEnumerable<ModTemplate> templates)
    {
        var count = 0;
        foreach (var tpl in templates)
        {
            tpl.Name = MakeUniqueName(tpl.ModDirectory, tpl.Name);
            _library.Templates.Add(tpl);
            count++;
        }

        if (count > 0)
        {
            SortInPlace();
            Save();
        }
        return count;
    }

    private string MakeUniqueName(string modDirectory, string baseName)
    {
        var taken = new HashSet<string>(
            ForMod(modDirectory).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName))
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Entfernt eine Vorlage und speichert.</summary>
    public void Remove(Guid id)
    {
        var removed = _library.Templates.RemoveAll(t => t.Id == id);
        if (removed > 0)
            Save();
    }

    // ----------------------------------------------------------------

    private void SortInPlace()
    {
        // Stabile Anzeige-Reihenfolge in der Galerie: erst Mod, dann
        // Vorlagen-Name. Sortierung hier statt in der UI, damit sie
        // nicht pro Frame läuft.
        _library.Templates = _library.Templates
            .OrderBy(t => t.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<ModTemplateLibrary>(json, JsonOptions);
            if (loaded is null)
            {
                _log.Warning("[GlamourDocumenter] Vorlagen-Datei leer oder ungültig: {Path}", _filePath);
                return;
            }

            if (loaded.FileVersion > ModTemplateLibrary.CurrentFileVersion)
            {
                // Datei stammt von einer neueren Plugin-Version. Nicht
                // laden, sonst würde ein späterer Save Felder verwerfen,
                // die wir nicht kennen.
                _log.Warning(
                    "[GlamourDocumenter] Vorlagen-Datei hat FileVersion {V} (unterstützt: {C}) — wird ignoriert.",
                    loaded.FileVersion, ModTemplateLibrary.CurrentFileVersion);
                return;
            }

            // Platz für künftige Migrationen (FileVersion < Current).
            loaded.FileVersion = ModTemplateLibrary.CurrentFileVersion;
            _library = loaded;
            SortInPlace();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Vorlagen-Datei konnte nicht gelesen werden: {Path}", _filePath);
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Erst in Temp-Datei, dann atomar ersetzen — ein Absturz
            // mitten im Schreiben hinterlässt so keine halbe JSON-Datei.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_library, JsonOptions));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Vorlagen-Datei konnte nicht geschrieben werden: {Path}", _filePath);
        }
    }

    /// <summary>
    ///     Platzhalter. Jede Mutation speichert sofort, daher gibt es
    ///     beim Dispose nichts zu flushen; keine unmanaged Ressourcen.
    /// </summary>
    public void Dispose()
    {
    }
}
