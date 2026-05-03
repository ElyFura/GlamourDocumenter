// ==========================================================================
//  Services/LuminaResolver.cs
//
//  Kapselt Lumina-Excel-Zugriffe für Namens-Auflösung: Item-IDs →
//  Item-Namen, Stain-IDs → Farbnamen, Race/Tribe-IDs → Volks-Namen.
//
//  Fehlschläge (unbekannte ID, Sheet nicht ladbar) liefern `null`; der
//  Aufrufer entscheidet über Fallback-Strings.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GlamourDocumenter.Services;

/// <summary>
///     Zentrale Namens-Auflösung für Export-Rendering. Lumina-Sheets
///     werden einmal beim Konstruktor gegriffen und danach schreibgeschützt
///     genutzt — deshalb reicht ein einzelner Resolver pro Plugin-Lebens-
///     dauer.
/// </summary>
/// <remarks>
///     Upstream: Lumina-Excel ist Teil der Dalamud-Dev-Lib. Sheet-Typen
///     liegen in <c>Lumina.Excel.Sheets</c>.
/// </remarks>
public sealed class LuminaResolver : IDisposable
{
    /// <summary>
    ///     WIC-Container-GUID für PNG (<c>GUID_ContainerFormatPng</c>).
    ///     Dalamuds <see cref="ITextureReadbackProvider.SaveToStreamAsync"/>
    ///     erwartet die Standard-WIC-GUIDs.
    /// </summary>
    private static readonly Guid PngContainerFormat =
        new("1B7CFAF4-713F-473C-BBCD-6137425FAEAF");

    /// <summary>
    ///     Timeout für einen einzelnen Icon-Rent + PNG-Encode. Beim ersten
    ///     Zugriff lädt Dalamud die Textur von Disk/GPU; danach ist alles
    ///     im Cache und ein paar Millisekunden schnell.
    /// </summary>
    /// <remarks>
    ///     Großzügig dimensioniert, weil Dalamuds GPU-Readback-Queue bei
    ///     parallelen Anfragen serialisiert: ein Icon, das hinter 20
    ///     anderen wartet, hat von "Token-Start" bis "tatsächliche
    ///     Verarbeitung" merkbare Latenz. Die effektive Drosselung
    ///     übernimmt <see cref="IconFetchConcurrency"/>.
    /// </remarks>
    private static readonly TimeSpan IconFetchTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Maximale parallele Icon-Fetches. Dalamuds Texture-Pipeline
    ///     serialisiert intern; mehr als eine Handvoll gleichzeitiger
    ///     SaveToStreamAsync-Calls bringen keinen Durchsatz, sondern
    ///     verursachen nur Cancellation-Stürme.
    /// </summary>
    private const int IconFetchConcurrency = 4;

    /// <summary>
    ///     Drosselt parallele <see cref="FetchIconDataUriUncached"/>-Aufrufe
    ///     auf <see cref="IconFetchConcurrency"/>. Prozessweit, weil der
    ///     Engpass die Dalamud-Texture-Pipeline ist, nicht unser Code.
    /// </summary>
    private static readonly SemaphoreSlim IconFetchGate = new(IconFetchConcurrency, IconFetchConcurrency);

    private readonly IPluginLog _log;
    private readonly IDataManager _dataManager;
    private readonly ITextureProvider _textureProvider;
    private readonly ITextureReadbackProvider _readback;

    /// <summary>
    ///     Cache pro Icon-ID. Mehrere Equipment-Slots zeigen oft dasselbe
    ///     „Nothing"-Icon (ID 0) oder gleiche Accessoire-Icons — ein Icon
    ///     wird pro Plugin-Lebensdauer nur einmal encodet.
    /// </summary>
    private readonly Dictionary<uint, string?> _iconDataUriCache = new();

    public LuminaResolver(
        IDataManager dataManager,
        ITextureProvider textureProvider,
        ITextureReadbackProvider readback,
        IPluginLog log)
    {
        _dataManager = dataManager;
        _textureProvider = textureProvider;
        _readback = readback;
        _log = log;
    }

    /// <summary>
    ///     Item-Name zur Item-ID. IDs ≥ 1_000_000 sind High-Quality-
    ///     Varianten (ID − 1_000_000 = Base-Item). ≥ 500_000 sind Glamour-
    ///     Modifikatoren — die werden auf das Base-Item zurückgerechnet.
    /// </summary>
    /// <returns>„Nothing" bei 0, Item-Name sonst, <c>null</c> bei unbekannter ID.</returns>
    public string? GetItemName(ulong itemId)
    {
        if (itemId == 0 || IsNothingSentinel(itemId))
            return "Nothing";

        try
        {
            // High-Quality-Flag: IDs > 1_000_000 sind HQ (ID − 1_000_000).
            // Glamourer legt Custom-Glamour-Items in einen eigenen Raum
            // (> 500_000, < 1_000_000) — dort gibt's kein Lumina-Mapping,
            // also null zurückgeben und den Aufrufer das Fallback entscheiden
            // lassen.
            var lookupId = itemId;
            if (lookupId is > 1_000_000 and < 2_000_000)
                lookupId -= 1_000_000;
            else if (lookupId is >= 500_000 and <= 1_000_000)
                return null;

            var sheet = _dataManager.GetExcelSheet<Item>();
            if (!sheet.TryGetRow((uint)lookupId, out var row))
                return null;

            var name = row.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Item-Lookup fehlgeschlagen (id={Id}).", itemId);
            return null;
        }
    }

    /// <summary>
    ///     Name einer Stain (Dye). 0 = keine Färbung → <c>null</c>, damit
    ///     das UI den Slot leer lässt.
    /// </summary>
    public string? GetStainName(byte stainId)
    {
        if (stainId == 0)
            return null;

        try
        {
            var sheet = _dataManager.GetExcelSheet<Stain>();
            if (!sheet.TryGetRow(stainId, out var row))
                return null;

            var name = row.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Stain-Lookup fehlgeschlagen (id={Id}).", stainId);
            return null;
        }
    }

    /// <summary>
    ///     RGB-Hex-Code einer Stain (ohne führendes <c>#</c>), z. B.
    ///     <c>"7F3C1A"</c>. <c>null</c> wenn Stain unbekannt oder ID 0.
    /// </summary>
    /// <remarks>
    ///     <c>Stain.Color</c> ist als <c>0x00RRGGBB</c>-UInt32 kodiert.
    ///     Alpha-Byte ignorieren wir; für unsere Swatch-Darstellung
    ///     reicht RGB.
    /// </remarks>
    public string? GetStainHex(byte stainId)
    {
        if (stainId == 0)
            return null;

        try
        {
            var sheet = _dataManager.GetExcelSheet<Stain>();
            if (!sheet.TryGetRow(stainId, out var row))
                return null;

            var c = row.Color;
            var r = (c >> 16) & 0xFF;
            var g = (c >> 8) & 0xFF;
            var b = c & 0xFF;
            return $"{r:X2}{g:X2}{b:X2}";
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Stain-Color-Lookup fehlgeschlagen (id={Id}).", stainId);
            return null;
        }
    }

    /// <summary>
    ///     Race-Name via Excel-Row. FFXIV-Races haben Masculine/Feminine-
    ///     Varianten; wir geben den generischen Masculine-Namen zurück
    ///     (den zeigt auch das Character-Creator-UI an, wenn kein Gender
    ///     gewählt wurde).
    /// </summary>
    public string? GetRaceName(uint raceId)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<Race>();
            if (!sheet.TryGetRow(raceId, out var row))
                return null;

            var name = row.Masculine.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Race-Lookup fehlgeschlagen (id={Id}).", raceId);
            return null;
        }
    }

    /// <summary>
    ///     Tribe/Clan-Name (Subrace). „Seeker of the Sun", „Wildwood", …
    /// </summary>
    public string? GetTribeName(uint tribeId)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<Tribe>();
            if (!sheet.TryGetRow(tribeId, out var row))
                return null;

            var name = row.Masculine.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Tribe-Lookup fehlgeschlagen (id={Id}).", tribeId);
            return null;
        }
    }

    /// <summary>
    ///     Job-Icon via FFXIV-Konvention <c>62100 + ClassJob-RowId</c>
    ///     (gerahmtes Job-Symbol aus dem UI). Liefert direkt das Data-URI
    ///     oder <c>null</c> bei Lade-Fehler.
    /// </summary>
    public string? GetJobIconDataUri(uint classJobRowId)
    {
        // Row 0 = Adventurer → kein sinnvolles Job-Icon.
        if (classJobRowId == 0)
            return null;

        var iconId = 62100u + classJobRowId;
        return GetItemIconDataUri(iconId);
    }

    /// <summary>
    ///     Bonus-Item-Name (Facewear/Glasses). Das zugehörige Sheet heißt
    ///     in Lumina <c>GlassesStyle</c>; ob das fürs Bonus-System stimmt,
    ///     hängt von der API-Version ab. Bei Sheet-not-found oder ID-Miss
    ///     liefern wir <c>null</c>.
    /// </summary>
    public string? GetBonusItemName(ulong bonusId)
    {
        // Bits 0..15 = eigentliche BonusItemId (Model). Wenn die 0 sind,
        // ist der Slot leer (Glamourer baut die "Nothing"-Variante über
        // CustomItemId(model: 0, variant: 0, slot)).
        var modelId = (uint)(bonusId & 0xFFFF);
        if (modelId == 0)
            return "Nothing";

        try
        {
            var sheet = _dataManager.GetExcelSheet<Glasses>();
            if (sheet.TryGetRow(modelId, out var row))
            {
                var name = row.Name.ExtractText();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Bonus-Item-Lookup fehlgeschlagen (id={Id}).", bonusId);
            return null;
        }
    }

    /// <summary>
    ///     Erkennt Glamourers Sentinel-IDs für leere bzw. Default-Slots.
    /// </summary>
    /// <remarks>
    ///     Glamourer benutzt drei Sentinel-Familien (alle in
    ///     <c>Glamourer/Services/ItemManager.cs</c>):
    ///     <list type="bullet">
    ///         <item>
    ///             <c>NothingId(EquipSlot)   = uint.MaxValue - 128 - slotIndex</c>
    ///             — leerer Equipment-Slot (Hat/Top/Hands/…).
    ///         </item>
    ///         <item>
    ///             <c>SmallclothesId(EquipSlot) = uint.MaxValue - 256 - slotIndex</c>
    ///             — NPC-Standardunterwäsche (Model 9903).
    ///         </item>
    ///         <item>
    ///             <c>NothingId(FullEquipType) = uint.MaxValue - 384 - typeIndex</c>
    ///             — leere Waffe / leerer Offhand (waffentyp-spezifisch).
    ///         </item>
    ///     </list>
    ///     EquipSlot zählt unter 16, FullEquipType ebenso überschaubar
    ///     — wir fangen großzügig die obersten 1024 Werte ab. Damit
    ///     gehören u. a. ID 4294966911 (Offhand-Nothing, Type 0) und
    ///     vergleichbare Waffen-Defaults dazu, die zuvor als "Unknown
    ///     item" gerendert wurden.
    /// </remarks>
    private static bool IsNothingSentinel(ulong itemId)
        => itemId is >= uint.MaxValue - 1024 and <= uint.MaxValue;

    /// <summary>
    ///     Liefert die Icon-ID eines Items. Erlaubt den Consumer, das
    ///     Item-PNG via <see cref="GetItemIconDataUri"/> nachzuziehen.
    ///     <c>null</c> bei unbekannter Item-ID.
    /// </summary>
    public ushort? GetItemIconId(ulong itemId)
    {
        if (itemId == 0 || IsNothingSentinel(itemId))
            return null;

        // Gleiche ID-Normierung wie in GetItemName — HQ zurück auf Base,
        // Glamour-Custom-Range überspringen.
        var lookupId = itemId;
        if (lookupId is > 1_000_000 and < 2_000_000)
            lookupId -= 1_000_000;
        else if (lookupId is >= 500_000 and <= 1_000_000)
            return null;

        try
        {
            var sheet = _dataManager.GetExcelSheet<Item>();
            if (!sheet.TryGetRow((uint)lookupId, out var row))
                return null;

            return row.Icon;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Item-Icon-Lookup fehlgeschlagen (id={Id}).", itemId);
            return null;
        }
    }

    /// <summary>
    ///     Rendert ein Spiel-Icon als Data-URI (PNG, Base64-inline). Wird
    ///     von Exportern genutzt, um Icons in eigenständige HTML-Dateien
    ///     einzubetten ohne externe Ressourcen.
    /// </summary>
    /// <remarks>
    ///     Synchron blockierend: der erste Aufruf pro Icon wartet bis zu
    ///     <see cref="IconFetchTimeout"/> auf Dalamuds Tex-Loader.
    ///     Nachfolgende Aufrufe mit derselben ID kommen aus dem internen
    ///     Cache. Wir rufen das aus dem UI-Thread auf; Dalamud lädt die
    ///     Textur auf einem Worker-Thread, daher kein Deadlock.
    /// </remarks>
    public string? GetItemIconDataUri(uint iconId)
    {
        if (iconId == 0)
            return null;

        if (_iconDataUriCache.TryGetValue(iconId, out var cached))
            return cached;

        var result = FetchIconDataUriUncached(iconId);
        _iconDataUriCache[iconId] = result;
        return result;
    }

    /// <summary>
    ///     Lädt mehrere Icons parallel in den Cache. Nach diesem Call
    ///     sind <see cref="GetItemIconDataUri"/>-Aufrufe für dieselben
    ///     IDs quasi instant.
    /// </summary>
    /// <remarks>
    ///     Wir starten alle Rent+PNG-Encode-Tasks gleichzeitig und
    ///     warten synchron auf den Abschluss. Dalamuds Texture-Loading
    ///     läuft intern auf einem Worker-Thread, daher kein UI-Block
    ///     länger als nötig. Bereits gecachte IDs werden übersprungen.
    /// </remarks>
    public void WarmUpIcons(IEnumerable<uint> iconIds)
    {
        var todo = iconIds
            .Distinct()
            .Where(id => id != 0 && !_iconDataUriCache.ContainsKey(id))
            .ToList();
        if (todo.Count == 0)
            return;

        var tasks = new Task<(uint Id, string? Uri)>[todo.Count];
        for (var i = 0; i < todo.Count; i++)
        {
            var id = todo[i];
            tasks[i] = Task.Run(() => (id, FetchIconDataUriUncached(id)));
        }

        try
        {
            // Gesamt-Timeout skaliert mit Icon-Anzahl: pro Concurrency-Slot
            // im schlimmsten Fall IconFetchTimeout, plus Puffer. Verhindert,
            // dass viele Items (Equipment+Materia+Job) im Cancellation-
            // Sturm enden.
            var batchTimeout = TimeSpan.FromSeconds(
                Math.Min(60, 15 + todo.Count / IconFetchConcurrency * 2));
            Task.WaitAll(tasks, batchTimeout);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[GlamourDocumenter] Icon-Warm-Up partiell fehlgeschlagen.");
        }

        foreach (var t in tasks)
        {
            if (t.IsCompletedSuccessfully)
                _iconDataUriCache[t.Result.Id] = t.Result.Uri;
        }
    }

    /// <summary>
    ///     Low-level Rent+Encode ohne Cache-Lookup. Wird von
    ///     <see cref="WarmUpIcons"/> und <see cref="GetItemIconDataUri"/>
    ///     gemeinsam genutzt.
    /// </summary>
    private string? FetchIconDataUriUncached(uint iconId)
    {
        // Drossel: erst rein in den Concurrency-Slot, *dann* erst den
        // Per-Task-Timeout starten. Sonst läuft das Token schon ab,
        // während wir noch hinter anderen Icons in der Queue stehen,
        // und SaveToStreamAsync wirft OperationCanceledException, ohne
        // dass wir je Compute-Zeit gesehen haben.
        IconFetchGate.Wait();
        try
        {
            var shared = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            // Getrennte CTS für Rent und Save: bei vielen parallelen
            // Warm-Up-Aufrufen kann RentAsync den Großteil der Zeit
            // verbrauchen; ein gemeinsames Token würde SaveToStream
            // dann sofort abbrechen, obwohl die Textur längst da ist.
            using var rentCts = new CancellationTokenSource(IconFetchTimeout);
            var wrap = shared.RentAsync(rentCts.Token).GetAwaiter().GetResult();
            try
            {
                using var saveCts = new CancellationTokenSource(IconFetchTimeout);
                using var ms = new MemoryStream();
                _readback
                    .SaveToStreamAsync(
                        wrap,
                        PngContainerFormat,
                        ms,
                        props: null,
                        leaveWrapOpen: true,
                        leaveStreamOpen: true,
                        saveCts.Token)
                    .GetAwaiter().GetResult();

                return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
            }
            finally
            {
                wrap.Dispose();
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[GlamourDocumenter] Icon-Fetch fehlgeschlagen (iconId={Id}).", iconId);
            return null;
        }
        finally
        {
            IconFetchGate.Release();
        }
    }

    public void Dispose()
    {
        // Cache wächst mit einzelnen ~2 KB-Einträgen bis max. ein paar
        // Dutzend Items — im Plugin-Lebenszyklus unkritisch.
        _iconDataUriCache.Clear();
    }
}
