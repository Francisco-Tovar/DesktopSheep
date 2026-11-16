using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace KawaiiDesktopPets;

internal static class UpdateChecker
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Francisco-Tovar/DesktopSheep/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/Francisco-Tovar/DesktopSheep/releases";

    private static readonly HttpClient Http = CreateHttpClient();
    private static int _isChecking;

    public static string CurrentVersionText
    {
        get
        {
            var v = GetCurrentVersion();
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task CheckForUpdatesAsync(SheepManager manager)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) == 1)
            return;

        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var openReleases = System.Windows.MessageBox.Show(
                    $"No GitHub Releases are published yet.\nCurrent version: v{CurrentVersionText}\n\nOpen the GitHub Releases page?",
                    "Check for Updates",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (openReleases == MessageBoxResult.Yes)
                    OpenUrl(ReleasesPageUrl);
                return;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;
            string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            if (!TryParseVersion(tagName, out var latestVersion))
            {
                System.Windows.MessageBox.Show(
                    $"Could not parse latest release version ({tagName}).\nCurrent version: v{CurrentVersionText}",
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var currentVersion = GetCurrentVersion();
            if (latestVersion <= currentVersion)
            {
                System.Windows.MessageBox.Show(
                    $"You are running the latest version (v{CurrentVersionText}).",
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string? exeAssetUrl = FindExeAssetUrl(root);
            string notesPreview = string.IsNullOrWhiteSpace(body)
                ? ""
                : "\n\nRelease notes:\n" + (body.Length > 300 ? body[..300] + "..." : body);

            if (string.IsNullOrEmpty(exeAssetUrl))
            {
                var openBrowser = System.Windows.MessageBox.Show(
                    $"Update available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build} (current: v{CurrentVersionText}).{notesPreview}\n\nOpen the download page?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (openBrowser == MessageBoxResult.Yes)
                    OpenUrl(htmlUrl);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"Update available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build} (current: v{CurrentVersionText}).{notesPreview}\n\nDownload and update KawaiiDesktopPets.exe automatically?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            await DownloadAndApplyUpdateAsync(exeAssetUrl, manager);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to check for updates:\n{ex.Message}",
                "Check for Updates",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static async Task DownloadAndApplyUpdateAsync(string assetUrl, SheepManager manager)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
        {
            System.Windows.MessageBox.Show(
                "Could not locate the running executable path to apply the update.",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        string tempExe = Path.Combine(Path.GetTempPath(), $"KawaiiDesktopPets_update_{Guid.NewGuid():N}.exe");
        string tempBat = Path.Combine(Path.GetTempPath(), $"KawaiiDesktopPets_updater_{Guid.NewGuid():N}.cmd");

        try
        {
            using (var dlResponse = await Http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                dlResponse.EnsureSuccessStatusCode();
                await using var netStream = await dlResponse.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(tempExe);
                await netStream.CopyToAsync(fileStream);
            }

            if (new FileInfo(tempExe).Length < 1024 * 1024)
            {
                File.Delete(tempExe);
                throw new InvalidDataException("Downloaded update file is unexpectedly small.");
            }

            int pid = Environment.ProcessId;
            string script = $"""
                @echo off
                setlocal
                set "PID={pid}"
                set "SRC={tempExe}"
                set "DST={currentExe}"
                :waitloop
                tasklist /FI "PID eq %PID%" 2>NUL | find /I "%PID%" >NUL
                if not errorlevel 1 (
                    timeout /t 1 /nobreak >NUL
                    goto waitloop
                )
                :copyloop
                move /Y "%SRC%" "%DST%" >NUL 2>&1
                if errorlevel 1 (
                    timeout /t 1 /nobreak >NUL
                    goto copyloop
                )
                start "" "%DST%"
                del "%~f0" >NUL 2>&1
                """;
            await File.WriteAllTextAsync(tempBat, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{tempBat}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            manager.ExitApplication();
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
            try { if (File.Exists(tempBat)) File.Delete(tempBat); } catch { }
            System.Windows.MessageBox.Show(
                $"Update download failed:\n{ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string? FindExeAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? fallbackExeUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string url = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url))
                continue;

            if (name.Equals("KawaiiDesktopPets.exe", StringComparison.OrdinalIgnoreCase))
                return url;

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fallbackExeUrl ??= url;
        }
        return fallbackExeUrl;
    }

    private static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        string cleaned = raw.Trim().TrimStart('v', 'V');
        int dash = cleaned.IndexOf('-');
        if (dash >= 0)
            cleaned = cleaned[..dash];

        if (Version.TryParse(cleaned, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KawaiiDesktopPets/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
