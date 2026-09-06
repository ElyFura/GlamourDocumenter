// ==========================================================================
//  Windows/MainWindow.Templates.cs
//
//  Vorlagen-Tab (partial von MainWindow): Optionssätze pro Penumbra-Mod
//  anlegen, bearbeiten und per Klick in die aktive Collection schreiben.
//
//  Aufbau von oben nach unten:
//    1. Mod-Auswahl (gefiltertes Combo über GetModList)
//    2. Optionen-Editor für den gewählten Mod (Radio für Single-Select,
//       Checkboxen für Multi), Priorität, Name, Speichern/Aktualisieren
//    3. Galerie aller Vorlagen, gruppiert nach Mod, mit Anwenden /
//       Bearbeiten / Löschen
//
//  IPC läuft wie im Import-Tab aus dem Draw heraus (CLAUDE.md §4.3:
//  nur LocalPlayer greifen, sofort fertig). Teure Aufrufe (Mod-Liste,
//  Optionsgruppen) werden gecacht und nur bei Auswahl-/Refresh-Aktionen
//  erneuert — nie pro Frame.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GlamourDocumenter.Models;
using GlamourDocumenter.Services;

namespace GlamourDocumenter.Windows;

public sealed partial class MainWindow
{
    // Obergrenze angezeigter Mods im Combo — bei sehr großen
    // Installationen sonst Frame-Einbrüche durch tausende Selectables.
    private const int TemplateModComboLimit = 300;

    // Mod-Liste (Verzeichnis → Anzeigename), sortiert nach Anzeigename.
    private List<KeyValuePair<string, string>> _tplMods = new();
    private bool _tplModsLoaded;
    private string _tplModFilter = string.Empty;

    // Aktive Collection des LocalPlayers, gecacht mit der Mod-Liste.
    private (Guid Id, string Name)? _tplCollection;

    // Gewählter Mod + Editor-Zustand.
    private string? _tplModDir;
    private string _tplModName = string.Empty;
    private IReadOnlyDictionary<string, ModOptionGroup> _tplGroups = new Dictionary<string, ModOptionGroup>();
    private Dictionary<string, List<string>> _tplEdit = new(StringComparer.Ordinal);
    private int _tplPriority;
    private string _tplName = string.Empty;
    private bool _tplDefaultsAssumed;
    private Guid? _tplEditingId;

    // Galerie-Optionen und Status-Zeile.
    private bool _tplOnlySelectedMod;
    private bool _tplEnableOnApply = true;
    private string _tplStatus = string.Empty;
    private bool _tplStatusIsError;

    // Eingabefeld für Share-Codes (Import).
    private string _tplImportCode = string.Empty;

    // -------------------------------------------------------------------
    //  Tab
    // -------------------------------------------------------------------

    private void DrawTemplatesTab()
    {
        ImGui.TextWrapped(Strings.TemplatesInfo);
        ImGui.Separator();

        if (!_penumbra.IsAvailable())
        {
            ImGui.TextDisabled(Strings.TemplateNoPenumbra);
            return;
        }

        if (!_tplModsLoaded)
            RefreshTemplateMods();

        DrawTemplateModPicker();

        if (_tplCollection is { } col)
            ImGui.TextDisabled(Strings.TemplateCollection(col.Name));
        else
            ImGui.TextDisabled(Strings.TemplateNoCollection);

        if (_tplModDir is not null)
            DrawTemplateEditor();

        ImGui.Separator();
        DrawTemplateGallery();

        if (!string.IsNullOrEmpty(_tplStatus))
        {
            if (_tplStatusIsError)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _tplStatus);
            else
                ImGui.TextWrapped(_tplStatus);
        }
    }

    // -------------------------------------------------------------------
    //  Mod-Auswahl
    // -------------------------------------------------------------------

    /// <summary>
    ///     Lädt Mod-Liste und aktive Collection neu. Wird beim ersten
    ///     Öffnen des Tabs und per Refresh-Button aufgerufen.
    /// </summary>
    private void RefreshTemplateMods()
    {
        _tplModsLoaded = true;
        _tplMods = _penumbra.GetModList()
            .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var target = _objectTable.LocalPlayer;
        _tplCollection = target is null ? null : _penumbra.GetCollectionForObject(target.ObjectIndex);
    }

    private void DrawTemplateModPicker()
    {
        ImGui.Text($"{Strings.TemplateModLabel}:");
        ImGui.SameLine();

        // Filter + Combo teilen sich die Zeile; Refresh-Button rechts.
        var refreshWidth = ImGui.CalcTextSize(Strings.TemplateRefresh).X + ImGui.GetStyle().FramePadding.X * 2;
        var avail = ImGui.GetContentRegionAvail().X - refreshWidth - ImGui.GetStyle().ItemSpacing.X * 2;
        var filterWidth = MathF.Max(120f, avail * 0.35f);

        ImGui.SetNextItemWidth(filterWidth);
        ImGui.InputText(
            "##tpl-mod-filter",
            ref _tplModFilter,
            128,
            ImGuiInputTextFlags.None,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
        DrawInputHintOverlay(_tplModFilter, Strings.TemplateModFilterHint);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(avail - filterWidth);
        var preview = _tplModDir is null ? Strings.SelectPlaceholder : _tplModName;
        if (ImGui.BeginCombo("##tpl-mod", preview))
        {
            var shown = 0;
            var hidden = 0;
            foreach (var (dir, name) in _tplMods)
            {
                if (!string.IsNullOrEmpty(_tplModFilter)
                    && !name.Contains(_tplModFilter, StringComparison.OrdinalIgnoreCase)
                    && !dir.Contains(_tplModFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (shown >= TemplateModComboLimit)
                {
                    hidden++;
                    continue;
                }
                shown++;

                var isSel = string.Equals(dir, _tplModDir, StringComparison.Ordinal);
                // Verzeichnis als ID-Suffix: Anzeigenamen sind nicht eindeutig.
                if (ImGui.Selectable($"{name}##{dir}", isSel))
                    SelectTemplateMod(dir, name);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(dir);
            }

            if (shown == 0)
                ImGui.TextDisabled(Strings.TemplateNoMods);
            if (hidden > 0)
                ImGui.TextDisabled(Strings.TemplateMoreMods(hidden));

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button(Strings.TemplateRefresh))
            RefreshTemplateMods();
    }

    /// <summary>
    ///     Zeichnet einen Platzhalter-Text über ein leeres, nicht
    ///     fokussiertes InputText (gleiche Technik wie im Import-Tab).
    /// </summary>
    private static void DrawInputHintOverlay(string value, string hint)
    {
        if (!string.IsNullOrEmpty(value) || ImGui.IsItemActive())
            return;

        var min = ImGui.GetItemRectMin();
        var pad = ImGui.GetStyle().FramePadding;
        ImGui.GetWindowDrawList().AddText(
            new Vector2(min.X + pad.X, min.Y + pad.Y),
            ImGui.GetColorU32(ImGuiCol.TextDisabled),
            hint);
    }

    /// <summary>
    ///     Wählt einen Mod aus, lädt seine Optionsgruppen und füllt den
    ///     Editor mit den aktuellen Einstellungen der Collection.
    /// </summary>
    private void SelectTemplateMod(string modDirectory, string modName)
    {
        _tplModDir = modDirectory;
        _tplModName = modName;
        _tplEditingId = null;
        _tplName = string.Empty;
        _tplGroups = _penumbra.GetAvailableModSettings(modDirectory);
        LoadCurrentSettingsIntoEditor();
    }

    /// <summary>
    ///     Füllt <see cref="_tplEdit"/> mit den aktuellen Collection-
    ///     Einstellungen des gewählten Mods; fehlt der Mod in der
    ///     Collection, mit Standardwerten (Single: erste Option, Multi:
    ///     leer).
    /// </summary>
    private void LoadCurrentSettingsIntoEditor()
    {
        if (_tplModDir is null)
            return;

        ModSettingsSnapshot? current = _tplCollection is { } col
            ? _penumbra.TryGetCurrentModSettings(col.Id, _tplModDir)
            : null;

        _tplDefaultsAssumed = current is null;
        _tplPriority = current?.Priority ?? 0;
        _tplEdit = BuildEditState(current?.Settings);
    }

    /// <summary>
    ///     Normalisiert einen Settings-Satz auf die bekannten Gruppen des
    ///     Mods: unbekannte Gruppen fliegen raus, fehlende bekommen
    ///     Defaults, Single-Select wird auf genau eine Option gekappt.
    /// </summary>
    private Dictionary<string, List<string>> BuildEditState(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? source)
    {
        var edit = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (group, def) in _tplGroups)
        {
            var chosen = source is not null && source.TryGetValue(group, out var v)
                ? v.Where(def.Options.Contains).ToList()
                : new List<string>();

            if (def.SingleSelect)
            {
                if (chosen.Count == 0 && def.Options.Count > 0)
                    chosen.Add(def.Options[0]);
                else if (chosen.Count > 1)
                    chosen = new List<string> { chosen[0] };
            }

            edit[group] = chosen;
        }
        return edit;
    }

    // -------------------------------------------------------------------
    //  Editor
    // -------------------------------------------------------------------

    private void DrawTemplateEditor()
    {
        if (!ImGui.CollapsingHeader(Strings.TemplateEditorHeader, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (_tplEditingId is { } editingId && _templates.Find(editingId) is { } editing)
        {
            ImGui.TextDisabled(Strings.TemplateEditing(editing.Name));
            ImGui.SameLine();
            if (ImGui.SmallButton(Strings.TemplateCancelEdit))
            {
                _tplEditingId = null;
                _tplName = string.Empty;
                LoadCurrentSettingsIntoEditor();
            }
        }

        if (ImGui.SmallButton(Strings.TemplateLoadCurrent))
            LoadCurrentSettingsIntoEditor();
        if (_tplDefaultsAssumed)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.TemplateDefaultsAssumed);
        }

        if (_tplGroups.Count == 0)
        {
            ImGui.TextDisabled(Strings.TemplateNoGroups);
        }
        else
        {
            // Gruppen in stabiler Reihenfolge (Name), damit der Editor
            // nicht bei jedem Frame springt.
            foreach (var (group, def) in _tplGroups.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!_tplEdit.TryGetValue(group, out var chosen))
                {
                    chosen = new List<string>();
                    _tplEdit[group] = chosen;
                }

                ImGui.PushID(group);
                ImGui.TextUnformatted(group);
                ImGui.Indent();

                if (def.SingleSelect)
                {
                    // Combo statt Radio-Reihe: Single-Gruppen können
                    // dutzende Optionen haben (Farb-Varianten).
                    var currentOpt = chosen.Count > 0 ? chosen[0] : string.Empty;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##single", currentOpt))
                    {
                        foreach (var opt in def.Options)
                        {
                            if (ImGui.Selectable(opt, string.Equals(opt, currentOpt, StringComparison.Ordinal)))
                            {
                                chosen.Clear();
                                chosen.Add(opt);
                            }
                        }
                        ImGui.EndCombo();
                    }
                }
                else
                {
                    foreach (var opt in def.Options)
                    {
                        var on = chosen.Contains(opt);
                        if (ImGui.Checkbox(opt, ref on))
                        {
                            if (on) chosen.Add(opt);
                            else chosen.Remove(opt);
                        }
                    }
                }

                ImGui.Unindent();
                ImGui.PopID();
            }
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt(Strings.TemplatePriority, ref _tplPriority);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(
            "##tpl-name",
            ref _tplName,
            128,
            ImGuiInputTextFlags.None,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
        DrawInputHintOverlay(_tplName, Strings.TemplateNameLabel);

        var nameOk = !string.IsNullOrWhiteSpace(_tplName);
        if (!nameOk) ImGui.BeginDisabled();

        if (_tplEditingId is { } id && _templates.Find(id) is { } existing)
        {
            if (ImGui.Button(Strings.TemplateUpdate))
            {
                WriteEditorInto(existing);
                _templates.Update(existing);
                SetTemplateStatus(Strings.TemplateSaved(existing.Name), isError: false);
            }
            ImGui.SameLine();
        }

        if (ImGui.Button(Strings.TemplateSaveNew))
        {
            var tpl = new ModTemplate();
            WriteEditorInto(tpl);
            _templates.Add(tpl);
            _tplEditingId = tpl.Id;
            SetTemplateStatus(Strings.TemplateSaved(tpl.Name), isError: false);
        }

        if (!nameOk) ImGui.EndDisabled();
        if (!nameOk)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.TemplateNameRequired);
        }

        ImGui.Spacing();
    }

    /// <summary>Überträgt den Editor-Zustand in eine Vorlage (Kopie der Listen).</summary>
    private void WriteEditorInto(ModTemplate tpl)
    {
        tpl.Name = _tplName.Trim();
        tpl.ModDirectory = _tplModDir ?? string.Empty;
        tpl.ModName = _tplModName;
        tpl.Priority = _tplPriority;
        // Listen kopieren, damit spätere Editor-Änderungen nicht in die
        // gespeicherte Vorlage durchschlagen, bevor „Aktualisieren" gedrückt wird.
        tpl.Settings = _tplEdit.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList(),
            StringComparer.Ordinal);
    }

    // -------------------------------------------------------------------
    //  Galerie
    // -------------------------------------------------------------------

    private void DrawTemplateGallery()
    {
        if (!ImGui.CollapsingHeader(Strings.TemplateGalleryHeader, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Checkbox(Strings.TemplateOnlySelectedMod, ref _tplOnlySelectedMod);
        ImGui.SameLine();
        ImGui.Checkbox(Strings.TemplateEnableOnApply, ref _tplEnableOnApply);

        DrawTemplateImportRow();

        var all = _templates.Templates;
        if (all.Count == 0)
        {
            ImGui.TextDisabled(Strings.TemplateGalleryEmpty);
            return;
        }

        var installed = new HashSet<string>(_tplMods.Select(m => m.Key), StringComparer.Ordinal);
        var ctrl = ImGui.GetIO().KeyCtrl;

        // Restliche Höhe minus Platz für die Status-Zeile.
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() * 2;
        if (ImGui.BeginChild("##tpl-gallery", new Vector2(0, MathF.Max(height, 80)), border: true))
        {
            // Der Store hält die Liste sortiert (Mod, dann Name) — die
            // Gruppierung folgt dieser Reihenfolge ohne erneutes Sortieren.
            foreach (var modGroup in all.GroupBy(t => t.ModDirectory, StringComparer.Ordinal))
            {
                if (_tplOnlySelectedMod && !string.Equals(modGroup.Key, _tplModDir, StringComparison.Ordinal))
                    continue;

                var isInstalled = installed.Contains(modGroup.Key);
                var header = modGroup.First().ModName;
                if (!isInstalled)
                    header += $" {Strings.TemplateNotInstalled}";

                ImGui.PushID(modGroup.Key);
                var open = ImGui.TreeNodeEx(header, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowItemOverlap);
                // „Alle teilen" rechts in der Kopfzeile — ein Code mit
                // allen Vorlagen dieses Mods.
                ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX()
                               - ImGui.CalcTextSize(Strings.TemplateShareAll).X
                               - ImGui.GetStyle().FramePadding.X * 2);
                if (ImGui.SmallButton(Strings.TemplateShareAll))
                    ShareTemplates(modGroup.ToList());
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Strings.TemplateShareAllHint);

                if (open)
                {
                    foreach (var tpl in modGroup)
                        DrawTemplateRow(tpl, isInstalled, ctrl);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void DrawTemplateRow(ModTemplate tpl, bool isInstalled, bool ctrl)
    {
        ImGui.PushID(tpl.Id.ToString());

        var canApply = isInstalled && _tplCollection is not null;
        if (!canApply) ImGui.BeginDisabled();
        if (ImGui.Button(Strings.TemplateApply))
            ApplyTemplate(tpl);
        if (!canApply) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!isInstalled) ImGui.BeginDisabled();
        if (ImGui.Button(Strings.TemplateEdit))
            BeginEditTemplate(tpl);
        if (!isInstalled) ImGui.EndDisabled();

        ImGui.SameLine();
        // Löschen nur mit gehaltener Strg-Taste — kein Modal nötig, aber
        // ein versehentlicher Klick tut nichts.
        if (!ctrl) ImGui.BeginDisabled();
        if (ImGui.Button(Strings.TemplateDelete))
        {
            _templates.Remove(tpl.Id);
            if (_tplEditingId == tpl.Id)
                _tplEditingId = null;
            SetTemplateStatus(Strings.TemplateDeleted(tpl.Name), isError: false);
        }
        if (!ctrl) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(Strings.TemplateDeleteHint);

        ImGui.SameLine();
        if (ImGui.Button(Strings.TemplateShareCode))
            ShareTemplates(new List<ModTemplate> { tpl });
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Strings.TemplateShareCodeHint);

        ImGui.SameLine();
        ImGui.TextUnformatted(tpl.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(Strings.TemplateSummary(tpl.Settings.Count, tpl.Priority));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(DescribeTemplate(tpl));

        ImGui.PopID();
    }

    /// <summary>Tooltip-Text: alle Gruppen mit gewählten Optionen.</summary>
    private static string DescribeTemplate(ModTemplate tpl)
    {
        if (tpl.Settings.Count == 0)
            return tpl.ModDirectory;

        return string.Join("\n", tpl.Settings
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}: {(kvp.Value.Count == 0 ? "—" : string.Join(", ", kvp.Value))}"));
    }

    /// <summary>Lädt eine Vorlage in den Editor (inkl. Mod-Wechsel).</summary>
    private void BeginEditTemplate(ModTemplate tpl)
    {
        if (!string.Equals(_tplModDir, tpl.ModDirectory, StringComparison.Ordinal))
        {
            _tplModDir = tpl.ModDirectory;
            _tplModName = tpl.ModName;
            _tplGroups = _penumbra.GetAvailableModSettings(tpl.ModDirectory);
        }

        _tplEditingId = tpl.Id;
        _tplName = tpl.Name;
        _tplPriority = tpl.Priority;
        _tplDefaultsAssumed = false;

        var asReadOnly = tpl.Settings.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value,
            StringComparer.Ordinal);
        _tplEdit = BuildEditState(asReadOnly);
    }

    private void ApplyTemplate(ModTemplate tpl)
    {
        // Collection frisch holen — der User kann sie zwischenzeitlich in
        // Penumbra gewechselt haben, und ein Schreibzugriff in die
        // falsche Collection wäre ärgerlicher als ein IPC-Call mehr.
        var target = _objectTable.LocalPlayer;
        _tplCollection = target is null ? null : _penumbra.GetCollectionForObject(target.ObjectIndex);
        if (_tplCollection is not { } col)
        {
            SetTemplateStatus(Strings.TemplateNoCollection, isError: true);
            return;
        }

        var result = _templateApplier.Apply(tpl, col.Id, _tplEnableOnApply);
        SetTemplateStatus(result, isError: false);

        // Editor nachziehen, wenn derselbe Mod offen ist — so sieht der
        // User sofort den neuen Ist-Zustand.
        if (string.Equals(_tplModDir, tpl.ModDirectory, StringComparison.Ordinal) && _tplEditingId is null)
            LoadCurrentSettingsIntoEditor();
    }

    private void SetTemplateStatus(string text, bool isError)
    {
        _tplStatus = text;
        _tplStatusIsError = isError;
    }

    // -------------------------------------------------------------------
    //  Teilen (Share-Code)
    // -------------------------------------------------------------------

    /// <summary>
    ///     Kodiert Vorlagen zu einem Share-Code und legt ihn in die
    ///     Zwischenablage. Die Zeichenzahl wird angezeigt, weil Chat-
    ///     Clients Nachrichtenlimits haben (Discord: 2000).
    /// </summary>
    private void ShareTemplates(List<ModTemplate> templates)
    {
        if (templates.Count == 0)
            return;

        try
        {
            var code = ModTemplateCodec.Encode(templates);
            ImGui.SetClipboardText(code);
            var status = templates.Count == 1
                ? Strings.TemplateShareCopied(templates[0].Name, code.Length)
                : Strings.TemplateShareCopiedMany(templates.Count, templates[0].ModName, code.Length);
            SetTemplateStatus(status, isError: false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Share-Code konnte nicht erzeugt werden.");
            SetTemplateStatus(Strings.ImportError(ex.Message), isError: true);
        }
    }

    /// <summary>
    ///     Eingabezeile für den Import: Textfeld + „Importieren" +
    ///     „Aus Zwischenablage" (liest direkt, ohne Einfügen ins Feld).
    /// </summary>
    private void DrawTemplateImportRow()
    {
        var importW = ImGui.CalcTextSize(Strings.TemplateImportButton).X + ImGui.GetStyle().FramePadding.X * 2;
        var clipW = ImGui.CalcTextSize(Strings.TemplateImportClipboard).X + ImGui.GetStyle().FramePadding.X * 2;
        var fieldW = ImGui.GetContentRegionAvail().X - importW - clipW - ImGui.GetStyle().ItemSpacing.X * 2;

        ImGui.SetNextItemWidth(MathF.Max(fieldW, 100f));
        // 16 KiB reichen für Codes mit dutzenden Vorlagen; Codes darüber
        // wären ohnehin nicht mehr chat-tauglich.
        ImGui.InputText(
            "##tpl-import-code",
            ref _tplImportCode,
            16384,
            ImGuiInputTextFlags.None,
            callback: (ImGui.ImGuiInputTextCallbackDelegate?)null);
        DrawInputHintOverlay(_tplImportCode, Strings.TemplateImportHint);

        ImGui.SameLine();
        var hasCode = !string.IsNullOrWhiteSpace(_tplImportCode);
        if (!hasCode) ImGui.BeginDisabled();
        if (ImGui.Button(Strings.TemplateImportButton))
        {
            if (ImportTemplateCode(_tplImportCode))
                _tplImportCode = string.Empty;
        }
        if (!hasCode) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Strings.TemplateImportClipboard))
            ImportTemplateCode(ImGui.GetClipboardText());
    }

    /// <summary>
    ///     Dekodiert einen Share-Code und übernimmt die Vorlagen in den
    ///     Store. Nicht installierte Mods werden trotzdem importiert —
    ///     die Galerie markiert sie, Anwenden ist bis zur Installation
    ///     deaktiviert.
    /// </summary>
    /// <returns><c>true</c> bei Erfolg.</returns>
    private bool ImportTemplateCode(string? code)
    {
        if (!ModTemplateCodec.TryDecode(code, out var templates, out var error))
        {
            _log.Debug("[GlamourDocumenter] Share-Code-Import abgelehnt: {Error}", error);
            SetTemplateStatus(error, isError: true);
            return false;
        }

        var count = _templates.Import(templates);
        var installed = new HashSet<string>(_tplMods.Select(m => m.Key), StringComparer.Ordinal);
        var modNames = string.Join(", ", templates.Select(t => t.ModName).Distinct(StringComparer.Ordinal));
        var status = Strings.TemplateImported(count, modNames);
        if (templates.Any(t => !installed.Contains(t.ModDirectory)))
            status += Strings.TemplateImportNotInstalledNote;

        SetTemplateStatus(status, isError: false);
        return true;
    }
}
