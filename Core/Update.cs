namespace ZephyrsElixir.Core;

public sealed class UpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
}

public static class Updater
{
    private const string UpdateJsonUrl = AppConfiguration.Urls.ZephyrUpdateJson;

    private static readonly HttpClient HttpClient;

    static Updater()
    {
        HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ZephyrsElixir-Updater/{version}");
    }

    public static async Task CheckForUpdatesAsync(Window owner, CancellationToken ct = default)
    {
        UpdateInfo? remoteInfo = await GetUpdateInfoAsync(ct).ConfigureAwait(true);

        if (remoteInfo is null || !IsNewVersionAvailable(remoteInfo))
            return;

        // Forced-update policy: outdated versions must not keep running.
        // Declining ("Exit") closes the application; the dialog states the update is required.
        if (DialogService.Instance.ShowUpdate(remoteInfo, owner))
            await DownloadAndUpdateAsync(remoteInfo, ct).ConfigureAwait(true);
        else
            Application.Current.Shutdown();
    }

    private static async Task<UpdateInfo?> GetUpdateInfoAsync(CancellationToken ct = default)
    {
        try
        {
            string json = await HttpClient.GetStringAsync(UpdateJsonUrl, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UpdateInfo>(json, CoreJson.CaseInsensitive)!;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    private static bool IsNewVersionAvailable(UpdateInfo remoteInfo)
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion is not null && Version.TryParse(remoteInfo.Version, out var remoteVersion))
            {
                return remoteVersion > currentVersion;
            }
            return false;
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"Error comparing versions: {ex.Message}");
             return false;
        }
    }
    
    private static async Task DownloadAndUpdateAsync(UpdateInfo remoteInfo, CancellationToken ct = default)
    {
        string tempInstallerName = $"ZephyrsElixir_Update_{remoteInfo.Version.SanitizeFileName(16)}.exe";
        string tempInstallerPath = Path.Combine(Path.GetTempPath(), tempInstallerName);

        try
        {
            // The installer is downloaded and then executed, so its URL — which comes from a remote
            // manifest — is held to the same allow-list as the Pro module rather than trusted as-is.
            if (!AppConfiguration.Urls.IsTrustedDownload(remoteInfo.DownloadUrl))
                throw new InvalidOperationException(Strings.License_Error_InvalidSource);

            // Stream straight to disk: the installer is ~70 MB, never buffer it in memory.
            using (var response = await HttpClient.GetAsync(remoteInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var fileStream = new FileStream(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tempInstallerPath,
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            var config = new DialogConfig
            {
                Title = TranslationManager.Instance["Dialog_Title_Error"],
                Message = string.Format(TranslationManager.Instance["Update_DownloadFailed"], ex.Message),
                Type = DialogType.Error,
                Buttons = new[] { new DialogButton(TranslationManager.Instance["Common_Button_OK"], DialogAction.Primary, ButtonStyle.Primary) }
            };
            DialogService.Instance.ShowCustom(config);
        }
    }
}
