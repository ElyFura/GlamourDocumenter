// ==========================================================================
//  Services/PenumbraIpc.cs
//
//  Wrapper um Penumbra.Api.IpcSubscribers. Kapselt alle IPC-Aufrufe mit
//  Verfügbarkeits-Check, try/catch und Logging (CLAUDE.md §4.1 — IPC-
//  Robustheit).
//
//  API-Version-Pinning: CLAUDE.md §4.2. Major = 5.
//
//  WICHTIG — Diskrepanzen zu CLAUDE.md (für Review):
//    - §6.3 erwähnt „GetCollectionEffectiveList". Dieser Endpoint existiert
//      im aktuellen Penumbra.Api 5.13.1 NICHT. Das funktionale Äquivalent
//      ist GetAllModSettings(collection, ignoreInheritance=false, ...).
//    - §6.2 beschreibt ein 5-Tuple (Enabled, Priority, Settings, Inherited,
//      Temporary). Das aktuelle GetCurrentModSettings liefert nur ein
//      4-Tuple (ohne Temporary); die Temporary-Variante heißt
//      GetCurrentModSettingsWithTemp und ist ein eigener Endpoint.
//    - GetCurrentModSettings hat außerdem einen zusätzlichen optionalen
//      modName-Parameter.
// ==========================================================================

using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;

namespace GlamourDocumenter.Services;

/// <summary>
///     IPC-Wrapper für Penumbra. Jede öffentliche Methode ist so gebaut,
///     dass ein deinstalliertes, deaktiviertes oder inkompatibles Penumbra
///     stumm durchrutscht und einen neutralen Fallback zurückgibt.
/// </summary>
/// <remarks>
///     Upstream: https://github.com/xivdev/Penumbra und
///     https://github.com/Ottermandias/Penumbra.Api — die API-Subscriber-
///     Klassen wohnen im Namespace <c>Penumbra.Api.IpcSubscribers</c> und
///     werden mit dem <see cref="IDalamudPluginInterface"/> instanziiert.
/// </remarks>
public sealed class PenumbraIpc : IDisposable
{
    /// <summary>Geforderte Major-Version der Penumbra-API. Siehe CLAUDE.md §4.2.</summary>
    private const int RequiredMajor = 5;

    private readonly IPluginLog _log;

    private readonly ApiVersion _apiVersion;
    private readonly GetModList _getModList;
    private readonly GetCollections _getCollections;
    private readonly GetCollectionForObject _getCollectionForObject;
    private readonly GetAllModSettings _getAllModSettings;
    private readonly GetCurrentModSettings _getCurrentModSettings;
    private readonly TrySetMod _trySetMod;
    private readonly TrySetModPriority _trySetModPriority;
    private readonly TrySetModSettings _trySetModSettings;

    public PenumbraIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;

        _apiVersion = new ApiVersion(pluginInterface);
        _getModList = new GetModList(pluginInterface);
        _getCollections = new GetCollections(pluginInterface);
        _getCollectionForObject = new GetCollectionForObject(pluginInterface);
        _getAllModSettings = new GetAllModSettings(pluginInterface);
        _getCurrentModSettings = new GetCurrentModSettings(pluginInterface);
        _trySetMod = new TrySetMod(pluginInterface);
        _trySetModPriority = new TrySetModPriority(pluginInterface);
        _trySetModSettings = new TrySetModSettings(pluginInterface);
    }

    /// <summary>
    ///     Prüft, ob Penumbra installiert ist und die erwartete Major-API-
    ///     Version spricht. Muss vor jedem IPC-Call aufgerufen werden.
    /// </summary>
    public bool IsAvailable()
    {
        try
        {
            // Upstream benennt das erste Tuple-Element „Breaking" (statt
            // „Major"). Wir vergleichen positional, damit künftige
            // Rename-Runden nicht stillschweigend hier vorbeigehen.
            var version = _apiVersion.Invoke();
            return version.Breaking == RequiredMajor;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[GlamourDocumenter] Penumbra.ApiVersion nicht erreichbar.");
            return false;
        }
    }

    /// <summary>
    ///     Liefert die für ein Spiel-Objekt aktive (effektive) Collection.
    /// </summary>
    /// <returns>(CollectionId, CollectionName) oder <c>null</c>, wenn das
    ///     Objekt keine gültige Identität hat oder IPC fehlschlägt.</returns>
    public (Guid Id, string Name)? GetCollectionForObject(int objectIndex)
    {
        if (!IsAvailable())
            return null;

        try
        {
            // Upstream-Signatur:
            //   (bool ObjectValid, bool IndividualSet,
            //    (Guid Id, string Name) EffectiveCollection)
            // Wir interessieren uns nur für die effektive Collection.
            var result = _getCollectionForObject.Invoke(objectIndex);
            if (!result.ObjectValid)
            {
                _log.Debug(
                    "[GlamourDocumenter] GetCollectionForObject: Objekt {Idx} hat keine gültige Identität.",
                    objectIndex);
                return null;
            }

            return (result.EffectiveCollection.Id, result.EffectiveCollection.Name);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] GetCollectionForObject fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>Liefert alle installierten Collections (ID → Name).</summary>
    public IReadOnlyDictionary<Guid, string> GetCollections()
    {
        if (!IsAvailable())
            return new Dictionary<Guid, string>();

        try
        {
            return _getCollections.Invoke();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] GetCollections fehlgeschlagen.");
            return new Dictionary<Guid, string>();
        }
    }

    /// <summary>
    ///     Liefert globale Mod-Liste (installierte Mods, ohne Bezug zu
    ///     einer Collection). Verzeichnisname → Anzeigename.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetModList()
    {
        if (!IsAvailable())
            return new Dictionary<string, string>();

        try
        {
            return _getModList.Invoke();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] GetModList fehlgeschlagen.");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    ///     Liefert alle wirksamen Mod-Settings einer Collection (inkl.
    ///     vererbter, exklusive Temporary-Settings).
    /// </summary>
    /// <remarks>
    ///     Das ist der funktionale Ersatz für das in CLAUDE.md §6.3
    ///     beschriebene „EffectiveList". Upstream-Signatur:
    ///     <code>
    ///     (PenumbraApiEc ec,
    ///      Dictionary&lt;string, (bool enabled, int priority,
    ///                            Dictionary&lt;string, List&lt;string&gt;&gt; settings,
    ///                            bool inherited, bool temporary)&gt;? data)
    ///     Invoke(Guid collectionId, bool ignoreInheritance = false,
    ///            bool ignoreTemporary = false, int key = 0)
    ///     </code>
    ///     Wir rufen mit <c>ignoreInheritance=false</c> und
    ///     <c>ignoreTemporary=true</c>: Temporary-Settings interessieren
    ///     für die Doku nicht (CLAUDE.md §6.2, letzter Satz).
    /// </remarks>
    public IReadOnlyDictionary<string, ModSettingsSnapshot> GetAllModSettings(Guid collectionId)
    {
        if (!IsAvailable())
            return new Dictionary<string, ModSettingsSnapshot>();

        try
        {
            var (ec, data) = _getAllModSettings.Invoke(
                collectionId,
                ignoreInheritance: false,
                ignoreTemporary: true,
                key: 0);

            if (ec != PenumbraApiEc.Success || data is null)
            {
                _log.Debug(
                    "[GlamourDocumenter] GetAllModSettings ec={Ec}, collection={Collection}.",
                    ec, collectionId);
                return new Dictionary<string, ModSettingsSnapshot>();
            }

            var result = new Dictionary<string, ModSettingsSnapshot>(data.Count);
            foreach (var kvp in data)
            {
                var (enabled, priority, settings, inherited, _temporary) = kvp.Value;

                // Settings-Dictionary in read-only-Form kopieren, damit
                // Consumer sich nicht darauf verlassen müssen, dass die
                // Upstream-Collection stabil bleibt.
                var frozenSettings = new Dictionary<string, IReadOnlyList<string>>(settings.Count);
                foreach (var setting in settings)
                    frozenSettings[setting.Key] = setting.Value;

                result[kvp.Key] = new ModSettingsSnapshot(
                    enabled, priority, frozenSettings, inherited);
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] GetAllModSettings fehlgeschlagen.");
            return new Dictionary<string, ModSettingsSnapshot>();
        }
    }

    /// <summary>
    ///     Liefert die Settings eines einzelnen Mods in einer Collection.
    /// </summary>
    /// <remarks>
    ///     CLAUDE.md §6.2 beschreibt ein 5-Tuple (inkl. Temporary). Das
    ///     aktuelle GetCurrentModSettings liefert nur 4 Elemente — das
    ///     <c>Temporary</c>-Flag ist in <see cref="GetCurrentModSettingsWithTemp"/>
    ///     ausgelagert. Hier wird nur die 4-Tuple-Variante genutzt, weil
    ///     Temporary-Settings für die Doku verworfen werden (§6.2).
    ///
    ///     Wird für den Haupt-Export aktuell nicht benutzt — <see cref="GetAllModSettings"/>
    ///     ist effizienter. Die Methode bleibt erhalten, falls jemand
    ///     gezielt die Settings eines einzelnen Mods abfragen will
    ///     (Roadmap: Re-Import-Flow, CLAUDE.md §10).
    /// </remarks>
    public ModSettingsSnapshot? TryGetCurrentModSettings(
        Guid collectionId, string modDirectory)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var (ec, payload) = _getCurrentModSettings.Invoke(
                collectionId, modDirectory, modName: string.Empty, ignoreInheritance: false);

            if (payload is null)
            {
                _log.Debug(
                    "[GlamourDocumenter] GetCurrentModSettings: ec={Ec}, mod={Mod} nicht in Collection.",
                    ec, modDirectory);
                return null;
            }

            var (enabled, priority, settings, inherited) = payload.Value;

            var frozenSettings = new Dictionary<string, IReadOnlyList<string>>(settings.Count);
            foreach (var setting in settings)
                frozenSettings[setting.Key] = setting.Value;

            return new ModSettingsSnapshot(enabled, priority, frozenSettings, inherited);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "[GlamourDocumenter] GetCurrentModSettings fehlgeschlagen (mod={Mod}).",
                modDirectory);
            return null;
        }
    }

    // ---------------------------------------------------------------------
    //  Setter-Wrapper (für Re-Import)
    // ---------------------------------------------------------------------

    /// <summary>Aktiviert / deaktiviert einen Mod in einer Collection.</summary>
    public PenumbraApiEc SetModEnabled(Guid collectionId, string modDirectory, bool enabled)
    {
        if (!IsAvailable())
            return PenumbraApiEc.ModMissing;
        try
        {
            return _trySetMod.Invoke(collectionId, modDirectory, enabled, modName: string.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] TrySetMod fehlgeschlagen (mod={Mod}).", modDirectory);
            return PenumbraApiEc.ModMissing;
        }
    }

    public PenumbraApiEc SetModPriority(Guid collectionId, string modDirectory, int priority)
    {
        if (!IsAvailable())
            return PenumbraApiEc.ModMissing;
        try
        {
            return _trySetModPriority.Invoke(collectionId, modDirectory, priority, modName: string.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] TrySetModPriority fehlgeschlagen (mod={Mod}).", modDirectory);
            return PenumbraApiEc.ModMissing;
        }
    }

    /// <summary>
    ///     Setzt die Options einer Option-Gruppe. Bei Single-Select
    ///     sollte <paramref name="options"/> genau einen Eintrag
    ///     enthalten; Multi-Select akzeptiert beliebig viele.
    /// </summary>
    public PenumbraApiEc SetModSettings(
        Guid collectionId, string modDirectory, string optionGroup, IReadOnlyList<string> options)
    {
        if (!IsAvailable())
            return PenumbraApiEc.ModMissing;
        try
        {
            // TrySetModSettings-Parameter-Reihenfolge in Penumbra.Api 5.13
            // (Invoke-Signatur): collection, modDirectory, optionGroup,
            // options, modName. Wir übergeben positional um sicher zu gehen.
            return _trySetModSettings.Invoke(
                collectionId, modDirectory, optionGroup, options, string.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "[GlamourDocumenter] TrySetModSettings fehlgeschlagen (mod={Mod}, group={Group}).",
                modDirectory, optionGroup);
            return PenumbraApiEc.ModMissing;
        }
    }

    /// <summary>
    ///     Platzhalter. Aktuell keine Subscriptions, keine unmanaged
    ///     Ressourcen. Dennoch implementiert (CLAUDE.md §4.6), damit
    ///     spätere Event-Subscriber hier sauber ihren Cleanup-Punkt finden.
    /// </summary>
    public void Dispose()
    {
        // Keine Ressourcen freizugeben.
    }
}

/// <summary>
///     Schnappschuss der wirksamen Mod-Settings eines einzelnen Mods in
///     einer Collection. Temporary-Flag wird bewusst verworfen (CLAUDE.md
///     §6.2).
/// </summary>
public readonly record struct ModSettingsSnapshot(
    bool Enabled,
    int Priority,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Settings,
    bool Inherited);
