// ==========================================================================
//  Services/DocumentationCollector.cs
//
//  Orchestriert das Einsammeln aller Daten aus den drei IPC-Services und
//  baut einen <see cref="DocumentationExport"/>-Record zusammen.
//
//  Thread-Kontext: Der Collector wird aus dem UI-Thread aufgerufen. Er
//  liest genau ein <c>IPlayerCharacter</c>-Snapshot (LocalPlayer oder
//  via ObjectTable ausgewählt) und arbeitet sofort fertig — das ist
//  laut CLAUDE.md §4.3 zulässig, solange sich an dieser Annahme nichts
//  ändert. Wer Event-Subscriber oder async-Arbeit einbaut, muss auf
//  den Framework-Thread marshallen.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Models;
using Newtonsoft.Json;

namespace GlamourDocumenter.Services;

/// <summary>
///     Zusammenführung der drei IPC-Quellen in einen Export-Record.
/// </summary>
public sealed class DocumentationCollector : IDisposable
{
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;
    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizePlusIpc _customizePlus;
    private readonly GlamourerStateParser _glamourerParser;
    private readonly LuminaResolver _lumina;

    public DocumentationCollector(
        IObjectTable objectTable,
        IPluginLog log,
        PenumbraIpc penumbra,
        GlamourerIpc glamourer,
        CustomizePlusIpc customizePlus,
        GlamourerStateParser glamourerParser,
        LuminaResolver lumina)
    {
        _objectTable = objectTable;
        _log = log;
        _penumbra = penumbra;
        _glamourer = glamourer;
        _customizePlus = customizePlus;
        _glamourerParser = glamourerParser;
        _lumina = lumina;
    }

    /// <summary>
    ///     Sammelt einen Export für den lokalen Spieler. Bequemlichkeits-
    ///     Wrapper mit Defaults.
    /// </summary>
    public DocumentationExport? Collect()
    {
        var player = _objectTable.LocalPlayer;
        if (player is null)
        {
            _log.Debug("[GlamourDocumenter] Kein LocalPlayer — Export abgebrochen.");
            return null;
        }
        return Collect(player);
    }

    /// <summary>
    ///     Sammelt einen Export für den explizit angegebenen Charakter.
    /// </summary>
    /// <param name="player">Ziel-Charakter.</param>
    /// <param name="onlyNonDefaultMods">
    ///     Filtert Mods mit reinen Default-Settings aus.
    /// </param>
    /// <param name="includeDesigns">
    ///     Wenn <c>true</c>, werden alle gespeicherten Glamourer-Designs
    ///     mit Re-Import-Blob an den Export angehängt. Kann bei grossen
    ///     Design-Pools den Report deutlich aufblähen.
    /// </param>
    public DocumentationExport Collect(
        IPlayerCharacter player,
        bool onlyNonDefaultMods = false,
        bool includeDesigns = false)
    {
        return new DocumentationExport
        {
            ExportedAt = DateTimeOffset.Now,
            PluginVersion = GetPluginVersion(),
            Character = BuildCharacterInfo(player),
            Penumbra = CollectPenumbra(player, onlyNonDefaultMods),
            Glamourer = CollectGlamourer(player, includeDesigns),
            CustomizePlus = CollectCustomizePlus(player),
        };
    }

    /// <summary>
    ///     Liest die Assembly-Version des Plugins aus. Siehe CLAUDE.md
    ///     §12 — <c>PluginVersion</c> wird bewusst aus der Assembly
    ///     gezogen statt manuell gepflegt.
    /// </summary>
    private static string GetPluginVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString() ?? "0.0.0.0";
    }

    private CharacterInfo BuildCharacterInfo(IPlayerCharacter player)
    {
        // HomeWorld via Excel-Row (CLAUDE.md §6.5). .ToString() würde
        // Debug-Payload liefern, daher ExtractText() auf der SeString.
        var worldName = player.HomeWorld.Value.Name.ExtractText();

        // Classjob-Name analog über Excel. Auch hier: ExtractText statt ToString.
        var jobName = player.ClassJob.Value.Name.ExtractText();
        var jobId = player.ClassJob.RowId;

        return new CharacterInfo
        {
            Name = player.Name.TextValue,
            HomeWorld = worldName,
            Job = jobName,
            Level = player.Level,
            JobIconDataUri = _lumina.GetJobIconDataUri(jobId),
        };
    }

    // ---------------------------------------------------------------------
    //  Penumbra
    // ---------------------------------------------------------------------

    private PenumbraExport? CollectPenumbra(IPlayerCharacter player, bool onlyNonDefaultMods = false)
    {
        if (!_penumbra.IsAvailable())
            return null;

        var collection = _penumbra.GetCollectionForObject(player.ObjectIndex);
        if (collection is null)
            return null;

        var (collectionId, collectionName) = collection.Value;

        // Ein einziger IPC-Call für alle wirksamen Mod-Settings der
        // Collection. Das Ergebnis enthält vererbte Einträge, aber keine
        // Temporary-Settings (siehe PenumbraIpc.GetAllModSettings-Doku).
        var settings = _penumbra.GetAllModSettings(collectionId);
        var modList = _penumbra.GetModList();

        var mods = new List<PenumbraModEntry>(settings.Count);
        foreach (var kvp in settings)
        {
            var snapshot = kvp.Value;

            // Nur aktive Mods in den Report aufnehmen. Deaktivierte
            // Einträge blähen den Bericht auf und sind für Character-
            // Dokumentation ohne Mehrwert — wer sie braucht, kann den
            // Penumbra-Backup-Export benutzen.
            if (!snapshot.Enabled)
                continue;

            // Optional: nur Mods mit vom Default abweichenden Settings.
            // Heuristik: leeres Settings-Dict = reine Group-Defaults.
            if (onlyNonDefaultMods && snapshot.Settings.Count == 0)
                continue;

            var modDirectory = kvp.Key;

            // Anzeigename aus der globalen Mod-List nachschlagen; Fallback
            // auf den Ordnernamen, wenn unbekannt (z. B. kurz zuvor
            // entfernter Mod).
            var modName = modList.TryGetValue(modDirectory, out var name) ? name : modDirectory;

            mods.Add(new PenumbraModEntry
            {
                ModDirectory = modDirectory,
                ModName = modName,
                Enabled = snapshot.Enabled,
                Priority = snapshot.Priority,
                Inherited = snapshot.Inherited,
                Settings = snapshot.Settings,
            });
        }

        return new PenumbraExport
        {
            CollectionName = collectionName,
            CollectionId = collectionId.ToString(),
            Mods = mods,
        };
    }

    // ---------------------------------------------------------------------
    //  Glamourer
    // ---------------------------------------------------------------------

    private GlamourerExport? CollectGlamourer(IPlayerCharacter player, bool includeDesigns = false)
    {
        // Base64-Blob für Re-Import holen. Ohne den ist der Export nur
        // Doku, aber nicht re-importierbar — wir brechen aber nicht ab,
        // falls nur der Blob-Endpoint zickt.
        var base64 = _glamourer.GetStateBase64(player);

        // Strukturierten State für Rendering holen.
        var state = _glamourer.GetState(player);

        if (base64 is null && state is null)
            return null;

        GlamourerCustomize? customize = null;
        IReadOnlyList<GlamourerEquipmentSlot>? equipment = null;
        IReadOnlyList<GlamourerBonusItem>? bonus = null;
        IReadOnlyList<GlamourerParameter>? parameters = null;
        GlamourerMetaFlags? metaFlags = null;
        IReadOnlyDictionary<string, string>? materials = null;
        string? stateJson = null;

        if (state is not null)
        {
            _glamourerParser.Fill(
                state,
                out customize, out equipment, out bonus,
                out parameters, out metaFlags, out materials);

            stateJson = state.ToString(Formatting.Indented);
        }

        // Designs als Backup mit-exportieren. Opt-in, weil pro Design
        // ein Base64-Blob den Report deutlich aufbläht und für die
        // Standard-Dokumentation irrelevant ist.
        var designs = includeDesigns ? CollectGlamourerDesigns() : null;

        return new GlamourerExport
        {
            StateBase64 = base64 ?? string.Empty,
            StateJson = stateJson,
            Customize = customize,
            Equipment = equipment,
            Bonus = bonus,
            Parameters = parameters,
            MetaFlags = metaFlags,
            Materials = materials,
            Designs = designs,
        };
    }

    private IReadOnlyList<GlamourerDesign>? CollectGlamourerDesigns()
    {
        if (!_glamourer.IsAvailable())
            return null;

        var list = _glamourer.GetDesignList();
        if (list.Count == 0)
            return null;

        var result = new List<GlamourerDesign>(list.Count);
        foreach (var kvp in list)
        {
            var blob = _glamourer.GetDesignBase64(kvp.Key);
            if (blob is null)
                continue;
            result.Add(new GlamourerDesign
            {
                Id = kvp.Key.ToString(),
                Name = kvp.Value,
                Base64 = blob,
            });
        }
        return result;
    }

    // ---------------------------------------------------------------------
    //  Customize+
    // ---------------------------------------------------------------------

    private CustomizePlusExport? CollectCustomizePlus(IPlayerCharacter player)
    {
        if (!_customizePlus.IsAvailable())
            return null;

        var all = _customizePlus.GetProfileList();

        CustomizePlusProfile? active = null;
        var activeId = _customizePlus.GetActiveProfileIdOnCharacter((ushort)player.ObjectIndex);
        if (activeId.HasValue)
        {
            var template = _customizePlus.GetProfileTemplate(activeId.Value);
            if (template is not null)
            {
                // Namen aus der Liste nachschlagen — der Detail-Call
                // liefert nur das Template-JSON, nicht die Meta-Felder.
                string name = activeId.Value.ToString();
                foreach (var summary in all)
                {
                    if (summary.UniqueId == activeId.Value.ToString())
                    {
                        name = summary.Name;
                        break;
                    }
                }

                active = new CustomizePlusProfile
                {
                    UniqueId = activeId.Value.ToString(),
                    Name = name,
                    Template = template,
                };
            }
        }

        return new CustomizePlusExport
        {
            ActiveProfile = active,
            AllProfiles = all,
        };
    }

    public void Dispose()
    {
        // Keine eigenen Ressourcen; die IPC-Services disposen sich selbst
        // über das Plugin-Root.
    }
}
