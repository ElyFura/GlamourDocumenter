// ==========================================================================
//  Services/DocumentationImporter.cs
//
//  Re-Import des JSON-Exports zurück in Penumbra / Glamourer.
//  Bietet Dry-Run und Apply als getrennte Methoden — das UI führt
//  ersteres zur Anzeige aus, zweiteres erst nach Bestätigung.
//
//  Customize+ wird bewusst ausgelassen: die IPC-Setter sind weniger
//  klar dokumentiert, und der User kann das Template-JSON direkt in
//  Customize+ einfügen (copy & paste).
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using GlamourDocumenter.Models;
using Penumbra.Api.Enums;

namespace GlamourDocumenter.Services;

/// <summary>
///     Führt Re-Imports durch — entweder als Vorschau (Dry-Run) oder
///     als Schreib-Operation (Apply). Beide liefern einen Markdown-
///     Report zurück, den die UI anzeigt.
/// </summary>
public sealed class DocumentationImporter
{
    private readonly IPluginLog _log;
    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;

    public DocumentationImporter(IPluginLog log, PenumbraIpc penumbra, GlamourerIpc glamourer)
    {
        _log = log;
        _penumbra = penumbra;
        _glamourer = glamourer;
    }

    /// <summary>
    ///     Zeigt ohne Schreib-Operation an, was ein Apply tun würde.
    /// </summary>
    public string DryRun(DocumentationExport export, IPlayerCharacter target)
        => BuildReport(export, target, applyWrites: false);

    /// <summary>
    ///     Führt die Änderungen tatsächlich durch. Glamourer wird vor
    ///     Penumbra angewendet, damit die Material-/Customize-Einträge
    ///     bereits konsistent sind, wenn Mods umgeschaltet werden.
    /// </summary>
    public string Apply(DocumentationExport export, IPlayerCharacter target)
        => BuildReport(export, target, applyWrites: true);

    // ----------------------------------------------------------------

    private string BuildReport(DocumentationExport export, IPlayerCharacter target, bool applyWrites)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(applyWrites ? "Re-Import" : "Dry-Run");
        sb.AppendLine();
        sb.Append("- **Ziel:** ").AppendLine(target.Name.TextValue);
        sb.Append("- **Quelle:** ").Append(export.Character.Name)
          .Append(" @ ").AppendLine(export.ExportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        sb.AppendLine();

        ProcessGlamourer(sb, export.Glamourer, target, applyWrites);
        ProcessPenumbra(sb, export.Penumbra, target, applyWrites);
        ProcessCustomizePlus(sb, export.CustomizePlus);

        return sb.ToString();
    }

    // ----------------------------------------------------------------- Glamourer

    private void ProcessGlamourer(
        StringBuilder sb, GlamourerExport? glam, IPlayerCharacter target, bool applyWrites)
    {
        sb.AppendLine("## Glamourer");
        sb.AppendLine();

        if (glam is null)
        {
            sb.AppendLine("_Kein Glamourer-State in der Quelle._");
            sb.AppendLine();
            return;
        }

        // Bevorzugt StateJson (schon als JObject-Darstellung), sonst Base64-Blob.
        var payload = !string.IsNullOrEmpty(glam.StateJson) ? glam.StateJson : glam.StateBase64;
        if (string.IsNullOrEmpty(payload))
        {
            sb.AppendLine("_Weder StateJson noch StateBase64 in der Quelle._");
            sb.AppendLine();
            return;
        }

        if (!applyWrites)
        {
            sb.Append("- Wird State auf `").Append(target.Name.TextValue)
              .AppendLine("` anwenden (Equipment + Customization).");
            sb.AppendLine();
            return;
        }

        try
        {
            // Flags: Equipment + Customization anwenden, Once-Flag NICHT
            // setzen, damit die Änderung persistiert bis zur nächsten
            // Glamourer-Operation. Lock bleibt aus, damit andere Tools
            // (z. B. Auto-Apply) weiterhin den State ändern dürfen.
            var flags = ApplyFlag.Equipment | ApplyFlag.Customization;
            var ec = _glamourer.ApplyState(payload, target, flags);
            sb.Append("- ApplyState-Ergebnis: `").Append(ec).AppendLine("`");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Glamourer-Apply fehlgeschlagen.");
            sb.Append("- **Fehler:** ").AppendLine(ex.Message);
        }
        sb.AppendLine();
    }

    // ----------------------------------------------------------------- Penumbra

    private void ProcessPenumbra(
        StringBuilder sb, PenumbraExport? penumbra, IPlayerCharacter target, bool applyWrites)
    {
        sb.AppendLine("## Penumbra");
        sb.AppendLine();

        if (penumbra is null)
        {
            sb.AppendLine("_Kein Penumbra-Teil in der Quelle._");
            sb.AppendLine();
            return;
        }

        // Ziel-Collection: die aktuell aktive Collection des Target-Actors.
        // Alternative wäre die Collection aus dem Export per Name zu
        // matchen — zu fehleranfällig (Collection-Name könnte anders
        // heißen auf dem Ziel-System), daher nehmen wir die aktive.
        var active = _penumbra.GetCollectionForObject(target.ObjectIndex);
        if (active is null)
        {
            sb.AppendLine("_Keine aktive Collection für das Ziel-Objekt. Abbruch._");
            sb.AppendLine();
            return;
        }

        var (collectionId, collectionName) = active.Value;
        sb.Append("- **Ziel-Collection:** `").Append(collectionName)
          .Append("` (`").Append(collectionId).AppendLine("`)");
        if (!string.Equals(collectionName, penumbra.CollectionName, StringComparison.Ordinal))
        {
            sb.AppendLine($"- _Hinweis: Export kam aus Collection „{penumbra.CollectionName}“. " +
                          "Wir schreiben in die aktuell aktive Ziel-Collection._");
        }
        sb.AppendLine();

        var okCount = 0;
        var failCount = 0;

        foreach (var mod in penumbra.Mods)
        {
            if (!applyWrites)
            {
                sb.Append("- **Plan:** `").Append(mod.ModName)
                  .Append("` → enabled=").Append(mod.Enabled)
                  .Append(", priority=").Append(mod.Priority)
                  .Append(", settings=").Append(mod.Settings.Count).AppendLine();
                continue;
            }

            var results = new List<string>();

            var ecEnabled = _penumbra.SetModEnabled(collectionId, mod.ModDirectory, mod.Enabled);
            results.Add($"enabled={ecEnabled}");

            var ecPriority = _penumbra.SetModPriority(collectionId, mod.ModDirectory, mod.Priority);
            results.Add($"priority={ecPriority}");

            foreach (var setting in mod.Settings)
            {
                var ec = _penumbra.SetModSettings(
                    collectionId, mod.ModDirectory, setting.Key, setting.Value);
                results.Add($"{setting.Key}={ec}");
            }

            var allOk = ecEnabled is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged
                     && ecPriority is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged;
            if (allOk) okCount++; else failCount++;

            sb.Append("- ").Append(allOk ? "✓" : "✗").Append(" `").Append(mod.ModName)
              .Append("`: ").AppendLine(string.Join(", ", results));
        }

        if (applyWrites)
        {
            sb.AppendLine();
            sb.Append("- **Zusammenfassung:** ").Append(okCount).Append(" OK, ")
              .Append(failCount).AppendLine(" mit Warnungen");
        }
        sb.AppendLine();
    }

    // ----------------------------------------------------------------- Customize+

    private static void ProcessCustomizePlus(StringBuilder sb, CustomizePlusExport? cplus)
    {
        sb.AppendLine("## Customize+");
        sb.AppendLine();

        if (cplus is null)
        {
            sb.AppendLine("_Kein Customize+ in der Quelle._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("_Customize+-Profile werden in diesem Release NICHT automatisch " +
                      "re-importiert. Öffne Customize+ und füge das folgende Template " +
                      "manuell als neues Profil hinzu:_");
        sb.AppendLine();

        if (cplus.ActiveProfile is { } active)
        {
            sb.Append("- **Template:** `").Append(active.Name).AppendLine("`");
            sb.AppendLine("```json");
            sb.AppendLine(active.Template);
            sb.AppendLine("```");
        }
        else
        {
            sb.AppendLine("_Kein aktives Profil in der Quelle._");
        }
        sb.AppendLine();
    }
}
