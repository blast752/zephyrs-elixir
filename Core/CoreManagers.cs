namespace ZephyrsElixir.Core
{
    internal static class StringHelper
    {
        public static readonly char[] Newlines = { '\r', '\n' };
        public static string[] SplitLines(this string s) => s.Split(Newlines, StringSplitOptions.RemoveEmptyEntries);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SafetyRiskLevel { Unknown, Safe, Caution, Critical }

    public enum AppState { User, System, Disabled }

    public enum StandbyBucket { Active = 10, WorkingSet = 20, Frequent = 30, Rare = 40, Restricted = 45 }

    public sealed class AppInfo
    {
        public string Name { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string Version { get; set; } = "N/A";
        public AppState State { get; set; } = AppState.User;
    }

    public sealed class PackageIntelligenceData
    {
        [JsonPropertyName("packageName")] public string PackageName { get; set; } = string.Empty;
        [JsonPropertyName("riskLevel")] public SafetyRiskLevel RiskLevel { get; set; } = SafetyRiskLevel.Unknown;
        [JsonPropertyName("safetyScore")] public double SafetyScore { get; set; } = 50.0;
        [JsonPropertyName("description")] public string Description { get; set; } = "Analyzing...";
        [JsonPropertyName("warningMessage")] public string? WarningMessage { get; set; }

        [JsonIgnore] public bool IsOfflineResult { get; set; }
    }

    public sealed class HistoryItem
    {
        public string PackageName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string IconBase64 { get; set; } = string.Empty;
        public DateTime UninstallDate { get; set; }
        public string? LocalApkPath { get; set; }
        public bool IsSystemApp { get; set; }
        public string? DeviceSerial { get; set; }
    }

    public class PermissionItem : INotifyPropertyChanged
    {
        public string PermissionKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Icon { get; init; } = "\uE774";

        private bool _isGranted;
        public bool IsGranted
        {
            get => _isGranted;
            set { if (_isGranted == value) return; _isGranted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGranted))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static class AiQuotaManager
    {
        private static readonly string QuotaFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZephyrsElixir", ".ai_quota");

        private static readonly object _lock = new();
        private static DateTime _lastResetDate = DateTime.MinValue;
        private static int _usedToday;
        private static bool _loaded;

        public static int DailyLimit => LicenseConfig.FreeAiAnalysisQuotaDaily;

        public static int RemainingToday
        {
            get
            {
                if (Features.IsAvailable(Features.AIAnalysisUnlimited)) return -1;
                EnsureLoaded();
                lock (_lock)
                {
                    ResetIfNewDay();
                    return Math.Max(0, DailyLimit - _usedToday);
                }
            }
        }

        public static bool HasQuota => Features.IsAvailable(Features.AIAnalysisUnlimited) || RemainingToday > 0;

        public static bool IsUnlimited => Features.IsAvailable(Features.AIAnalysisUnlimited);

        public static int UsedToday
        {
            get
            {
                EnsureLoaded();
                lock (_lock)
                {
                    ResetIfNewDay();
                    return _usedToday;
                }
            }
        }

        public static bool TryConsume()
        {
            if (Features.IsAvailable(Features.AIAnalysisUnlimited)) return true;

            EnsureLoaded();
            lock (_lock)
            {
                ResetIfNewDay();
                if (_usedToday >= DailyLimit) return false;
                _usedToday++;
                SaveAsync();
                return true;
            }
        }

        public static int ConsumeBatch(int count)
        {
            if (Features.IsAvailable(Features.AIAnalysisUnlimited)) return count;

            EnsureLoaded();
            lock (_lock)
            {
                ResetIfNewDay();
                var available = Math.Max(0, DailyLimit - _usedToday);
                var consumed = Math.Min(available, count);
                _usedToday += consumed;
                SaveAsync();
                return consumed;
            }
        }

        private static void ResetIfNewDay()
        {
            var today = DateTime.UtcNow.Date;
            if (_lastResetDate < today)
            {
                _lastResetDate = today;
                _usedToday = 0;
                SaveAsync();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                Load();
                _loaded = true;
            }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(QuotaFilePath)) return;
                var lines = File.ReadAllLines(QuotaFilePath);
                if (lines.Length >= 2 &&
                    DateTime.TryParse(lines[0], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var date) &&
                    int.TryParse(lines[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var used))
                {
                    _lastResetDate = date.Date;
                    _usedToday = Math.Max(0, used);
                    ResetIfNewDay();
                }
            }
            catch { /* best-effort load */ }
        }

        private static readonly object _ioLock = new();

        private static void SaveAsync()
        {
            var date = _lastResetDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var used = _usedToday.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = Task.Run(() =>
            {
                lock (_ioLock)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(QuotaFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllLines(QuotaFilePath, new[] { date, used });
                    }
                    catch { /* best-effort save */ }
                }
            });
        }

        internal static void Reset()
        {
            lock (_lock)
            {
                _lastResetDate = DateTime.UtcNow.Date;
                _usedToday = 0;
                SaveAsync();
            }
        }
    }

    public static class AdbExecutor
    {
        private static readonly string AdbPath;
        private static readonly SemaphoreSlim Semaphore = new(16);

        static AdbExecutor()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "adb", "adb.exe");
            AdbPath = File.Exists(toolsPath) ? toolsPath : "adb.exe";
        }

        public static string GetAdbPath() => AdbPath;

        public static async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default, Action<string>? onOutput = null)
        {
            await Semaphore.WaitAsync(ct);
            try
            {
                var result = await ExecuteCoreAsync(command, ct, onOutput);
                AdbLogger.Instance.LogAdbCommand(command, result, result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase));
                return result;
            }
            finally { Semaphore.Release(); }
        }

        public static string ExecuteCommand(string command) => ExecuteCommandAsync(command).GetAwaiter().GetResult();

        private static async Task<string> ExecuteCoreAsync(string command, CancellationToken ct, Action<string>? onOutput = null)
        {
            var psi = new ProcessStartInfo(AdbPath, command)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return "Error: Could not start ADB process.";

            using var reg = ct.Register(() => { try { process.Kill(true); } catch { } });

            if (onOutput != null)
            {
                var sb = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { onOutput(e.Data); sb.AppendLine(e.Data); } };
                process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { onOutput(e.Data); sb.AppendLine(e.Data); } };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return sb.ToString().Trim();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(30000);
            try { await process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { try { process.Kill(true); } catch { } return "Error: Command timeout."; }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(error) ? error.Trim() : output.Trim();
        }
        }
    }

    public sealed class DeviceManager
    {
        private static readonly Lazy<DeviceManager> _lazy = new(() => new DeviceManager());
        public static DeviceManager Instance => _lazy.Value;

        public bool IsConnected { get; private set; }
        public int BatteryLevel { get; private set; }
        public string DeviceName { get; private set; } = Strings.DeviceStatus_NoDevice;
        public string DeviceSerial { get; private set; } = string.Empty;
        public string StatusText => IsConnected ? DeviceName : Strings.DeviceStatus_NoDevice;

        public event EventHandler<bool>? DeviceStatusChanged;
        public event EventHandler<(string DeviceName, int BatteryLevel)>? DeviceInfoUpdated;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _monitoring;
        private bool _updating;

        private DeviceManager() => _timer.Tick += async (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            try { await UpdateAsync(); }
            finally { _updating = false; }
        };

        public void StartMonitoring()
        {
            if (_monitoring) return;
            _monitoring = true;
            _timer.Start();
            _ = Task.Run(async () =>
            {
                if (_updating) return;
                _updating = true;
                try { await UpdateAsync(); }
                finally { _updating = false; }
            });
        }

        public void StopMonitoring() { _monitoring = false; _timer.Stop(); }

        private async Task UpdateAsync()
        {
            try
            {
                bool was = IsConnected;
                IsConnected = await CheckConnectedAsync();

                if (was != IsConnected) DeviceStatusChanged?.Invoke(this, IsConnected);

                if (IsConnected)
                {
                    var batteryTask = GetBatteryAsync();
                    var nameTask = GetNameAsync();
                    var serialTask = GetSerialAsync();
                    await Task.WhenAll(batteryTask, nameTask, serialTask);

                    var newSerial = serialTask.Result;
                    if (batteryTask.Result != BatteryLevel || nameTask.Result != DeviceName || newSerial != DeviceSerial)
                    {
                        BatteryLevel = batteryTask.Result;
                        DeviceName = nameTask.Result;
                        DeviceSerial = newSerial;
                        DeviceInfoUpdated?.Invoke(this, (DeviceName, BatteryLevel));
                    }
                }
                else if (was)
                {
                    BatteryLevel = 0;
                    DeviceName = Strings.DeviceStatus_NoDevice;
                    DeviceSerial = string.Empty;
                    DeviceInfoUpdated?.Invoke(this, (DeviceName, BatteryLevel));
                }
            }
            catch { if (IsConnected) { IsConnected = false; DeviceStatusChanged?.Invoke(this, false); } }
        }

        public async Task<bool> CheckConnectedAsync()
        {
            var result = await AdbExecutor.ExecuteCommandAsync("devices");
            if (string.IsNullOrWhiteSpace(result)) return false;
            return result.SplitLines()
                .Skip(1).Any(l => !string.IsNullOrWhiteSpace(l) && l.Trim().EndsWith("device", StringComparison.OrdinalIgnoreCase));
        }

        public async Task<int> GetBatteryAsync()
        {
            var output = await AdbExecutor.ExecuteCommandAsync("shell dumpsys battery");
            var line = output.SplitLines()
                .FirstOrDefault(l => l.Trim().StartsWith("level:", StringComparison.OrdinalIgnoreCase));
            if (line != null && int.TryParse(line.Split(':')[1].Trim(), out int level))
                return Math.Clamp(level, 0, 100);
            return 0;
        }

        public async Task<string> GetNameAsync()
        {
            var brand = Clean(await AdbExecutor.ExecuteCommandAsync("shell getprop ro.product.brand"));
            var model = Clean(await AdbExecutor.ExecuteCommandAsync("shell getprop ro.product.model"));
            if (!string.IsNullOrEmpty(brand))
                brand = char.ToUpper(brand[0], CultureInfo.InvariantCulture) + brand[1..];
            var name = $"{brand} {model}".Trim();
            return string.IsNullOrWhiteSpace(name) ? Strings.DeviceStatus_NoDevice : name;
        }

        public async Task<string> GetSerialAsync()
        {
            var serial = Clean(await AdbExecutor.ExecuteCommandAsync("shell getprop ro.serialno"));
            if (string.IsNullOrEmpty(serial))
                serial = Clean(await AdbExecutor.ExecuteCommandAsync("get-serialno"));
            return serial;
        }

        public async Task<string> GetFullDevicePropertiesAsync() => await AdbExecutor.ExecuteCommandAsync("shell getprop");

        private static string Clean(string v) => string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim().Replace("\r", "").Replace("\n", "");
    }

    public static class PermissionManager
    {
        private static readonly Dictionary<string, (string Name, string Icon)> Known = new()
        {
            { "android.permission.CAMERA", ("Camera", "\uE722") },
            { "android.permission.ACCESS_FINE_LOCATION", ("Precise Location", "\uE81D") },
            { "android.permission.ACCESS_COARSE_LOCATION", ("Approximate Location", "\uE81D") },
            { "android.permission.RECORD_AUDIO", ("Microphone", "\uE720") },
            { "android.permission.READ_CONTACTS", ("Read Contacts", "\uE779") },
            { "android.permission.WRITE_CONTACTS", ("Write Contacts", "\uE779") },
            { "android.permission.READ_CALENDAR", ("Read Calendar", "\uE787") },
            { "android.permission.WRITE_CALENDAR", ("Write Calendar", "\uE787") },
            { "android.permission.READ_EXTERNAL_STORAGE", ("Read Storage", "\uE8B7") },
            { "android.permission.WRITE_EXTERNAL_STORAGE", ("Write Storage", "\uE8B7") },
            { "android.permission.POST_NOTIFICATIONS", ("Notifications", "\uEA8F") },
            { "android.permission.BODY_SENSORS", ("Body Sensors", "\uE9D9") }
        };

        public static async Task<List<PermissionItem>> GetAppPermissionsAsync(string pkg)
        {
            var output = await AdbExecutor.ExecuteCommandAsync($"shell dumpsys package {pkg}");
            var perms = new List<PermissionItem>();
            bool inSection = false;

            foreach (var line in output.SplitLines())
            {
                var trim = line.Trim();
                if (trim.StartsWith("runtime permissions:", StringComparison.OrdinalIgnoreCase)) { inSection = true; continue; }
                if (inSection)
                {
                    if (string.IsNullOrWhiteSpace(trim) || !trim.Contains(": granted=")) break;
                    var parts = trim.Split(':');
                    var key = parts[0].Trim();
                    var granted = parts[1].Contains("true", StringComparison.OrdinalIgnoreCase);
                    if (Known.TryGetValue(key, out var info))
                        perms.Add(new PermissionItem { PermissionKey = key, DisplayName = info.Name, Icon = info.Icon, IsGranted = granted });
                }
            }
            return perms.OrderBy(p => p.DisplayName).ToList();
        }

        public static Task SetPermissionAsync(string pkg, string perm, bool grant) =>
            AdbExecutor.ExecuteCommandAsync($"shell pm {(grant ? "grant" : "revoke")} {pkg} {perm}");

        public static async Task<StandbyBucket> GetAppStandbyBucketAsync(string pkg)
        {
            var output = await AdbExecutor.ExecuteCommandAsync($"shell am get-standby-bucket {pkg}");
            return int.TryParse(output.Trim(), out int b) && Enum.IsDefined(typeof(StandbyBucket), b) ? (StandbyBucket)b : StandbyBucket.Active;
        }

        public static Task SetAppStandbyBucketAsync(string pkg, StandbyBucket bucket) =>
            AdbExecutor.ExecuteCommandAsync($"shell am set-standby-bucket {pkg} {(int)bucket}");
    }

    public static class UninstallHistoryManager
    {
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZephyrsElixir", "Backups");
        private static readonly string HistoryFile = Path.Combine(BaseDir, "history.json");
        private static readonly SemaphoreSlim _lock = new(1, 1);

        static UninstallHistoryManager()
        {
            if (!Directory.Exists(BaseDir)) Directory.CreateDirectory(BaseDir);
        }

        public static async Task<List<HistoryItem>> LoadHistoryAsync(string? deviceSerial = null)
        {
            await _lock.WaitAsync();
            try
            {
                var all = await LoadAllInternalAsync();

                if (deviceSerial is null) return all;

                if (string.IsNullOrEmpty(deviceSerial))
                    return all.Where(h => string.IsNullOrEmpty(h.DeviceSerial)).ToList();

                return all
                    .Where(h => h.DeviceSerial == deviceSerial || string.IsNullOrEmpty(h.DeviceSerial))
                    .GroupBy(h => h.PackageName)
                    .Select(g => g.OrderByDescending(h => h.UninstallDate).First())
                    .OrderByDescending(h => h.UninstallDate)
                    .ToList();
            }
            catch { return new(); }
            finally { _lock.Release(); }
        }

        public static async Task AddEntryAsync(HistoryItem item)
        {
            await _lock.WaitAsync();
            try
            {
                var list = await LoadAllInternalAsync();

                list.RemoveAll(x =>
                    x.PackageName == item.PackageName &&
                    (x.DeviceSerial == item.DeviceSerial ||
                    (string.IsNullOrEmpty(x.DeviceSerial) && string.IsNullOrEmpty(item.DeviceSerial))));

                list.Insert(0, item);
                await SaveAsync(list);
            }
            finally { _lock.Release(); }
        }

        public static async Task RemoveEntryAsync(HistoryItem item)
        {
            await _lock.WaitAsync();
            try
            {
                var list = await LoadAllInternalAsync();
                var target = list.FirstOrDefault(x =>
                    x.PackageName == item.PackageName &&
                    x.UninstallDate == item.UninstallDate);

                if (target is null) return;

                if (!string.IsNullOrEmpty(target.LocalApkPath) && File.Exists(target.LocalApkPath))
                    try { File.Delete(target.LocalApkPath); } catch { }

                list.Remove(target);
                await SaveAsync(list);
            }
            finally { _lock.Release(); }
        }

        public static string GetBackupPath(string pkg, string ver) =>
            Path.Combine(BaseDir, $"{pkg}_{ver}.apk");

        private static async Task<List<HistoryItem>> LoadAllInternalAsync()
        {
            if (!File.Exists(HistoryFile)) return new();
            try
            {
                using var stream = File.OpenRead(HistoryFile);
                return await JsonSerializer.DeserializeAsync<List<HistoryItem>>(stream) ?? new();
            }
            catch (JsonException)
            {
                try { File.Delete(HistoryFile); } catch { }
                return new();
            }
        }

        private static async Task SaveAsync(List<HistoryItem> list)
        {
            using var stream = File.Create(HistoryFile);
            await JsonSerializer.SerializeAsync(stream, list);
        }
    }

    public static class CloudIntelligenceManager
    {
        private static readonly ConcurrentDictionary<string, PackageIntelligenceData> Cache = new();
        private static readonly HttpClient Http;
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        private const string Api = "https://elixirsite.vercel.app/api/analyze";

        static CloudIntelligenceManager()
        {
            Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            Http.DefaultRequestHeaders.Add("User-Agent", "ZephyrsElixir/2.5");
            Http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        public static event Action? QuotaExhausted;

        public static async Task AnalyzeBatchStreamAsync(IEnumerable<string> packages, Action<PackageIntelligenceData> onResult, CancellationToken ct)
        {
            var toAnalyze = new List<string>();
            var needsCloud = new List<string>();

            foreach (var pkg in packages)
            {
                if (Cache.TryGetValue(pkg, out var cached))
                {
                    onResult(cached);
                    continue;
                }

                if (TryOffline(pkg, out var offline))
                {
                    Cache.TryAdd(pkg, offline!);
                    onResult(offline!);
                    continue;
                }

                needsCloud.Add(pkg);
            }

            if (needsCloud.Count == 0) return;

            var quotaAvailable = AiQuotaManager.IsUnlimited
                ? needsCloud.Count
                : AiQuotaManager.ConsumeBatch(needsCloud.Count);

            var cloudPackages = needsCloud.Take(quotaAvailable).ToList();
            var fallbackPackages = needsCloud.Skip(quotaAvailable).ToList();

            if (fallbackPackages.Count > 0 && !AiQuotaManager.IsUnlimited)
            {
                QuotaExhausted?.Invoke();
            }

            if (cloudPackages.Count > 0)
            {
                await Parallel.ForEachAsync(cloudPackages, new ParallelOptions { MaxDegreeOfParallelism = 7, CancellationToken = ct }, async (pkg, _) =>
                {
                    var result = await FetchAsync(pkg, ct);
                    Cache.TryAdd(pkg, result);
                    onResult(result);
                });
            }

            foreach (var pkg in fallbackPackages)
            {
                var fallback = CreateQuotaFallback(pkg);
                Cache.TryAdd(pkg, fallback);
                onResult(fallback);
            }
        }

        public static async Task<PackageIntelligenceData> AnalyzeSingleAsync(string packageName, CancellationToken ct = default)
        {
            if (Cache.TryGetValue(packageName, out var cached))
                return cached;

            if (TryOffline(packageName, out var offline))
            {
                Cache.TryAdd(packageName, offline!);
                return offline!;
            }

            if (AiQuotaManager.TryConsume())
            {
                var result = await FetchAsync(packageName, ct);
                Cache.TryAdd(packageName, result);
                return result;
            }

            var fallback = CreateQuotaFallback(packageName);
            Cache.TryAdd(packageName, fallback);
            return fallback;
        }

        private static PackageIntelligenceData CreateQuotaFallback(string pkg) => new()
        {
            PackageName = pkg,
            RiskLevel = SafetyRiskLevel.Unknown,
            SafetyScore = 50,
            Description = Strings.Debloat_AI_QuotaExhausted,
            WarningMessage = null,
            IsOfflineResult = true
        };

        private static async Task<PackageIntelligenceData> FetchAsync(string pkg, CancellationToken ct)
        {
            try
            {
                var resp = await Http.PostAsJsonAsync(Api, new { packageName = pkg }, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var r = await resp.Content.ReadFromJsonAsync<PackageIntelligenceData>(JsonOpts, ct);
                    if (r != null)
                    {
                        if (string.IsNullOrEmpty(r.PackageName)) r.PackageName = pkg;
                        if (r.SafetyScore < 1 || r.SafetyScore > 100) r.SafetyScore = 50;
                        if (r.RiskLevel == SafetyRiskLevel.Unknown && !string.IsNullOrEmpty(r.Description) && r.Description != "Unavailable")
                            r.RiskLevel = r.SafetyScore <= 15 ? SafetyRiskLevel.Critical : r.SafetyScore <= 50 ? SafetyRiskLevel.Caution : SafetyRiskLevel.Safe;
                        if (r.WarningMessage is "none" or "null") r.WarningMessage = null;
                        return r;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { AdbLogger.Instance.LogWarning("CloudAI", $"Network error {pkg}: {ex.Message}"); }
            return Fallback(pkg);
        }

        private static bool TryOffline(string pkg, out PackageIntelligenceData? data)
        {
            data = null;
            var lower = pkg.ToLowerInvariant();

            if (IsCriticalPackage(lower))
            {
                data = new() { PackageName = pkg, RiskLevel = SafetyRiskLevel.Critical, SafetyScore = 5, Description = "Core System Component", WarningMessage = "REMOVAL WILL BRICK DEVICE", IsOfflineResult = true };
                return true;
            }
            if (IsSafeBloat(lower, out var desc, out var warn, out var score))
            {
                data = new() { PackageName = pkg, RiskLevel = SafetyRiskLevel.Safe, SafetyScore = score, Description = desc, WarningMessage = warn, IsOfflineResult = true };
                return true;
            }
            if (IsCaution(lower, out var cdesc))
            {
                data = new() { PackageName = pkg, RiskLevel = SafetyRiskLevel.Caution, SafetyScore = 35, Description = cdesc, WarningMessage = "May affect device functionality", IsOfflineResult = true };
                return true;
            }
            return false;
        }

        public static bool IsCriticalPackage(string packageName)
        {
            var p = packageName.ToLowerInvariant();
            return p == "android" || CriticalExact.Contains(p) || CriticalPatterns.Any(c => p.Contains(c));
        }

        private static readonly HashSet<string> CriticalExact = new()
        {
            "com.android.systemui", "com.android.phone", "com.android.settings", "com.android.launcher3",
            "com.android.inputmethod.latin", "com.android.packageinstaller", "com.android.permissioncontroller",
            "com.android.shell", "com.android.keychain", "com.android.nfc", "com.android.providers.settings",
            "com.android.providers.contacts", "com.android.providers.telephony", "com.android.providers.downloads",
            "com.android.providers.media", "com.android.providers.calendar",
            "com.android.server.telecom", "com.android.server.wifi",
            "com.android.launcher", "com.android.inputmethod",
            "com.android.biometrics", "com.android.location.fused",
            "com.google.android.gms", "com.android.vending",
            "com.google.android.inputmethod", "com.google.android.inputmethod.latin",
            "com.google.android.permissioncontroller", "com.google.android.biometrics",
            "com.samsung.android.incallui", "com.samsung.android.dialer", "com.sec.android.app.launcher",
            "com.samsung.android.honeyboard", "com.sec.android.inputmethod",
            "com.miui.home", "com.miui.securitycenter",
            "com.huawei.android.launcher", "com.huawei.systemmanager",
            "com.oppo.launcher", "com.coloros.safecenter",
            "com.bbk.launcher2"
        };

        private static readonly string[] CriticalPatterns = { "bluetooth", "telephony", "biometrics", "keyguard", "fingerprint", "facerecognition", "wifi.service", "networkstack", "tethering", "vpn", "ipsec", "proxy", "audio.service", "system_server", "webview" };

        private static readonly string[] CarrierPatterns = { "sprint", "verizon", "tmobile", "att.", "vodafone", "orange", "docomo", "softbank", "telstra", "turkcell", "claro", "movistar", "airtel", "jio." };
        private static readonly string[] SocialPatterns = { "facebook", "instagram", "tiktok", "twitter", "snapchat", "linkedin", "meta.catapult", "meta.provider" };
        private static readonly string[] AnalyticsPatterns = { "analytics", "telemetry", "tracking", "diagnostics", "crashlytics", "metrics", "appsflyer", "adjust.sdk", "braze" };
        private static readonly string[] AdPatterns = { ".ads", "admob", "advertising", "adservices", "ironsource", "applovin", "mopub" };
        private static readonly string[] PreinstalledPrefixes = { "com.facebook.", "com.instagram.", "com.netflix.", "com.spotify.", "com.amazon.", "com.microsoft.office", "com.ebay.", "com.booking.", "flipboard", "com.linkedin.", "com.tiktok." };

        private static readonly Dictionary<string, (string Desc, string Warn)> PrivacyRisks = new()
        {
            ["com.samsung.android.appcloud"] = ("Samsung AppCloud", "SPYWARE: uploads app data silently"),
            ["com.samsung.android.mobileservice"] = ("Samsung Mobile Service", "PRIVACY: background data exfiltration"),
            ["com.samsung.android.voc"] = ("Samsung Voice Pipeline", "PRIVACY: voice data collection"),
            ["com.sec.spp.push"] = ("Samsung Push Service", "PRIVACY: persistent device tracking"),
            ["com.samsung.android.bixby.agent"] = ("Bixby Agent", "PRIVACY: voice/usage data collection"),
            ["com.samsung.android.bixby.service"] = ("Bixby Service", "PRIVACY: voice/usage data collection"),
            ["com.samsung.android.game.gamehome"] = ("Game Launcher", "PRIVACY: tracks gaming habits"),
            ["com.samsung.android.game.gametools"] = ("Game Tools Overlay", "PRIVACY: gaming activity tracking"),
            ["com.samsung.android.da.daagent"] = ("Samsung Dual Messenger", "PRIVACY: app usage monitoring"),
            ["com.samsung.android.rubin.app"] = ("Samsung Customization", "PRIVACY: behavioral profiling"),
            ["com.samsung.android.samsungpass"] = ("Samsung Pass", "PRIVACY: biometric data sync"),
            ["com.miui.analytics"] = ("Xiaomi Analytics", "PRIVACY: heavy device telemetry"),
            ["com.xiaomi.mipicks"] = ("Xiaomi GetApps", "PRIVACY: app usage data"),
            ["com.miui.cloudservice"] = ("Mi Cloud Sync", "PRIVACY: cloud sync to CN servers"),
            ["com.miui.msa.global"] = ("Xiaomi Ad Service", "ADWARE: ad injection framework"),
            ["com.xiaomi.midrop"] = ("Mi Share", "PRIVACY: nearby device scanning"),
            ["com.miui.daemon"] = ("MIUI Daemon", "PRIVACY: persistent telemetry"),
            ["com.miui.yellowpage"] = ("MIUI Yellow Pages", "PRIVACY: contacts data upload"),
            ["com.huawei.hianalytics"] = ("Huawei Analytics", "PRIVACY: telemetry to CN servers"),
            ["com.huawei.hivision"] = ("HiVision Scanner", "PRIVACY: camera data processing"),
            ["com.huawei.hicloud"] = ("Huawei Cloud", "PRIVACY: cloud sync to CN servers"),
            ["com.heytap.usercenter"] = ("OPPO User Center", "PRIVACY: behavioral data collection"),
            ["com.coloros.ocs"] = ("ColorOS Cloud", "PRIVACY: device data sync"),
            ["com.google.android.adservices"] = ("Google Ad Services", "PRIVACY: cross-app ad tracking"),
        };

        private static readonly Dictionary<string, string> CautionPatterns = new()
        {
            ["camera"] = "Camera App", ["gallery"] = "Gallery App", ["keyboard"] = "Keyboard", ["email"] = "Email Client",
            ["calendar"] = "Calendar", ["contacts"] = "Contacts", ["browser"] = "Browser", ["music"] = "Music Player",
            ["video"] = "Video Player", ["filemanager"] = "File Manager", ["backup"] = "Backup Service",
            ["smartswitch"] = "Data Transfer", ["pay"] = "Payment Service", ["wallet"] = "Wallet Service",
            ["clock"] = "Clock/Alarm", ["calculator"] = "Calculator", ["updater"] = "System Updater",
            ["myfiles"] = "File Manager", ["recorder"] = "Voice Recorder"
        };

        private static readonly HashSet<string> CautionExact = new()
        {
            "com.samsung.android.app.notes", "com.samsung.android.calendar", "com.samsung.android.email.provider",
            "com.sec.android.app.myfiles", "com.sec.android.app.camera",
            "com.miui.gallery", "com.miui.player", "com.miui.miservice",
            "com.huawei.camera", "com.huawei.photos", "com.huawei.contacts",
            "com.android.chrome"
        };

        private static bool IsSafeBloat(string p, out string desc, out string? warn, out double score)
        {
            desc = "Bloatware"; warn = null; score = 95;

            if (CarrierPatterns.Any(c => p.Contains(c))) { desc = "Carrier Bloatware"; score = 95; return true; }
            if (SocialPatterns.Any(c => p.Contains(c))) { desc = "Social Media Bloat"; score = 93; return true; }
            if (AnalyticsPatterns.Any(c => p.Contains(c))) { desc = "Analytics/Tracking"; warn = "PRIVACY: collects usage data"; score = 90; return true; }
            if (AdPatterns.Any(c => p.Contains(c))) { desc = "Advertising Service"; warn = "AD TRACKING: injected ad infrastructure"; score = 92; return true; }
            if (PrivacyRisks.TryGetValue(p, out var r)) { desc = r.Desc; warn = r.Warn; score = warn != null && warn.Contains("SPYWARE") ? 93 : 85; return true; }
            if (p.Contains("bixby")) { desc = "Bixby Service"; warn = "PRIVACY: voice data collection"; score = 78; return true; }
            if (PreinstalledPrefixes.Any(c => p.StartsWith(c))) { desc = "Preinstalled App"; score = 88; return true; }
            return false;
        }

        private static bool IsCaution(string p, out string desc)
        {
            desc = "OEM Feature";
            foreach (var (k, v) in CautionPatterns) if (p.Contains(k)) { desc = v; return true; }
            if (CautionExact.Contains(p)) { desc = "OEM Core App"; return true; }
            return false;
        }

        private static PackageIntelligenceData Fallback(string p) => new() { PackageName = p, RiskLevel = SafetyRiskLevel.Unknown, SafetyScore = 50, Description = "Network unavailable" };
        public static void ClearCache() => Cache.Clear();
        public static int CacheCount => Cache.Count;
    }

    internal sealed class AgentAppInfo
    {
        [JsonPropertyName("packageName")] public string PackageName { get; set; } = string.Empty;
        [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
        [JsonPropertyName("versionName")] public string VersionName { get; set; } = "N/A";
        [JsonPropertyName("isSystemApp")] public bool IsSystemApp { get; set; }
        [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
    }

    public static class ZephyrsAgent
    {
        internal static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        private const string Pkg = "com.zephyrselixir.agent";
        private const string Activity = $"{Pkg}/.StartServiceActivity";
        private const string Uri = "http://localhost:8080";
        private const string VersionCheckUrl = "https://zephyrselixir.com/agent-version.txt";

        private static bool _running;
        private static readonly SemaphoreSlim Semaphore = new(1);
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public static async Task<bool> EnsureAgentIsRunningAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            await Semaphore.WaitAsync(ct);
            try
            {
                if (_running)
                {
                    try { await HttpClient.GetAsync($"{Uri}/apps", ct); return true; }
                    catch { _running = false; progress?.Report("Reconnecting..."); }
                }

                progress?.Report("Checking ZephyrsAgent...");

                var installedVersion = await GetInstalledVersionAsync(ct);
                var needsInstall = installedVersion is null;
                var needsUpdate = false;

                if (!needsInstall)
                {
                    var latestVersion = await GetLatestVersionAsync(ct);
                    needsUpdate = latestVersion is not null && CompareVersions(installedVersion!, latestVersion) < 0;

                    if (needsUpdate)
                        progress?.Report($"Updating agent ({installedVersion} → {latestVersion})...");
                }

                if (needsInstall || needsUpdate)
                {
                    progress?.Report(needsInstall ? "Installing ZephyrsAgent..." : "Updating ZephyrsAgent...");
                    if (!await InstallAsync(ct))
                    {
                        progress?.Report("Agent installation failed.");
                        return false;
                    }
                    progress?.Report("Agent ready.");
                }

                progress?.Report("Setting up connection...");
                await AdbExecutor.ExecuteCommandAsync("forward tcp:8080 tcp:8080", ct);
                await AdbExecutor.ExecuteCommandAsync($"shell am start -n {Activity}", ct);
                await Task.Delay(500, ct);

                try
                {
                    await HttpClient.GetAsync($"{Uri}/apps", ct);
                    _running = true;
                    progress?.Report("Agent ready.");
                    return true;
                }
                catch
                {
                    progress?.Report("Connection failed.");
                    _running = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Error: {ex.Message}");
                _running = false;
                return false;
            }
            finally { Semaphore.Release(); }
        }

        private static async Task<string?> GetInstalledVersionAsync(CancellationToken ct)
        {
            var output = await AdbExecutor.ExecuteCommandAsync($"shell \"dumpsys package {Pkg} | grep versionName\"", ct);

            if (string.IsNullOrWhiteSpace(output) || output.Contains("Unable to find"))
                return null;

            var match = output.Split('=', StringSplitOptions.RemoveEmptyEntries);
            return match.Length > 1 ? match[1].Trim() : null;
        }

        private static async Task<string?> GetLatestVersionAsync(CancellationToken ct)
        {
            try
            {
                var response = await HttpClient.GetStringAsync(VersionCheckUrl, ct);
                var version = response?.Trim();
                return !string.IsNullOrEmpty(version) ? version : GetEmbeddedVersion();
            }
            catch
            {
                return GetEmbeddedVersion();
            }
        }

        private static string? GetEmbeddedVersion()
        {
            var apkPath = GetApkPath();
            if (apkPath is null || !File.Exists(apkPath)) return null;

            var versionFile = Path.ChangeExtension(apkPath, ".version");
            if (File.Exists(versionFile))
                return File.ReadAllText(versionFile).Trim();

            return null;
        }

        private static int CompareVersions(string v1, string v2)
        {
            static int[] Parse(string v) => v.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

            var p1 = Parse(v1);
            var p2 = Parse(v2);
            var len = Math.Max(p1.Length, p2.Length);

            for (int i = 0; i < len; i++)
            {
                var a = i < p1.Length ? p1[i] : 0;
                var b = i < p2.Length ? p2[i] : 0;
                if (a != b) return a.CompareTo(b);
            }
            return 0;
        }

        private static string? GetApkPath()
        {
            var dir = Path.GetDirectoryName(AdbExecutor.GetAdbPath());
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "ZephyrsAgent.apk");
        }

        private static async Task<bool> InstallAsync(CancellationToken ct)
        {
            var apk = GetApkPath();
            if (apk is null || !File.Exists(apk)) return false;

            var remote = $"/data/local/tmp/{Pkg}.apk";
            await AdbExecutor.ExecuteCommandAsync($"push \"{apk}\" {remote}", ct);

            var result = await AdbExecutor.ExecuteCommandAsync($"shell pm install -r {remote}", ct);

            if (!result.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                await AdbExecutor.ExecuteCommandAsync($"shell pm uninstall {Pkg}", ct);
                result = await AdbExecutor.ExecuteCommandAsync($"shell pm install {remote}", ct);
            }

            await AdbExecutor.ExecuteCommandAsync($"shell rm {remote}", ct);
            return result.Contains("Success", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<List<AppInfo>> GetInstalledAppsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            progress?.Report("Retrieving app list...");
            var json = await HttpClient.GetStringAsync($"{Uri}/apps", ct);
            if (string.IsNullOrWhiteSpace(json)) { progress?.Report("Empty list."); return new(); }

            var apps = JsonSerializer.Deserialize<List<AgentAppInfo>>(json, JsonOpts);
            if (apps == null) { progress?.Report("Parse failed."); return new(); }

            progress?.Report($"Processing {apps.Count} apps...");
            return apps.Where(a => !string.IsNullOrEmpty(a.PackageName))
                .Select(a => new AppInfo
                {
                    PackageName = a.PackageName,
                    Name = string.IsNullOrWhiteSpace(a.Label) ? a.PackageName : a.Label,
                    Version = string.IsNullOrWhiteSpace(a.VersionName) ? "N/A" : a.VersionName,
                    State = !a.IsEnabled ? AppState.Disabled : a.IsSystemApp ? AppState.System : AppState.User
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static class DeviceDependentControlExtensions
    {
        public static void SetDeviceDependent(this UIElement e, bool? state = null)
        {
            if (e == null) return;
            var connected = state ?? DeviceManager.Instance.IsConnected;
            if (e is Control c) c.IsEnabled = connected;
            else e.IsEnabled = connected;
        }

        public static void SubscribeToDeviceState(this FrameworkElement e, params UIElement[] controls)
        {
            if (e == null) return;

            void Update(object? _, bool c)
            {
                e.Dispatcher.BeginInvoke(() => { foreach (var ctrl in controls) ctrl?.SetDeviceDependent(c); });
            }

            DeviceManager.Instance.DeviceStatusChanged += Update;
            e.Unloaded += (_, _) => DeviceManager.Instance.DeviceStatusChanged -= Update;

            foreach (var ctrl in controls) ctrl?.SetDeviceDependent();
        }

        public static void SubscribeToDeviceUpdates(this FrameworkElement e, Action<bool>? onStatusChanged = null, Action<string, int>? onInfoUpdated = null, params UIElement[] controls)
        {
            if (e == null) return;

            void OnStatus(object? _, bool c)
            {
                e.Dispatcher.BeginInvoke(() => { foreach (var ctrl in controls) ctrl?.SetDeviceDependent(c); onStatusChanged?.Invoke(c); });
            }
            void OnInfo(object? _, (string N, int B) i) => e.Dispatcher.BeginInvoke(() => onInfoUpdated?.Invoke(i.N, i.B));

            DeviceManager.Instance.DeviceStatusChanged += OnStatus;
            DeviceManager.Instance.DeviceInfoUpdated += OnInfo;
            e.Unloaded += (_, _) => { DeviceManager.Instance.DeviceStatusChanged -= OnStatus; DeviceManager.Instance.DeviceInfoUpdated -= OnInfo; };

            foreach (var ctrl in controls) ctrl?.SetDeviceDependent();
            if (DeviceManager.Instance.IsConnected) onInfoUpdated?.Invoke(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);
        }
    }
