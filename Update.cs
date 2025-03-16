using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace SonicsElixir
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
    }

    public static class Updater
    {
        private static readonly string updateJsonUrl = "https://elixirsite.vercel.app/update.json";

        public static async Task<UpdateInfo> GetUpdateInfoAsync()
        {
            using (var httpClient = new HttpClient())
            {
                string json = await httpClient.GetStringAsync(updateJsonUrl);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<UpdateInfo>(json, options);
            }
        }

        public static bool IsNewVersionAvailable(UpdateInfo remoteInfo)
        {
            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            Version remoteVersion;
            if (Version.TryParse(remoteInfo.Version, out remoteVersion))
            {
                return remoteVersion > currentVersion;
            }
            return false;
        }

        public static async Task DownloadAndUpdateAsync(UpdateInfo remoteInfo)
        {
            string tempInstallerPath = Path.Combine(Path.GetTempPath(), "SonicElixir_Update.exe");
            using (var client = new HttpClient())
            {
                byte[] installerBytes = await client.GetByteArrayAsync(remoteInfo.DownloadUrl);
                File.WriteAllBytes(tempInstallerPath, installerBytes);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = tempInstallerPath,
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
    }
}
