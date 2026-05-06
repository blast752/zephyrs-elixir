namespace ZephyrsElixir.Licensing;

#region Guardian Configuration

public static class ProDllGuardianConfig
{
    public const int PeriodicCheckIntervalMinutes = 30;
    public const int NetworkChangeDebounceSeconds = 4;
    public const int FileSystemDebounceSeconds = 2;
    public const int StartupGraceDelaySeconds = 3;
    public const int GateAcquireTimeoutSeconds = 2;

    public static readonly TimeSpan[] BackoffSchedule =
    {
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1)
    };
}

#endregion

#region Trigger Source

internal enum GuardianTrigger
{
    Startup,
    LicenseStateChanged,
    NetworkRestored,
    FileSystem,
    Periodic,
    Manual
}

#endregion

#region Guardian

public sealed class ProDllGuardian : IDisposable
{
    #region Singleton

    private static readonly Lazy<ProDllGuardian> _instance = new(
        () => new ProDllGuardian(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static ProDllGuardian Instance => _instance.Value;

    #endregion

    #region Fields

    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private Timer? _periodicTimer;
    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _disposed;

    private DateTime _lastDownloadAttemptUtc = DateTime.MinValue;
    private int _consecutiveFailures;

    private EventHandler<LicenseStateChangedEventArgs>? _licenseHandler;
    private NetworkAvailabilityChangedEventHandler? _networkHandler;

    #endregion

    private ProDllGuardian() { }

    #region Lifecycle

    public async Task StartAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProDllGuardian));
        if (_started) return;
        _started = true;

        _cts = new CancellationTokenSource();

        _licenseHandler = OnLicenseStateChanged;
        LicenseService.Instance.StateChanged += _licenseHandler;

        _networkHandler = OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAvailabilityChanged += _networkHandler;

        TrySetupFileSystemWatcher();

        Log("Start", "Guardian started");

        await Task.Delay(
            TimeSpan.FromSeconds(ProDllGuardianConfig.StartupGraceDelaySeconds),
            _cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);

        await EnsureCorrectStateAsync(GuardianTrigger.Startup);

        var interval = TimeSpan.FromMinutes(ProDllGuardianConfig.PeriodicCheckIntervalMinutes);
        _periodicTimer = new Timer(
            _ => _ = SafeEnsureAsync(GuardianTrigger.Periodic),
            null, interval, interval);
    }

    public Task TriggerAsync() => EnsureCorrectStateAsync(GuardianTrigger.Manual);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }

        if (_licenseHandler is not null)
            LicenseService.Instance.StateChanged -= _licenseHandler;
        if (_networkHandler is not null)
            NetworkChange.NetworkAvailabilityChanged -= _networkHandler;

        _periodicTimer?.Dispose();
        _watcher?.Dispose();
        _cts?.Dispose();
        _gate.Dispose();

        Log("Dispose", "Guardian disposed");
    }

    #endregion

    #region Event Handlers

    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        if (_disposed) return;

        var relevant = e.Reason
            is LicenseChangeReason.Activation
            or LicenseChangeReason.Deactivation
            or LicenseChangeReason.Expiration
            or LicenseChangeReason.Revocation
            or LicenseChangeReason.Validation
            or LicenseChangeReason.NetworkChange;

        if (!relevant) return;

        _ = SafeEnsureAsync(GuardianTrigger.LicenseStateChanged);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (_disposed || !e.IsAvailable) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(ProDllGuardianConfig.NetworkChangeDebounceSeconds),
                    _cts!.Token);
                await EnsureCorrectStateAsync(GuardianTrigger.NetworkRestored);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("NetworkEvent", $"Error: {ex.Message}"); }
        });
    }

    private void DispatchFileSystemEvent()
    {
        if (_disposed) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(ProDllGuardianConfig.FileSystemDebounceSeconds),
                    _cts!.Token);
                await EnsureCorrectStateAsync(GuardianTrigger.FileSystem);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("FsEvent", $"Error: {ex.Message}"); }
        });
    }

    #endregion

    #region Core Decision Logic

    private async Task SafeEnsureAsync(GuardianTrigger trigger)
    {
        try { await EnsureCorrectStateAsync(trigger); }
        catch (Exception ex) { Log(trigger.ToString(), $"Unhandled: {ex.Message}"); }
    }

    private async Task EnsureCorrectStateAsync(GuardianTrigger trigger)
    {
        if (_disposed) return;

        var acquired = await _gate.WaitAsync(
            TimeSpan.FromSeconds(ProDllGuardianConfig.GateAcquireTimeoutSeconds))
            .ConfigureAwait(false);

        if (!acquired)
        {
            Log(trigger.ToString(), "Skipped — another check in progress");
            return;
        }

        try
        {
            var state = LicenseService.Instance.CurrentState;

            if (ShouldRemoveDll(state))
            {
                await EnforceRemovalAsync(trigger, state);
                return;
            }

            if (ShouldEnsurePresence(state))
            {
                await EnforcePresenceAsync(trigger, state);
                return;
            }

            _consecutiveFailures = 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ShouldRemoveDll(LicenseState state)
    {
        var dllPresent =
            File.Exists(ProDllConfig.EncryptedDllPath) ||
            File.Exists(ProDllConfig.FingerprintPath) ||
            File.Exists(ProDllConfig.TempDllPath);

        if (!dllPresent) return false;

        if (state.LicenseKey is null) return true;
        if (state.Tier < LicenseTier.Pro) return true;
        if (state.OfflineGraceExpired) return true;
        if (state.IsExpired) return true;

        return state.Status
            is LicenseStatus.Refunded
            or LicenseStatus.Blocked
            or LicenseStatus.Invalid
            or LicenseStatus.Expired
            or LicenseStatus.Suspended;
    }

    private static bool ShouldEnsurePresence(LicenseState state)
    {
        if (state.LicenseKey is null) return false;
        if (state.EffectiveTier < LicenseTier.Pro) return false;
        if (state.OfflineGraceExpired) return false;
        if (state.DllState == ProDllState.Ready) return false;
        if (state.DllState == ProDllState.Downloading) return false;

        return true;
    }

    #endregion

    #region Enforcement

    private async Task EnforceRemovalAsync(GuardianTrigger trigger, LicenseState state)
    {
        Log(trigger.ToString(),
            $"Removing Pro DLL → tier={state.EffectiveTier}, status={state.Status}, " +
            $"graceExpired={state.OfflineGraceExpired}, isExpired={state.IsExpired}");

        ProLoader.Unload();

        var removed = await LicenseService.Instance.CleanupProDllAsync().ConfigureAwait(false);
        _consecutiveFailures = 0;

        Log(trigger.ToString(), removed ? "Removal complete" : "Nothing to remove (already clean)");
    }

    private async Task EnforcePresenceAsync(GuardianTrigger trigger, LicenseState state)
    {
        var isImportantTrigger = trigger
            is GuardianTrigger.Startup
            or GuardianTrigger.LicenseStateChanged
            or GuardianTrigger.NetworkRestored
            or GuardianTrigger.FileSystem
            or GuardianTrigger.Manual;

        if (!isImportantTrigger && IsBackoffActive())
        {
            Log(trigger.ToString(),
                $"Backoff active (failures={_consecutiveFailures}, " +
                $"nextAttempt≈{NextAttemptEta():mm\\:ss})");
            return;
        }

        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            Log(trigger.ToString(), "No network — deferring until restored");
            return;
        }

        Log(trigger.ToString(),
            $"Healing Pro DLL (state={state.DllState}, attempt={_consecutiveFailures + 1})");

        _lastDownloadAttemptUtc = DateTime.UtcNow;

        var success = await LicenseService.Instance.EnsureProDllAsync().ConfigureAwait(false);

        if (success)
        {
            _consecutiveFailures = 0;
            Log(trigger.ToString(), "Healing complete");
        }
        else
        {
            _consecutiveFailures = Math.Min(
                _consecutiveFailures + 1,
                ProDllGuardianConfig.BackoffSchedule.Length - 1);
            Log(trigger.ToString(), $"Healing failed (consecutive failures={_consecutiveFailures})");
        }
    }

    private bool IsBackoffActive()
    {
        if (_consecutiveFailures == 0) return false;

        var idx = Math.Min(_consecutiveFailures, ProDllGuardianConfig.BackoffSchedule.Length - 1);
        var wait = ProDllGuardianConfig.BackoffSchedule[idx];
        return DateTime.UtcNow - _lastDownloadAttemptUtc < wait;
    }

    private TimeSpan NextAttemptEta()
    {
        if (_consecutiveFailures == 0) return TimeSpan.Zero;

        var idx = Math.Min(_consecutiveFailures, ProDllGuardianConfig.BackoffSchedule.Length - 1);
        var wait = ProDllGuardianConfig.BackoffSchedule[idx];
        var elapsed = DateTime.UtcNow - _lastDownloadAttemptUtc;
        var remaining = wait - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    #endregion

    #region File System Watcher

    private void TrySetupFileSystemWatcher()
    {
        try
        {
            if (!Directory.Exists(ProDllConfig.ProDirectory))
                Directory.CreateDirectory(ProDllConfig.ProDirectory);

            _watcher = new FileSystemWatcher(ProDllConfig.ProDirectory)
            {
                Filter = ProDllConfig.EncryptedDllFileName,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Deleted += (_, _) => DispatchFileSystemEvent();
            _watcher.Renamed += (_, _) => DispatchFileSystemEvent();

            Log("Watcher", $"Watching {ProDllConfig.ProDirectory}");
        }
        catch (Exception ex)
        {
            Log("Watcher", $"Setup failed (non-fatal): {ex.Message}");
            _watcher = null;
        }
    }

    #endregion

    #region Logging

    private static void Log(string context, string message)
    {
        var full = $"[ProDllGuardian.{context}] {message}";
        try { AdbLogger.Instance.LogInfo("Guardian", full); }
        catch { Debug.WriteLine(full); }
    }

    #endregion
}

#endregion
