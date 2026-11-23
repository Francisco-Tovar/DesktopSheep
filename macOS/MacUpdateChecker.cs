using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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

    public static async Task CheckForUpdatesAsync(MacSheepManager manager, bool silentWhenUpToDate = false)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) == 1)
            return;

        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (!silentWhenUpToDate)
                {
                    bool openReleases = await ShowDialogAsync(
                        "Check for Updates",
                        $"No GitHub Releases are published yet.\nCurrent version: v{CurrentVersionText}\n\nOpen the GitHub Releases page?",
                        yesNo: true);
                    if (openReleases)
                        OpenUrl(ReleasesPageUrl);
                }
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
                if (!silentWhenUpToDate)
                {
                    await ShowDialogAsync(
                        "Check for Updates",
                        $"Could not parse latest release version ({tagName}).\nCurrent version: v{CurrentVersionText}",
                        yesNo: false);
                }
                return;
            }

            var currentVersion = GetCurrentVersion();
            if (latestVersion <= currentVersion)
            {
                if (!silentWhenUpToDate)
                {
                    await ShowDialogAsync(
                        "Check for Updates",
                        $"You are running the latest version (v{CurrentVersionText}).",
                        yesNo: false);
                }
                return;
            }

            string? macZipUrl = FindMacAssetUrl(root);
            string notesPreview = string.IsNullOrWhiteSpace(body)
                ? ""
                : "\n\nRelease notes:\n" + (body.Length > 300 ? body[..300] + "..." : body);

            string? appBundlePath = TryGetCurrentAppBundlePath();
            if (string.IsNullOrEmpty(macZipUrl) || string.IsNullOrEmpty(appBundlePath))
            {
                bool openBrowser = await ShowDialogAsync(
                    "Update Available",
                    $"Update available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build} (current: v{CurrentVersionText}).{notesPreview}\n\nOpen the download page?",
                    yesNo: true);
                if (openBrowser)
                    OpenUrl(htmlUrl);
                return;
            }

            bool confirm = await ShowDialogAsync(
                "Update Available",
                $"Update available: v{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build} (current: v{CurrentVersionText}).{notesPreview}\n\nDownload and update KawaiiDesktopPets.app automatically?",
                yesNo: true);

            if (!confirm)
                return;

            await DownloadAndApplyMacUpdateAsync(macZipUrl, appBundlePath, manager);
        }
        catch (Exception ex)
        {
            if (!silentWhenUpToDate)
                await ShowDialogAsync("Check for Updates", $"Failed to check for updates:\n{ex.Message}", yesNo: false);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static async Task DownloadAndApplyMacUpdateAsync(string zipUrl, string appBundlePath, MacSheepManager manager)
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"kdp_update_{Guid.NewGuid():N}.zip");
        string tempDir = Path.Combine(Path.GetTempPath(), $"kdp_extract_{Guid.NewGuid():N}");
        string tempScript = Path.Combine(Path.GetTempPath(), $"kdp_updater_{Guid.NewGuid():N}.sh");

        try
        {
            using (var dlResponse = await Http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                dlResponse.EnsureSuccessStatusCode();
                await using var netStream = await dlResponse.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(tempZip);
                await netStream.CopyToAsync(fileStream);
            }

            if (new FileInfo(tempZip).Length < 1024 * 1024)
            {
                File.Delete(tempZip);
                throw new InvalidDataException("Downloaded update archive is unexpectedly small.");
            }

            int pid = Environment.ProcessId;
            string script = $"""
                #!/bin/sh
                PID={pid}
                ZIP_PATH="{tempZip}"
                EXTRACT_DIR="{tempDir}"
                APP_PATH="{appBundlePath}"
                while kill -0 $PID 2>/dev/null; do
                    sleep 0.5
                done
                mkdir -p "$EXTRACT_DIR"
                ditto -xk "$ZIP_PATH" "$EXTRACT_DIR" || unzip -oq "$ZIP_PATH" -d "$EXTRACT_DIR"
                NEW_APP=$(find "$EXTRACT_DIR" -maxdepth 2 -name "*.app" | head -n 1)
                if [ -n "$NEW_APP" ]; then
                    rm -rf "$APP_PATH"
                    mv "$NEW_APP" "$APP_PATH"
                    open "$APP_PATH"
                fi
                rm -rf "$EXTRACT_DIR" "$ZIP_PATH" "$0"
                """;
            await File.WriteAllTextAsync(tempScript, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { tempScript },
                CreateNoWindow = true,
                UseShellExecute = false
            });

            manager.ExitApplication();
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
            await ShowDialogAsync("Update Error", $"Update download failed:\n{ex.Message}", yesNo: false);
        }
    }

    private static string? TryGetCurrentAppBundlePath()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return null;

        var dir = new DirectoryInfo(Path.GetDirectoryName(exe)!);
        while (dir != null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? FindMacAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string preferred = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "macos-arm64.zip"
            : "macos-x64.zip";

        string? fallbackUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string url = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url))
                continue;

            if (name.Contains(preferred, StringComparison.OrdinalIgnoreCase))
                return url;

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.Contains("macos", StringComparison.OrdinalIgnoreCase))
                fallbackUrl ??= url;
        }
        return fallbackUrl;
    }

    private static Task<bool> ShowDialogAsync(string title, string message, bool yesNo)
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        if (yesNo)
        {
            var yesBtn = new Button { Content = "Yes", Width = 76 };
            yesBtn.Click += (_, _) =>
            {
                tcs.TrySetResult(true);
                dialog.Close();
            };
            var noBtn = new Button { Content = "No", Width = 76 };
            noBtn.Click += (_, _) =>
            {
                tcs.TrySetResult(false);
                dialog.Close();
            };
            btnPanel.Children.Add(yesBtn);
            btnPanel.Children.Add(noBtn);
        }
        else
        {
            var okBtn = new Button { Content = "OK", Width = 76 };
            okBtn.Click += (_, _) =>
            {
                tcs.TrySetResult(true);
                dialog.Close();
            };
            btnPanel.Children.Add(okBtn);
        }

        panel.Children.Add(btnPanel);
        dialog.Content = panel;
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        dialog.Show();
        return tcs.Task;
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

    public static void OpenUrl(string url)
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
