// ==========================================================================
//  Exporters/HtmlExporter.cs
//
//  Rendert den Export als in sich geschlossenes HTML-Dokument. Intern
//  wird der MarkdownExporter benutzt und dessen Output zu HTML
//  konvertiert. CLAUDE.md §2 verbietet neue NuGet-Pakete, daher ein
//  kleiner handgeschriebener Markdown-Renderer, der ausschließlich die
//  Konstrukte unterstützt, die MarkdownExporter tatsächlich emittiert:
//
//    - Überschriften (#, ##, ###, ####)
//    - Absätze mit Inline-Formatierung (bold **x**, italic _x_, code `x`)
//    - Listen (- Punkt, zweifach eingerückt für Unterpunkte)
//    - Tabellen (Pipe-Syntax mit Trenner-Zeile)
//    - Fenced Code-Blöcke (```lang ... ```)
//
//  Alles andere wird als Text behandelt und HTML-escaped.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GlamourDocumenter.Models;

namespace GlamourDocumenter.Exporters;

/// <summary>
///     HTML-Export. Erzeugt eine eigenständige Datei mit eingebettetem
///     CSS — keine externen Ressourcen, offline verwendbar.
/// </summary>
/// <remarks>
///     Komposition statt Erweiterung: Das strukturelle Format stammt
///     aus dem <see cref="MarkdownExporter"/>; wir stempeln nur eine
///     andere Hülle drumherum. Sollte sich MarkdownExporter ändern,
///     zieht HtmlExporter automatisch nach, solange die verwendeten
///     Markdown-Konstrukte unterstützt bleiben.
/// </remarks>
public sealed class HtmlExporter : IDocumentExporter
{
    public string FileExtension => ".html";
    public string DisplayName => "HTML";

    private readonly MarkdownExporter _markdown = new();

    public string Render(DocumentationExport export)
    {
        var markdownBody = _markdown.Render(export);
        var htmlBody = MarkdownToHtml.Convert(markdownBody);
        htmlBody = WrapCollapsibleSections(htmlBody);

        var title = $"Glamour Documenter — {WebUtility.HtmlEncode(export.Character.Name)}";
        return BuildDocument(title, htmlBody);
    }

    /// <summary>
    ///     H2-Titel, die im HTML-Output zu eingeklappten
    ///     <c>&lt;details&gt;</c>-Sektionen werden. „Charakter" bleibt
    ///     bewusst draußen — das sind die generellen Infos, die immer
    ///     sichtbar sein sollen.
    /// </summary>
    private static readonly HashSet<string> CollapsibleSections = new()
    {
        "Penumbra",
        "Glamourer",
        "Customize+",
    };

    /// <summary>
    ///     Wandelt jede H2-Sektion mit einem in
    ///     <see cref="CollapsibleSections"/> gelisteten Titel in einen
    ///     <c>&lt;details&gt;</c>-Block um, der standardmäßig zugeklappt
    ///     ist. Die Sektion endet beim nächsten H2 oder am Body-Ende.
    /// </summary>
    /// <remarks>
    ///     Der MarkdownToHtml-Konverter emittiert Heading-Tags ohne
    ///     Attribute und ohne verschachtelte Markup im Title — daher
    ///     reicht eine simple Regex statt eines vollwertigen HTML-Parsers.
    ///     Innerhalb der bekannten Sektionen werden zusätzlich
    ///     Unter-Headings eingeklappt (siehe <see cref="WrapHeadingsAtLevel"/>).
    /// </remarks>
    private static string WrapCollapsibleSections(string html)
    {
        var matches = H2Regex.Matches(html);
        if (matches.Count == 0)
            return html;

        var sb = new StringBuilder(html.Length + 512);
        var lastIndex = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            // Alles vor dem H2 (Header, Summary-Block, Charakter-Tabelle)
            // unverändert übernehmen.
            sb.Append(html, lastIndex, m.Index - lastIndex);

            var rawTitle = m.Groups[1].Value;
            var decodedTitle = WebUtility.HtmlDecode(rawTitle).Trim();

            // Inner-Range: vom Ende dieses H2 bis zum nächsten H2 (oder
            // Body-Ende). Die Inner-Slice schließt nachfolgende Inhalte
            // bis zum nächsten Top-Level-Schnitt ein.
            var innerStart = m.Index + m.Length;
            var innerEnd = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            var inner = html.Substring(innerStart, innerEnd - innerStart);

            if (CollapsibleSections.Contains(decodedTitle))
            {
                // Pro Sektion eigene Nesting-Regel: Penumbra hat einen
                // einzigen H3 ("Mods") und darunter pro Mod ein H4 — wir
                // klappen die H4 ein. Glamourer hat mehrere thematische
                // H3-Blöcke (Customize, Equipment, …) — die alle.
                var processedInner = decodedTitle switch
                {
                    "Penumbra"   => WrapHeadingsAtLevel(inner, 4),
                    "Glamourer"  => WrapHeadingsAtLevel(inner, 3),
                    _            => inner,
                };

                sb.Append("<details class=\"section\"><summary class=\"h2-summary\">")
                  .Append(rawTitle)
                  .AppendLine("</summary>");
                sb.Append(processedInner);
                sb.AppendLine("</details>");
            }
            else
            {
                sb.Append(m.Value);
                sb.Append(inner);
            }

            lastIndex = innerEnd;
        }

        sb.Append(html, lastIndex, html.Length - lastIndex);
        return sb.ToString();
    }

    /// <summary>
    ///     Wickelt jeden Heading der angegebenen Ebene (3 oder 4) im
    ///     gegebenen HTML-Fragment in ein zugeklapptes
    ///     <c>&lt;details&gt;</c>. Eine Sub-Sektion läuft vom Heading bis
    ///     zum nächsten Heading derselben Ebene oder Fragment-Ende.
    /// </summary>
    /// <remarks>
    ///     Inhalte vor dem ersten Heading bleiben unverändert sichtbar
    ///     (z. B. die <c>**Collection:**</c>-Zeile vor "### Mods" in
    ///     Penumbra). Nur die einzelnen Heading-Blöcke werden eingeklappt.
    /// </remarks>
    private static string WrapHeadingsAtLevel(string innerHtml, int level)
    {
        var regex = level == 3 ? H3Regex : H4Regex;
        var matches = regex.Matches(innerHtml);
        if (matches.Count == 0)
            return innerHtml;

        var sb = new StringBuilder(innerHtml.Length + 256);
        var lastIndex = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];

            if (i == 0)
            {
                // Material vor dem ersten Heading bleibt frei sichtbar.
                sb.Append(innerHtml, lastIndex, m.Index - lastIndex);
            }
            else
            {
                // Vorheriges Sub-Details ablassen, dann den Block bis
                // hierhin als dessen Inhalt anhängen.
                sb.Append(innerHtml, lastIndex, m.Index - lastIndex);
                sb.AppendLine("</details>");
            }

            sb.Append("<details class=\"sub-section\"><summary class=\"h")
              .Append(level)
              .Append("-summary\">")
              .Append(m.Groups[1].Value)
              .AppendLine("</summary>");

            lastIndex = m.Index + m.Length;
        }

        // Rest hinter dem letzten Heading ist Inhalt der letzten Sub-Sektion.
        sb.Append(innerHtml, lastIndex, innerHtml.Length - lastIndex);
        sb.AppendLine("</details>");

        return sb.ToString();
    }

    /// <summary>
    ///     Matcht <c>&lt;h2&gt;TITEL&lt;/h2&gt;</c>. MarkdownToHtml setzt
    ///     keine Attribute und keine verschachtelte Markup im Title, daher
    ///     reicht der einfache <c>[^&lt;]</c>-Charakter-Set.
    /// </summary>
    private static readonly Regex H2Regex = new(
        @"<h2>([^<]+)</h2>",
        RegexOptions.Compiled);

    private static readonly Regex H3Regex = new(
        @"<h3>([^<]+)</h3>",
        RegexOptions.Compiled);

    private static readonly Regex H4Regex = new(
        @"<h4>([^<]+)</h4>",
        RegexOptions.Compiled);

    private static string BuildDocument(string title, string body)
    {
        // Zentrales, selbstgenügsames Dark/Light-Theme. Alle Style-Regeln
        // und JS inline, damit die HTML-Datei ohne externe Requests
        // funktioniert und der User sie weiterleiten / archivieren kann,
        // ohne broken assets zu haben.
        var sb = new StringBuilder(capacity: body.Length + 4096);
        sb.AppendLine("<!DOCTYPE html>");
        // data-theme auf <html> — Default „dark", JS kann togglen.
        sb.AppendLine("<html lang=\"de\" data-theme=\"dark\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.Append("<title>").Append(title).AppendLine("</title>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<button id=\"theme-toggle\" type=\"button\" title=\"Theme umschalten\">◐</button>");
        sb.AppendLine("<main>");
        sb.AppendLine(body);
        sb.AppendLine("</main>");
        sb.AppendLine("<script>");
        sb.AppendLine(ThemeToggleJs);
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    // JS ist absichtlich klein gehalten: LocalStorage-Persistenz, DOM-
    // Attribut-Toggle. Nichts anderes soll passieren — das HTML bleibt
    // statisch und auditierbar.
    private const string ThemeToggleJs = """
        (function () {
            const root = document.documentElement;
            const btn = document.getElementById('theme-toggle');
            const stored = localStorage.getItem('gdoc-theme');
            if (stored === 'light' || stored === 'dark') root.setAttribute('data-theme', stored);
            btn.addEventListener('click', function () {
                const next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
                root.setAttribute('data-theme', next);
                localStorage.setItem('gdoc-theme', next);
            });
        })();
        """;

    // CSS absichtlich in Verbatim-String gehalten — damit Code-Reviews
    // nicht an Zeilenumbruch-Escaping scheitern.
    private const string Css = """
        /*
         * Zwei Themes über data-theme-Attribut auf <html>. Standard =
         * dark (zum FFXIV/Glamourer-Look passend). Light steht über
         * das selector-overriding Block unten zur Verfügung.
         */
        :root,
        [data-theme="dark"] {
            color-scheme: dark;
            --bg: #1a1a1f;
            --surface: #232329;
            --surface-alt: #2a2a32;
            --border: #3a3a44;
            --text: #e4e4e8;
            --muted: #9898a0;
            --accent: #d4a373;
            --code-bg: #14141a;
            --code-fg: #d8d8dc;
        }
        [data-theme="light"] {
            color-scheme: light;
            --bg: #faf7f2;
            --surface: #ffffff;
            --surface-alt: #f0ebe3;
            --border: #dcd4c8;
            --text: #1a1a1f;
            --muted: #6c6358;
            --accent: #8a5a30;
            --code-bg: #f4efe6;
            --code-fg: #2a2620;
        }
        #theme-toggle {
            position: fixed;
            top: 0.75rem;
            right: 0.75rem;
            width: 2rem;
            height: 2rem;
            border-radius: 50%;
            border: 1px solid var(--border);
            background: var(--surface);
            color: var(--accent);
            font-size: 1.1rem;
            cursor: pointer;
            z-index: 10;
        }
        #theme-toggle:hover { background: var(--surface-alt); }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            background: var(--bg);
            color: var(--text);
            line-height: 1.6;
        }
        main {
            max-width: 960px;
            margin: 0 auto;
            padding: 2rem 1.5rem 4rem;
        }
        h1, h2, h3, h4 {
            color: var(--accent);
            margin-top: 2rem;
            margin-bottom: 0.75rem;
            font-weight: 600;
        }
        h1 { font-size: 1.85rem; border-bottom: 1px solid var(--border); padding-bottom: 0.35rem; }
        h2 { font-size: 1.45rem; border-bottom: 1px solid var(--border); padding-bottom: 0.25rem; }
        h3 { font-size: 1.2rem; }
        h4 { font-size: 1.05rem; color: var(--text); }
        p { margin: 0.5rem 0; }
        em { color: var(--muted); font-style: italic; }
        strong { color: var(--text); }
        a { color: var(--accent); }
        ul { padding-left: 1.4rem; margin: 0.4rem 0; }
        li { margin: 0.15rem 15px; }
        code {
            background: var(--code-bg);
            color: var(--code-fg);
            padding: 0.1rem 0.35rem;
            border-radius: 3px;
            font-family: "JetBrains Mono", "Cascadia Code", "Consolas", monospace;
            font-size: 0.9em;
        }
        pre {
            background: var(--code-bg);
            padding: 0.9rem 1rem;
            border-radius: 6px;
            overflow-x: auto;
            border: 1px solid var(--border);
            margin: 0.75rem 0;
        }
        pre code {
            background: transparent;
            padding: 0;
            font-size: 0.85em;
            white-space: pre;
        }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 0.75rem 0 1.25rem;
            font-size: 0.93rem;
        }
        th, td {
            border: 1px solid var(--border);
            padding: 0.45rem 0.75rem;
            text-align: left;
            vertical-align: top;
        }
        th {
            background: var(--surface-alt);
            color: var(--accent);
            font-weight: 600;
        }
        tr:nth-child(even) td { background: var(--surface); }
        /*
         * Color-Swatch vor Hex-Codes. Der MarkdownToHtml-Konverter
         * injected diese <span>-Elemente vor jedem Hex-Token in der
         * Advanced-Customization-Tabelle.
         */
        .swatch {
            display: inline-block;
            width: 0.85rem;
            height: 0.85rem;
            border: 1px solid var(--border);
            border-radius: 3px;
            margin-right: 0.35rem;
            vertical-align: -0.1rem;
        }
        /*
         * Item-Icon links neben dem Item-Namen in der Equipment-Tabelle.
         * Grösse an FFXIV-Icon-Proportionen orientiert (24x24 ist ein
         * guter Kompromiss zwischen Erkennbarkeit und Tabellenhöhe).
         */
        .item-icon {
            width: 24px;
            height: 24px;
            vertical-align: middle;
            margin-right: 0.4rem;
            border-radius: 3px;
            background: var(--code-bg);
        }
        /*
         * Einklappbare Hauptsektionen (Penumbra, Glamourer, Customize+).
         * Standardmäßig zugeklappt; nur „Charakter" oben bleibt offen.
         * Summary übernimmt das H2-Look-and-Feel, damit die Optik
         * konsistent zur Markdown-Struktur bleibt.
         */
        details.section {
            margin-top: 2rem;
            border-bottom: 1px solid var(--border);
        }
        details.section[open] {
            border-bottom: none;
        }
        summary.h2-summary {
            list-style: none;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 0.5rem;
            color: var(--accent);
            font-size: 1.45rem;
            font-weight: 600;
            padding-bottom: 0.25rem;
            border-bottom: 1px solid var(--border);
            user-select: none;
        }
        summary.h2-summary::-webkit-details-marker {
            display: none;
        }
        /* Eigener Disclosure-Pfeil — rotiert beim Aufklappen. */
        summary.h2-summary::before {
            content: "▶";
            display: inline-block;
            font-size: 0.8em;
            transition: transform 120ms ease-out;
            color: var(--muted);
        }
        details[open] > summary.h2-summary::before {
            transform: rotate(90deg);
        }
        summary.h2-summary:hover {
            color: var(--text);
        }
        /*
         * Sub-Sektionen: H3 (Glamourer-Unterpunkte) und H4 (Penumbra-Mods).
         * Optisch flacher als H2-Sektionen — kein border-bottom, kleinerer
         * Font, weniger Abstand.
         */
        details.sub-section {
            margin: 0.6rem 0 0.6rem 0.25rem;
        }
        summary.h3-summary,
        summary.h4-summary {
            list-style: none;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 0.45rem;
            font-weight: 600;
            user-select: none;
        }
        summary.h3-summary {
            color: var(--accent);
            font-size: 1.2rem;
            margin-top: 0.4rem;
        }
        /*
         * Penumbra-Mod-Karten (H4-Sub-Sektionen). Jeder Mod-Eintrag ist
         * gerahmt + leicht abgesetzt, damit sich aufgeklappte Mods optisch
         * sauber von der nächsten Karte trennen — vorher ist nur der
         * Pfeil + Mod-Name als Abgrenzung übriggeblieben, was bei
         * mehrzeiligem Inhalt schwer lesbar war.
         */
        details.sub-section:has(> summary.h4-summary) {
            border: 1px solid var(--border);
            border-radius: 4px;
            background: var(--surface);
            margin: 0.5rem 0;
            padding: 0;
        }
        details.sub-section:has(> summary.h4-summary)[open] {
            background: var(--surface-alt);
        }
        summary.h4-summary {
            color: var(--text);
            font-size: 1.05rem;
            padding: 0.45rem 0.7rem;
            border-radius: 4px;
        }
        details.sub-section[open] > summary.h4-summary {
            border-bottom: 1px solid var(--border);
            border-radius: 4px 4px 0 0;
        }
        summary.h4-summary:hover {
            background: var(--surface-alt);
        }
        /*
         * Mod-Inhalt einrücken, damit der Body sichtbar zur Summary-Zeile
         * gehört statt linksbündig wie loser Fließtext zu wirken.
         */
        details.sub-section:has(> summary.h4-summary) > ul,
        details.sub-section:has(> summary.h4-summary) > p {
            padding: 0.5rem 0.85rem;
            margin: 0;
        }
        summary.h3-summary::-webkit-details-marker,
        summary.h4-summary::-webkit-details-marker {
            display: none;
        }
        summary.h3-summary::before,
        summary.h4-summary::before {
            content: "▸";
            display: inline-block;
            font-size: 0.85em;
            color: var(--muted);
            transition: transform 120ms ease-out;
        }
        details[open] > summary.h3-summary::before,
        details[open] > summary.h4-summary::before {
            transform: rotate(90deg);
        }
        summary.h3-summary:hover,
        summary.h4-summary:hover {
            color: var(--text);
        }
        """;
}

/// <summary>
///     Minimaler Markdown→HTML-Konverter. Nur die Konstrukte, die
///     <see cref="MarkdownExporter"/> tatsächlich produziert.
/// </summary>
/// <remarks>
///     State-Machine über Zeilen: Code-Block-Modus, Listen-Tiefe und
///     Tabellen-Erkennung werden direkt im <see cref="Convert"/>-Aufruf
///     verwaltet. Inline-Formatierung (bold / italic / inline code) läuft
///     per Regex auf bereits HTML-escapten Text.
/// </remarks>
internal static class MarkdownToHtml
{
    public static string Convert(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(capacity: markdown.Length * 2);

        var inCodeBlock = false;
        string? codeLang = null;
        var listStack = new Stack<int>(); // eingerückte Listen-Tiefen (Spaces)
        var inParagraph = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Code-Block-Eintritt / -Austritt
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                CloseParagraph(sb, ref inParagraph);
                CloseLists(sb, listStack);

                if (!inCodeBlock)
                {
                    var lang = line.Trim().Substring(3).Trim();
                    codeLang = lang.Length > 0 ? lang : null;
                    inCodeBlock = true;
                    sb.Append("<pre><code");
                    if (codeLang is not null)
                        sb.Append(" class=\"language-").Append(WebUtility.HtmlEncode(codeLang)).Append('"');
                    sb.Append('>');
                }
                else
                {
                    inCodeBlock = false;
                    codeLang = null;
                    sb.AppendLine("</code></pre>");
                }
                continue;
            }

            // Innerhalb eines Code-Blocks: reine HTML-Escape, keine
            // Markdown-Interpretation.
            if (inCodeBlock)
            {
                sb.AppendLine(WebUtility.HtmlEncode(line));
                continue;
            }

            // Leere Zeile schließt Absätze und lose Strukturen.
            if (string.IsNullOrWhiteSpace(line))
            {
                CloseParagraph(sb, ref inParagraph);
                CloseLists(sb, listStack);
                continue;
            }

            // Überschriften.
            var heading = TryParseHeading(line);
            if (heading is { } h)
            {
                CloseParagraph(sb, ref inParagraph);
                CloseLists(sb, listStack);
                sb.Append("<h").Append(h.Level).Append('>')
                  .Append(FormatInline(h.Text))
                  .Append("</h").Append(h.Level).AppendLine(">");
                continue;
            }

            // Tabellen: erkennen, indem wir auf die Trenner-Zeile
            // (zweite Zeile mit `---`) vorausschauen.
            if (line.StartsWith("|", StringComparison.Ordinal) &&
                i + 1 < lines.Length &&
                IsTableSeparator(lines[i + 1]))
            {
                CloseParagraph(sb, ref inParagraph);
                CloseLists(sb, listStack);

                var consumed = RenderTable(sb, lines, i);
                i += consumed - 1; // -1, weil die for-Schleife gleich +1 macht
                continue;
            }

            // Listen: `- ` oder mit Einrückung `  - ` / `    - `.
            var listMatch = ListItemRegex.Match(line);
            if (listMatch.Success)
            {
                CloseParagraph(sb, ref inParagraph);
                var indent = listMatch.Groups[1].Length;
                var itemText = listMatch.Groups[2].Value;

                // Eingerückter als bisher → neue <ul>-Ebene öffnen.
                while (listStack.Count == 0 || indent > listStack.Peek())
                {
                    sb.AppendLine("<ul>");
                    listStack.Push(indent);
                }
                // Weniger eingerückt → Ebenen schließen.
                while (listStack.Count > 0 && indent < listStack.Peek())
                {
                    sb.AppendLine("</ul>");
                    listStack.Pop();
                }

                sb.Append("<li>").Append(FormatInline(itemText)).AppendLine("</li>");
                continue;
            }

            // Alles andere: normaler Absatz-Text. Konsekutive Zeilen
            // werden zu einem <p>, Absätze werden durch Blanks getrennt.
            CloseLists(sb, listStack);
            if (!inParagraph)
            {
                sb.Append("<p>");
                inParagraph = true;
            }
            else
            {
                sb.Append(' ');
            }
            sb.Append(FormatInline(line));
        }

        CloseParagraph(sb, ref inParagraph);
        CloseLists(sb, listStack);

        return sb.ToString();
    }

    private static void CloseParagraph(StringBuilder sb, ref bool inParagraph)
    {
        if (inParagraph)
        {
            sb.AppendLine("</p>");
            inParagraph = false;
        }
    }

    private static void CloseLists(StringBuilder sb, Stack<int> listStack)
    {
        while (listStack.Count > 0)
        {
            sb.AppendLine("</ul>");
            listStack.Pop();
        }
    }

    private static (int Level, string Text)? TryParseHeading(string line)
    {
        var trimmed = line.TrimStart();
        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level is < 1 or > 4)
            return null;
        if (level >= trimmed.Length || trimmed[level] != ' ')
            return null;

        return (level, trimmed[(level + 1)..].TrimEnd());
    }

    private static bool IsTableSeparator(string line)
    {
        // Erwartet `|---|---|` o. Ä. mit optional Alignment (`:---:`).
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
            return false;

        var inner = trimmed.Trim('|');
        foreach (var cell in inner.Split('|'))
        {
            var c = cell.Trim();
            if (c.Length == 0)
                return false;
            foreach (var ch in c)
            {
                if (ch is not ('-' or ':'))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Rendert eine Tabelle und liefert die Anzahl verbrauchter Zeilen.</summary>
    private static int RenderTable(StringBuilder sb, string[] lines, int start)
    {
        var headerCells = ParseTableRow(lines[start]);
        var consumed = 2; // Header + Separator

        sb.AppendLine("<table>");
        sb.Append("<thead><tr>");
        foreach (var cell in headerCells)
            sb.Append("<th>").Append(FormatInline(cell)).Append("</th>");
        sb.AppendLine("</tr></thead>");

        sb.AppendLine("<tbody>");
        for (var i = start + 2; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith('|'))
                break;

            var cells = ParseTableRow(raw);
            sb.Append("<tr>");
            foreach (var cell in cells)
                sb.Append("<td>").Append(FormatInline(cell)).Append("</td>");
            sb.AppendLine("</tr>");
            consumed++;
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
        return consumed;
    }

    private static List<string> ParseTableRow(string line)
    {
        // Pipes, die mit \| escaped sind, müssen wir rein passieren lassen.
        // Strategie: zeichenweise durch, Escape-Sequenz aktiv halten.
        var cells = new List<string>();
        var current = new StringBuilder();
        var trimmed = line.Trim();

        // Führendes und schließendes `|` sind reine Syntax.
        var body = trimmed;
        if (body.StartsWith('|')) body = body[1..];
        if (body.EndsWith('|')) body = body[..^1];

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }
            if (ch == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    // ------------------------------------------------------------- Inline

    // Matches "- Text" oder "  - Text" mit beliebiger gerader Einrücktiefe.
    // Erste Gruppe = Einrückung (Leerzeichen), zweite Gruppe = Text.
    private static readonly Regex ListItemRegex = new(
        @"^(\s*)-\s+(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex InlineCodeRegex = new(
        @"`([^`\n]+?)`",
        RegexOptions.Compiled);

    private static readonly Regex BoldRegex = new(
        @"\*\*([^*\n]+?)\*\*",
        RegexOptions.Compiled);

    // Italic mit Unterstrichen an Wort-Grenzen — verhindert False-Positives
    // in Bezeichnern wie `_exporters`.
    private static readonly Regex ItalicRegex = new(
        @"(?<![\w_])_([^_\n]+?)_(?![\w_])",
        RegexOptions.Compiled);

    // Color-Hex: `#RRGGBB` oder `#RRGGBBAA`, jeweils an Wort-Grenzen.
    // Verhindert False-Positives bei IDs wie „fileVersion=12345".
    private static readonly Regex ColorHexRegex = new(
        @"(?<![\w#])#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b",
        RegexOptions.Compiled);

    // Item-Icon-Marker. Der MarkdownExporter emittiert
    //   <!--gdoc-icon:data:image/png;base64,...-->
    // WebUtility.HtmlEncode verwandelt `<` und `>` in `&lt;`/`&gt;`,
    // daher matcht die Regex die ENCODETE Form. Base64 selbst enthält
    // weder `&` noch `<`, daher ist die Inhalts-Klasse sicher.
    private static readonly Regex IconMarkerRegex = new(
        @"&lt;!--gdoc-icon:([^&]+?)--&gt;",
        RegexOptions.Compiled);

    // Dye-Swatch-Marker. Format: <!--gdoc-swatch:RRGGBB-->.
    // Analog zu IconMarker — Pfad über HTML-encoded form.
    private static readonly Regex SwatchMarkerRegex = new(
        @"&lt;!--gdoc-swatch:([0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)--&gt;",
        RegexOptions.Compiled);

    /// <summary>
    ///     HTML-escape + Inline-Markdown (Code vor Bold vor Italic). Code
    ///     muss zuerst laufen, damit der Inhalt danach nicht mehr als Bold
    ///     oder Italic fehlinterpretiert wird.
    /// </summary>
    private static string FormatInline(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);

        // `code` — zuerst, danach sind die Inhalte geschützt innerhalb
        // von <code>-Tags (die keine weiteren Markdown-Regeln auf sie
        // anwenden würden, weil die Regex in den folgenden Schritten auf
        // die Marker außerhalb der Tags abzielt — aber wir sind
        // trotzdem defensiv).
        escaped = InlineCodeRegex.Replace(escaped, "<code>$1</code>");
        escaped = BoldRegex.Replace(escaped, "<strong>$1</strong>");
        escaped = ItalicRegex.Replace(escaped, "<em>$1</em>");

        // Farb-Swatch vor Hex-Codes. Der Swatch übernimmt den Hex-Wert
        // als CSS-Background. CSS versteht #RRGGBB und #RRGGBBAA nativ.
        escaped = ColorHexRegex.Replace(escaped,
            m => $"<span class=\"swatch\" style=\"background:#{m.Groups[1].Value}\"></span>#{m.Groups[1].Value}");

        // Item-Icon-Marker → <img>. Die Data-URI im Marker ist bereits
        // web-safe (Base64 + statisches Mime-Präfix). Kein zusätzliches
        // Escaping nötig, aber das `alt` bleibt leer, damit Screenreader
        // das Item nicht doppelt lesen (Name steht direkt daneben).
        escaped = IconMarkerRegex.Replace(escaped,
            m => $"<img class=\"item-icon\" src=\"{m.Groups[1].Value}\" alt=\"\">");

        // Dye-Swatch-Marker → <span>. Nutzt dieselbe Swatch-CSS wie
        // die RGB-Farben in Advanced Customization.
        escaped = SwatchMarkerRegex.Replace(escaped,
            m => $"<span class=\"swatch\" style=\"background:#{m.Groups[1].Value}\"></span>");

        return escaped;
    }
}
