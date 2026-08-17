namespace ZephyrsElixir.Core;

public static class StringHelper
{
    private static readonly char[] Newlines = { '\r', '\n' };
    public static string[] SplitLines(this string s) => s.Split(Newlines, StringSplitOptions.RemoveEmptyEntries);

    public static string SanitizeFileName(this string value, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}

internal static class CoreJson
{
    internal static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
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
    public string Icon { get; init; } = "globe";

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
        AppConfiguration.Paths.LocalAppDataRoot, ".ai_quota");

    private static readonly object _lock = new();
    private static readonly object _ioLock = new();
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

    /// <summary>
    /// Gives back allowance that was reserved for a request the cloud never answered. Quota is
    /// taken up-front (the batch is sized before any call leaves), so without this a single
    /// offline session would silently spend the whole day's analyses on nothing.
    /// </summary>
    public static void Refund(int count)
    {
        if (count <= 0 || Features.IsAvailable(Features.AIAnalysisUnlimited)) return;

        EnsureLoaded();
        lock (_lock)
        {
            ResetIfNewDay();
            _usedToday = Math.Max(0, _usedToday - count);
            SaveAsync();
        }
    }

    /// <summary>
    /// Spends the rest of today's allowance in one go. Used when the service refuses a request for
    /// quota reasons, so the counter shown in the app matches the one the server actually applies.
    /// </summary>
    public static void MarkExhausted()
    {
        if (Features.IsAvailable(Features.AIAnalysisUnlimited)) return;

        EnsureLoaded();
        lock (_lock)
        {
            ResetIfNewDay();
            if (_usedToday >= DailyLimit) return;
            _usedToday = DailyLimit;
            SaveAsync();
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
                DateTime.TryParse(lines[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) &&
                int.TryParse(lines[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var used))
            {
                _lastResetDate = date.Date;
                _usedToday = Math.Max(0, used);
                ResetIfNewDay();
            }
        }
        catch { /* best-effort load */ }
    }

    private static void SaveAsync()
    {
        var date = _lastResetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var used = _usedToday.ToString(CultureInfo.InvariantCulture);
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
}

public static class AdbExecutor
{
    private const int MaxConcurrentCommands = 16;
    private const int CommandTimeoutMs = 30_000;
    private const int TransferTimeoutMs = 180_000;
    private const string AdbExeName = "adb.exe";

    private static readonly string AdbPath;
    private static readonly SemaphoreSlim Semaphore = new(MaxConcurrentCommands);
    private static readonly AsyncLocal<string?> _ambientSerial = new();

    // Commands addressed to the adb server itself rather than to a device: these must never
    // receive a -s target or they fail outright (connect/pair) or change meaning (devices).
    private static readonly HashSet<string> ServerCommands = new(StringComparer.OrdinalIgnoreCase)
        { "devices", "version", "start-server", "kill-server", "connect", "disconnect", "pair", "mdns", "keygen" };

    static AdbExecutor()
    {
        var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfiguration.Paths.AdbDir, AdbExeName);
        AdbPath = File.Exists(toolsPath) ? toolsPath : AdbExeName;
    }

    public static string GetAdbPath() => AdbPath;

    /// <summary>
    /// Device serial that flows with the current async context. Set by multi-device workflows
    /// (e.g. a recipe running on several phones in parallel) so every adb call in that flow —
    /// including calls made by the Pro module, which has no serial parameter — targets the right
    /// device without threading a serial through every signature.
    /// </summary>
    public static string? AmbientSerial
    {
        get => _ambientSerial.Value;
        set => _ambientSerial.Value = value;
    }

    /// <summary>
    /// Resolution order for the target device: explicit parameter → ambient async-local serial →
    /// the globally active device. With a single device connected the -s prefix is redundant but
    /// harmless; with several it is what keeps every feature of the app working.
    /// </summary>
    private static string ApplyTarget(string command, string? serial)
    {
        var target = serial ?? AmbientSerial;
        if (string.IsNullOrEmpty(target)) target = DeviceManager.SharedActiveSerial;
        if (string.IsNullOrEmpty(target)) return command;

        var trimmed = command.TrimStart();
        if (trimmed.StartsWith("-s ", StringComparison.Ordinal)) return command;

        var space = trimmed.IndexOf(' ');
        var first = space < 0 ? trimmed : trimmed[..space];
        return ServerCommands.Contains(first) ? command : $"-s {target} {command}";
    }

    // Binary-compatible signature: the Pro module ships precompiled against it, so the
    // long-timeout variant is a separate method rather than an added optional parameter.
    public static Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default, Action<string>? onOutput = null, bool log = true, string? serial = null)
        => ExecuteTimedAsync(command, ct, onOutput, log, serial, CommandTimeoutMs);

    /// <summary>File transfers and installs (push/pull/install) get a generous timeout so a
    /// large APK over a slow link is never killed by the standard 30s command cap.</summary>
    public static Task<string> ExecuteTransferAsync(string command, CancellationToken ct = default, string? serial = null)
        => ExecuteTimedAsync(command, ct, null, true, serial, TransferTimeoutMs);

    /// <summary>Executor handed to the Pro module, which has no timeout parameter of its own:
    /// transfers pick the generous cap so a DLL-driven APK pull or install is not killed at 30s.</summary>
    public static Task<string> ExecuteModuleAsync(string command, CancellationToken ct = default)
        => ExecuteTimedAsync(command, ct, null, true, null, IsTransfer(command) ? TransferTimeoutMs : CommandTimeoutMs);

    private static bool IsTransfer(string command)
    {
        var trimmed = command.TrimStart();
        return trimmed.StartsWith("pull", StringComparison.Ordinal)
            || trimmed.StartsWith("push", StringComparison.Ordinal)
            || trimmed.StartsWith("install", StringComparison.Ordinal)
            || trimmed.Contains("pm install", StringComparison.Ordinal);
    }

    private static async Task<string> ExecuteTimedAsync(string command, CancellationToken ct, Action<string>? onOutput, bool log, string? serial, int timeoutMs)
    {
        // ConfigureAwait(false): keeps the whole method off the captured (UI) context so the
        // synchronous ExecuteCommand() wrapper can't deadlock if the semaphore is contended.
        await Semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var targeted = ApplyTarget(command, serial);
            var result = await ExecuteCoreAsync(targeted, ct, onOutput, timeoutMs);
            if (log)
                AdbLogger.Instance.LogAdbCommand(targeted, result, IsLikelyFailure(result));
            return result;
        }
        finally { Semaphore.Release(); }
    }

    public static string ExecuteCommand(string command) => ExecuteCommandAsync(command).GetAwaiter().GetResult();

    private static readonly string[] FailureMarkers =
        { "error", "failure", "failed", "denied", "not found", "unauthorized", "no devices", "cannot", "unable", "exception" };

    // Decides whether an adb result is worth keeping in diagnostics with its full output. Catches
    // both the explicit "Error:" results this class emits and the stderr that most adb failures
    // print, while letting routine successful output stay out of the log.
    private static bool IsLikelyFailure(string result) =>
        !string.IsNullOrEmpty(result) &&
        FailureMarkers.Any(m => result.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ExecuteCoreAsync(string command, CancellationToken ct, Action<string>? onOutput, int timeoutMs)
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
        cts.CancelAfter(timeoutMs);
        try { await process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Killing the process breaks both reads: neither task is awaited from here on, so
            // observe them explicitly or a faulted read resurfaces as an unobserved exception.
            try { process.Kill(true); } catch { }
            Observe(outputTask);
            Observe(errorTask);
            if (ct.IsCancellationRequested) throw;
            return "Error: Command timeout.";
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        // Modern adb separates the streams (shell_v2): a command can print its result on stdout
        // while also emitting warnings on stderr. Both must survive — dropping stdout when stderr
        // is non-empty would turn a successful install ("Success" + warning) into a false failure.
        if (string.IsNullOrWhiteSpace(error)) return output.Trim();
        if (string.IsNullOrWhiteSpace(output)) return error.Trim();
        return $"{output.Trim()}\n{error.Trim()}";
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

/// <summary>
/// Central raw-adb-output → human explanation mapper. Every failure surface in the app funnels
/// through here so the same adb error is always explained the same localized way, with the fix
/// the user actually needs (including OEM quirks like MIUI's "Install via USB" switch).
/// </summary>
public static class AdbErrorCatalog
{
    private static readonly (string[] Patterns, Func<string> Message)[] Entries =
    {
        (new[] { "unauthorized" }, () => Strings.AdbError_Unauthorized),
        (new[] { "device offline" }, () => Strings.AdbError_Offline),
        (new[] { "no devices/emulators", "device not found", "no devices found" }, () => Strings.AdbError_NoDevice),
        (new[] { "more than one device" }, () => Strings.AdbError_MultipleDevices),
        (new[] { "Command timeout" }, () => Strings.AdbError_Timeout),
        (new[] { "INSTALL_FAILED_USER_RESTRICTED" }, () => Strings.AdbError_UserRestricted),
        (new[] { "INSTALL_FAILED_VERSION_DOWNGRADE" }, () => Strings.AdbError_VersionDowngrade),
        (new[] { "INSTALL_FAILED_INSUFFICIENT_STORAGE" }, () => Strings.AdbError_InsufficientStorage),
        (new[] { "INSTALL_FAILED_UPDATE_INCOMPATIBLE", "INSTALL_FAILED_ALREADY_EXISTS", "signatures do not match" }, () => Strings.AdbError_SignatureMismatch),
        (new[] { "INSTALL_PARSE_FAILED_NO_CERTIFICATES" }, () => Strings.AdbError_NotSigned),
        (new[] { "INSTALL_PARSE_FAILED" }, () => Strings.AdbError_ParseFailed),
        (new[] { "INSTALL_FAILED_VERIFICATION" }, () => Strings.AdbError_VerificationBlocked),
        (new[] { "INSTALL_FAILED_MISSING_SPLIT" }, () => Strings.AdbError_MissingSplit),
        (new[] { "INSTALL_FAILED_TEST_ONLY" }, () => Strings.AdbError_TestOnly),
        (new[] { "INSTALL_FAILED_OLDER_SDK", "INSTALL_FAILED_NEWER_SDK" }, () => Strings.AdbError_SdkMismatch),
        (new[] { "not installed for user" }, () => Strings.AdbError_NotInstalledForUser),
        (new[] { "Permission denied", "SecurityException", "does not have permission" }, () => Strings.AdbError_PermissionDenied)
    };

    /// <summary>Localized explanation + fix for a raw adb output, or null when nothing matches.</summary>
    public static string? Explain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var (patterns, message) in Entries)
            if (patterns.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return message();
        return null;
    }

    /// <summary>The explanation when one exists, otherwise the caller's own fallback text.</summary>
    public static string Humanize(string? raw, string fallback) => Explain(raw) ?? fallback;

    /// <summary>Fallback message with the explanation appended when the raw output is recognized.</summary>
    public static string Enrich(string fallback, string? raw) =>
        Explain(raw) is { } explanation ? $"{fallback}\n\n{explanation}" : fallback;
}

public sealed class AndroidDevice
{
    public string Serial { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int BatteryLevel { get; init; }
    public bool IsAuthorized { get; init; } = true;
    public bool IsWireless => Serial.Contains(':');

    public bool SameAs(AndroidDevice other) =>
        Serial == other.Serial && Name == other.Name &&
        BatteryLevel == other.BatteryLevel && IsAuthorized == other.IsAuthorized;
}

public sealed class DeviceManager
{
    private static readonly Lazy<DeviceManager> _lazy = new(() => new DeviceManager());
    public static DeviceManager Instance => _lazy.Value;

    // Static so AdbExecutor can read the active target without forcing the singleton (and its
    // DispatcherTimer) to be constructed on a background thread.
    private static volatile string _activeSerial = string.Empty;
    internal static string SharedActiveSerial => _activeSerial;

    private IReadOnlyList<AndroidDevice> _devices = Array.Empty<AndroidDevice>();

    public bool IsConnected { get; private set; }
    public int BatteryLevel { get; private set; }
    public string DeviceName { get; private set; } = Strings.DeviceStatus_NoDevice;
    public string DeviceSerial { get; private set; } = string.Empty;
    public string StatusText => IsConnected ? DeviceName : Strings.DeviceStatus_NoDevice;

    public IReadOnlyList<AndroidDevice> Devices => _devices;
    public string ActiveSerial => _activeSerial;
    public AndroidDevice? ActiveDevice => _devices.FirstOrDefault(d => d.Serial == _activeSerial);

    public event EventHandler<bool>? DeviceStatusChanged;
    public event EventHandler<(string DeviceName, int BatteryLevel)>? DeviceInfoUpdated;
    public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesChanged;
    public event EventHandler<string>? ActiveDeviceChanged;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _monitoring;
    private int _updating;

    private DeviceManager() => _timer.Tick += async (_, _) => await GuardedUpdateAsync();

    private async Task GuardedUpdateAsync()
    {
        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0) return;
        try { await UpdateAsync(); }
        finally { Interlocked.Exchange(ref _updating, 0); }
    }

    public void StartMonitoring()
    {
        if (_monitoring) return;
        _monitoring = true;
        _timer.Start();
        _ = Task.Run(GuardedUpdateAsync);
    }

    public void StopMonitoring() { _monitoring = false; _timer.Stop(); }

    /// <summary>
    /// Makes <paramref name="serial"/> the device every serial-less adb call targets.
    /// Legacy single-device properties and events follow it so existing pages update in place.
    /// </summary>
    public void SetActiveDevice(string serial)
    {
        var device = _devices.FirstOrDefault(d => d.Serial == serial && d.IsAuthorized);
        if (device is null || _activeSerial == serial) return;

        _activeSerial = serial;
        ApplyActiveDevice(device);
        DevicesChanged?.Invoke(this, _devices);
        ActiveDeviceChanged?.Invoke(this, serial);
    }

    private void ApplyActiveDevice(AndroidDevice device)
    {
        BatteryLevel = device.BatteryLevel;
        DeviceName = device.Name;
        DeviceSerial = device.Serial;
        DeviceInfoUpdated?.Invoke(this, (DeviceName, BatteryLevel));
    }

    /// <summary>
    /// Two-phase refresh: connection state and the device list are published as soon as the cheap
    /// <c>adb devices</c> round-trip returns (serials already carry the last known name/battery),
    /// then the per-device shell fetch enriches them. Detection latency stays at the cost of one
    /// adb spawn — never gated on a freshly-connected device's first (slow) shell session.
    /// </summary>
    private async Task UpdateAsync()
    {
        try
        {
            var entries = await ListDeviceEntriesAsync();

            var known = _devices.ToDictionary(d => d.Serial);
            var provisional = entries.Select(e => e.State == "device"
                ? known.TryGetValue(e.Serial, out var prev) && prev.IsAuthorized
                    ? prev
                    : new AndroidDevice { Serial = e.Serial, Name = e.Serial }
                : known.TryGetValue(e.Serial, out var prevUnauthorized) && !prevUnauthorized.IsAuthorized
                    ? prevUnauthorized
                    : new AndroidDevice { Serial = e.Serial, Name = Strings.Devices_Unauthorized, IsAuthorized = false })
                .ToList();

            Publish(provisional);

            if (provisional.Any(d => d.IsAuthorized))
            {
                var enriched = await Task.WhenAll(provisional.Select(d =>
                    d.IsAuthorized ? FetchDeviceInfoAsync(d.Serial) : Task.FromResult(d)));
                Publish(enriched);
            }
        }
        catch { Publish(Array.Empty<AndroidDevice>()); }
    }

    private void Publish(IReadOnlyList<AndroidDevice> devices)
    {
        var ready = devices.Where(d => d.IsAuthorized).ToList();

        bool was = IsConnected;
        IsConnected = ready.Count > 0;

        var previous = _devices;
        _devices = devices;

        var previousActive = _activeSerial;
        if (ready.All(d => d.Serial != _activeSerial))
            _activeSerial = ready.FirstOrDefault()?.Serial ?? string.Empty;

        if (was != IsConnected) DeviceStatusChanged?.Invoke(this, IsConnected);

        // The active device can also change without user action (the active one was unplugged
        // while another stays connected): pages pinned to the active device must know either way.
        if (_activeSerial != previousActive && _activeSerial.Length > 0)
            ActiveDeviceChanged?.Invoke(this, _activeSerial);

        if (IsConnected)
        {
            var active = ActiveDevice!;
            if (active.BatteryLevel != BatteryLevel || active.Name != DeviceName || active.Serial != DeviceSerial)
                ApplyActiveDevice(active);
        }
        else if (was)
        {
            BatteryLevel = 0;
            DeviceName = Strings.DeviceStatus_NoDevice;
            DeviceSerial = string.Empty;
            DeviceInfoUpdated?.Invoke(this, (DeviceName, BatteryLevel));
        }

        if (previous.Count != devices.Count || !previous.Zip(devices).All(p => p.First.SameAs(p.Second)))
            DevicesChanged?.Invoke(this, devices);
    }

    public async Task<bool> CheckConnectedAsync()
    {
        var result = await AdbExecutor.ExecuteCommandAsync("devices", log: false);
        if (string.IsNullOrWhiteSpace(result)) return false;
        return result.SplitLines()
            .Skip(1).Any(l => !string.IsNullOrWhiteSpace(l) && l.Trim().EndsWith("device", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<List<(string Serial, string State)>> ListDeviceEntriesAsync()
    {
        var output = await AdbExecutor.ExecuteCommandAsync("devices", log: false);
        if (string.IsNullOrWhiteSpace(output)) return new();

        return output.SplitLines()
            .Skip(1)
            .Select(l => l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2)
            .Select(p => (Serial: p[0], State: p[1]))
            .Where(e => e.State is "device" or "unauthorized")
            .ToList();
    }

    /// <summary>
    /// Single batched shell round-trip per device on the 2s polling tick: battery, brand and
    /// model in one adb.exe spawn instead of three. Parsing semantics are unchanged.
    /// </summary>
    private static async Task<AndroidDevice> FetchDeviceInfoAsync(string serial)
    {
        const string Sep = "ZE-SEP";
        var output = await AdbExecutor.ExecuteCommandAsync(
            $"shell \"dumpsys battery; echo {Sep}; getprop ro.product.brand; echo {Sep}; getprop ro.product.model\"",
            log: false, serial: serial);

        var sections = output.Split(Sep, StringSplitOptions.None);

        int battery = 0;
        var line = sections[0].SplitLines()
            .FirstOrDefault(l => l.Trim().StartsWith("level:", StringComparison.OrdinalIgnoreCase));
        if (line is not null && int.TryParse(line.Split(':')[1].Trim(), out int level))
            battery = Math.Clamp(level, 0, 100);

        var brand = sections.Length > 1 ? Clean(sections[1]) : string.Empty;
        var model = sections.Length > 2 ? Clean(sections[2]) : string.Empty;
        if (!string.IsNullOrEmpty(brand))
            brand = char.ToUpper(brand[0], CultureInfo.InvariantCulture) + brand[1..];
        var name = $"{brand} {model}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = serial;

        return new AndroidDevice { Serial = serial, Name = name, BatteryLevel = battery };
    }

    public async Task<string> GetFullDevicePropertiesAsync() => await AdbExecutor.ExecuteCommandAsync("shell getprop");

    private static string Clean(string v) => string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim().Replace("\r", "").Replace("\n", "");
}

/// <summary>
/// Android-version-aware command surface: resolves the device API level once per serial and
/// picks the right pm/cmd syntax so every feature stays compatible from Android 5 to current.
/// </summary>
public static class DeviceApi
{
    public const int Lollipop = 21, Oreo = 26, Pie = 28;

    private static readonly ConcurrentDictionary<string, int> SdkCache = new();

    public static async Task<int> GetSdkAsync(string? serial = null, CancellationToken ct = default)
    {
        var key = serial ?? DeviceManager.SharedActiveSerial;
        if (string.IsNullOrEmpty(key)) return 0;
        if (SdkCache.TryGetValue(key, out var cached)) return cached;

        var output = await AdbExecutor.ExecuteCommandAsync("shell getprop ro.build.version.sdk", ct, log: false, serial: key);
        if (!int.TryParse(output.Trim(), out var sdk) || sdk <= 0) return 0;
        SdkCache[key] = sdk;
        return sdk;
    }

    private static bool AtLeast(int sdk, int level) => sdk == 0 || sdk >= level;

    public static string DisableCommand(int sdk, string pkg) =>
        AtLeast(sdk, Lollipop) ? $"shell pm disable-user --user 0 {pkg}" : $"shell pm disable-user {pkg}";

    public static string EnableCommand(int sdk, string pkg) =>
        AtLeast(sdk, Lollipop) ? $"shell pm enable --user 0 {pkg}" : $"shell pm enable {pkg}";

    public static string UninstallCommand(int sdk, string pkg, bool keepForRestore) =>
        keepForRestore && AtLeast(sdk, Lollipop) ? $"shell pm uninstall -k --user 0 {pkg}" : $"shell pm uninstall {pkg}";

    public static bool SupportsInstallExisting(int sdk) => AtLeast(sdk, Oreo);

    public static string InstallExistingCommand(string pkg) => $"shell cmd package install-existing --user 0 {pkg}";

    public static bool SupportsStandbyBucket(int sdk) => AtLeast(sdk, Pie);

    public static async Task<IReadOnlyList<string>> GetPackagePathsAsync(string pkg, string? serial = null, CancellationToken ct = default)
    {
        var output = await AdbExecutor.ExecuteCommandAsync($"shell pm path {pkg}", ct, serial: serial);
        return output.SplitLines()
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("package:", StringComparison.Ordinal))
            .Select(l => l["package:".Length..].Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    public static async Task<HashSet<string>> GetSystemPackagesAsync(string? serial = null, CancellationToken ct = default)
    {
        var output = await AdbExecutor.ExecuteCommandAsync("shell pm list packages -s", ct, log: false, serial: serial);
        return output.SplitLines()
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("package:", StringComparison.Ordinal))
            .Select(l => l["package:".Length..].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSuccess(string result) =>
        result.Contains("success", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("new state", StringComparison.OrdinalIgnoreCase);
}

public static class PermissionManager
{
    private static readonly Dictionary<string, (string Name, string Icon)> Known = new()
    {
        { "android.permission.CAMERA", ("Camera", "camera") },
        { "android.permission.ACCESS_FINE_LOCATION", ("Precise Location", "location") },
        { "android.permission.ACCESS_COARSE_LOCATION", ("Approximate Location", "location") },
        { "android.permission.RECORD_AUDIO", ("Microphone", "microphone") },
        { "android.permission.READ_CONTACTS", ("Read Contacts", "contacts") },
        { "android.permission.WRITE_CONTACTS", ("Write Contacts", "contacts") },
        { "android.permission.READ_CALENDAR", ("Read Calendar", "calendar") },
        { "android.permission.WRITE_CALENDAR", ("Write Calendar", "calendar") },
        { "android.permission.READ_EXTERNAL_STORAGE", ("Read Storage", "folder") },
        { "android.permission.WRITE_EXTERNAL_STORAGE", ("Write Storage", "folder") },
        { "android.permission.POST_NOTIFICATIONS", ("Notifications", "bell") },
        { "android.permission.BODY_SENSORS", ("Body Sensors", "thermometer") }
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
        return int.TryParse(output.Trim(), out int b) && Enum.IsDefined((StandbyBucket)b) ? (StandbyBucket)b : StandbyBucket.Active;
    }

    public static Task SetAppStandbyBucketAsync(string pkg, StandbyBucket bucket) =>
        AdbExecutor.ExecuteCommandAsync($"shell am set-standby-bucket {pkg} {(int)bucket}");
}

public static class UninstallHistoryManager
{
    private static readonly string BaseDir = Path.Combine(
        AppConfiguration.Paths.LocalAppDataRoot, "Backups");
    private static readonly string HistoryFile = Path.Combine(BaseDir, "history.json");
    private static readonly SemaphoreSlim _lock = new(1, 1);

    static UninstallHistoryManager()
    {
        try { Directory.CreateDirectory(BaseDir); } catch { }
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

            DeleteBackup(target.LocalApkPath);
            list.Remove(target);
            await SaveAsync(list);
        }
        finally { _lock.Release(); }
    }

    public static string GetBackupPath(string pkg, string ver) =>
        Path.Combine(BaseDir, $"{Sanitize(pkg)}_{Sanitize(ver)}.apk");

    public static string GetBackupDirectory(string pkg, string ver)
    {
        var dir = Path.Combine(BaseDir, $"{Sanitize(pkg)}_{Sanitize(ver)}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool BackupExists(string? path) =>
        !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

    public static IReadOnlyList<string> GetBackupFiles(string path) =>
        File.Exists(path) ? new[] { path }
        : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.apk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
        : Array.Empty<string>();

    public static void DeleteBackup(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static string Sanitize(string value) => value.SanitizeFileName();

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
        Directory.CreateDirectory(BaseDir);
        var temp = HistoryFile + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, list);
        File.Move(temp, HistoryFile, overwrite: true);
    }
}

public sealed class SettingChange
{
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? VialValue { get; init; }
    public string? CurrentValue { get; init; }
    public bool IsProtected { get; init; }
}

public sealed class SettingsSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TakenUtc { get; set; }
    public string DeviceSerial { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Trigger { get; set; } = SettingsTimeMachine.TriggerManual;
    public string? Label { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, string>> Namespaces { get; set; } = new();

    [JsonIgnore] public string? FilePath { get; set; }
    [JsonIgnore] public int SettingCount => Namespaces.Sum(n => n.Value.Count);
}

internal sealed class LedgerEntry
{
    public HashSet<string> Ops { get; set; } = new(StringComparer.Ordinal);
    public string? BaselineVialPath { get; set; }
}

/// <summary>
/// The undo ledger behind "Reset all" — a per-device, on-disk record of every reversible change
/// Optimize and Advanced applied. It outlives the process, so a device tuned yesterday can still
/// be put back today, and it pins the vial bottled just before the first change so those settings
/// are poured back to their real previous values instead of guessed defaults.
/// Debloat is deliberately outside its scope: uninstalls are undone from the History tab.
/// </summary>
public static class OperationLedger
{
    public static class Ops
    {
        public const string Optimization = "optimization", Compilation = "compilation", Battery = "battery_spoofed",
            Dns = "dns", Animations = "animations", SafetyCore = "safety_core", AdId = "ad_id",
            CaptivePortal = "captive_portal", GoogleCore = "google_core", RamExpansion = "ram_expansion";
    }

    // The only `settings` keys the app writes on its own initiative. A restore touches exactly
    // these, so anything the user changed on the phone meanwhile is never silently reverted.
    public static readonly string[] NetworkKeys =
        { "wifi_watchdog_poor_network_test_enabled", "network_recommendations_enabled", "wifi_scan_always_enabled", "ble_scan_always_enabled" };
    public static readonly string[] AnimationKeys =
        { "animator_duration_scale", "transition_animation_scale", "window_animation_scale" };
    public static readonly string[] DnsKeys = { "private_dns_mode", "private_dns_specifier" };

    private static readonly HashSet<string> OwnedKeys =
        new(NetworkKeys.Concat(AnimationKeys).Concat(DnsKeys), StringComparer.OrdinalIgnoreCase);

    // Single source of truth for "which Pro toggle maps to which undo entry", shared by the
    // Advanced cards and by recipes so neither can drift from the other.
    private static readonly Dictionary<string, string> ProCommandOps = new(StringComparer.Ordinal)
    {
        [ProCommandIds.SafetyCore] = Ops.SafetyCore,
        [ProCommandIds.ResetAdId] = Ops.AdId,
        [ProCommandIds.CaptivePortal] = Ops.CaptivePortal,
        [ProCommandIds.GoogleCoreControl] = Ops.GoogleCore,
        [ProCommandIds.RamExpansion] = Ops.RamExpansion
    };

    public static string? OpForProCommand(string commandId) =>
        ProCommandOps.TryGetValue(commandId, out var op) ? op : null;

    private static readonly string BaseDir = Path.Combine(AppConfiguration.Paths.LocalAppDataRoot, "Operations");
    private static readonly object Gate = new();

    public static event Action? Changed;

    public static bool Owns(string key) => OwnedKeys.Contains(key);

    public static IReadOnlyCollection<string> Get(string? serial)
    {
        lock (Gate) return Load(serial)?.Ops.ToArray() ?? Array.Empty<string>();
    }

    public static int Count(string? serial) => Get(serial).Count;

    public static string? BaselineVialPath(string? serial)
    {
        lock (Gate) return Load(serial)?.BaselineVialPath;
    }

    /// <summary>Records an applied change, pinning the pre-change vial the first time round.</summary>
    public static void Track(string? serial, string op) => Mutate(serial, entry =>
    {
        var isFirst = entry.Ops.Count == 0;
        if (!entry.Ops.Add(op) && !isFirst) return false;
        if (isFirst || entry.BaselineVialPath is null)
            entry.BaselineVialPath = SettingsTimeMachine.NewestVialPath(serial);
        return true;
    });

    /// <summary>Drops a single change after it has been reverted on its own card.</summary>
    public static void Forget(string? serial, string op) => Mutate(serial, entry => entry.Ops.Remove(op));

    public static void Clear(string? serial) => Mutate(serial, entry =>
    {
        if (entry.Ops.Count == 0 && entry.BaselineVialPath is null) return false;
        entry.Ops.Clear();
        entry.BaselineVialPath = null;
        return true;
    });

    private static void Mutate(string? serial, Func<LedgerEntry, bool> change)
    {
        var key = Resolve(serial);
        if (key is null) return;

        lock (Gate)
        {
            var entry = Load(key) ?? new LedgerEntry();
            if (!change(entry)) return;
            Save(key, entry);
        }
        Changed?.Invoke();
    }

    private static string? Resolve(string? serial)
    {
        var target = string.IsNullOrEmpty(serial) ? DeviceManager.SharedActiveSerial : serial;
        return string.IsNullOrEmpty(target) ? null : target;
    }

    private static LedgerEntry? Load(string? serial)
    {
        var key = Resolve(serial);
        if (key is null) return null;
        try
        {
            var path = PathFor(key);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<LedgerEntry>(File.ReadAllText(path), CoreJson.CaseInsensitive)
                : null;
        }
        catch { return null; }
    }

    private static void Save(string serial, LedgerEntry entry)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllText(PathFor(serial), JsonSerializer.Serialize(entry));
        }
        catch { }
    }

    private static string PathFor(string serial) => Path.Combine(BaseDir, $"{serial.SanitizeFileName()}.json");
}

/// <summary>
/// Time Vials — the app-wide settings journal. Bottles the device's global/secure/system settings
/// before every experiment (optimization, recipe, advanced tweak) so any change made anywhere in
/// the app can be inspected and poured back later. Identical consecutive states are deduplicated
/// by content hash, so automatic captures never flood the shelf.
/// </summary>
public static class SettingsTimeMachine
{
    public const string TriggerOptimize = "optimize";
    public const string TriggerRecipe = "recipe";
    public const string TriggerAdvanced = "advanced";
    public const string TriggerManual = "manual";

    private const int MaxVialsPerDevice = 15;
    private const string Sep = "ZE-NS";
    private static readonly string[] NamespaceNames = { "global", "secure", "system" };
    private static readonly string BaseDir = Path.Combine(AppConfiguration.Paths.LocalAppDataRoot, "TimeVials");
    private static readonly SemaphoreSlim _lock = new(1, 1);

    // Restoring these could sever the adb connection or re-trigger device setup — they are shown
    // in the diff but never written back.
    private static readonly HashSet<string> ProtectedKeys = new(StringComparer.OrdinalIgnoreCase)
    { "adb_enabled", "development_settings_enabled", "device_provisioned", "user_setup_complete", "android_id" };

    public static event Action? VialsChanged;

    public static async Task<SettingsSnapshot?> CaptureAsync(string trigger, string? serial = null, string? label = null, string? deviceName = null, CancellationToken ct = default)
    {
        try
        {
            var target = serial ?? DeviceManager.SharedActiveSerial;
            if (string.IsNullOrEmpty(target)) return null;

            var namespaces = await ReadNamespacesAsync(target, ct);
            if (namespaces is null) return null;

            var hash = ComputeHash(namespaces);

            await _lock.WaitAsync(ct);
            try
            {
                var existing = LoadForSerial(target);
                if (existing.FirstOrDefault() is { } latest && latest.ContentHash == hash)
                    return latest;

                var snapshot = new SettingsSnapshot
                {
                    TakenUtc = DateTime.UtcNow,
                    DeviceSerial = target,
                    DeviceName = string.IsNullOrWhiteSpace(deviceName) ? target : deviceName,
                    Trigger = trigger,
                    Label = label,
                    ContentHash = hash,
                    Namespaces = namespaces
                };

                var dir = Path.Combine(BaseDir, target.SanitizeFileName());
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{snapshot.TakenUtc:yyyyMMdd_HHmmssfff}.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot), ct);
                snapshot.FilePath = path;

                // The vial pinned as the undo baseline outlives the cap: evicting it would leave
                // "Reset all" with nothing to pour back.
                var pinned = OperationLedger.BaselineVialPath(target);
                foreach (var old in existing.Skip(MaxVialsPerDevice - 1))
                    if (!string.Equals(old.FilePath, pinned, StringComparison.OrdinalIgnoreCase))
                        TryDeleteFile(old.FilePath);

                return snapshot;
            }
            finally
            {
                _lock.Release();
                VialsChanged?.Invoke();
            }
        }
        catch
        {
            return null;
        }
    }

    public static Task<IReadOnlyList<SettingsSnapshot>> LoadAsync(string serial) =>
        Task.Run<IReadOnlyList<SettingsSnapshot>>(() => LoadForSerial(serial));

    /// <summary>Path of the most recent vial — captured just before the change about to be tracked.</summary>
    public static string? NewestVialPath(string? serial) =>
        string.IsNullOrEmpty(serial) ? null : LoadForSerial(serial).FirstOrDefault()?.FilePath;

    public static SettingsSnapshot? LoadVial(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var vial = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(path), CoreJson.CaseInsensitive);
            if (vial is null || vial.SettingCount == 0) return null;
            vial.FilePath = path;
            return vial;
        }
        catch { return null; }
    }

    public static void Delete(SettingsSnapshot vial)
    {
        TryDeleteFile(vial.FilePath);
        VialsChanged?.Invoke();
    }

    public static async Task<List<SettingChange>?> DiffAsync(SettingsSnapshot vial, CancellationToken ct = default)
    {
        var current = await ReadNamespacesAsync(vial.DeviceSerial, ct);
        if (current is null) return null;

        var changes = new List<SettingChange>();
        foreach (var ns in NamespaceNames)
        {
            var bottled = vial.Namespaces.TryGetValue(ns, out var b) ? b : new Dictionary<string, string>();
            var live = current.TryGetValue(ns, out var l) ? l : new Dictionary<string, string>();

            foreach (var key in bottled.Keys.Union(live.Keys).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var inVial = bottled.TryGetValue(key, out var vialValue);
                var inLive = live.TryGetValue(key, out var liveValue);
                if (inVial && inLive && vialValue == liveValue) continue;

                changes.Add(new SettingChange
                {
                    Namespace = ns,
                    Key = key,
                    VialValue = inVial ? vialValue : null,
                    CurrentValue = inLive ? liveValue : null,
                    IsProtected = ProtectedKeys.Contains(key)
                });
            }
        }
        return changes;
    }

    public static async Task<int> RestoreAsync(IEnumerable<SettingChange> changes, string serial, CancellationToken ct = default)
    {
        int applied = 0;
        foreach (var change in changes.Where(c => !c.IsProtected))
        {
            ct.ThrowIfCancellationRequested();
            var command = change.VialValue is null
                ? $"shell settings delete {change.Namespace} {change.Key}"
                : BuildPutCommand(change.Namespace, change.Key, change.VialValue);

            var result = await AdbExecutor.ExecuteCommandAsync(command, ct, serial: serial);
            if (!result.Contains("exception", StringComparison.OrdinalIgnoreCase) &&
                !result.Contains("error", StringComparison.OrdinalIgnoreCase))
                applied++;
        }
        return applied;
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>?> ReadNamespacesAsync(string serial, CancellationToken ct)
    {
        var output = await AdbExecutor.ExecuteCommandAsync(
            $"shell \"settings list global; echo {Sep}; settings list secure; echo {Sep}; settings list system\"",
            ct, log: false, serial: serial);

        var sections = output.Split(Sep, StringSplitOptions.None);
        if (sections.Length < NamespaceNames.Length) return null;

        var result = new Dictionary<string, Dictionary<string, string>>();
        for (int i = 0; i < NamespaceNames.Length; i++)
            result[NamespaceNames[i]] = ParseSection(sections[i]);

        return result.Values.All(d => d.Count == 0) ? null : result;
    }

    private static Dictionary<string, string> ParseSection(string section)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in section.SplitLines())
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            if (key.Length == 0 || key.Contains(' ')) continue;
            map[key] = line[(idx + 1)..];
        }
        return map;
    }

    private static string ComputeHash(Dictionary<string, Dictionary<string, string>> namespaces)
    {
        var sb = new StringBuilder();
        foreach (var ns in NamespaceNames)
            if (namespaces.TryGetValue(ns, out var map))
                foreach (var pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                    sb.Append(ns).Append('|').Append(pair.Key).Append('|').Append(pair.Value).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static List<SettingsSnapshot> LoadForSerial(string serial)
    {
        var dir = Path.Combine(BaseDir, serial.SanitizeFileName());
        if (!Directory.Exists(dir)) return new();

        var vials = new List<SettingsSnapshot>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var vial = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(file), CoreJson.CaseInsensitive);
                if (vial is null || vial.SettingCount == 0) continue;
                vial.FilePath = file;
                vials.Add(vial);
            }
            catch { }
        }
        return vials.OrderByDescending(v => v.TakenUtc).ToList();
    }

    private static string BuildPutCommand(string ns, string key, string value)
    {
        if (value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/' or '@' or '+' or ','))
            return $"shell settings put {ns} {key} {value}";

        var deviceQuoted = "'" + value.Replace("'", "'\\''") + "'";
        return $"shell {WindowsQuote($"settings put {ns} {key} {deviceQuoted}")}";
    }

    private static string WindowsQuote(string s)
    {
        var escaped = Regex.Replace(s, "(\\\\*)\"", m => new string('\\', m.Groups[1].Value.Length * 2) + "\\\"");
        escaped = Regex.Replace(escaped, "(\\\\+)$", m => new string('\\', m.Groups[1].Value.Length * 2));
        return $"\"{escaped}\"";
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); } catch { }
    }
}

public static class CloudIntelligenceManager
{
    private static readonly ConcurrentDictionary<string, PackageIntelligenceData> Cache = new();
    private static readonly HttpClient Http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private const string Api = AppConfiguration.Urls.CloudApiAnalyzeFull;

    static CloudIntelligenceManager()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        Http.DefaultRequestHeaders.Add("User-Agent", AppConfiguration.Urls.HttpUserAgent);
        Http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

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

        if (cloudPackages.Count > 0)
        {
            await Parallel.ForEachAsync(cloudPackages, new ParallelOptions { MaxDegreeOfParallelism = 7, CancellationToken = ct }, async (pkg, token) =>
            {
                var result = await FetchAsync(pkg, token);
                if (result is not null)
                {
                    Cache.TryAdd(pkg, result);
                    onResult(result);
                    return;
                }

                // Unreachable cloud: hand the allowance back and leave the cache untouched so the
                // next load can still get a real verdict instead of a frozen "Network unavailable".
                AiQuotaManager.Refund(1);
                onResult(Fallback(pkg));
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
            if (result is not null)
            {
                Cache.TryAdd(packageName, result);
                return result;
            }

            AiQuotaManager.Refund(1);
            return Fallback(packageName);
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
        Description = string.Format(Strings.Debloat_AI_QuotaExhausted, AiQuotaManager.DailyLimit),
        WarningMessage = null,
        IsOfflineResult = true
    };

    /// <summary>Cloud verdict, or null when the service could not be reached at all.</summary>
    private static async Task<PackageIntelligenceData?> FetchAsync(string pkg, CancellationToken ct)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync(Api, BuildRequest(pkg), ct);

            // The service enforces the same daily allowance server-side. When it says the day is
            // spent, the local counter is out of step with it: burn what is left so both agree from
            // here on. A plain burst refusal is transient and is treated like any unreachable call.
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var rejection = await resp.Content.ReadFromJsonAsync<QuotaRejection>(JsonOpts, ct);
                if (rejection?.QuotaExceeded != true) return null;

                AiQuotaManager.MarkExhausted();
                return CreateQuotaFallback(pkg);
            }

            if (resp.IsSuccessStatusCode)
            {
                var r = await resp.Content.ReadFromJsonAsync<PackageIntelligenceData>(JsonOpts, ct);
                if (r is not null)
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
        return null;
    }

    private sealed record QuotaRejection(bool QuotaExceeded);

    /// <summary>
    /// The package to rate, the pseudonymous install id the daily allowance is counted against, and
    /// — for a Pro install — the signed licence tuple that lifts the allowance server-side.
    /// </summary>
    private static object BuildRequest(string pkg)
    {
        var proof = LicenseService.Instance.ProEntitlement;

        return new
        {
            packageName = pkg,
            clientId = LicenseService.Instance.DeviceFingerprint,
            pro = proof is null ? null : (object?)new
            {
                key = proof.Key,
                tier = proof.Tier,
                expiresAt = proof.ExpiresAt,
                timestamp = proof.Timestamp,
                signature = proof.Signature
            }
        };
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

    private const string AgentPort = "8080";
    private const string Pkg = "com.zephyrselixir.agent";
    private const string Activity = $"{Pkg}/.StartServiceActivity";
    internal const string BaseUri = $"http://localhost:{AgentPort}";
    private const string VersionCheckUrl = AppConfiguration.Urls.ZephyrAgentVersion;

    private static bool _running;
    private static string _boundSerial = string.Empty;
    private static readonly SemaphoreSlim Semaphore = new(1);

    public static async Task<bool> EnsureAgentIsRunningAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await Semaphore.WaitAsync(ct);
        try
        {
            var serial = DeviceManager.Instance.ActiveSerial;

            if (_running && _boundSerial == serial)
            {
                if (await PingAsync(ct)) return true;
                _running = false;
                progress?.Report("Reconnecting...");
            }

            // The local port can only be forwarded to one device at a time: release the previous
            // binding before pointing it at the newly active device.
            if (_boundSerial.Length > 0 && _boundSerial != serial)
            {
                await AdbExecutor.ExecuteCommandAsync($"forward --remove tcp:{AgentPort}", ct, log: false, serial: _boundSerial);
                _running = false;
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
                var (installed, detail) = await InstallAsync(ct);
                if (!installed)
                {
                    progress?.Report(AdbErrorCatalog.Enrich("Agent installation failed.", detail));
                    return false;
                }
                progress?.Report("Agent ready.");
            }

            progress?.Report("Setting up connection...");
            await AdbExecutor.ExecuteCommandAsync($"forward tcp:{AgentPort} tcp:{AgentPort}", ct);
            await AdbExecutor.ExecuteCommandAsync($"shell am start -n {Activity}", ct);
            await Task.Delay(500, ct);

            if (await PingAsync(ct))
            {
                _running = true;
                _boundSerial = serial;
                progress?.Report("Agent ready.");
                return true;
            }

            progress?.Report("Connection failed.");
            _running = false;
            return false;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            _running = false;
            return false;
        }
        finally { Semaphore.Release(); }
    }

    private static async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync($"{BaseUri}/health", HttpCompletionOption.ResponseHeadersRead, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<string?> GetInstalledVersionAsync(CancellationToken ct)
    {
        var output = await AdbExecutor.ExecuteCommandAsync($"shell dumpsys package {Pkg}", ct, log: false);

        if (string.IsNullOrWhiteSpace(output) || output.Contains("Unable to find"))
            return null;

        var line = output.SplitLines().FirstOrDefault(l => l.Contains("versionName=", StringComparison.Ordinal));
        return line?.Split('=', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } parts
            ? parts[1].Trim()
            : null;
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

    private static async Task<(bool Ok, string Detail)> InstallAsync(CancellationToken ct)
    {
        var apk = GetApkPath();
        if (apk is null || !File.Exists(apk)) return (false, string.Empty);

        var remote = $"/data/local/tmp/{Pkg}.apk";
        await AdbExecutor.ExecuteTransferAsync($"push \"{apk}\" {remote}", ct);

        var result = await AdbExecutor.ExecuteCommandAsync($"shell pm install -r {remote}", ct);

        if (!result.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            await AdbExecutor.ExecuteCommandAsync($"shell pm uninstall {Pkg}", ct);
            result = await AdbExecutor.ExecuteCommandAsync($"shell pm install {remote}", ct);
        }

        await AdbExecutor.ExecuteCommandAsync($"shell rm {remote}", ct);
        return (result.Contains("Success", StringComparison.OrdinalIgnoreCase), result);
    }

    public static async Task<List<AppInfo>> GetInstalledAppsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Retrieving app list...");
        var json = await HttpClient.GetStringAsync($"{BaseUri}/apps", ct);
        if (string.IsNullOrWhiteSpace(json)) { progress?.Report("Empty list."); return new(); }

        var apps = JsonSerializer.Deserialize<List<AgentAppInfo>>(json, CoreJson.CaseInsensitive);
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

    /// <summary>
    /// The single live device wiring of one element. Pages are built once and kept alive across
    /// navigation, so they wire themselves from their own Loaded handler every time they come back:
    /// one hook set per element — created on first use, re-attached on Loaded and released on
    /// Unloaded — is what keeps that idempotent. Without it each visit stacked another handler pair
    /// on the element, and a page that wired itself only once (Home) went permanently deaf after
    /// its first navigation away.
    /// </summary>
    private sealed class DeviceHooks
    {
        private readonly FrameworkElement _owner;
        private bool _attached;

        public Action<bool>? StatusChanged;
        public Action<string, int>? InfoUpdated;
        public Action<string>? ActiveDeviceChanged;
        public UIElement[] Controls = Array.Empty<UIElement>();

        public DeviceHooks(FrameworkElement owner)
        {
            _owner = owner;
            owner.Loaded += (_, _) => Attach();
            owner.Unloaded += (_, _) => Detach();
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            DeviceManager.Instance.DeviceStatusChanged += OnStatus;
            DeviceManager.Instance.DeviceInfoUpdated += OnInfo;
            DeviceManager.Instance.ActiveDeviceChanged += OnActive;

            // Events missed while the page was away can't be replayed: re-align the guarded
            // controls with reality on the way back in, instead of leaving them on the state they
            // happened to have when the user navigated off.
            foreach (var control in Controls) control?.SetDeviceDependent();
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            DeviceManager.Instance.DeviceStatusChanged -= OnStatus;
            DeviceManager.Instance.DeviceInfoUpdated -= OnInfo;
            DeviceManager.Instance.ActiveDeviceChanged -= OnActive;
        }

        private void OnStatus(object? _, bool connected) => _owner.Dispatcher.BeginInvoke(() =>
        {
            foreach (var control in Controls) control?.SetDeviceDependent(connected);
            StatusChanged?.Invoke(connected);
        });

        private void OnInfo(object? _, (string Name, int Battery) info) =>
            _owner.Dispatcher.BeginInvoke(() => InfoUpdated?.Invoke(info.Name, info.Battery));

        private void OnActive(object? _, string serial) =>
            _owner.Dispatcher.BeginInvoke(() => ActiveDeviceChanged?.Invoke(serial));
    }

    private static readonly ConditionalWeakTable<FrameworkElement, DeviceHooks> Hooks = new();

    private static DeviceHooks HooksFor(FrameworkElement e)
    {
        var hooks = Hooks.GetValue(e, static owner => new DeviceHooks(owner));
        // Callers wire from their own Loaded handler, so this element's Loaded has already fired
        // (or is firing now): attach here as well, Attach being idempotent.
        hooks.Attach();
        return hooks;
    }

    public static void SubscribeToDeviceState(this FrameworkElement e, params UIElement[] controls) =>
        e.SubscribeToDeviceUpdates(controls: controls);

    /// <summary>
    /// Runs <paramref name="onActiveDeviceChanged"/> (on the element's dispatcher) whenever the
    /// active device switches — via the sidebar picker or because the previous active device was
    /// unplugged. Pages holding device-specific state subscribe here so they never keep showing
    /// one phone's data while commands target another.
    /// </summary>
    public static void SubscribeToActiveDevice(this FrameworkElement e, Action<string> onActiveDeviceChanged)
    {
        if (e == null) return;
        HooksFor(e).ActiveDeviceChanged = onActiveDeviceChanged;
    }

    public static void SubscribeToDeviceUpdates(this FrameworkElement e, Action<bool>? onStatusChanged = null, Action<string, int>? onInfoUpdated = null, params UIElement[] controls)
    {
        if (e == null) return;

        var hooks = HooksFor(e);
        if (onStatusChanged is not null) hooks.StatusChanged = onStatusChanged;
        if (onInfoUpdated is not null) hooks.InfoUpdated = onInfoUpdated;
        if (controls.Length > 0) hooks.Controls = controls;

        foreach (var control in controls) control?.SetDeviceDependent();
        if (DeviceManager.Instance.IsConnected) onInfoUpdated?.Invoke(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);
    }
}

/// <summary>
/// Centralized parser for <c>adb shell dumpsys meminfo</c> output.
/// Extracted from <see cref="UI.Pages.Optimize"/> to be reusable across the free Optimize page
/// and the Pro Performance Monitor (open-core boundary: DRY without duplicating logic).
/// </summary>
public static partial class MemInfoParser
{
    [GeneratedRegex(@"^([\d,]+)\s*K[B]?:\s*([\w\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MemInfoPssRegex();

    [GeneratedRegex(@"([\d,]+)\s*K[B]?.*?(com\.[\w\.]+|org\.[\w\.]+|net\.[\w\.]+)")]
    private static partial Regex MemInfoFallbackRegex();

    /// <summary>
    /// Parses the <c>dumpsys meminfo</c> output and returns a list of (package, memory in kB).
    /// Primary path: "Total PSS by process" / "Total RSS by process" section.
    /// Fallback: scan-the-rest with a permissive regex.
    /// Behavior is identical to the original <c>Optimize.xaml.cs::ParseMemInfo</c> implementation.
    /// </summary>
    public static List<(string Package, long MemoryKb)> ParseMemInfo(string output)
    {
        var result = new List<(string, long)>();
        bool inPssSection = false;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Total PSS by process", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Total RSS by process", StringComparison.OrdinalIgnoreCase))
            {
                inPssSection = true;
                continue;
            }

            if (inPssSection && string.IsNullOrWhiteSpace(trimmed))
                break;

            if (inPssSection)
            {
                var match = MemInfoPssRegex().Match(trimmed);
                if (match.Success)
                {
                    var kb = long.Parse(match.Groups[1].Value.Replace(",", ""));
                    var pkg = match.Groups[2].Value;
                    if (pkg.Contains('.') && !pkg.StartsWith("pid"))
                        result.Add((pkg, kb));
                }
            }
            else
            {
                var match = MemInfoFallbackRegex().Match(trimmed);
                if (match.Success && !result.Any(r => r.Item1 == match.Groups[2].Value))
                    result.Add((match.Groups[2].Value, long.Parse(match.Groups[1].Value.Replace(",", ""))));
            }
        }
        return result;
    }
}
