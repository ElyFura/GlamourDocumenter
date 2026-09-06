// ==========================================================================
//  Models/ModTemplate.cs
//
//  Plugin-agnostische Datenschicht für die Vorlagen-Galerie: gespeicherte
//  Optionssätze pro Penumbra-Mod, zwischen denen der User direkt
//  umschalten kann. Siehe CLAUDE.md §3 — keine Referenzen auf
//  Penumbra.Api oder Dalamud.
//
//  Bewusst mutable Klassen statt Records: Vorlagen werden in der Galerie
//  umbenannt und aktualisiert; der Store schreibt das Objekt danach
//  unverändert zurück auf Platte.
// ==========================================================================

using System;
using System.Collections.Generic;

namespace GlamourDocumenter.Models;

/// <summary>
///     Persistenz-Container der Vorlagen-Galerie (eine Datei pro
///     Plugin-Installation).
/// </summary>
/// <remarks>
///     <see cref="FileVersion"/> ist von Anfang an dabei, weil die Datei
///     — anders als der Export — vom Plugin selbst wieder eingelesen
///     wird. Ein Breaking-Change am Schema muss hier hochzählen und in
///     <c>ModTemplateStore</c> migriert werden.
/// </remarks>
public sealed class ModTemplateLibrary
{
    /// <summary>Aktuelle Schema-Version der Datei.</summary>
    public const int CurrentFileVersion = 1;

    public int FileVersion { get; set; } = CurrentFileVersion;

    public List<ModTemplate> Templates { get; set; } = new();
}

/// <summary>
///     Ein gespeicherter Optionssatz für genau einen Mod.
/// </summary>
public sealed class ModTemplate
{
    /// <summary>Stabile ID — Anzeigename und Mod dürfen sich ändern.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Vom User vergebener Anzeigename der Vorlage.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Ordner-Name des Mods (Penumbra-interne ID). Einziger
    ///     eindeutiger Schlüssel; <see cref="ModName"/> ist nur Anzeige.
    /// </summary>
    public string ModDirectory { get; set; } = string.Empty;

    /// <summary>Anzeigename des Mods zum Zeitpunkt des Speicherns.</summary>
    public string ModName { get; set; } = string.Empty;

    /// <summary>Priorität, die beim Anwenden gesetzt wird.</summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Gewählte Optionen pro Option-Gruppe. Key = Gruppenname,
    ///     Value = gewählte Optionen (Single-Select: genau ein Eintrag).
    /// </summary>
    public Dictionary<string, List<string>> Settings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
