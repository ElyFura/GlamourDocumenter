// ==========================================================================
//  Services/DocumentationImporter.cs
//
//  Re-Import des JSON-Exports zurück in Penumbra / Glamourer.
//  Bietet Dry-Run und Apply als getrennte Methoden — das UI führt
//  ersteres zur Anzeige aus, zweiteres erst nach Bestätigung.
//
//  Was importiert wird, steuert ein ImportOptions-Objekt (Plugin-Teile,
//  Unter-Flags, einzelne Mods). Abgewählte Teile erscheinen im Report
//  als „übersprungen", damit der Dry-Run 1:1 zeigt, was Apply tut.
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
    ///     Zeigt ohne Schreib-Operation an, was ein Apply mit denselben
    ///     <paramref name="options"/> tun würde.
    /// </summary>
    public string DryRun(DocumentationExport export, IPlayerCharacter target, ImportOptions options)
        => BuildReport(export, target, options, applyWrites: false);

    /// <summary>
    ///     Führt die Änderungen tatsächlich durch. Glamourer wird vor
    ///     Penumbra angewendet, damit die Material-/Customize-Einträge
    ///     bereits konsistent sind, wenn Mods umgeschaltet werden.
    /// </summary>
    public string Apply(DocumentationExport export, IPlayerCharacter target, ImportOptions options)
        => BuildReport(export, target, options, applyWrites: true);

    // ----------------------------------------------------------------

    private string BuildReport(
        DocumentationExport export, IPlayerCharacter target, ImportOptions options, bool applyWrites)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(applyWrites ? "Re-Import" : "Dry-Run");
        sb.AppendLine();
        sb.Append("- **Ziel:** ").AppendLine(target.Name.TextValue);
        sb.Append("- **Quelle:** ").Append(export.Character.Name)
          .Append(" @ ").AppendLine(export.ExportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        sb.AppendLine();

        ProcessGlamourer(sb, export.Glamourer, target, options, applyWrites);
        ProcessPenumbra(sb, export.Penumbra, target, options, applyWrites);
        ProcessCustomizePlus(sb, export.CustomizePlus, options);

        return sb.ToString();
    }

    // ----------------------------------------------------------------- Glamourer

    private void ProcessGlamourer(
        StringBuilder sb, GlamourerExport? glam, IPlayerCharacter target,
        ImportOptions options, bool applyWrites)
    {
        sb.AppendLine("## Glamourer");
        sb.AppendLine();

        if (!options.Glamourer)
        {
            sb.AppendLine("_Übersprungen — in der Auswahl abgewählt._");
            sb.AppendLine();
            return;
        }

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

        // Flags aus den Unter-Checkboxen zusammensetzen. Once-Flag NICHT
        // setzen, damit die Änderung persistiert bis zur nächsten
        // Glamourer-Operation. Lock bleibt aus, damit andere Tools
        // (z. B. Auto-Apply) weiterhin den State ändern dürfen.
        var flags = (ApplyFlag)0;
        var parts = new List<string>(2);
        if (options.GlamourerEquipment)
        {
            flags |= ApplyFlag.Equipment;
            parts.Add("Equipment");
        }
        if (options.GlamourerCustomization)
        {
            flags |= ApplyFlag.Customization;
            parts.Add("Customization");
        }

        if (parts.Count == 0)
        {
            sb.AppendLine("_Übersprungen — weder Equipment noch Customization ausgewählt._");
            sb.AppendLine();
            return;
        }

        var partsLabel = string.Join(" + ", parts);

        if (!applyWrites)
        {
            sb.Append("- Wird State auf `").Append(target.Name.TextValue)
              .Append("` anwenden (").Append(partsLabel).AppendLine(").");
            sb.AppendLine();
            return;
        }

        try
        {
            var ec = _glamourer.ApplyState(payload, target, flags);
            sb.Append("- ApplyState (").Append(partsLabel).Append("): `").Append(ec).AppendLine("`");
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
        StringBuilder sb, PenumbraExport? penumbra, IPlayerCharacter target,
        ImportOptions options, bool applyWrites)
    {
        sb.AppendLine("## Penumbra");
        sb.AppendLine();

        if (!options.Penumbra)
        {
            sb.AppendLine("_Übersprungen — in der Auswahl abgewählt._");
            sb.AppendLine();
            return;
        }

        if (penumbra is null)
        {
            sb.AppendLine("_Kein Penumbra-Teil in der Quelle._");
            sb.AppendLine();
            return;
        }

        if (!options.PenumbraEffective)
        {
            sb.AppendLine("_Übersprungen — weder Status, Priorität noch Optionen ausgewählt._");
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

        // Welche Felder pro Mod geschrieben werden — einmal oben im
        // Report, statt pro Mod zu wiederholen.
        var fields = new List<string>(3);
        if (options.PenumbraEnabledState) fields.Add("Status");
        if (options.PenumbraPriority) fields.Add("Priorität");
        if (options.PenumbraSettings) fields.Add("Optionen");
        sb.Append("- **Felder:** ").AppendLine(string.Join(", ", fields));

        var selectedMods = penumbra.Mods.Where(m => options.IsModSelected(m.ModDirectory)).ToList();
        var skippedCount = penumbra.Mods.Count - selectedMods.Count;
        sb.Append("- **Mods:** ").Append(selectedMods.Count).Append(" von ")
          .Append(penumbra.Mods.Count).Append(" ausgewählt");
        if (skippedCount > 0)
            sb.Append(" (").Append(skippedCount).Append(" übersprungen)");
        sb.AppendLine();
        sb.AppendLine();

        if (selectedMods.Count == 0)
        {
            sb.AppendLine("_Keine Mods ausgewählt — nichts zu tun._");
            sb.AppendLine();
            return;
        }

        var okCount = 0;
        var failCount = 0;

        foreach (var mod in selectedMods)
        {
            if (!applyWrites)
            {
                sb.Append("- **Plan:** `").Append(mod.ModName).Append('`');
                if (options.PenumbraEnabledState)
                    sb.Append(" → enabled=").Append(mod.Enabled);
                if (options.PenumbraPriority)
                    sb.Append(", priority=").Append(mod.Priority);
                if (options.PenumbraSettings)
                    sb.Append(", settings=").Append(mod.Settings.Count);
                sb.AppendLine();
                continue;
            }

            var results = new List<string>();
            var allOk = true;

            if (options.PenumbraEnabledState)
            {
                var ecEnabled = _penumbra.SetModEnabled(collectionId, mod.ModDirectory, mod.Enabled);
                results.Add($"enabled={ecEnabled}");
                allOk &= IsOk(ecEnabled);
            }

            if (options.PenumbraPriority)
            {
                var ecPriority = _penumbra.SetModPriority(collectionId, mod.ModDirectory, mod.Priority);
                results.Add($"priority={ecPriority}");
                allOk &= IsOk(ecPriority);
            }

            if (options.PenumbraSettings)
            {
                foreach (var setting in mod.Settings)
                {
                    var ec = _penumbra.SetModSettings(
                        collectionId, mod.ModDirectory, setting.Key, setting.Value);
                    results.Add($"{setting.Key}={ec}");
                    // Settings-Fehler zählen bewusst NICHT in allOk — sie
                    // waren es auch vorher nicht (Option-Gruppen können auf
                    // dem Ziel-System fehlen, ohne dass der Mod als solcher
                    // „kaputt" ist). Sichtbar bleiben sie über results.
                }
            }

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

    /// <summary>
    ///     <c>NothingChanged</c> zählt als Erfolg — der Zielzustand ist
    ///     dann bereits erreicht.
    /// </summary>
    private static bool IsOk(PenumbraApiEc ec)
        => ec is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged;

    // ----------------------------------------------------------------- Customize+

    private static void ProcessCustomizePlus(
        StringBuilder sb, CustomizePlusExport? cplus, ImportOptions options)
    {
        sb.AppendLine("## Customize+");
        sb.AppendLine();

        if (!options.ShowCustomizePlusTemplate)
        {
            sb.AppendLine("_Template-Ausgabe in der Auswahl abgewählt._");
            sb.AppendLine();
            return;
        }

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
