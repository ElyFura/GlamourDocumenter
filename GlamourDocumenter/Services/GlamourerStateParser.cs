// ==========================================================================
//  Services/GlamourerStateParser.cs
//
//  Übersetzt das JObject, das Glamourer via GetState liefert, in die
//  plugin-agnostischen Records aus Models/ExportData.cs. Alles, was
//  hier nicht sauber gemappt werden kann, landet unverändert im
//  GlamourerExport.StateJson-Feld (das ist der Full-Dump).
//
//  JObject-Schema (Glamourer v1.6.x, DesignBase.JsonSerialize):
//    FileVersion: int
//    Equipment: { Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists,
//                 RFinger, LFinger, MainHand, OffHand: ItemSlot-JObject;
//                 Hat, VieraEars, Visor, Weapon: Meta-Flags }
//      ItemSlot: { ItemId, Stain, Stain2, Crest, Apply,
//                  ApplyStain, ApplyCrest }
//    Bonus: { <BonusItemFlag>: { BonusId, Apply } }
//    Customize: { ModelId, <CustomizeIndex>: { Value, Apply },
//                 Wetness: { Value, Apply } }
//    Parameters: { <CustomizeParameterFlag>: { Value / Percentage / RGB /
//                                              RGBA, Apply } }
//    Materials: { <hex-uint>: <MaterialValueDesign> }
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using GlamourDocumenter.Models;
using Newtonsoft.Json.Linq;

namespace GlamourDocumenter.Services;

/// <summary>
///     Zustandsloser Parser — eine Instanz pro Plugin reicht.
/// </summary>
public sealed class GlamourerStateParser
{
    private readonly LuminaResolver _lumina;
    private readonly IPluginLog _log;

    /// <summary>
    ///     Reihenfolge der Equipment-Slots, wie sie in der Glamourer-UI
    ///     gerendert werden. Gleiche Reihenfolge benutzen wir im Markdown
    ///     für Wiedererkennung.
    /// </summary>
    private static readonly string[] EquipmentSlotOrder =
    [
        "MainHand", "OffHand",
        "Head", "Body", "Hands", "Legs", "Feet",
        "Ears", "Neck", "Wrists", "RFinger", "LFinger",
    ];

    public GlamourerStateParser(LuminaResolver lumina, IPluginLog log)
    {
        _lumina = lumina;
        _log = log;
    }

    /// <summary>
    ///     Wandelt das rohe Glamourer-JObject in die strukturierten
    ///     Export-Records um. Bei Parser-Fehlern wird der jeweilige
    ///     Unter-Block <c>null</c>, der Rest bleibt gültig.
    /// </summary>
    public void Fill(
        JObject state,
        out GlamourerCustomize? customize,
        out IReadOnlyList<GlamourerEquipmentSlot>? equipment,
        out IReadOnlyList<GlamourerBonusItem>? bonus,
        out IReadOnlyList<GlamourerParameter>? parameters,
        out GlamourerMetaFlags? metaFlags,
        out IReadOnlyDictionary<string, string>? materials)
    {
        customize = TryParseCustomize(state["Customize"] as JObject);
        var equipmentObj = state["Equipment"] as JObject;
        equipment = TryParseEquipment(equipmentObj);
        metaFlags = TryParseMetaFlags(equipmentObj);
        bonus = TryParseBonus(state["Bonus"] as JObject);
        parameters = TryParseParameters(state["Parameters"] as JObject);
        materials = TryParseMaterials(state["Materials"] as JObject);
    }

    // ----------------------------------------------------------------- Customize

    /// <summary>
    ///     UI-Label-Mapping für die Standard-CustomizeIndex-Keys. Matcht
    ///     die Beschriftungen aus Glamourers Customize-Panel. Race-
    ///     spezifische Felder (RaceFeatureType/Size) werden erst nach
    ///     dem Parsen der Race per <see cref="RaceFeatureLabel"/> finalisiert.
    /// </summary>
    private static readonly Dictionary<string, string> CustomizeLabels = new(StringComparer.Ordinal)
    {
        ["Race"]              = "Race",
        ["Gender"]            = "Gender",
        ["BodyType"]          = "Body Type",
        ["Height"]            = "Height",
        ["Clan"]              = "Clan",
        ["Face"]              = "Face",
        ["Hairstyle"]         = "Hairstyle",
        ["Highlights"]        = "Enable Highlights",
        ["SkinColor"]         = "Skin Color",
        ["EyeColorRight"]     = "Right Eye",
        ["HairColor"]         = "Hair Color",
        ["HighlightsColor"]   = "Highlights Color",
        ["FacialFeature1"]    = "Facial Feature 1",
        ["FacialFeature2"]    = "Facial Feature 2",
        ["FacialFeature3"]    = "Facial Feature 3",
        ["FacialFeature4"]    = "Facial Feature 4",
        ["FacialFeature5"]    = "Facial Feature 5",
        ["FacialFeature6"]    = "Facial Feature 6",
        ["FacialFeature7"]    = "Facial Feature 7",
        ["LegacyTattoo"]      = "Legacy Tattoo",
        ["TattooColor"]       = "Tattoo Color",
        ["Eyebrows"]          = "Eyebrows",
        ["EyeColorLeft"]      = "Left Eye",
        ["EyeShape"]          = "Eye Shape",
        ["SmallIris"]         = "Small Iris",
        ["Nose"]              = "Nose",
        ["Jaw"]               = "Jaw",
        ["Mouth"]             = "Mouth",
        ["Lipstick"]          = "Enable Lipstick",
        ["LipColor"]          = "Lip Color",
        ["MuscleMass"]        = "Muscle Mass",
        ["BustSize"]          = "Bust Size",
        ["FacePaint"]         = "Face Paint",
        ["FacePaintReversed"] = "Reverse Face Paint",
        ["FacePaintColor"]    = "Face Paint Color",
        // RaceFeatureType/Size werden race-aware unten ersetzt.
    };

    /// <summary>
    ///     Reihenfolge der Felder im Report — orientiert an Glamourers
    ///     Customize-Panel (Identität zuerst, Proportionen, Form, Farben,
    ///     Flags). Unbekannte Keys landen am Ende, alphabetisch.
    /// </summary>
    private static readonly string[] CustomizeFieldOrder =
    [
        "Race", "Gender", "Clan", "BodyType",
        "Height", "RaceFeatureSize", "BustSize", "MuscleMass",
        "Face", "Hairstyle", "RaceFeatureType",
        "FacePaint",
        "FacialFeature1", "FacialFeature2", "FacialFeature3",
        "FacialFeature4", "FacialFeature5", "FacialFeature6",
        "FacialFeature7", "LegacyTattoo",
        "Eyebrows", "EyeShape", "Nose", "Jaw", "Mouth",
        "SkinColor", "TattooColor", "HairColor", "HighlightsColor",
        "EyeColorLeft", "EyeColorRight",
        "LipColor", "FacePaintColor",
        "Highlights", "SmallIris", "Lipstick", "FacePaintReversed",
    ];

    /// <summary>Felder, deren Value als Bool gerendert werden soll.</summary>
    private static readonly HashSet<string> BooleanCustomizeFields = new(StringComparer.Ordinal)
    {
        "Highlights", "LegacyTattoo", "SmallIris", "Lipstick", "FacePaintReversed",
    };

    /// <summary>
    ///     Race-spezifische Labels für RaceFeatureType / RaceFeatureSize.
    ///     Fehlt die Race oder ist sie nicht in der Tabelle, nutzen wir
    ///     generische Labels.
    /// </summary>
    private static (string Type, string Size) RaceFeatureLabel(uint raceId) => raceId switch
    {
        4 => ("Tail Shape", "Tail Length"),   // Miqo'te
        6 => ("Horn Shape", "Horn Size"),     // Au Ra
        8 => ("Ear Shape", "Ear Length"),     // Viera
        _ => ("Race Feature (Shape)", "Race Feature (Size)"),
    };

    private GlamourerCustomize? TryParseCustomize(JObject? node)
    {
        if (node is null)
            return null;

        try
        {
            var modelId = node["ModelId"]?.Value<uint>() ?? 0;

            // Nicht-humanoide Wesen haben statt Einzelfeldern ein
            // Array-Base64. Dann liefern wir nur ModelId und ein Hinweis-
            // Feld. Der komplette Blob ist weiterhin im StateJson.
            if (node["Array"] is { } arrNode)
            {
                return new GlamourerCustomize
                {
                    ModelId = modelId,
                    Fields = new[]
                    {
                        new CustomizeField(
                            "CustomizeArray",
                            arrNode.Value<string>() ?? string.Empty,
                            "(Non-human: komprimierter Array-Base64 — siehe StateJson)",
                            Apply: true),
                    },
                };
            }

            // Race zuerst lesen, damit wir race-abhängige Labels bauen können.
            uint raceId = 0;
            if (node["Race"] is JObject raceObj && raceObj["Value"] is { } raceVal)
                TryGetUInt(raceVal, out raceId);
            var (raceFeatureTypeLabel, raceFeatureSizeLabel) = RaceFeatureLabel(raceId);

            var entries = new Dictionary<string, CustomizeField>(StringComparer.Ordinal);
            bool? wetness = null, wetnessApply = null;

            foreach (var prop in node.Properties())
            {
                if (prop.Name is "ModelId" or "Array")
                    continue;

                if (prop.Name == "Wetness" && prop.Value is JObject wObj)
                {
                    wetness = wObj["Value"]?.Value<bool?>();
                    wetnessApply = wObj["Apply"]?.Value<bool?>();
                    continue;
                }

                if (prop.Value is not JObject obj)
                    continue;

                var rawToken = obj["Value"];
                var apply = obj["Apply"]?.Value<bool>() ?? false;
                var rawValue = rawToken?.ToString(Newtonsoft.Json.Formatting.None) ?? "";

                // Display-Wert: Race/Clan/Gender aus Lumina, Bool-Felder
                // als Ja/Nein, alles andere als Rohwert.
                string display;
                if (BooleanCustomizeFields.Contains(prop.Name))
                {
                    display = rawValue switch
                    {
                        "true" or "1" => "Ja",
                        "false" or "0" => "Nein",
                        _ => rawValue,
                    };
                }
                else
                {
                    display = ResolveCustomizeDisplay(prop.Name, rawToken) ?? rawValue;
                }

                // UI-Label auflösen (race-aware).
                string label = prop.Name switch
                {
                    "RaceFeatureType" => raceFeatureTypeLabel,
                    "RaceFeatureSize" => raceFeatureSizeLabel,
                    _ => CustomizeLabels.TryGetValue(prop.Name, out var l) ? l : prop.Name,
                };

                entries[prop.Name] = new CustomizeField(label, rawValue, display, apply);
            }

            // In UI-Reihenfolge ausgeben, unbekannte am Ende alphabetisch.
            var fields = new List<CustomizeField>(entries.Count);
            foreach (var key in CustomizeFieldOrder)
            {
                if (entries.TryGetValue(key, out var f))
                {
                    fields.Add(f);
                    entries.Remove(key);
                }
            }
            foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                fields.Add(kv.Value);

            return new GlamourerCustomize
            {
                ModelId = modelId,
                Fields = fields,
                Wetness = wetness,
                WetnessApply = wetnessApply,
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Customize-Parse fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Auflösung für Customize-Felder, die wir in Klartext umwandeln
    ///     können (Race/Tribe/Gender). Für alles andere geben wir
    ///     <c>null</c> zurück, und der Aufrufer zeigt den Rohwert.
    /// </summary>
    private string? ResolveCustomizeDisplay(string name, JToken? value)
    {
        if (value is null)
            return null;

        // Integer-Typen einziehen; wenn Value kein Number ist (z. B.
        // ein Apply-Array), schweigend überspringen.
        if (!TryGetUInt(value, out var id))
            return null;

        return name switch
        {
            "Race" => _lumina.GetRaceName(id),
            "Clan" or "Tribe" => _lumina.GetTribeName(id),
            "Gender" => id switch
            {
                0 => "Male",
                1 => "Female",
                _ => null,
            },
            "BodyType" or "ModelType" => id switch
            {
                1 => "Adult",
                3 => "Child",
                4 => "Lalafell/Child-Body",
                _ => null,
            },
            _ => null,
        };
    }

    private static bool TryGetUInt(JToken token, out uint value)
    {
        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<uint>();
            return true;
        }
        if (token.Type == JTokenType.String &&
            uint.TryParse(token.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        value = 0;
        return false;
    }

    // ----------------------------------------------------------------- Equipment

    private IReadOnlyList<GlamourerEquipmentSlot>? TryParseEquipment(JObject? node)
    {
        if (node is null)
            return null;

        try
        {
            // Non-human — nur Array-Blob. Wir geben eine leere Liste zurück,
            // damit der Renderer sauber einen „nur-Blob"-Fall erkennt; der
            // komplette Blob liegt ohnehin im StateJson.
            if (node["Array"] is not null && !EquipmentSlotOrder.Any(s => node[s] is JObject))
                return Array.Empty<GlamourerEquipmentSlot>();

            // Warm-Up: erst alle Icon-IDs einsammeln und parallel vorladen.
            // Das spart bei 10+ Slots einen Faktor N gegenüber sequenziell.
            var iconIds = new List<uint>();
            foreach (var slotName in EquipmentSlotOrder)
            {
                if (node[slotName] is not JObject s) continue;
                var itemId = s["ItemId"]?.Value<ulong>() ?? 0;
                if (_lumina.GetItemIconId(itemId) is { } iid && iid != 0)
                    iconIds.Add(iid);
            }
            if (iconIds.Count > 0)
                _lumina.WarmUpIcons(iconIds);

            var slots = new List<GlamourerEquipmentSlot>();
            foreach (var slotName in EquipmentSlotOrder)
            {
                if (node[slotName] is not JObject slot)
                    continue;

                var itemId = slot["ItemId"]?.Value<ulong>() ?? 0;
                var stain1 = slot["Stain"]?.Value<byte>() ?? 0;
                var stain2 = slot["Stain2"]?.Value<byte>() ?? 0;
                var crest = slot["Crest"]?.Value<bool>() ?? false;
                var apply = slot["Apply"]?.Value<bool>() ?? false;
                var applyStain = slot["ApplyStain"]?.Value<bool>() ?? false;
                var applyCrest = slot["ApplyCrest"]?.Value<bool>() ?? false;

                // Icon-Auflösung: IconId aus Lumina → Data-URI. Beide
                // Schritte können null liefern; HTML-Exporter prüft das.
                string? iconDataUri = null;
                if (_lumina.GetItemIconId(itemId) is { } iconId && iconId != 0)
                    iconDataUri = _lumina.GetItemIconDataUri(iconId);

                slots.Add(new GlamourerEquipmentSlot
                {
                    SlotName = slotName,
                    ItemId = itemId,
                    ItemName = _lumina.GetItemName(itemId) ?? FormatUnknownItem(itemId),
                    Stain1 = stain1,
                    Stain2 = stain2,
                    Stain1Name = _lumina.GetStainName(stain1),
                    Stain2Name = _lumina.GetStainName(stain2),
                    Stain1Hex = _lumina.GetStainHex(stain1),
                    Stain2Hex = _lumina.GetStainHex(stain2),
                    Crest = crest,
                    Apply = apply,
                    ApplyStain = applyStain,
                    ApplyCrest = applyCrest,
                    IconDataUri = iconDataUri,
                });
            }

            return slots;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Equipment-Parse fehlgeschlagen.");
            return null;
        }
    }

    private static string FormatUnknownItem(ulong itemId) =>
        itemId == 0 ? "Nothing" : $"Unknown item (id={itemId})";

    private GlamourerMetaFlags? TryParseMetaFlags(JObject? equipmentNode)
    {
        if (equipmentNode is null)
            return null;

        try
        {
            return new GlamourerMetaFlags
            {
                HatVisible = (equipmentNode["Hat"] as JObject)?["Show"]?.Value<bool?>(),
                HatApply = (equipmentNode["Hat"] as JObject)?["Apply"]?.Value<bool?>(),
                VieraEarsVisible = (equipmentNode["VieraEars"] as JObject)?["Show"]?.Value<bool?>(),
                VieraEarsApply = (equipmentNode["VieraEars"] as JObject)?["Apply"]?.Value<bool?>(),
                VisorToggled = (equipmentNode["Visor"] as JObject)?["IsToggled"]?.Value<bool?>(),
                VisorApply = (equipmentNode["Visor"] as JObject)?["Apply"]?.Value<bool?>(),
                WeaponVisible = (equipmentNode["Weapon"] as JObject)?["Show"]?.Value<bool?>(),
                WeaponApply = (equipmentNode["Weapon"] as JObject)?["Apply"]?.Value<bool?>(),
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Meta-Flag-Parse fehlgeschlagen.");
            return null;
        }
    }

    // ----------------------------------------------------------------- Bonus

    private IReadOnlyList<GlamourerBonusItem>? TryParseBonus(JObject? node)
    {
        if (node is null)
            return null;

        try
        {
            var list = new List<GlamourerBonusItem>();
            foreach (var prop in node.Properties())
            {
                if (prop.Value is not JObject obj)
                    continue;

                // BonusId ist ein gepacktes CustomItemId-ulong (siehe
                // Penumbra.GameData IdTypes.CustomItemId): Bits 0..15 = Model
                // (= eigentliche BonusItemId), Bits 16..23 Variant, Bits
                // 24..31 Slot, Bits 48/49 Custom-/BonusItemFlag. Die
                // Empty-Sentinel BonusItemNothing setzt Model=0 und alle
                // Flag-Bits — der ulong übersteigt UInt32.MaxValue, daher
                // muss hier zwingend ulong gelesen werden.
                var bonusId = obj["BonusId"]?.Value<ulong>() ?? 0UL;
                var apply = obj["Apply"]?.Value<bool>() ?? false;

                list.Add(new GlamourerBonusItem
                {
                    SlotName = prop.Name,
                    BonusId = bonusId,
                    ItemName = _lumina.GetBonusItemName(bonusId) ??
                               $"Unknown (id={bonusId})",
                    Apply = apply,
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Bonus-Parse fehlgeschlagen.");
            return null;
        }
    }

    // ----------------------------------------------------------------- Parameters
    //
    // Glamourer-JSON-Schema (aus DesignBase.SerializeParameters):
    //   RGB   → { Red, Green, Blue, Apply }
    //   RGBA  → { Red, Green, Blue, Alpha, Apply }
    //   Value → { Value, Apply }
    //   Pct   → { Percentage, Apply }
    //
    // Die UI-Bezeichnungen („Skin Color" etc.) übernehmen wir aus
    // Glamourers [Name()]-Attributen auf CustomizeParameterFlag; die
    // Reihenfolge in PreferredOrder entspricht der Panel-Anordnung im
    // Spiel.

    /// <summary>
    ///     Enum-Key (Glamourer) → UI-Label (wie im Spiel sichtbar).
    ///     Unbekannte Keys landen mit ihrem Roh-Namen im Export.
    /// </summary>
    private static readonly Dictionary<string, string> ParameterLabels = new(StringComparer.Ordinal)
    {
        ["SkinDiffuse"]            = "Skin Color",
        ["MuscleTone"]             = "Muscle Tone",
        ["SkinSpecular"]           = "Skin Shine",
        ["LipDiffuse"]             = "Lip Color",
        ["HairDiffuse"]            = "Hair Color",
        ["HairSpecular"]           = "Hair Shine",
        ["HairHighlight"]          = "Hair Highlights",
        ["LeftEye"]                = "Left Eye Color",
        ["RightEye"]               = "Right Eye Color",
        ["FeatureColor"]           = "Feature Color",
        ["FacePaintUvMultiplier"]  = "Multiplier for Face Paint",
        ["FacePaintUvOffset"]      = "Offset of Face Paint",
        ["DecalColor"]             = "Face Paint Color",
        ["LeftLimbalIntensity"]    = "Left Limbal Ring Intensity",
        ["RightLimbalIntensity"]   = "Right Limbal Ring Intensity",
    };

    /// <summary>
    ///     UI-Reihenfolge der Advanced-Customization-Parameter, exakt wie
    ///     Glamourer sie im Panel darstellt (Farben oben, Skalare unten).
    /// </summary>
    private static readonly string[] ParameterOrder =
    [
        "SkinDiffuse", "HairDiffuse", "HairHighlight",
        "LeftEye", "RightEye", "FeatureColor",
        "LipDiffuse", "DecalColor",
        "MuscleTone",
        "LeftLimbalIntensity", "RightLimbalIntensity",
        "FacePaintUvMultiplier", "FacePaintUvOffset",
        "SkinSpecular", "HairSpecular",
    ];

    private IReadOnlyList<GlamourerParameter>? TryParseParameters(JObject? node)
    {
        if (node is null)
            return null;

        try
        {
            // Erst alle Einträge einsammeln, dann nach ParameterOrder sortieren.
            var entries = new Dictionary<string, (string Value, bool Apply)>(StringComparer.Ordinal);

            foreach (var prop in node.Properties())
            {
                if (prop.Value is not JObject obj)
                    continue;

                var apply = obj["Apply"]?.Value<bool>() ?? false;
                var value = FormatParameterValue(obj);
                entries[prop.Name] = (value, apply);
            }

            var list = new List<GlamourerParameter>(entries.Count);

            // Bekannte Parameter in UI-Reihenfolge.
            foreach (var key in ParameterOrder)
            {
                if (!entries.TryGetValue(key, out var entry))
                    continue;
                list.Add(new GlamourerParameter
                {
                    Name = ParameterLabels.TryGetValue(key, out var label) ? label : key,
                    Value = entry.Value,
                    Apply = entry.Apply,
                });
                entries.Remove(key);
            }

            // Unbekannte Parameter am Ende (alphabetisch, mit Roh-Name).
            foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                list.Add(new GlamourerParameter
                {
                    Name = kv.Key,
                    Value = kv.Value.Value,
                    Apply = kv.Value.Apply,
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Parameters-Parse fehlgeschlagen.");
            return null;
        }
    }

    /// <summary>
    ///     Formatiert einen Parameter-Eintrag abhängig vom Typ. RGB/RGBA
    ///     bekommen einen Hex-Präfix (den der HTML-Exporter zu einem
    ///     Farb-Swatch aufwertet), Prozent bekommen ein `%`, Skalare
    ///     bleiben als Zahl.
    /// </summary>
    private static string FormatParameterValue(JObject obj)
    {
        var red = obj["Red"]?.Value<double?>();
        var green = obj["Green"]?.Value<double?>();
        var blue = obj["Blue"]?.Value<double?>();
        var alpha = obj["Alpha"]?.Value<double?>();
        var percentage = obj["Percentage"]?.Value<double?>();
        var scalar = obj["Value"]?.Value<double?>();

        if (red.HasValue && green.HasValue && blue.HasValue)
        {
            var hex = alpha.HasValue
                ? $"#{ToHex(red.Value)}{ToHex(green.Value)}{ToHex(blue.Value)}{ToHex(alpha.Value)}"
                : $"#{ToHex(red.Value)}{ToHex(green.Value)}{ToHex(blue.Value)}";

            var body = alpha.HasValue
                ? $"R {red.Value:F3}, G {green.Value:F3}, B {blue.Value:F3}, A {alpha.Value:F3}"
                : $"R {red.Value:F3}, G {green.Value:F3}, B {blue.Value:F3}";

            return $"{hex} — {body}";
        }

        if (percentage.HasValue)
            return percentage.Value.ToString("F2", CultureInfo.InvariantCulture) + "%";

        if (scalar.HasValue)
            return scalar.Value.ToString("F6", CultureInfo.InvariantCulture);

        // Unbekannter Typ — Roh-JSON als Fallback, damit keine Info verloren geht.
        return obj.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ToHex(double normalized)
    {
        // Glamourer speichert Farbkanäle als lineare 0..1-Werte. Clampen
        // auf sRGB-Byte-Raum, dann Upper-Case-Hex.
        var clamped = Math.Max(0, Math.Min(1, normalized));
        return ((int)Math.Round(clamped * 255)).ToString("X2");
    }

    // ----------------------------------------------------------------- Materials

    private IReadOnlyDictionary<string, string>? TryParseMaterials(JObject? node)
    {
        if (node is null || !node.Properties().Any())
            return null;

        try
        {
            var dict = new Dictionary<string, string>();
            foreach (var prop in node.Properties())
                dict[prop.Name] = prop.Value.ToString(Newtonsoft.Json.Formatting.None);
            return dict;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Materials-Parse fehlgeschlagen.");
            return null;
        }
    }
}
