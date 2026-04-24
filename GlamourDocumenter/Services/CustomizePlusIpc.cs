// ==========================================================================
//  Services/CustomizePlusIpc.cs
//
//  Wrapper um die String-basierten IPC-Endpoints von Customize+.
//  Customize+ veröffentlicht keine NuGet-Bindings, daher wird direkt
//  gegen Dalamuds <c>ICallGateSubscriber</c> gesprochen.
//
//  Endpoint-Namen sind Konstanten (CLAUDE.md §6.4). Bei Upstream-Renames
//  MUSS diese Datei in einem Commit mit Konstante + Signatur angepasst
//  werden; ein Release von Customize+ darf nicht stumm durchrutschen.
// ==========================================================================

using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Services;

/// <summary>
///     IPC-Wrapper für Customize+. Alle Aufrufe sind defensiv: Fehlende
///     Installation oder Major-Version-Mismatch führt zu leeren
///     Ergebnissen, niemals zu Exceptions im Aufrufer.
/// </summary>
/// <remarks>
///     Upstream: https://github.com/Aether-Tools/CustomizePlus. Der
///     Standard-Rückgabe-Typ ist <c>(int ec, T? data)</c> mit
///     <c>ec == 0</c> = Success (CLAUDE.md §6.4).
/// </remarks>
public sealed class CustomizePlusIpc : IDisposable
{
    /// <summary>Geforderte Major-Version der Customize+-API. Siehe CLAUDE.md §2.</summary>
    private const int RequiredMajor = 6;

    // Endpoint-Konstanten gemäß CLAUDE.md §6.4.
    private const string EpGetApiVersion = "CustomizePlus.General.GetApiVersion";
    private const string EpGetProfileList = "CustomizePlus.Profile.GetList";
    private const string EpGetActiveProfileIdOnCharacter = "CustomizePlus.Profile.GetActiveProfileIdOnCharacter";
    private const string EpGetProfileByUniqueId = "CustomizePlus.Profile.GetByUniqueId";

    private readonly IPluginLog _log;

    private readonly ICallGateSubscriber<(int, int)> _getApiVersion;
    private readonly ICallGateSubscriber<IList<IPCProfileDataTuple>> _getProfileList;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _getActiveProfileIdOnCharacter;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _getProfileByUniqueId;

    public CustomizePlusIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;

        _getApiVersion = pluginInterface.GetIpcSubscriber<(int, int)>(EpGetApiVersion);
        _getProfileList = pluginInterface.GetIpcSubscriber<IList<IPCProfileDataTuple>>(EpGetProfileList);
        _getActiveProfileIdOnCharacter = pluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>(EpGetActiveProfileIdOnCharacter);
        _getProfileByUniqueId = pluginInterface.GetIpcSubscriber<Guid, (int, string?)>(EpGetProfileByUniqueId);
    }

    /// <summary>Prüft Verfügbarkeit und Major-Version.</summary>
    public bool IsAvailable()
    {
        try
        {
            var (major, _) = _getApiVersion.InvokeFunc();
            return major == RequiredMajor;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[GlamourDocumenter] Customize+.GetApiVersion nicht erreichbar.");
            return false;
        }
    }

    /// <summary>
    ///     Liefert die Liste aller Customize+-Profile (Kurzform).
    /// </summary>
    public IReadOnlyList<CustomizePlusProfileSummary> GetProfileList()
    {
        if (!IsAvailable())
            return Array.Empty<CustomizePlusProfileSummary>();

        try
        {
            var raw = _getProfileList.InvokeFunc();
            if (raw is null)
                return Array.Empty<CustomizePlusProfileSummary>();

            var result = new List<CustomizePlusProfileSummary>(raw.Count);
            foreach (var entry in raw)
            {
                result.Add(new CustomizePlusProfileSummary
                {
                    UniqueId = entry.UniqueId.ToString(),
                    Name = entry.Name,
                    IsEnabled = entry.IsEnabled,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Customize+.GetProfileList fehlgeschlagen.");
            return Array.Empty<CustomizePlusProfileSummary>();
        }
    }

    /// <summary>
    ///     Liefert das aktive Profil eines Spiel-Objekts als UUID.
    /// </summary>
    public Guid? GetActiveProfileIdOnCharacter(ushort objectIndex)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var (ec, id) = _getActiveProfileIdOnCharacter.InvokeFunc(objectIndex);
            if (ec != 0)
            {
                _log.Debug(
                    "[GlamourDocumenter] Customize+.GetActiveProfileIdOnCharacter ec={Ec}.",
                    ec);
                return null;
            }

            return id;
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "[GlamourDocumenter] Customize+.GetActiveProfileIdOnCharacter fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Holt das komplette Profil-Template (natives JSON) für eine
    ///     UniqueId.
    /// </summary>
    public string? GetProfileTemplate(Guid uniqueId)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var (ec, json) = _getProfileByUniqueId.InvokeFunc(uniqueId);
            if (ec != 0 || string.IsNullOrEmpty(json))
            {
                _log.Debug(
                    "[GlamourDocumenter] Customize+.GetProfileByUniqueId ec={Ec}, leer={Leer}.",
                    ec, string.IsNullOrEmpty(json));
                return null;
            }

            return json;
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "[GlamourDocumenter] Customize+.GetProfileByUniqueId fehlgeschlagen (id={Id}).",
                uniqueId);
            return null;
        }
    }

    public void Dispose()
    {
        // CallGateSubscriber hat kein explizites Dispose.
    }

    // ---------------------------------------------------------------------
    //  Lokale DTO-Form für das „GetList"-Rückgabe-Tuple. Customize+ gibt
    //  ein Array von (Guid, string, bool)-Tuples zurück; wir wrappen das
    //  in einen Record, damit der Aufruf typisiert bleibt.
    //
    //  WICHTIG: Dieser Typ muss mit der exakten Tuple-Signatur des
    //  Upstream-IPC übereinstimmen. Bei Änderungen: CLAUDE.md §6.4 folgen.
    // ---------------------------------------------------------------------

    /// <summary>
    ///     Repräsentiert einen Eintrag aus <c>CustomizePlus.Profile.GetList</c>.
    /// </summary>
    public readonly record struct IPCProfileDataTuple(
        Guid UniqueId,
        string Name,
        bool IsEnabled);
}
