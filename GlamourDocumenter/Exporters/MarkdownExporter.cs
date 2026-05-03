// ==========================================================================
//  Exporters/MarkdownExporter.cs
//
//  Menschenlesbarer Export. Ziel: Dokument zum Archivieren, Teilen oder
//  ins Wiki ziehen. Kein Re-Import.
//
//  Alle benutzersichtbaren Texte laufen über <see cref="Strings"/>, damit
//  ein Sprachwechsel im Settings-Tab den Markdown-Output und damit auch
//  die HTML-Variante (die auf MarkdownExporter aufsetzt) sofort umschaltet.
//
//  Markenbezeichner (Penumbra / Glamourer / Customize+) bleiben Wort-
//  identisch in beiden Sprachen — der HtmlExporter-Post-Processor erkennt
//  Sektionen per exact-match auf diese Strings.
// ==========================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GlamourDocumenter.Models;
using GlamourDocumenter.Services;

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
        sb.Append("# ").AppendLine(Strings.MdHeaderTitle);
        sb.AppendLine();
        var ts = export.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        sb.Append('_').Append(Strings.MdCreatedWith(ts, export.PluginVersion)).AppendLine("_");
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
            sb.Append("- **").Append(Strings.MdActiveMods).Append(":** ")
              .Append(pen.Mods.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" (").Append(Strings.MdCollectionLabel).Append(" „")
              .Append(pen.CollectionName)
              .AppendLine("“)");
            anyLine = true;
        }

        if (export.Glamourer is { } glam)
        {
            var slots = glam.Equipment?.Count ?? 0;
            var bonus = glam.Bonus?.Count ?? 0;
            sb.Append("- **").Append(Strings.MdGlamourerSummary).Append(":** ")
              .Append(Strings.MdEquipmentSlotsSuffix(slots))
              .Append(", ")
              .AppendLine(Strings.MdBonusItemsSuffix(bonus));
            anyLine = true;
        }

        if (export.CustomizePlus is { } cplus)
        {
            var profiles = cplus.AllProfiles.Count;
            var activeTxt = cplus.ActiveProfile is not null
                ? Strings.MdActiveProfileSummary(cplus.ActiveProfile.Name)
                : Strings.MdNoActiveProfileSummary;
            // Wenn ein aktives Profil benannt ist, schließen wir die
            // Anführungszeichen hier — sonst lassen wir's neutral.
            var closingQuote = cplus.ActiveProfile is not null ? "“" : "";
            sb.Append("- **Customize+:** ")
              .Append(activeTxt)
              .Append(closingQuote)
              .Append(", ")
              .AppendLine(Strings.MdProfilesAvailable(profiles));
            anyLine = true;
        }

        if (anyLine)
            sb.AppendLine();
    }

    private static void RenderCharacter(StringBuilder sb, CharacterInfo character)
    {
        sb.Append("## ").AppendLine(Strings.MdCharacter);
        sb.AppendLine();
        sb.Append("- **").Append(Strings.MdName).Append(":** ").AppendLine(character.Name);
        sb.Append("- **").Append(Strings.MdWorld).Append(":** ").AppendLine(character.HomeWorld);

        // Job-Zeile: optional mit Icon-Marker vorweg (HtmlExporter
        // expandiert ihn zum <img>; MarkdownViewer ignoriert HTML-
        // Kommentare).
        sb.Append("- **").Append(Strings.MdJob).Append(":** ");
        if (!string.IsNullOrEmpty(character.JobIconDataUri))
            sb.Append("<!--gdoc-icon:").Append(character.JobIconDataUri).Append("-->");
        sb.Append(character.Job).Append(" (").Append(Strings.MdLevelAbbr).Append(' ')
          .Append(character.Level).AppendLine(")");
        sb.AppendLine();
    }

    private static void RenderPenumbra(StringBuilder sb, PenumbraExport? penumbra)
    {
        // Brand-Name "Penumbra" bleibt in beiden Sprachen identisch — der
        // HtmlExporter erkennt die Sektion per exact-match.
        sb.AppendLine("## Penumbra");
        sb.AppendLine();

        if (penumbra is null)
        {
            sb.Append('_').Append(Strings.MdPenumbraNotAvail).AppendLine("_");
            sb.AppendLine();
            return;
        }

        sb.Append("**").Append(Strings.MdCollectionLabel).Append(":** ")
          .Append(penumbra.CollectionName)
          .Append(" (`").Append(penumbra.CollectionId).AppendLine("`)");
        sb.AppendLine();

        if (penumbra.Mods.Count == 0)
        {
            sb.Append('_').Append(Strings.MdNoEffectiveMods).AppendLine("_");
            sb.AppendLine();
            return;
        }

        sb.Append("### ").AppendLine(Strings.MdMods);
        sb.AppendLine();

        foreach (var mod in penumbra.Mods)
        {
            var state = mod.Enabled ? Strings.MdActive : Strings.MdInactive;
            var inherited = mod.Inherited ? $" ({Strings.MdInherited})" : "";
            sb.Append("#### ").Append(mod.ModName).AppendLine();
            sb.Append("- **").Append(Strings.MdDirectory).Append(":** `")
              .Append(mod.ModDirectory).AppendLine("`");
            sb.Append("- **").Append(Strings.MdStatus).Append(":** ")
              .Append(state).Append(inherited).AppendLine();
            sb.Append("- **").Append(Strings.MdPriority).Append(":** ")
              .Append(mod.Priority).AppendLine();

            if (mod.Settings.Count > 0)
            {
                sb.Append("- **").Append(Strings.MdOptions).AppendLine(":**");
                foreach (var setting in mod.Settings)
                {
                    sb.Append("  - `").Append(setting.Key).Append("`: ");
                    if (setting.Value.Count == 0)
                    {
                        sb.Append('_').Append(Strings.MdNone).AppendLine("_");
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
        // Brand-Name — identisch in beiden Sprachen.
        sb.AppendLine("## Glamourer");
        sb.AppendLine();

        if (glamourer is null)
        {
            sb.Append('_').Append(Strings.MdGlamourerNotAvail).AppendLine("_");
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
            sb.Append("### ").AppendLine(Strings.MdReimportBlob);
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(glamourer.StateBase64);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(glamourer.StateJson))
        {
            sb.Append("### ").AppendLine(Strings.MdFullStateJson);
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

        sb.Append("### ").AppendLine(Strings.MdCustomize);
        sb.AppendLine();
        sb.Append("- **").Append(Strings.MdModelId).Append(":** ")
          .AppendLine(customize.ModelId.ToString(CultureInfo.InvariantCulture));
        if (customize.Wetness.HasValue)
        {
            var applySuffix = customize.WetnessApply == true ? "" : $" _{Strings.MdNotApplied}_";
            sb.Append("- **").Append(Strings.MdForceWetness).Append(":** ")
              .Append(customize.Wetness.Value ? Strings.MdYes : Strings.MdNo)
              .AppendLine(applySuffix);
        }
        sb.AppendLine();

        if (customize.Fields.Count == 0)
            return;

        sb.Append("| ").Append(Strings.MdField)
          .Append(" | ").Append(Strings.MdValue)
          .Append(" | ").Append(Strings.MdAppliedColumn).AppendLine(" |");
        sb.AppendLine("|------|------|------------|");
        foreach (var field in customize.Fields)
        {
            var value = field.DisplayValue;
            // Wenn Auflösung und Rohwert unterschiedlich sind, beides
            // zeigen — die Raw-ID ist für Re-Import und Bug-Reports nützlich.
            if (!string.Equals(value, field.RawValue, System.StringComparison.Ordinal))
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

        sb.Append("### ").AppendLine(Strings.MdEquipment);
        sb.AppendLine();
        sb.Append("| ").Append(Strings.MdSlot)
          .Append(" | ").Append(Strings.MdItem)
          .Append(" | Dye 1 | Dye 2 | ").Append(Strings.MdCrest)
          .Append(" | ").Append(Strings.MdApply).AppendLine(" |");
        sb.AppendLine("|------|------|-------|-------|-------|-------|");
        foreach (var slot in equipment)
        {
            var dye1 = FormatDye(slot.Stain1, slot.Stain1Name, slot.Stain1Hex, slot.ApplyStain);
            var dye2 = FormatDye(slot.Stain2, slot.Stain2Name, slot.Stain2Hex, slot.ApplyStain);
            var crest = slot.Crest
                ? (slot.ApplyCrest ? "✓" : $"✓ _{Strings.MdNotAppliedShort}_")
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
        return applied ? $"{prefix}{label}" : $"{prefix}{label} _{Strings.MdNotAppliedShort}_";
    }

    private static void RenderGlamourerMetaFlags(StringBuilder sb, GlamourerMetaFlags? flags)
    {
        if (flags is null)
            return;

        var any = flags.HatVisible.HasValue || flags.VieraEarsVisible.HasValue
               || flags.VisorToggled.HasValue || flags.WeaponVisible.HasValue;
        if (!any)
            return;

        sb.Append("### ").AppendLine(Strings.MdVisibilityToggles);
        sb.AppendLine();
        AppendFlag(sb, Strings.MdHatVisible, flags.HatVisible, flags.HatApply);
        AppendFlag(sb, Strings.MdVieraEarsVisible, flags.VieraEarsVisible, flags.VieraEarsApply);
        AppendFlag(sb, Strings.MdVisorOpen, flags.VisorToggled, flags.VisorApply);
        AppendFlag(sb, Strings.MdWeaponVisible, flags.WeaponVisible, flags.WeaponApply);
        sb.AppendLine();
    }

    private static void AppendFlag(StringBuilder sb, string label, bool? value, bool? apply)
    {
        if (!value.HasValue)
            return;
        var applySuffix = apply == true ? "" : $" _{Strings.MdNotApplied}_";
        sb.Append("- **").Append(label).Append(":** ")
          .Append(value.Value ? Strings.MdYes : Strings.MdNo)
          .AppendLine(applySuffix);
    }

    private static void RenderGlamourerBonus(StringBuilder sb, IReadOnlyList<GlamourerBonusItem>? bonus)
    {
        if (bonus is null || bonus.Count == 0)
            return;

        sb.Append("### ").AppendLine(Strings.MdBonusSlots);
        sb.AppendLine();
        sb.Append("| ").Append(Strings.MdSlot)
          .Append(" | ").Append(Strings.MdItem)
          .Append(" | ").Append(Strings.MdApply).AppendLine(" |");
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
        sb.Append("### ").AppendLine(Strings.MdAdvancedCustomization);
        sb.AppendLine();
        sb.Append("| ").Append(Strings.MdParameter)
          .Append(" | ").Append(Strings.MdValue)
          .Append(" | ").Append(Strings.MdApply).AppendLine(" |");
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

        sb.Append("### ").AppendLine(Strings.MdAdvancedDyes);
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

        sb.Append("### ").AppendLine(Strings.MdDesigns(designs.Count));
        sb.AppendLine();
        sb.Append('_').Append(Strings.MdDesignsHint).AppendLine("_");
        sb.AppendLine();

        foreach (var design in designs)
        {
            sb.Append("#### ").AppendLine(design.Name);
            sb.Append("- **").Append(Strings.MdId).Append(":** `")
              .Append(design.Id).AppendLine("`");
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
        // Brand-Name "Customize+" — identisch in beiden Sprachen.
        sb.AppendLine("## Customize+");
        sb.AppendLine();

        if (customizePlus is null)
        {
            sb.Append('_').Append(Strings.MdCustomizePlusNotAvail).AppendLine("_");
            sb.AppendLine();
            return;
        }

        if (customizePlus.ActiveProfile is { } active)
        {
            sb.Append("### ").AppendLine(Strings.MdActiveProfileSection);
            sb.AppendLine();
            sb.Append("- **").Append(Strings.MdName).Append(":** ").AppendLine(active.Name);
            sb.Append("- **").Append(Strings.MdUniqueId).Append(":** `")
              .Append(active.UniqueId).AppendLine("`");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(active.Template);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        else
        {
            sb.Append('_').Append(Strings.MdNoActiveProfileSection).AppendLine("_");
            sb.AppendLine();
        }

        if (customizePlus.AllProfiles.Count > 0)
        {
            sb.Append("### ").AppendLine(Strings.MdAllProfiles);
            sb.AppendLine();
            foreach (var profile in customizePlus.AllProfiles)
            {
                var state = profile.IsEnabled ? Strings.MdEnabled : Strings.MdDisabled;
                sb.Append("- **").Append(profile.Name).Append("** (")
                  .Append(state).Append(") — `")
                  .Append(profile.UniqueId).AppendLine("`");
            }
            sb.AppendLine();
        }
    }
}
