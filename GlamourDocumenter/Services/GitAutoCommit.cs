// ==========================================================================
//  Services/GitAutoCommit.cs
//
//  Führt nach einem Export optional einen Git-Commit im Export-Ordner
//  aus. Benutzt ausschließlich `git.exe` aus dem PATH — keine
//  libgit2sharp-Dependency, damit das Plugin schlank bleibt (CLAUDE.md
//  §2).
//
//  Alle Git-Aufrufe laufen mit kurzen Timeouts und ignorieren Fehler
//  still: wer Git nicht installiert hat oder den Ordner nicht als Repo
//  haben will, bekommt trotzdem einen funktionierenden Export. Wir
//  loggen nur.
// ==========================================================================

using System;
using System.Diagnostics;
using System.IO;
using Dalamud.Plugin.Services;

namespace GlamourDocumenter.Services;

/// <summary>
///     Minimal-Wrapper um <c>git.exe</c>. Stateless — der Aufrufer
///     entscheidet pro Call, in welchem Ordner gearbeitet wird.
/// </summary>
public sealed class GitAutoCommit
{
    private readonly IPluginLog _log;

    public GitAutoCommit(IPluginLog log)
    {
        _log = log;
    }

    /// <summary>
    ///     Fügt alle Änderungen zum Index und committed sie. Initialisiert
    ///     das Repo, falls <c>.git</c> nicht existiert.
    /// </summary>
    /// <param name="workingDir">Absoluter Pfad des Export-Ordners.</param>
    /// <param name="message">Commit-Message.</param>
    public void CommitAll(string workingDir, string message)
    {
        try
        {
            if (!Directory.Exists(workingDir))
                return;

            // Init-Schritt nur bei erstem Aufruf. Ohne Option `-q` wäre
            // die Konsolen-Ausgabe laut, das git.exe-Default ignorieren
            // wir ohnehin.
            if (!Directory.Exists(Path.Combine(workingDir, ".git")))
            {
                RunGit(workingDir, "init -q");
                RunGit(workingDir, "config user.email \"glamdoc@local\"");
                RunGit(workingDir, "config user.name \"Glamour Documenter\"");
            }

            RunGit(workingDir, "add -A");
            // `|| true`-Äquivalent gibt's in PS nicht; wir akzeptieren
            // einen Exit-Code != 0 als „nichts zu commiten" und loggen
            // das nur auf Debug.
            RunGit(workingDir, $"commit -q -m \"{EscapeQuotes(message)}\"");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[GlamourDocumenter] Git-AutoCommit fehlgeschlagen.");
        }
    }

    private void RunGit(string workingDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi);
        if (p is null)
        {
            _log.Debug("[GlamourDocumenter] Git-Prozess konnte nicht gestartet werden: {Args}", args);
            return;
        }

        // 5 Sekunden sollten für einen lokalen git-Call mehr als genug
        // sein; hängt er länger, killen wir.
        if (!p.WaitForExit(5_000))
        {
            _log.Warning("[GlamourDocumenter] Git-Timeout bei: {Args}", args);
            try { p.Kill(); } catch { /* swallow */ }
        }

        if (p.ExitCode != 0)
        {
            _log.Debug("[GlamourDocumenter] git {Args} → ExitCode {Code}", args, p.ExitCode);
        }
    }

    private static string EscapeQuotes(string s) => s.Replace("\"", "'");
}
