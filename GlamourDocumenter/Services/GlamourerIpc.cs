// ==========================================================================
//  Services/GlamourerIpc.cs
//
//  Wrapper um Glamourer.Api.IpcSubscribers. Siehe CLAUDE.md §6.1 für das
//  Base64-Blob-Format und CLAUDE.md §4.1 für das Robustheits-Kontrakt.
//
//  API-Version-Pinning: Major 1. Achtung: die NuGet-Paketversion (2.8.0)
//  ist NICHT die IPC-API-Version. Verifiziert gegen Glamourer v1.6.0.5:
//  GlamourerApi.get_ApiVersion liefert (1, 7). Bei einem Major-Bump
//  des Upstream: Konstante + ggf. Subscriber-Signaturen in einem Commit
//  anpassen (CLAUDE.md §4.2).
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Newtonsoft.Json.Linq;

namespace GlamourDocumenter.Services;

/// <summary>
///     IPC-Wrapper für Glamourer. Liefert den nativen Base64-State und
///     optional dessen Klartext-Dekodierung.
/// </summary>
/// <remarks>
///     Upstream: https://github.com/Ottermandias/Glamourer — API-Typen
///     unter <c>Glamourer.Api.IpcSubscribers</c>.
/// </remarks>
public sealed class GlamourerIpc : IDisposable
{
    /// <summary>Geforderte Major-Version der Glamourer-API. Siehe CLAUDE.md §2.</summary>
    private const int RequiredMajor = 1;

    private readonly IPluginLog _log;

    private readonly ApiVersion _apiVersion;
    private readonly GetStateBase64 _getStateBase64;
    private readonly GetState _getState;
    private readonly GetDesignList _getDesignList;
    private readonly GetDesignBase64 _getDesignBase64;
    private readonly ApplyState _applyState;

    public GlamourerIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;
        _apiVersion = new ApiVersion(pluginInterface);
        _getStateBase64 = new GetStateBase64(pluginInterface);
        _getState = new GetState(pluginInterface);
        _getDesignList = new GetDesignList(pluginInterface);
        _getDesignBase64 = new GetDesignBase64(pluginInterface);
        _applyState = new ApplyState(pluginInterface);
    }

    /// <summary>Prüft die Verfügbarkeit und Major-Version.</summary>
    public bool IsAvailable()
    {
        try
        {
            var (major, _) = _apiVersion.Invoke();
            return major == RequiredMajor;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[GlamourDocumenter] Glamourer.ApiVersion nicht erreichbar.");
            return false;
        }
    }

    /// <summary>
    ///     Holt den kompletten Glamourer-State des lokalen Spielers als
    ///     Base64-Blob. Der Blob ist das native Re-Import-Format — wir
    ///     reichen ihn unverändert durch (CLAUDE.md §6.1).
    /// </summary>
    /// <returns>Base64-String oder <c>null</c> bei Nicht-Verfügbarkeit.</returns>
    public string? GetStateBase64(IPlayerCharacter player)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var (ec, base64) = _getStateBase64.Invoke(player.ObjectIndex);
            if (ec != GlamourerApiEc.Success || string.IsNullOrEmpty(base64))
            {
                _log.Debug(
                    "[GlamourDocumenter] GetStateBase64 ec={Ec}, base64leer={Leer}.",
                    ec, string.IsNullOrEmpty(base64));
                return null;
            }

            return base64;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] GetStateBase64 fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Holt den strukturierten Glamourer-State als JObject (parsiert).
    /// </summary>
    /// <remarks>
    ///     Das ist der bevorzugte Weg für Rendering — der Base64-Blob
    ///     bleibt zusätzlich für Re-Import erhalten. JObject-Schema:
    ///     { FileVersion, Equipment, Bonus, Customize, Parameters,
    ///       Materials }; Details siehe <see cref="GlamourerStateParser"/>.
    /// </remarks>
    public JObject? GetState(IPlayerCharacter player)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var (ec, jobj) = _getState.Invoke(player.ObjectIndex);
            if (ec != GlamourerApiEc.Success || jobj is null)
            {
                _log.Debug(
                    "[GlamourDocumenter] Glamourer.GetState ec={Ec}, jobjNull={Null}.",
                    ec, jobj is null);
                return null;
            }

            return jobj;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Glamourer.GetState fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Versucht, den Base64-Blob zu dekodieren, um eine menschen-
    ///     lesbare JSON-Vorschau zu erzeugen.
    /// </summary>
    /// <remarks>
    ///     Format laut CLAUDE.md §6.1:
    ///     Byte 0 = Format-Version (wird übersprungen).
    ///     Rest = GZip-komprimiertes UTF-8-JSON.
    ///
    ///     Diese Methode ist „best effort": wenn sich das Format ändert,
    ///     liefert sie <c>null</c> und der Export fällt auf den reinen
    ///     Blob zurück. Re-Import in Glamourer funktioniert trotzdem,
    ///     weil Glamourer das Versions-Byte kennt.
    /// </remarks>
    public string? TryDecodeStateBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length < 2)
                return null;

            // Byte 0 ist die Format-Version; der GZip-Stream beginnt bei Byte 1.
            using var memoryStream = new MemoryStream(bytes, 1, bytes.Length - 1, writable: false);
            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            // Kein Warning — Format-Änderungen sind erwartbar, siehe §6.1.
            _log.Debug(ex, "[GlamourDocumenter] TryDecodeStateBase64 fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Liefert alle gespeicherten Glamourer-Designs (GUID → Name).
    /// </summary>
    public IReadOnlyDictionary<Guid, string> GetDesignList()
    {
        if (!IsAvailable())
            return new Dictionary<Guid, string>();

        try
        {
            return _getDesignList.Invoke();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Glamourer.GetDesignList fehlgeschlagen.");
            return new Dictionary<Guid, string>();
        }
    }

    /// <summary>
    ///     Holt den Re-Import-Blob eines einzelnen Designs.
    /// </summary>
    public string? GetDesignBase64(Guid designId)
    {
        if (!IsAvailable())
            return null;

        try
        {
            var result = _getDesignBase64.Invoke(designId);
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "[GlamourDocumenter] Glamourer.GetDesignBase64 fehlgeschlagen (id={Id}).",
                designId);
            return null;
        }
    }

    /// <summary>
    ///     Wendet einen State auf einen Actor an. Nimmt den roh gespeicherten
    ///     State-JSON (JObject-Serialisierung) oder einen Base64-Blob.
    /// </summary>
    /// <remarks>
    ///     Die IpcSubscribers-Invoke-Signatur erwartet ein <c>JObject</c>
    ///     — Base64 wird daher hier dekodiert und geparst.
    /// </remarks>
    public GlamourerApiEc ApplyState(string stateJsonOrBase64, IPlayerCharacter target, ApplyFlag flags)
    {
        if (!IsAvailable())
            return GlamourerApiEc.ActorNotFound;

        try
        {
            JObject jobject;

            // Heuristik: JSON-Objekte starten mit '{', Base64 nicht.
            var trimmed = stateJsonOrBase64.TrimStart();
            if (trimmed.StartsWith('{'))
            {
                jobject = JObject.Parse(stateJsonOrBase64);
            }
            else
            {
                var decoded = TryDecodeStateBase64(stateJsonOrBase64);
                if (decoded is null)
                {
                    _log.Warning("[GlamourDocumenter] ApplyState: Base64 konnte nicht dekodiert werden.");
                    return GlamourerApiEc.ActorNotFound;
                }
                jobject = JObject.Parse(decoded);
            }

            return _applyState.Invoke(jobject, target.ObjectIndex, key: 0, flags);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] ApplyState fehlgeschlagen.");
            return GlamourerApiEc.ActorNotFound;
        }
    }

    public void Dispose()
    {
        // Keine Ressourcen freizugeben.
    }
}
