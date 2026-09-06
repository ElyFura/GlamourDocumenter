// ==========================================================================
//  Services/ModTemplateApplier.cs
//
//  Brücke zwischen Vorlagen-Galerie und Penumbra-IPC: erfasst die
//  aktuellen Einstellungen eines Mods als Vorlage und schreibt eine
//  Vorlage zurück in die aktive Collection. Nutzt ausschließlich die
//  bereits gewrappten Setter in PenumbraIpc.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Models;
using Penumbra.Api.Enums;

namespace GlamourDocumenter.Services;

/// <summary>
///     Erfassen und Anwenden von <see cref="ModTemplate"/>-Objekten.
/// </summary>
public sealed class ModTemplateApplier
{
    private readonly IPluginLog _log;
    private readonly PenumbraIpc _penumbra;

    public ModTemplateApplier(IPluginLog log, PenumbraIpc penumbra)
    {
        _log = log;
        _penumbra = penumbra;
    }

    /// <summary>
    ///     Liest die aktuellen Einstellungen eines Mods in einer
    ///     Collection als neue, noch unbenannte Vorlage.
    /// </summary>
    /// <returns><c>null</c>, wenn der Mod in der Collection unbekannt ist
    ///     oder Penumbra nicht antwortet.</returns>
    public ModTemplate? CaptureCurrent(Guid collectionId, string modDirectory, string modName)
    {
        var snapshot = _penumbra.TryGetCurrentModSettings(collectionId, modDirectory);
        if (snapshot is null)
            return null;

        var (_, priority, settings, _) = snapshot.Value;
        return new ModTemplate
        {
            ModDirectory = modDirectory,
            ModName = modName,
            Priority = priority,
            Settings = settings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList(),
                StringComparer.Ordinal),
        };
    }

    /// <summary>
    ///     Schreibt Optionen und Priorität einer Vorlage in die Collection.
    ///     Optional wird der Mod dabei aktiviert — eine Vorlage anwenden
    ///     und den Mod aus lassen ist der seltene Fall.
    /// </summary>
    /// <returns>Kurzer, einzeiliger Ergebnis-Text für die UI.</returns>
    public string Apply(ModTemplate template, Guid collectionId, bool enableMod)
    {
        var sb = new StringBuilder();
        var problems = 0;

        try
        {
            if (enableMod)
            {
                var ecEnabled = _penumbra.SetModEnabled(collectionId, template.ModDirectory, enabled: true);
                if (!IsOk(ecEnabled))
                {
                    problems++;
                    sb.Append("enabled=").Append(ecEnabled).Append("; ");
                }
            }

            var ecPriority = _penumbra.SetModPriority(collectionId, template.ModDirectory, template.Priority);
            if (!IsOk(ecPriority))
            {
                problems++;
                sb.Append("priority=").Append(ecPriority).Append("; ");
            }

            foreach (var (group, options) in template.Settings)
            {
                var ec = _penumbra.SetModSettings(collectionId, template.ModDirectory, group, options);
                if (!IsOk(ec))
                {
                    problems++;
                    sb.Append(group).Append('=').Append(ec).Append("; ");
                }
            }
        }
        catch (Exception ex)
        {
            // Die IPC-Wrapper fangen selbst — hier landet nur, was
            // außerhalb davon schiefgeht (z. B. eine korrupte Vorlage).
            _log.Warning(ex, "[GlamourDocumenter] Vorlage „{Name}“ konnte nicht angewendet werden.", template.Name);
            return Strings.TemplateApplyError(ex.Message);
        }

        return problems == 0
            ? Strings.TemplateApplyOk(template.Name, template.Settings.Count)
            : Strings.TemplateApplyPartial(template.Name, problems, sb.ToString().TrimEnd(' ', ';'));
    }

    /// <summary>
    ///     <c>NothingChanged</c> zählt als Erfolg — der Zielzustand ist
    ///     dann bereits erreicht.
    /// </summary>
    private static bool IsOk(PenumbraApiEc ec)
        => ec is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged;
}
