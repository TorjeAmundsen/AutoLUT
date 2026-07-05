using System.IO.Compression;
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
    string ReleaseNotesUrl);

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

            var info = await FindNewerReleaseAsync();
            if (info is null)
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
        UpdateInfo? info;
        try
        {
            info = await FindNewerReleaseAsync();
        }
        catch
        {
            await dialogs.ShowWarningAsync(
                "Check for updates",
                "Could not check for updates. Check your internet connection and try again.");
            return;
        }

        if (info is null)
        {
            await dialogs.ShowWarningAsync(
                "Check for updates",
                $"You're on the latest version ({CurrentVersion}).");
            return;
        }

        await PromptAndMaybeUpdateAsync(owner, info);
    }

    /// <summary>
    /// Fetches the latest release and returns it if it is newer and has a matching asset;
    /// null when up to date or no asset. Consults no settings and lets network errors throw.
    /// </summary>
    private async Task<UpdateInfo?> FindNewerReleaseAsync()
    {
        var current = ParseVersion(CurrentVersion);
        if (current is null)
        {
            return null;
        }

        var release = await FetchLatestReleaseAsync();
        if (release is null)
        {
            return null;
        }

        var latest = ParseVersion(release.TagName);
        if (latest is null || latest <= current)
        {
            return null;
        }

        var rid = GetRuntimeIdentifier();
        if (rid is null)
        {
            return null;
        }

        var assetName = $"{Repo}-{release.TagName}-{rid}.zip";
        var asset = (release.Assets ?? [])
            .FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return null;
        }

        return new UpdateInfo(latest, release.TagName, assetName, asset.DownloadUrl, release.HtmlUrl);
    }

    private static string CurrentVersion => MainWindow.AppVersion;

    /// <summary>Shows the update dialog and applies the user's choice.</summary>
    public async Task PromptAndMaybeUpdateAsync(Window owner, UpdateInfo info)
    {
        var choice = await UpdateDialog.Show(
            owner, info, CurrentVersion, progress => DownloadAndApplyAsync(info, progress));

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
    /// helper that waits for this process to exit, mirrors the new folder over the install
    /// directory, and relaunches. Hard-exits the process at the end so the exe/native DLL
    /// file locks release for the helper to overwrite.
    /// </summary>
    public static async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var installDir = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"autolut-update-{Environment.ProcessId}");
        var stagingDir = Path.Combine(tempRoot, "staging");
        var zipPath = Path.Combine(tempRoot, info.AssetName);

        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        Directory.CreateDirectory(tempRoot);

        await DownloadFileAsync(info.DownloadUrl, zipPath, progress);
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingDir));

        LaunchHelperAndExit(installDir, stagingDir, tempRoot);
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var json = await http.GetStringAsync(LatestReleaseUrl);
        return JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);
    }

    private static async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report((double)readTotal / total.Value * 100);
            }
        }
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
            // survived - a half-mirrored install is better relaunched than left with nothing.
            var content = $"""
                @echo off
                :wait
                tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
                if not errorlevel 1 (
                  ping -n 2 127.0.0.1 >nul
                  goto wait
                )
                robocopy "{stagingDir}" "{installDir}" /MIR /NFL /NDL /NJH /NJS /NP >nul
                if errorlevel 8 goto failed
                start "" "{exe}"
                rmdir /s /q "{tempRoot}"
                del "%~f0"
                exit /b
                :failed
                powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms;[void][System.Windows.Forms.MessageBox]::Show('AutoLUT could not finish updating. Your installation may be incomplete - please reinstall from https://github.com/{Owner}/{Repo}/releases. The downloaded files were kept at {tempRoot}.','AutoLUT update')"
                start "" "{exe}"
                del "%~f0"
                """;
            File.WriteAllText(script, content.ReplaceLineEndings("\r\n"));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), $"autolut-update-{pid}.sh");
            var exe = Path.Combine(installDir, "AutoLUT");

            // If the mirror fails (permissions, disk full) skip the cleanup so the staged files
            // survive for manual recovery, warn if a GUI dialog tool is available, and still try
            // to relaunch whatever exe is present rather than leaving the user with nothing.
            var content = $"""
                #!/bin/sh
                while kill -0 {pid} 2>/dev/null; do sleep 1; done
                if command -v rsync >/dev/null 2>&1; then
                  rsync -a --delete "{stagingDir}/" "{installDir}/"
                else
                  rm -rf "{installDir}"/* && cp -a "{stagingDir}/." "{installDir}/"
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
                rm -- "$0"
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
