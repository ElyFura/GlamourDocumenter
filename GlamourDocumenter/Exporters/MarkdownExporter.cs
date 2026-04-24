// ==========================================================================
//  Exporters/MarkdownExporter.cs
//
//  Menschenlesbarer Export. Ziel: Dokument zum Archivieren, Teilen oder
//  ins Wiki ziehen. Kein Re-Import.
// ==========================================================================

using System.Globalization;
using System.Text;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Exporters;

/// <summary>
///     Rendert einen <see cref="DocumentationExport"/> als Markdown-
///     Dokument.
/// </summary>
/// <remarks>
///     Der Glamourer-Base64-Blob wird in einen Code-Block geschrieben,
///     damit er beim Re-Import (copy &amp; paste) kein Whitespace einfängt.
/// </remarks>
public sealed class MarkdownExporter : IDocumentExporter
{
    public string FileExtension => ".md";
    public string DisplayName => "Markdown";

    public string Render(DocumentationExport export)
    {
        var sb = new StringBuilder();

        RenderHeader(sb, export);
        if (export.IncludeStatsHeader)
            RenderSummary(sb, export);
        RenderCharacter(sb, export.Character);
        RenderPenumbra(sb, export.Penumbra);
        RenderGlamourer(sb, export.Glamourer);
        RenderCustomizePlus(sb, export.CustomizePlus);

        return sb.ToString();
    }

    private static void RenderHeader(StringBuilder sb, DocumentationExport export)
    {
        sb.AppendLine("# Glamour Documenter Export");
        sb.AppendLine();
        sb.Append("_Erstellt am ")
          .Append(export.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
          .Append(" mit Plugin-Version ")
          .Append(export.PluginVersion)
          .AppendLine("._");
        sb.AppendLine();
    }

    /// <summary>
    ///     Summary-Block direkt unter dem Header. Nur Plugins, die
    ///     tatsächlich Daten beigesteuert haben, tauchen auf — nicht-
    ///     verfügbare Plugins werden übersprungen, damit die Liste
    ///     nicht mit „n/a"-Zeilen vollläuft.
    /// </summary>
    private static void RenderSummary(StringBuilder sb, DocumentationExport export)
    {
        // Schnelle Ein-Zeilen-Statistik: wie viel steckt im Report?
        // Der Collector filtert Penumbra-Mods bereits auf enabled=true,
        // daher ist Mods.Count der aktive Count.
        var anyLine = false;

        if (export.Penumbra is { } pen)
        {
            sb.Append("- **Aktive Mods:** ")
              .Append(pen.Mods.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" (Collection „")
              .Append(pen.CollectionName)
              .AppendLine("“)");
            anyLine = true;
        }

        if (export.Glamourer is { } glam)
        {
            var slots = glam.Equipment?.Count ?? 0;
            var bonus = glam.Bonus?.Count ?? 0;
            sb.Append("- **Glamourer:** ")
              .Append(slots.ToString(CultureInfo.InvariantCulture))
              .Append(" Equipment-Slots, ")
              .Append(bonus.ToString(CultureInfo.InvariantCulture))
              .AppendLine(" Bonus-Items");
            anyLine = true;
        }

        if (export.CustomizePlus is { } cplus)
        {
            var profiles = cplus.AllProfiles.Count;
            var activeTxt = cplus.ActiveProfile is not null
                ? $"aktives Profil „{cplus.ActiveProfile.Name}"
                : "kein aktives Profil";
            sb.Append("- **Customize+:** ")
              .Append(activeTxt)
              .Append("“, ")
              .Append(profiles.ToString(CultureInfo.InvariantCulture))
              .AppendLine(" Profile verfügbar");
            anyLine = true;
        }

        if (anyLine)
            sb.AppendLine();
    }

    private static void RenderCharacter(StringBuilder sb, CharacterInfo character)
    {
        sb.AppendLine("## Charakter");
        sb.AppendLine();
        sb.Append("- **Name:** ").AppendLine(character.Name);
        sb.Append("- **Welt:** ").AppendLine(character.HomeWorld);

        // Job-Zeile: optional mit Icon-Marker vorweg (HtmlExporter
        // expandiert ihn zum <img>; MarkdownViewer ignoriert HTML-
        // Kommentare).
        sb.Append("- **Job:** ");
        if (!string.IsNullOrEmpty(character.JobIconDataUri))
            sb.Append("<!--gdoc-icon:").Append(character.JobIconDataUri).Append("-->");
        sb.Append(character.Job).Append(" (Lv ").Append(character.Level).AppendLine(")");
        sb.AppendLine();
    }

    private static void RenderPenumbra(StringBuilder sb, PenumbraExport? penumbra)
    {
        sb.AppendLine("## Penumbra");
        sb.AppendLine();

        if (penumbra is null)
        {
            sb.AppendLine("_Penumbra nicht verfügbar oder nicht aktiv._");
            sb.AppendLine();
            return;
        }

        sb.Append("**Collection:** ").Append(penumbra.CollectionName)
          .Append(" (`").Append(penumbra.CollectionId).AppendLine("`)");
        sb.AppendLine();

        if (penumbra.Mods.Count == 0)
        {
            sb.AppendLine("_Keine wirksamen Mods in dieser Collection._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("### Mods");
        sb.AppendLine();

        foreach (var mod in penumbra.Mods)
        {
            var state = mod.Enabled ? "aktiv" : "inaktiv";
            var inherited = mod.Inherited ? " (vererbt)" : "";
            sb.Append("#### ").Append(mod.ModName).AppendLine();
            sb.Append("- **Verzeichnis:** `").Append(mod.ModDirectory).AppendLine("`");
            sb.Append("- **Status:** ").Append(state).Append(inherited).AppendLine();
            sb.Append("- **Priorität:** ").Append(mod.Priority).AppendLine();

            if (mod.Settings.Count > 0)
            {
                sb.AppendLine("- **Optionen:**");
                foreach (var setting in mod.Settings)
                {
                    sb.Append("  - `").Append(setting.Key).Append("`: ");
                    if (setting.Value.Count == 0)
                    {
                        sb.AppendLine("_(keine)_");
                    }
                    else
                    {
                        sb.AppendLine(string.Join(", ", setting.Value));
                    }
                }
            }

            sb.AppendLine();
        }
    }

    private static void RenderGlamourer(StringBuilder sb, GlamourerExport? glamourer)
    {
        sb.AppendLine("## Glamourer");
        sb.AppendLine();

        if (glamourer is null)
        {
            sb.AppendLine("_Glamourer nicht verfügbar oder kein State vorhanden._");
            sb.AppendLine();
            return;
        }

        RenderGlamourerCustomize(sb, glamourer.Customize);
        RenderGlamourerEquipment(sb, glamourer.Equipment);
        RenderGlamourerMetaFlags(sb, glamourer.MetaFlags);
        RenderGlamourerBonus(sb, glamourer.Bonus);
        RenderGlamourerParameters(sb, glamourer.Parameters);
        RenderGlamourerMaterials(sb, glamourer.Materials);
        RenderGlamourerDesigns(sb, glamourer.Designs);

        // Re-Import-Blob bleibt immer dabei, unabhängig davon, was wir
        // strukturiert ausgeben konnten.
        if (!string.IsNullOrEmpty(glamourer.StateBase64))
        {
            sb.AppendLine("### Re-Import-Blob (Base64)");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(glamourer.StateBase64);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(glamourer.StateJson))
        {
            sb.AppendLine("### Vollständiger State (nativ, JSON)");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(glamourer.StateJson);
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static void RenderGlamourerCustomize(StringBuilder sb, GlamourerCustomize? customize)
    {
        if (customize is null)
            return;

        sb.AppendLine("### Customize");
        sb.AppendLine();
        sb.Append("- **ModelId:** ").AppendLine(customize.ModelId.ToString(CultureInfo.InvariantCulture));
        if (customize.Wetness.HasValue)
        {
            var applySuffix = customize.WetnessApply == true ? "" : " _(nicht angewendet)_";
            sb.Append("- **Force Wetness:** ").Append(customize.Wetness.Value ? "ja" : "nein")
              .AppendLine(applySuffix);
        }
        sb.AppendLine();

        if (customize.Fields.Count == 0)
            return;

        sb.AppendLine("| Feld | Wert | Angewendet |");
        sb.AppendLine("|------|------|------------|");
        foreach (var field in customize.Fields)
        {
            var value = field.DisplayValue;
            // Wenn Auflösung und Rohwert unterschiedlich sind, beides
            // zeigen — die Raw-ID ist für Re-Import und Bug-Reports nützlich.
            if (!string.Equals(value, field.RawValue, StringComparison.Ordinal))
                value = $"{field.DisplayValue} ({field.RawValue})";

            sb.Append("| ").Append(field.Name)
              .Append(" | ").Append(EscapePipes(value))
              .Append(" | ").Append(field.Apply ? "✓" : "—")
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void RenderGlamourerEquipment(
        StringBuilder sb, IReadOnlyList<GlamourerEquipmentSlot>? equipment)
    {
        if (equipment is null || equipment.Count == 0)
            return;

        sb.AppendLine("### Equipment");
        sb.AppendLine();
        sb.AppendLine("| Slot | Item | Dye 1 | Dye 2 | Crest | Apply |");
        sb.AppendLine("|------|------|-------|-------|-------|-------|");
        foreach (var slot in equipment)
        {
            var dye1 = FormatDye(slot.Stain1, slot.Stain1Name, slot.Stain1Hex, slot.ApplyStain);
            var dye2 = FormatDye(slot.Stain2, slot.Stain2Name, slot.Stain2Hex, slot.ApplyStain);
            var crest = slot.Crest
                ? (slot.ApplyCrest ? "✓" : "✓ _(nicht angew.)_")
                : "—";

            // Item-Zellen-Inhalt: optional Icon-Marker (HTML-Kommentar,
            // den MD-Viewer ignorieren und HtmlExporter zum <img>-Tag
            // expandiert) gefolgt vom Namen.
            var itemCell = slot.IconDataUri is not null
                ? $"<!--gdoc-icon:{slot.IconDataUri}-->{EscapePipes(slot.ItemName)}"
                : EscapePipes(slot.ItemName);

            sb.Append("| ").Append(slot.SlotName)
              .Append(" | ").Append(itemCell)
              .Append(" | ").Append(EscapePipes(dye1))
              .Append(" | ").Append(EscapePipes(dye2))
              .Append(" | ").Append(crest)
              .Append(" | ").Append(slot.Apply ? "✓" : "—")
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static string FormatDye(byte id, string? name, string? hex, bool applied)
    {
        if (id == 0)
            return "—";

        var label = name ?? $"#{id}";
        // Swatch-Marker vor dem Namen: unsichtbar im Markdown, im HTML
        // zum Farbquadrat expandiert.
        var prefix = hex is not null ? $"<!--gdoc-swatch:{hex}-->" : string.Empty;
        return applied ? $"{prefix}{label}" : $"{prefix}{label} _(nicht angew.)_";
    }

    private static void RenderGlamourerMetaFlags(StringBuilder sb, GlamourerMetaFlags? flags)
    {
        if (flags is null)
            return;

        var any = flags.HatVisible.HasValue || flags.VieraEarsVisible.HasValue
               || flags.VisorToggled.HasValue || flags.WeaponVisible.HasValue;
        if (!any)
            return;

        sb.AppendLine("### Sichtbarkeits-Toggles");
        sb.AppendLine();
        AppendFlag(sb, "Hat sichtbar", flags.HatVisible, flags.HatApply);
        AppendFlag(sb, "Viera-Ohren sichtbar", flags.VieraEarsVisible, flags.VieraEarsApply);
        AppendFlag(sb, "Visor offen", flags.VisorToggled, flags.VisorApply);
        AppendFlag(sb, "Waffe sichtbar", flags.WeaponVisible, flags.WeaponApply);
        sb.AppendLine();
    }

    private static void AppendFlag(StringBuilder sb, string label, bool? value, bool? apply)
    {
        if (!value.HasValue)
            return;
        var applySuffix = apply == true ? "" : " _(nicht angewendet)_";
        sb.Append("- **").Append(label).Append(":** ")
          .Append(value.Value ? "ja" : "nein")
          .AppendLine(applySuffix);
    }

    private static void RenderGlamourerBonus(StringBuilder sb, IReadOnlyList<GlamourerBonusItem>? bonus)
    {
        if (bonus is null || bonus.Count == 0)
            return;

        sb.AppendLine("### Bonus-Slots");
        sb.AppendLine();
        sb.AppendLine("| Slot | Item | Apply |");
        sb.AppendLine("|------|------|-------|");
        foreach (var item in bonus)
        {
            sb.Append("| ").Append(item.SlotName)
              .Append(" | ").Append(EscapePipes(item.ItemName))
              .Append(" | ").Append(item.Apply ? "✓" : "—")
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void RenderGlamourerParameters(
        StringBuilder sb, IReadOnlyList<GlamourerParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return;

        // Section-Name exakt wie in Glamourer-UI (Panel-Überschrift im
        // Character-Editor).
        sb.AppendLine("### Advanced Customization");
        sb.AppendLine();
        sb.AppendLine("| Parameter | Wert | Apply |");
        sb.AppendLine("|-----------|------|-------|");
        foreach (var p in parameters)
        {
            sb.Append("| ").Append(p.Name)
              .Append(" | ").Append(EscapePipes(p.Value))
              .Append(" | ").Append(p.Apply ? "✓" : "—")
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void RenderGlamourerMaterials(
        StringBuilder sb, IReadOnlyDictionary<string, string>? materials)
    {
        if (materials is null || materials.Count == 0)
            return;

        sb.AppendLine("### Advanced Dyes (Materials)");
        sb.AppendLine();
        foreach (var kv in materials)
        {
            sb.Append("- `").Append(kv.Key).Append("`: `").Append(kv.Value).AppendLine("`");
        }
        sb.AppendLine();
    }

    private static void RenderGlamourerDesigns(
        StringBuilder sb, IReadOnlyList<GlamourerDesign>? designs)
    {
        if (designs is null || designs.Count == 0)
            return;

        sb.Append("### Designs (")
          .Append(designs.Count.ToString(CultureInfo.InvariantCulture))
          .AppendLine(")");
        sb.AppendLine();
        sb.AppendLine(
            "_Gespeicherte Glamourer-Designs als Backup. Der Blob ist das " +
            "native Re-Import-Format — in Glamourer unter „Designs“ einfügen._");
        sb.AppendLine();

        foreach (var design in designs)
        {
            sb.Append("#### ").AppendLine(design.Name);
            sb.Append("- **Id:** `").Append(design.Id).AppendLine("`");
            sb.AppendLine("```");
            sb.AppendLine(design.Base64);
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    /// <summary>
    ///     Pipes in Zellwerten würden Markdown-Tabellenspalten zerbrechen.
    ///     HTML-Entity-Escape ist die saubere Lösung.
    /// </summary>
    private static string EscapePipes(string value) => value.Replace("|", "\\|");

    private static void RenderCustomizePlus(StringBuilder sb, CustomizePlusExport? customizePlus)
    {
        sb.AppendLine("## Customize+");
        sb.AppendLine();

        if (customizePlus is null)
        {
            sb.AppendLine("_Customize+ nicht verfügbar._");
            sb.AppendLine();
            return;
        }

        if (customizePlus.ActiveProfile is { } active)
        {
            sb.AppendLine("### Aktives Profil");
            sb.AppendLine();
            sb.Append("- **Name:** ").AppendLine(active.Name);
            sb.Append("- **UniqueId:** `").Append(active.UniqueId).AppendLine("`");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(active.Template);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("_Kein aktives Profil auf diesem Charakter._");
            sb.AppendLine();
        }

        if (customizePlus.AllProfiles.Count > 0)
        {
            sb.AppendLine("### Alle Profile");
            sb.AppendLine();
            foreach (var profile in customizePlus.AllProfiles)
            {
                var state = profile.IsEnabled ? "aktiviert" : "deaktiviert";
                sb.Append("- **").Append(profile.Name).Append("** (")
                  .Append(state).Append(") — `")
                  .Append(profile.UniqueId).AppendLine("`");
            }
            sb.AppendLine();
        }
    }
}
