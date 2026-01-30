namespace ZephyrsElixir
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    public static class Updater
    {
        private const string UpdateJsonUrl = "https://zephyrselixir.com/zupdate.json";

        private static readonly HttpClient HttpClient;

        static Updater()
        {
            HttpClient = new HttpClient();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ZephyrsElixir-Updater/{version}");
        }

        public static async Task CheckForUpdatesAsync(Window owner)
        {
            UpdateInfo? remoteInfo = await GetUpdateInfoAsync();

            if (remoteInfo == null || !IsNewVersionAvailable(remoteInfo))
                return;

            bool userAccepted = DialogService.Instance.ShowUpdate(remoteInfo, owner);

            if (userAccepted)
                await DownloadAndUpdateAsync(remoteInfo);
            else
                Application.Current.Shutdown();
        }

        private static async Task<UpdateInfo?> GetUpdateInfoAsync()
        {
            try
            {
                string json = await HttpClient.GetStringAsync(UpdateJsonUrl).ConfigureAwait(false);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<UpdateInfo>(json, options)!;
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
        
        private static async Task DownloadAndUpdateAsync(UpdateInfo remoteInfo)
        {
            string tempInstallerName = $"ZephyrsElixir_Update_{remoteInfo.Version}.exe";
            string tempInstallerPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), tempInstallerName);

            try
            {
                byte[] installerBytes = await HttpClient.GetByteArrayAsync(remoteInfo.DownloadUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempInstallerPath, installerBytes).ConfigureAwait(false);

                Process.Start(new ProcessStartInfo
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
}