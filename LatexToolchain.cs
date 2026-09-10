using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeViewer.Latex;

/// <summary>TeX engine used to turn a .tex source into a PDF.</summary>
public enum LatexEngine { PdfLatex, XeLatex, LuaLatex }

public enum LatexCompileStatus { Succeeded, Failed, TimedOut, NoToolchain }

/// <summary>Result of one <see cref="LatexToolchain.CompileAsync"/> run.</summary>
public sealed record LatexCompileOutcome(
    LatexCompileStatus Status,
    string? Tool,
    string? PdfPath,
    string Output,
    string Message)
{
    /// <summary>A single line fit for a status bar; <see cref="Message"/> may span several.</summary>
    public string StatusLine => Message.IndexOf('\n') is var i && i >= 0 ? Message[..i] : Message;
}

/// <summary>One resolved way of building a document: an executable plus its arguments.</summary>
public sealed record LatexInvocation(string Name, string ExecutablePath, string[] Arguments, int MaxPasses);

/// <summary>
/// Locates a TeX installation and drives it to completion.
///
/// Two things make a naive "run pdflatex and look at the exit code" approach fail on real
/// machines: the toolchain usually is not on the PATH a GUI process inherits (a macOS .app
/// launched from Finder and a Linux .desktop launcher both get launchd/systemd's minimal
/// PATH, not the shell's), and a single pass leaves cross-references, the table of contents
/// and the bibliography unresolved. Both are handled here.
/// </summary>
public static class LatexToolchain
{
    /// <summary>Set this to a directory (or a list of them) holding the TeX binaries to force a specific install.</summary>
    public const string PathOverrideVariable = "CODEVIEWER_LATEX_PATH";

    private const int DefaultTimeoutMs = 120_000;

    private static readonly string[] RerunMarkers =
    {
        "Rerun to get cross-references right",
        "Rerun to get outlines right",
        "Rerun to get the bars right",
        "There were undefined references",
        "Label(s) may have changed",
        "Citation(s) may have changed",
        "Please rerun LaTeX",
        "Please (re)run Biber",
        "Package rerunfilecheck Warning",
    };

    // "% !TEX program = xelatex" / "% !TeX TS-program = lualatex", as written by TeXShop and TeXworks.
    private static readonly Regex MagicProgram = new(
        @"^\s*%+\s*!\s*TE?X\s+(?:TS-)?program\s*=\s*([A-Za-z]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex UsePackage = new(
        @"\\usepackage\s*(?:\[[^\]]*\])?\s*\{([^}]*)\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Packages that only work under a Unicode engine; running pdflatex on them fails outright.
    private static readonly string[] UnicodeEnginePackages =
        { "fontspec", "unicode-math", "polyglossia", "luatextra", "luacode", "luaotfload" };

    /// <summary>Compiles <paramref name="texFilePath"/> to a PDF beside the source.</summary>
    public static async Task<LatexCompileOutcome> CompileAsync(
        string texFilePath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(texFilePath))!;
        var file = Path.GetFileName(texFilePath);
        var jobName = Path.GetFileNameWithoutExtension(file);
        var pdfPath = Path.Combine(directory, jobName + ".pdf");

        var source = await ReadPreambleAsync(texFilePath, cancellationToken).ConfigureAwait(false);
        var plan = BuildPlan(DetectEngine(source), file);
        if (plan.Count == 0)
            return new LatexCompileOutcome(LatexCompileStatus.NoToolchain, null, null, string.Empty, InstallHint());

        var transcript = new StringBuilder();

        for (var attempt = 0; attempt < plan.Count; attempt++)
        {
            var invocation = plan[attempt];
            var lastAttempt = attempt == plan.Count - 1;
            var logStampBefore = Stamp(Path.Combine(directory, jobName + ".log"));
            LatexCompileOutcome? failure = null;

            for (var pass = 1; pass <= invocation.MaxPasses; pass++)
            {
                status?.Report(invocation.MaxPasses > 1
                    ? $"Compiling {file} with {invocation.Name} (pass {pass} of {invocation.MaxPasses})…"
                    : $"Compiling {file} with {invocation.Name}…");

                var run = await RunAsync(invocation.ExecutablePath, invocation.Arguments, directory,
                    DefaultTimeoutMs, cancellationToken).ConfigureAwait(false);

                transcript.Append($"$ {invocation.Name} {string.Join(' ', invocation.Arguments)}\n")
                          .Append(run.Output);
                if (transcript[^1] != '\n') transcript.Append('\n');

                if (run.TimedOut)
                {
                    return new LatexCompileOutcome(LatexCompileStatus.TimedOut, invocation.Name, null,
                        Combine(transcript, directory, jobName),
                        $"{invocation.Name} did not finish within {DefaultTimeoutMs / 1000} seconds.");
                }

                if (run.ExitCode != 0)
                {
                    failure = new LatexCompileOutcome(LatexCompileStatus.Failed, invocation.Name, null,
                        Combine(transcript, directory, jobName),
                        $"{invocation.Name} failed (exit {run.ExitCode}).");
                    break;
                }

                // latexmk and tectonic resolve references and the bibliography themselves.
                if (invocation.MaxPasses == 1 || pass == invocation.MaxPasses) break;

                var ranBibliography = await RunBibliographyToolAsync(directory, jobName, transcript, status,
                    cancellationToken).ConfigureAwait(false);
                if (!ranBibliography && !WantsAnotherPass(ReadLog(directory, jobName)) && !WantsAnotherPass(run.Output))
                    break;
            }

            if (failure != null)
            {
                // A wrapper that never reached TeX at all (MiKTeX's latexmk without Perl, a broken
                // shim) leaves no log behind. That is a toolchain problem, not a document error, so
                // move on to the next engine instead of blaming the user's source.
                if (!lastAttempt && Stamp(Path.Combine(directory, jobName + ".log")) == logStampBefore)
                {
                    transcript.Append($"\n{invocation.Name} did not run TeX; trying {plan[attempt + 1].Name} instead.\n\n");
                    continue;
                }
                return failure;
            }

            if (!File.Exists(pdfPath))
            {
                var noPdf = new LatexCompileOutcome(LatexCompileStatus.Failed, invocation.Name, null,
                    Combine(transcript, directory, jobName),
                    $"{invocation.Name} reported success but produced no PDF.");
                if (lastAttempt) return noPdf;
                transcript.Append($"\n{invocation.Name} produced no PDF; trying {plan[attempt + 1].Name} instead.\n\n");
                continue;
            }

            return new LatexCompileOutcome(LatexCompileStatus.Succeeded, invocation.Name, pdfPath,
                Combine(transcript, directory, jobName), $"Compiled with {invocation.Name} → {pdfPath}");
        }

        return new LatexCompileOutcome(LatexCompileStatus.Failed, plan[^1].Name, null,
            Combine(transcript, directory, jobName), "LaTeX compilation failed.");
    }

    /// <summary>Every buildable route available on this machine, best first. Empty when no TeX is installed.</summary>
    public static IReadOnlyList<LatexInvocation> BuildPlan(LatexEngine engine, string file)
    {
        var plan = new List<LatexInvocation>();

        // latexmk drives the engine until references and the bibliography settle, so prefer it.
        if (FindExecutable("latexmk") is { } latexmk)
        {
            var mode = engine switch
            {
                LatexEngine.XeLatex => "-pdfxe",
                LatexEngine.LuaLatex => "-pdflua",
                _ => "-pdf",
            };
            plan.Add(new LatexInvocation("latexmk", latexmk,
                new[] { mode, "-interaction=nonstopmode", "-halt-on-error", "-file-line-error", file }, 1));
        }

        // tectonic is self-contained and also handles reruns on its own.
        if (FindExecutable("tectonic") is { } tectonic)
            plan.Add(new LatexInvocation("tectonic", tectonic, new[] { file }, 1));

        foreach (var name in EngineExecutables(engine))
        {
            if (FindExecutable(name) is not { } enginePath) continue;
            plan.Add(new LatexInvocation(name, enginePath,
                new[] { "-interaction=nonstopmode", "-halt-on-error", "-file-line-error", file }, 3));
        }

        return plan;
    }

    /// <summary>Engine executables to try, starting with the one the document asks for.</summary>
    public static IEnumerable<string> EngineExecutables(LatexEngine engine)
    {
        switch (engine)
        {
            case LatexEngine.XeLatex: yield return "xelatex"; yield return "lualatex"; break;
            case LatexEngine.LuaLatex: yield return "lualatex"; yield return "xelatex"; break;
            default: yield return "pdflatex"; yield return "xelatex"; yield return "lualatex"; break;
        }
    }

    /// <summary>Picks the engine from a "% !TEX program" line, else from packages that require Unicode support.</summary>
    public static LatexEngine DetectEngine(string source)
    {
        var magic = MagicProgram.Match(source);
        if (magic.Success)
        {
            switch (magic.Groups[1].Value.ToLowerInvariant())
            {
                case "xelatex": case "xetex": return LatexEngine.XeLatex;
                case "lualatex": case "luatex": return LatexEngine.LuaLatex;
                case "pdflatex": case "pdftex": case "latex": return LatexEngine.PdfLatex;
            }
        }

        foreach (Match use in UsePackage.Matches(source))
        {
            foreach (var name in use.Groups[1].Value.Split(','))
            {
                var trimmed = name.Trim().ToLowerInvariant();
                if (UnicodeEnginePackages.Contains(trimmed))
                    return trimmed.StartsWith("lua", StringComparison.Ordinal) ? LatexEngine.LuaLatex : LatexEngine.XeLatex;
            }
        }

        return LatexEngine.PdfLatex;
    }

    /// <summary>True when the transcript or log asks for another pass.</summary>
    public static bool WantsAnotherPass(string text) =>
        !string.IsNullOrEmpty(text) &&
        RerunMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds <paramref name="name"/> on the PATH, and failing that in the places TeX actually
    /// installs itself. A GUI process does not inherit the shell PATH, so the well-known
    /// directories are what make MacTeX and a manual TeX Live install discoverable at all.
    /// </summary>
    public static string? FindExecutable(string name)
    {
        foreach (var directory in CandidateDirectories())
        {
            foreach (var candidate in ExecutableNames(name))
            {
                string full;
                try { full = Path.Combine(directory, candidate); }
                catch (ArgumentException) { break; } // directory holds invalid path characters
                if (IsExecutableFile(full)) return full;
            }
        }
        return null;
    }

    /// <summary>Directories searched for TeX binaries, in priority order.</summary>
    public static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var directory in Split(Environment.GetEnvironmentVariable(PathOverrideVariable)))
            if (seen.Add(directory)) yield return directory;

        foreach (var directory in Split(Environment.GetEnvironmentVariable("PATH")))
            if (seen.Add(directory)) yield return directory;

        foreach (var directory in WellKnownDirectories())
            if (seen.Add(directory)) yield return directory;
    }

    /// <summary>Reads the LaTeX log, which carries far more detail than the terminal transcript.</summary>
    public static string ReadLog(string directory, string jobName)
    {
        var log = Path.Combine(directory, jobName + ".log");
        try { return File.Exists(log) ? File.ReadAllText(log) : string.Empty; }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    public static string InstallHint()
    {
        var install = OperatingSystem.IsWindows() ? "MiKTeX (miktex.org) or TeX Live"
            : OperatingSystem.IsMacOS() ? "MacTeX (tug.org/mactex) or `brew install --cask mactex-no-gui`"
            : "TeX Live (`apt install texlive-latex-extra latexmk` or tug.org/texlive)";

        return $"No LaTeX toolchain found (looked for latexmk, tectonic, pdflatex, xelatex and lualatex " +
               $"on PATH and in the usual TeX install locations).\n\nInstall {install}.\n\n" +
               $"If TeX is already installed somewhere unusual, set {PathOverrideVariable} to the " +
               $"directory holding its binaries and restart codeviewer.";
    }

    // ---------- internals ----------

    private static async Task<string> ReadPreambleAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[8192];
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return new string(buffer, 0, read);
        }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    /// <summary>Runs bibtex or biber when the document cites anything. Returns true if it ran.</summary>
    private static async Task<bool> RunBibliographyToolAsync(
        string directory, string jobName, StringBuilder transcript, IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        // biblatex leaves a .bcf control file and is driven by biber; plain BibTeX is not.
        var bcf = Path.Combine(directory, jobName + ".bcf");
        var aux = Path.Combine(directory, jobName + ".aux");
        string tool;

        if (File.Exists(bcf))
        {
            tool = "biber";
        }
        else
        {
            string auxText;
            try { auxText = File.Exists(aux) ? await File.ReadAllTextAsync(aux, cancellationToken).ConfigureAwait(false) : string.Empty; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            if (!auxText.Contains("\\bibdata", StringComparison.Ordinal)) return false;
            tool = "bibtex";
        }

        // Already resolved on an earlier pass: the generated bibliography is newer than its input.
        var generated = Path.Combine(directory, jobName + ".bbl");
        var driver = tool == "biber" ? bcf : aux;
        if (File.Exists(generated) && File.GetLastWriteTimeUtc(generated) >= File.GetLastWriteTimeUtc(driver))
            return false;

        if (FindExecutable(tool) is not { } toolPath) return false;

        status?.Report($"Resolving bibliography with {tool}…");
        var run = await RunAsync(toolPath, new[] { jobName }, directory, DefaultTimeoutMs, cancellationToken)
            .ConfigureAwait(false);
        transcript.Append($"$ {tool} {jobName}\n").Append(run.Output);
        if (transcript.Length > 0 && transcript[^1] != '\n') transcript.Append('\n');

        // A bibliography error is reported through the LaTeX log rather than aborting the build.
        return true;
    }

    private static async Task<(int ExitCode, string Output, bool TimedOut)> RunAsync(
        string executablePath, string[] arguments, string workingDirectory, int timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        // Without a closed stdin a TeX error prompt would block forever despite nonstopmode.
        try { process.StandardInput.Close(); } catch (IOException) { }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            cancellationToken.ThrowIfCancellationRequested();
            return (-1, await SafeText(stdout, stderr).ConfigureAwait(false), true);
        }

        return (process.ExitCode, await SafeText(stdout, stderr).ConfigureAwait(false), false);
    }

    /// <summary>Keeps both streams: TeX writes errors to stdout, tectonic writes them to stderr.</summary>
    private static async Task<string> SafeText(Task<string> stdout, Task<string> stderr)
    {
        static async Task<string> Read(Task<string> task)
        {
            try { return await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { return string.Empty; }
            catch (IOException) { return string.Empty; }
        }

        var output = await Read(stdout).ConfigureAwait(false);
        var error = await Read(stderr).ConfigureAwait(false);
        if (output.Length == 0) return error;
        if (error.Length == 0) return output;
        return output.TrimEnd() + "\n" + error;
    }

    private static string Combine(StringBuilder transcript, string directory, string jobName)
    {
        var log = ReadLog(directory, jobName);
        if (log.Length == 0) return transcript.ToString();
        return transcript.ToString().TrimEnd() + $"\n\n----- {jobName}.log -----\n" + log;
    }

    /// <summary>Size and write time, used to tell whether a run actually touched a file.</summary>
    private static (long Length, DateTime Written) Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : default;
        }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }

    private static IEnumerable<string> Split(string? value)
    {
        if (string.IsNullOrEmpty(value)) yield break;
        foreach (var part in value.Split(Path.PathSeparator))
        {
            var trimmed = part.Trim().Trim('"');
            if (trimmed.Length > 0) yield return trimmed;
        }
    }

    private static IEnumerable<string> ExecutableNames(string name)
    {
        if (!OperatingSystem.IsWindows()) { yield return name; yield break; }
        yield return name + ".exe";
        yield return name + ".bat";
        yield return name + ".cmd";
        yield return name;
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Standard TeX install locations, which a GUI process almost never has on its PATH.</summary>
    private static IEnumerable<string> WellKnownDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var directory in WindowsDirectories()) yield return directory;
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            // MacTeX symlinks every binary here; /etc/paths.d only reaches shells, not Finder.
            yield return "/Library/TeX/texbin";
            yield return "/usr/texbin";
            yield return "/opt/homebrew/bin";
        }

        yield return "/usr/local/bin";
        yield return "/usr/bin";
        yield return "/bin";
        yield return Path.Combine(Home(), ".local", "bin");
        yield return Path.Combine(Home(), "bin");

        foreach (var root in new[] { "/usr/local/texlive", "/opt/texlive", Path.Combine(Home(), "texlive") })
            foreach (var directory in TexLiveBinDirectories(root))
                yield return directory;

        // TinyTeX, as installed by R's tinytex package.
        foreach (var root in new[] { Path.Combine(Home(), ".TinyTeX"), Path.Combine(Home(), "Library", "TinyTeX") })
            foreach (var directory in EnumerateDirectories(Path.Combine(root, "bin")))
                yield return directory;
    }

    private static IEnumerable<string> WindowsDirectories()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var root in new[] { programFiles, programFilesX86, localAppData, appData })
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "MiKTeX", "miktex", "bin", "x64");
            yield return Path.Combine(root, "MiKTeX", "miktex", "bin");
            yield return Path.Combine(root, "Programs", "MiKTeX", "miktex", "bin", "x64");
            yield return Path.Combine(root, "Programs", "MiKTeX", "miktex", "bin");
        }

        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "Programs", "Tectonic");

        // TeX Live keeps a year-stamped tree; newest first.
        var texLiveRoots = string.IsNullOrEmpty(programFiles)
            ? new[] { @"C:\texlive" }
            : new[] { @"C:\texlive", Path.Combine(programFiles, "texlive") };
        foreach (var root in texLiveRoots)
        {
            foreach (var year in EnumerateDirectories(root).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(year, "bin", "windows");
                yield return Path.Combine(year, "bin", "win32");
            }
        }
    }

    /// <summary>TeX Live nests binaries under &lt;root&gt;/&lt;year&gt;/bin/&lt;platform&gt;.</summary>
    private static IEnumerable<string> TexLiveBinDirectories(string root) =>
        EnumerateDirectories(root)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .SelectMany(year => EnumerateDirectories(Path.Combine(year, "bin")));

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
            ? home
            : Environment.CurrentDirectory;
}
