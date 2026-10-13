using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AutoLUT.App.Views;
using Avalonia.Controls;

namespace AutoLUT.App.Services.Update;

/// <summary>A newer release that matches this build's platform, ready to download.</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string AssetName,
    string DownloadUrl,
    string? AssetDigest,
    string ReleaseNotesUrl);

/// <summary>Why a release lookup did or did not produce an update.</summary>
public enum UpdateCheckOutcome
{
    Available,
    UpToDate,
    /// <summary>This build's own version string could not be parsed.</summary>
    UnknownCurrentVersion,
    /// <summary>The latest release's tag could not be read as a version.</summary>
    UnreadableRelease,
    /// <summary>A newer release exists but has no package for this platform.</summary>
    NoPackageForPlatform,
}

/// <summary>Outcome of a release lookup. Info is non-null exactly when Outcome is Available;
/// LatestTag is the latest release tag, when one was fetched.</summary>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, UpdateInfo? Info = null, string? LatestTag = null);

/// <summary>
/// Desktop-only on-launch update checker. Compares the running version against the latest
/// GitHub release, and (on request) downloads the matching RID zip, swaps the install
/// folder in place via a detached helper script, and relaunches.
///
/// Everything here is only ever invoked from the desktop lifetime branch, so the HTTP,
/// file-system, and process code never runs in the WASM build.
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "TorjeAmundsen";
    private const string Repo = "AutoLUT";
    private const string LatestReleaseUrl =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string UserAgent = "AutoLUT-Updater";

    /// <summary>
    /// The on-launch check: returns a newer release for this platform, or <c>null</c> when up
    /// to date, when suppressed by "Don't ask again"/"Skip this version", or on any error
    /// (offline is a silent no-op).
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var settings = UpdateSettings.Load();
            if (settings.AutoCheckDisabled)
            {
                return null;
            }

            if ((await FindNewerReleaseAsync()).Info is not { } info)
            {
                return null;
            }

            if (string.Equals(settings.SkippedVersion, info.TagName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return info;
        }
        catch
        {
            // Offline, DNS failure, malformed payload - the updater must be invisible on failure.
            return null;
        }
    }

    /// <summary>
    /// A user-initiated check. Ignores the "Don't ask again"/"Skip" settings and always
    /// reports the outcome (up to date / couldn't check) so the button gives feedback.
    /// </summary>
    public async Task CheckManuallyAsync(Window owner, IDialogService dialogs)
    {
        UpdateCheckResult result;
        try
        {
            result = await FindNewerReleaseAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            await dialogs.ShowWarningAsync(
                "Check for updates",
                "GitHub is limiting update checks from your network right now. Try again in an hour.");
            return;
        }
        catch
        {
            await dialogs.ShowWarningAsync(
                "Check for updates",
                "Could not check for updates. Check your internet connection and try again.");
            return;
        }

        if (result.Info is { } info)
        {
            await PromptAndMaybeUpdateAsync(owner, info);
            return;
        }

        string releasesUrl = $"https://github.com/{Owner}/{Repo}/releases";
        string message = result.Outcome switch
        {
            UpdateCheckOutcome.UpToDate => $"You're on the latest version ({CurrentVersion}).",
            UpdateCheckOutcome.NoPackageForPlatform =>
                $"AutoLUT {result.LatestTag} is available, but it has no download for this platform. "
                + $"Get it from {releasesUrl}.",
            UpdateCheckOutcome.UnknownCurrentVersion =>
                $"This build's version ({CurrentVersion}) is unknown, so it cannot be compared. "
                + $"Check {releasesUrl} for the latest release.",
            _ => $"The latest release could not be read. Check {releasesUrl} for the latest release.",
        };
        await dialogs.ShowWarningAsync("Check for updates", message);
    }

    /// <summary>
    /// Fetches the latest release and reports whether it is a newer one with a package for this
    /// platform. Consults no settings and lets network errors throw.
    /// </summary>
    private async Task<UpdateCheckResult> FindNewerReleaseAsync()
    {
        var current = ParseVersion(CurrentVersion);
        if (current is null)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UnknownCurrentVersion);
        }

        var release = await FetchLatestReleaseAsync();
        if (release is null || ParseVersion(release.TagName) is not { } latest)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UnreadableRelease);
        }

        if (latest <= current)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, LatestTag: release.TagName);
        }

        var rid = GetRuntimeIdentifier();
        var assetName = $"{Repo}-{release.TagName}-{rid}.zip";
        var asset = rid is null
            ? null
            : (release.Assets ?? [])
                .FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NoPackageForPlatform, LatestTag: release.TagName);
        }

        var info = new UpdateInfo(latest, release.TagName, assetName, asset.DownloadUrl, asset.Digest, release.HtmlUrl);
        return new UpdateCheckResult(UpdateCheckOutcome.Available, info, release.TagName);
    }

    private static string CurrentVersion => MainWindow.AppVersion;

    /// <summary>Shows the update dialog and applies the user's choice.</summary>
    public async Task PromptAndMaybeUpdateAsync(Window owner, UpdateInfo info)
    {
        var choice = await UpdateDialog.Show(
            owner, info, CurrentVersion,
            (progress, cancellationToken) => DownloadAndApplyAsync(info, progress, cancellationToken));

        switch (choice)
        {
            case UpdateChoice.Skip:
                {
                    // Skip this version, but keep auto-checks on so a newer version still prompts -
                    // this also overrides a previous "Don't ask again".
                    var settings = UpdateSettings.Load();
                    settings.AutoCheckDisabled = false;
                    settings.SkippedVersion = info.TagName;
                    settings.Save();
                    break;
                }
            case UpdateChoice.Later:
                {
                    // Remind me later: re-enable auto-checks and clear any skip so the next launch
                    // prompts again - this also overrides a previous "Don't ask again".
                    var settings = UpdateSettings.Load();
                    settings.AutoCheckDisabled = false;
                    settings.SkippedVersion = null;
                    settings.Save();
                    break;
                }
            case UpdateChoice.Never:
                {
                    var settings = UpdateSettings.Load();
                    settings.AutoCheckDisabled = true;
                    settings.Save();
                    break;
                }
            case UpdateChoice.Update:
            case UpdateChoice.Failed:
            default:
                // Update is handled by the dialog (exits on success); Failed persists nothing.
                break;
        }
    }

    /// <summary>
    /// Downloads the release zip, extracts it to a staging folder, and launches a detached
    /// helper that waits for this process to exit, copies the new files over the install
    /// directory, and relaunches. Hard-exits the process at the end so the exe/native DLL
    /// file locks release for the helper to overwrite.
    ///
    /// The copy only adds and overwrites, never deletes: the release zip is flat, so users
    /// extract it into shared folders (Downloads, Desktop) and the install directory can hold
    /// their own files - a mirror would wipe them. Files a newer release dropped are left
    /// behind, which is harmless.
    ///
    /// The zip must come from this repository's GitHub release downloads and match the SHA-256
    /// digest GitHub reports for the asset; that catches a corrupted or swapped download, not a
    /// compromised release. Cancelling (closing the dialog) stops before anything is launched.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string expectedSha256 = ValidateAsset(info);
        var installDir = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureWritable(installDir);
        // Random name, owner-only (0700) on Unix: a fixed /tmp path could be pre-created by
        // another local user to swap the helper script before it runs.
        var tempRoot = Directory.CreateTempSubdirectory("autolut-update-").FullName;
        var stagingDir = Path.Combine(tempRoot, "staging");
        var zipPath = Path.Combine(tempRoot, info.AssetName);

        try
        {
            string actualSha256 = await DownloadFileAsync(info.DownloadUrl, zipPath, progress, cancellationToken);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update does not match its published checksum.");
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingDir), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }

        LaunchHelperAndExit(installDir, stagingDir, tempRoot);
    }

    /// <summary>Checks the asset URL points at this repository's release downloads and returns
    /// its expected SHA-256 hex digest.</summary>
    private static string ValidateAsset(UpdateInfo info)
    {
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update download link does not point at the AutoLUT GitHub releases.");
        }

        const string prefix = "sha256:";
        if (info.AssetDigest is not { } digest
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || digest.Length != prefix.Length + 64)
        {
            throw new InvalidDataException("The update has no published checksum to verify the download against.");
        }

        return digest[prefix.Length..];
    }

    /// <summary>Fails up front when this process cannot create files in the install folder (an
    /// install under Program Files, say). The helper copies with the same rights, so it would fail
    /// too, but only after the app had exited, leaving the user with a warning and the old version.</summary>
    private static void EnsureWritable(string installDir)
    {
        var probe = Path.Combine(installDir, $".autolut-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"AutoLUT cannot write to its folder ({installDir}), so it cannot update itself there. "
                + "Move the AutoLUT folder somewhere you can write to, such as Documents, or download "
                + $"the new version from https://github.com/{Owner}/{Repo}/releases.", ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of a temp folder.
        }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var json = await http.GetStringAsync(LatestReleaseUrl);
        return JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);
    }

    /// <summary>Downloads to <paramref name="destination"/> and returns the SHA-256 hex digest of the bytes written.</summary>
    private static async Task<string> DownloadFileAsync(
        string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report((double)readTotal / total.Value * 100);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void LaunchHelperAndExit(string installDir, string stagingDir, string tempRoot)
    {
        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), $"autolut-update-{pid}.cmd");
            var exe = Path.Combine(installDir, "AutoLUT.exe");

            // ping is used as the delay because `timeout` fails when stdin is redirected
            // (which it is under a windowless helper launch).
            //
            // `if errorlevel 8` is true when robocopy's exit code is >= 8, its failure range
            // (0-7 are success/no-op codes). On failure we skip the cleanup, keep the staged
            // files for manual recovery, warn the user, and still try to relaunch whatever exe
            // survived - a half-copied install is better relaunched than left with nothing.
            //
            // Paths reach the script through environment variables, never as script text: cmd
            // reads .cmd files in the OEM code page, so a non-ASCII path (a user profile name
            // with an o-slash, say) written into the UTF-8 script garbles and robocopy fails.
            // The environment block is UTF-16 and cmd expands %VAR% values verbatim, which also
            // keeps quotes and percent signs in paths from breaking the script.
            var content = $"""
                @echo off
                :wait
                tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
                if not errorlevel 1 (
                  ping -n 2 127.0.0.1 >nul
                  goto wait
                )
                robocopy "%AUTOLUT_STAGING%" "%AUTOLUT_INSTALL%" /E /NFL /NDL /NJH /NJS /NP >nul
                if errorlevel 8 goto failed
                start "" "%AUTOLUT_EXE%"
                rmdir /s /q "%AUTOLUT_TEMP%"
                del "%~f0"
                exit /b
                :failed
                powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms;[void][System.Windows.Forms.MessageBox]::Show('AutoLUT could not finish updating. Your installation may be incomplete - please reinstall from https://github.com/{Owner}/{Repo}/releases. The downloaded files were kept at ' + $env:AUTOLUT_TEMP + '.','AutoLUT update')"
                start "" "%AUTOLUT_EXE%"
                del "%~f0"
                """;
            File.WriteAllText(script, content.ReplaceLineEndings("\r\n"));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["AUTOLUT_STAGING"] = stagingDir;
            startInfo.Environment["AUTOLUT_INSTALL"] = installDir;
            startInfo.Environment["AUTOLUT_EXE"] = exe;
            startInfo.Environment["AUTOLUT_TEMP"] = tempRoot;
            System.Diagnostics.Process.Start(startInfo);
        }
        else
        {
            // Inside the owner-only temp folder: /tmp is shared, so a fixed script path there
            // could be pre-created or swapped by another local user before sh runs it.
            var script = Path.Combine(tempRoot, "update.sh");
            var exe = Path.Combine(installDir, "AutoLUT");

            // If the copy fails (permissions, disk full) skip the cleanup so the staged files
            // survive for manual recovery, warn if a GUI dialog tool is available, and still try
            // to relaunch whatever exe is present rather than leaving the user with nothing.
            var content = $"""
                #!/bin/sh
                while kill -0 {pid} 2>/dev/null; do sleep 1; done
                if command -v rsync >/dev/null 2>&1; then
                  rsync -a "{stagingDir}/" "{installDir}/"
                else
                  cp -a "{stagingDir}/." "{installDir}/"
                fi
                if [ $? -ne 0 ]; then
                  command -v zenity >/dev/null 2>&1 && zenity --error --text="AutoLUT could not finish updating. Reinstall from https://github.com/{Owner}/{Repo}/releases. Files kept at {tempRoot}." 2>/dev/null
                  chmod +x "{exe}" 2>/dev/null
                  ( "{exe}" & )
                  rm -- "$0"
                  exit 1
                fi
                chmod +x "{exe}"
                ( "{exe}" & )
                rm -rf "{tempRoot}"
                """;
            // Shell scripts must be LF-only; a CR before the shebang/commands breaks sh.
            File.WriteAllText(script, content.ReplaceLineEndings("\n"));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"nohup sh '{script}' >/dev/null 2>&1 &\"",
                UseShellExecute = false,
            });
        }

        // Hard-exit so the running exe + native sidecar DLLs unlock for the helper's swap.
        Environment.Exit(0);
    }

    // The only RIDs built and published (build.ps1, release.yml); the AOT exe is x64-only.
    private static string? GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }
        if (OperatingSystem.IsLinux())
        {
            return "linux-x64";
        }
        return null;
    }

    /// <summary>Parses "v1.2.3" / "1.2.3-rc1" into the numeric core; null if unparseable.</summary>
    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return Version.TryParse(text, out var version) ? version : null;
    }
}
