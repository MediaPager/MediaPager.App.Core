using System.Diagnostics;

namespace MediaPager.App.Core.Services;

/// <summary>One stage of a deploy, reported while the pipeline runs (drives the
/// install-progress bar and its status log in Settings → Plugins).</summary>
public sealed record PluginDeployProgress(int Percent, string Stage, string Message);

/// <summary>Where a deploy landed: the assembly name actually used (for a bare repo it is
/// discovered from the project file), the deployed dll path, and the plugin manifest the
/// install was vetted with.</summary>
public sealed record PluginDeployOutcome(string Assembly, string Path, PluginManifest? Manifest = null);

/// <summary>
/// Installs a plugin from its source repo into a plugins directory — the one pipeline
/// official and community installs share (shipped officials deploy at build via the Api's
/// deploy target; optional officials and community plugins install on demand through this
/// class). Layout matches the build-time deploy exactly: <c>&lt;directory&gt;/&lt;Assembly&gt;/&lt;Assembly&gt;.dll</c>,
/// so boot just scans the official and community directories.
/// </summary>
public sealed class PluginDeployer(
    IPluginBuildSdkProvider buildSdkProvider,
    IGitPrivateKeyProvider gitPrivateKeyProvider)
{
    // Plugin repos live inside the superproject and reference the plugin SDK by relative
    // path (../MediaPager.App.PluginContracts), so a bare plugin checkout can't publish
    // alone: when a csproj asks for that sibling, stage a clone next to it.
    private const string PluginContractsRepo =
        "https://github.com/MediaPager/MediaPager.App.PluginContracts.git";

    /// <summary>Clone the repo, publish the plugin project, and copy the plugin dll into
    /// the target directory. An explicit <paramref name="assembly"/> picks the project to
    /// publish (assembly name == folder name); null discovers it when the repo has exactly
    /// one project. <paramref name="branch"/> checks out a specific branch (null = the
    /// repo's default branch). <paramref name="progress"/> receives stage updates
    /// (clone → inspect → publish → verify → deploy).</summary>
    public async Task<PluginDeployOutcome> InstallAsync(
        string repo, string? assembly, string directory,
        string? branch = null,
        IProgress<PluginDeployProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException("The plugin has no source repo to install from.");

        var staging = Path.Combine(Path.GetTempPath(), $"mediapager-plugin-{Guid.NewGuid():N}");
        try
        {
            var pluginName = ExpectedPluginName(repo, assembly);
            progress?.Report(new PluginDeployProgress(5, "cloning",
                $"Cloning {repo}{(string.IsNullOrWhiteSpace(branch) ? "" : $" ({branch})")}…"));
            var repoDir = Path.Combine(staging, "repo");
            var branchArg = string.IsNullOrWhiteSpace(branch) ? "" : $"--branch \"{branch}\" ";
            await using var privateKeyLease = GitRepoUrl.IsSshLike(repo)
                ? await gitPrivateKeyProvider.CreateLeaseAsync(cancellationToken)
                : null;
            var (cloneUrl, gitArgs) = GitRepoUrl.Resolve(repo, privateKeyLease?.Path);
            await RunAsync("git", $"{gitArgs}clone --depth 1 --quiet {branchArg}\"{cloneUrl}\" \"{repoDir}\"", cancellationToken);

            // Every MediaPager plugin ships MediaPager.Plugins.<Name>.manifest.json at the
            // repo root — it is what gates the Community tab, so an install requires it too.
            // The manifest must be found and parse cleanly before anything is inspected or
            // published; its name must line up with the repo's MediaPager.Plugins.* suffix.
            progress?.Report(new PluginDeployProgress(22, "manifest", "Validating the plugin manifest…"));
            var manifestPath = Path.Combine(repoDir, PluginManifestFile.FileName(pluginName));
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException(
                    $"No plugin manifest at the repo root — expected {Path.GetFileName(manifestPath)}. "
                    + "Every MediaPager plugin must ship one (it gates the Community tab and installs).");
            var manifestText = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = PluginManifestFile.Parse(manifestText);

            foreach (var referenceRepo in manifest.References ?? [])
            {
                var normalizedReference = GitRepoUrl.Normalize(referenceRepo);
                var referenceName = normalizedReference[(normalizedReference.LastIndexOf('/') + 1)..];
                if (referenceName.Length == 0 || referenceName is "." or ".." ||
                    referenceName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
                    throw new InvalidOperationException($"The project reference repo '{referenceRepo}' has an invalid name.");

                var referenceDirectory = Path.Combine(staging, referenceName);
                if (Directory.Exists(referenceDirectory))
                    throw new InvalidOperationException($"More than one manifest reference resolves to '{referenceName}'.");
                progress?.Report(new PluginDeployProgress(24, "references", $"Cloning project reference {referenceName}…"));
                await using var referenceKeyLease = GitRepoUrl.IsSshLike(referenceRepo)
                    ? await gitPrivateKeyProvider.CreateLeaseAsync(cancellationToken)
                    : null;
                var (referenceUrl, referenceGitArgs) = GitRepoUrl.Resolve(referenceRepo, referenceKeyLease?.Path);
                await RunAsync("git", $"{referenceGitArgs}clone --depth 1 --quiet \"{referenceUrl}\" \"{referenceDirectory}\"", cancellationToken);
            }

            progress?.Report(new PluginDeployProgress(25, "inspecting", "Inspecting project…"));
            var projects = Directory.GetFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();
            var project = (assembly is null
                    ? (projects.Count == 1 ? projects[0] : null)
                    : projects.FirstOrDefault(path =>
                        string.Equals(Path.GetFileName(path), $"{assembly}.csproj", StringComparison.OrdinalIgnoreCase)))
                ?? (assembly is null && projects.Count == 1 ? projects[0] : null);
            if (project is null)
                throw new InvalidOperationException(assembly is null
                    ? $"Could not pick a project to publish in {repo} ({projects.Count} .csproj files found — the repo must contain exactly one)."
                    : $"Could not find a {assembly}.csproj in {repo}.");
            var actualAssembly = assembly ?? Path.GetFileNameWithoutExtension(project);

            var projectFiles = Directory.GetFiles(staging, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();
            if (projectFiles.Any(path => File.ReadAllText(path).Contains("MediaPager.App.PluginContracts")))
            {
                await RunAsync("git",
                    $"clone --depth 1 --quiet \"{PluginContractsRepo}\" \"{Path.Combine(staging, "MediaPager.App.PluginContracts")}\"",
                    cancellationToken);
            }

            var buildSdk = await buildSdkProvider.EnsureInstalledAsync(
                new InlineProgress<PluginBuildSdkProgress>(update =>
                    progress?.Report(new PluginDeployProgress(
                        30 + (int)Math.Round(update.Percent * 0.09), "sdk", update.Message))),
                cancellationToken);
            progress?.Report(new PluginDeployProgress(40, "publishing",
                $"Building the plugin with pinned .NET SDK {PluginBuildSdkVersion.Version} (this can take a minute)…"));
            var publishDir = Path.Combine(staging, "publish");
            Directory.CreateDirectory(buildSdk.NuGetPackagesDirectory);
            var publish = new ProcessStartInfo
            {
                FileName = buildSdk.Executable,
                WorkingDirectory = staging,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            publish.ArgumentList.Add("publish");
            publish.ArgumentList.Add(project);
            publish.ArgumentList.Add("-c");
            publish.ArgumentList.Add("Release");
            publish.ArgumentList.Add("-o");
            publish.ArgumentList.Add(publishDir);
            publish.ArgumentList.Add("--nologo");
            publish.ArgumentList.Add("-v:q");
            publish.Environment["DOTNET_ROOT"] = buildSdk.InstallDirectory;
            publish.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            publish.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            publish.Environment["DOTNET_NOLOGO"] = "1";
            publish.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            publish.Environment["DOTNET_CLI_HOME"] = Path.Combine(buildSdk.InstallDirectory, ".cli-home");
            publish.Environment["NUGET_PACKAGES"] = buildSdk.NuGetPackagesDirectory;
            await RunAsync(publish, cancellationToken);

            progress?.Report(new PluginDeployProgress(80, "verifying", "Verifying build output…"));
            var source = Path.Combine(publishDir, $"{actualAssembly}.dll");
            if (!File.Exists(source))
                throw new InvalidOperationException($"Publish output is missing {actualAssembly}.dll.");

            progress?.Report(new PluginDeployProgress(88, "deploying", $"Deploying {actualAssembly}…"));
            var destinationDirectory = Path.Combine(directory, actualAssembly);
            Directory.CreateDirectory(destinationDirectory);
            foreach (var publishedAssembly in Directory.GetFiles(publishDir, "*.dll"))
                File.Copy(publishedAssembly, Path.Combine(destinationDirectory, Path.GetFileName(publishedAssembly)), overwrite: true);
            var publishedUi = Path.Combine(publishDir, "ui");
            var repositoryUi = Path.Combine(repoDir, "ui");
            var uiSource = Directory.Exists(publishedUi) ? publishedUi : repositoryUi;
            var installedUi = Path.Combine(destinationDirectory, "ui");
            if (Directory.Exists(installedUi))
            {
                if ((File.GetAttributes(installedUi) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("The existing plugin UI path may not be a symbolic link or reparse point.");
                Directory.Delete(installedUi, recursive: true);
            }
            else if (File.Exists(installedUi))
            {
                File.Delete(installedUi);
            }
            if (Directory.Exists(uiSource))
                CopyPluginUiDirectory(uiSource, installedUi);
            var destination = Path.Combine(destinationDirectory, $"{actualAssembly}.dll");
            File.Copy(manifestPath, Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileName(manifestPath)), overwrite: true);
            return new PluginDeployOutcome(actualAssembly, destination, manifest);
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; a leftover temp dir must not fail the install.
            }
        }
    }

    private static void CopyPluginUiDirectory(string sourceDirectory, string destinationDirectory)
    {
        if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Plugin UI assets may not be rooted in a symbolic link or reparse point.");
        var sourceRoot = Path.GetFullPath(sourceDirectory) + Path.DirectorySeparatorChar;
        CopyDirectory(sourceDirectory);

        void CopyDirectory(string currentDirectory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Plugin UI assets may not contain symbolic links or reparse points.");
                var fullPath = Path.GetFullPath(entry);
                if (!fullPath.StartsWith(sourceRoot,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("A plugin UI asset escaped its package directory.");
                var relative = Path.GetRelativePath(sourceDirectory, entry);
                var destination = Path.Combine(destinationDirectory, relative);
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    CopyDirectory(entry);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(entry, destination, overwrite: true);
            }
        }
    }

    /// <summary>List a remote repo's branches plus its default branch (the remote HEAD
    /// when it names one, else main if present, else master) — drives the branch picker
    /// in the Add-plugin dialog.</summary>
    public async Task<(IReadOnlyList<string> Branches, string? Default)> ListBranchesAsync(
        string repo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
            throw new ArgumentException("A repository URL is required.", nameof(repo));

        await using var privateKeyLease = GitRepoUrl.IsSshLike(repo)
            ? await gitPrivateKeyProvider.CreateLeaseAsync(cancellationToken)
            : null;
        var (remotesUrl, gitArgs) = GitRepoUrl.Resolve(repo, privateKeyLease?.Path);
        var heads = await RunAsync("git", $"{gitArgs}ls-remote --heads \"{remotesUrl}\"", cancellationToken);
        var branches = heads
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t') is { Length: > 1 } parts ? parts[1] : line)
            .Select(reference => reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? reference["refs/heads/".Length..]
                : reference)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? defaultBranch = null;
        try
        {
            var head = await RunAsync("git", $"{gitArgs}ls-remote --symref \"{remotesUrl}\" HEAD", cancellationToken);
            var symref = head
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => line.StartsWith("ref: refs/heads/", StringComparison.Ordinal));
            if (symref is not null)
                defaultBranch = symref["ref: refs/heads/".Length..].Split('\t')[0].Trim();
        }
        catch (InvalidOperationException)
        {
            // A repo without a resolvable HEAD still lists its branches — fall through.
        }
        defaultBranch ??= branches.FirstOrDefault(name => name == "main")
            ?? branches.FirstOrDefault(name => name == "master");

        if (defaultBranch is not null && branches.Count > 0)
            branches = branches
                .OrderByDescending(name => string.Equals(name, defaultBranch, StringComparison.Ordinal))
                .ToList();
        return (branches, defaultBranch);
    }

    /// <summary>The plugin's <c>&lt;PluginType&gt;.&lt;PluginName&gt;</c> name from the naming
    /// convention — used to locate its manifest. The assembly (when the catalog pins it) wins,
    /// then the repo's MediaPager.Plugins.* suffix; either way the suffix must carry a known
    /// plugin type (<c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;</c>) so the
    /// manifest file is always findable and the repo declares what kind of plugin it is.</summary>
    private static string ExpectedPluginName(string repo, string? assembly)
    {
        string suffix;
        if (!string.IsNullOrWhiteSpace(assembly) &&
            assembly.StartsWith(PluginManifestFile.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            suffix = assembly[PluginManifestFile.Prefix.Length..];
        }
        else
        {
            var normalized = GitRepoUrl.Normalize(repo);
            var repoName = normalized.Contains('/')
                ? normalized[(normalized.LastIndexOf('/') + 1)..]
                : normalized;
            if (!repoName.StartsWith(PluginManifestFile.Prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"This repo does not follow the MediaPager.Plugins.<PluginType>.<PluginName> naming convention (repo '{repoName}'), "
                    + "so there is no manifest to validate it with.");
            suffix = repoName[PluginManifestFile.Prefix.Length..];
        }

        if (!PluginManifestFile.IsValidSuffix(suffix))
            throw new InvalidOperationException(
                $"'{PluginManifestFile.Prefix}{suffix}' does not follow the MediaPager.Plugins.<PluginType>.<PluginName> naming convention — "
                + "<PluginType> must be one of: " + string.Join(", ", PluginManifestFile.KnownTypeNames) + ".");
        return suffix;
    }

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return await RunAsync(startInfo, cancellationToken);
    }

    private static async Task<string> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"{startInfo.FileName} exited {process.ExitCode}: {detail.Trim()}");
        }
        return stdout;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
