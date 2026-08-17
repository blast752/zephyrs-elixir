namespace ZephyrsElixir.Core;

/// <summary>
/// Headless device-optimization pipeline extracted from the Optimize page so the same steps can
/// run from the UI, from a recipe, and on any connected device (via an explicit serial) without
/// duplicating a single adb command. Behavior and step order are identical to the original page.
/// </summary>
public sealed partial class OptimizationEngine
{
    public const int TotalSteps = 120;
    private const int CacheIterations = 100;
    private const long MemoryThresholdKb = 102400;

    private readonly string? _serial;
    private int _currentStep;

    public OptimizationReport Report { get; }
    public bool Extreme { get; init; }
    public Action<string>? Log { get; init; }
    public Action<string>? StepChanged { get; init; }
    public Action<int>? ProgressChanged { get; init; }

    public OptimizationEngine(string? serial = null, OptimizationReport? report = null)
    {
        _serial = serial;
        Report = report ?? new OptimizationReport();
    }

    public async Task<OptimizationOutcome> RunAsync(CancellationToken ct)
    {
        _currentStep = 0;
        Report.Reset();

        // Extreme mode runs through the Pro module, whose adb bridge has no serial parameter:
        // the ambient serial makes those calls land on this engine's device too.
        if (_serial is not null) AdbExecutor.AmbientSerial = _serial;

        await SettingsTimeMachine.CaptureAsync(SettingsTimeMachine.TriggerOptimize, _serial, ct: ct);

        // Tracked up front, not on success: a run cancelled halfway has still touched the device,
        // and "Reset all" has to be able to put those changes back.
        OperationLedger.Track(_serial, OperationLedger.Ops.Optimization);

        try
        {
            await RunStep(ClearCacheAsync, ct);
            await RunStep(ManageMemoryAsync, ct);
            await RunStep(DeepCleanStorageAsync, ct);
            await RunStep(OptimizeNetworkAsync, ct);
            await RunStep(OptimizeSystemAsync, ct);
            await RunStep(CompilePackagesAsync, ct);
            await RunStep(OptimizeDexAsync, ct);

            Report.Outcome = OptimizationOutcome.Success;
            StepChanged?.Invoke(Strings.Common_Status_Success);
            Log?.Invoke($"✓ {Strings.Common_Status_Success}\n");
        }
        catch (OperationCanceledException)
        {
            Report.Outcome = OptimizationOutcome.Partial;
            StepChanged?.Invoke(Strings.Common_Button_Cancel);
            Log?.Invoke($"⚠ {Strings.Optimize_Console_Interrupted}\n");
        }
        catch (Exception ex)
        {
            Report.Outcome = OptimizationOutcome.Error;
            Report.ErrorMessage = ex.Message;
            StepChanged?.Invoke(Strings.Common_Status_Error.Replace("{0}", ""));
            Log?.Invoke($"✗ {ex.Message}\n");
        }

        return Report.Outcome;
    }

    private async Task RunStep(Func<CancellationToken, Task> step, CancellationToken ct)
    {
        GuardRunning(ct);
        await step(ct);
        Report.CompletedSteps++;
    }

    #region Optimization Tasks

    private async Task ClearCacheAsync(CancellationToken ct)
    {
        for (int i = 1; i <= CacheIterations; i++)
        {
            StepChanged?.Invoke(string.Format(Strings.Optimize_Status_ClearingCache, i, CacheIterations));

            if (i % 20 == 0)
                Log?.Invoke($"{string.Format(Strings.Optimize_Console_CacheProgress, i)}\n");

            await RunAdbAsync("shell pm trim-caches 1000G", ct);
            Advance();
            await Task.Delay(30, ct);
        }

        Report.CacheCleared = true;
        Log?.Invoke($"✓ {Strings.Optimize_Console_CacheCleared}\n");
    }

    private async Task ManageMemoryAsync(CancellationToken ct)
    {
        StepChanged?.Invoke(Strings.Optimize_Status_AnalyzingMemory);
        Log?.Invoke($"{Strings.Optimize_Console_AnalyzingProcesses}\n");

        var heavyApps = await GetHeavyAppsAsync(ct);
        await RunAdbAsync("shell am kill-all", ct);
        Report.ProcessesKilled++;
        Advance();

        foreach (var (pkg, mem) in heavyApps.Take(10))
        {
            if (CloudIntelligenceManager.IsCriticalPackage(pkg)) continue;

            var appName = pkg.Split('.').Last();
            StepChanged?.Invoke(string.Format(Strings.Optimize_Status_Stopping, appName));
            Log?.Invoke($"{string.Format(Strings.Optimize_Console_ForceStoppingApp, pkg, $"{mem / 1024.0:F1}")}\n");
            await RunAdbAsync($"shell am force-stop {pkg}", ct);
            Report.AppsForceKilled.Add((pkg, mem));
            Report.MemoryFreedKb += mem;
        }

        Advance();
        Log?.Invoke($"✓ {string.Format(Strings.Optimize_Console_MemoryOptimized, $"{Report.MemoryFreedKb / 1024.0:F1}")}\n");

        if (Extreme)
        {
            var result = await Pro.ExecuteAsync(ProCommandIds.ExtremeCachedAppsFreezer, ct: ct);
            if (result.Success)
                Log?.Invoke($"✓ {Strings.Optimize_Console_CachedAppsFreezer}\n");
        }

        Advance();
    }

    private async Task DeepCleanStorageAsync(CancellationToken ct)
    {
        StepChanged?.Invoke(Strings.Optimize_Status_DeepCleaning);
        Log?.Invoke($"{Strings.Optimize_Console_StartingDeepClean}\n");

        var cleanupOps = new (string? Path, string Command, string Description)[]
        {
            ("/data/local/tmp", "shell rm -rf /data/local/tmp/*", Strings.Optimize_Clean_TempFiles),
            (null, "shell pm trim-caches 1000G", Strings.Optimize_Clean_PackageCaches),
            ("/data/anr", "shell rm -rf /data/anr/*", Strings.Optimize_Clean_AnrTraces),
            ("/data/tombstones", "shell rm -rf /data/tombstones/*", Strings.Optimize_Clean_CrashDumps),
            (null, "shell logcat -c", Strings.Optimize_Clean_LogcatBuffer),
            ("/data/system/dropbox", "shell rm -rf /data/system/dropbox/*", Strings.Optimize_Clean_SystemDropbox)
        };

        long storageBefore = await GetAvailableStorageAsync(ct);
        long totalCleaned = 0;

        foreach (var (path, cmd, desc) in cleanupOps)
        {
            StepChanged?.Invoke(string.Format(Strings.Optimize_Status_Cleaning, desc));

            var dirSize = path != null ? await GetDirectorySizeAsync(path, ct) : 0;
            var result = await RunAdbAsync(cmd, ct);

            if (!result.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                if (path != null && dirSize > 0)
                {
                    totalCleaned += dirSize;
                    Log?.Invoke($"✓ {desc} ({dirSize / 1024.0:F1} MB)\n");
                }
                else
                {
                    Log?.Invoke($"✓ {desc}\n");
                }
                Report.CleanedItems.Add(desc);
            }
            Advance();
        }

        long storageAfter = await GetAvailableStorageAsync(ct);
        if (storageAfter - storageBefore > 0)
            totalCleaned = storageAfter - storageBefore;

        StepChanged?.Invoke(Strings.Optimize_Status_RunningTrim);
        await RunAdbAsync("shell sm fstrim /data", ct);

        var sdkStr = await RunAdbAsync("shell getprop ro.build.version.sdk", ct);
        if (int.TryParse(sdkStr.Trim(), out var sdk) && sdk < 29)
            await RunAdbAsync("shell sm fstrim /cache", ct);

        Advance();

        Report.TrimExecuted = true;
        Report.StorageCleanedKb = totalCleaned;
        Log?.Invoke($"✓ {string.Format(Strings.Optimize_Console_StorageComplete, $"{totalCleaned / 1024.0:F1}")}\n");
    }

    private async Task OptimizeNetworkAsync(CancellationToken ct)
    {
        StepChanged?.Invoke(Strings.Optimize_Status_OptimizingNetwork);
        Log?.Invoke($"{Strings.Optimize_Console_ApplyingNetwork}\n");

        foreach (var key in OperationLedger.NetworkKeys)
        {
            await RunAdbAsync($"shell settings put global {key} 0", ct);
            Advance();
        }

        Report.NetworkOptimized = true;
        Log?.Invoke($"✓ {Strings.Optimize_Console_NetworkComplete}\n");
    }

    private async Task OptimizeSystemAsync(CancellationToken ct)
    {
        StepChanged?.Invoke(Strings.Optimize_Status_OptimizingSystem);

        if (Extreme)
        {
            Log?.Invoke($"{Strings.Optimize_Console_MulticoreScheduler}\n");
            await Pro.ExecuteAsync(ProCommandIds.ExtremeMulticoreScheduler, ct: ct);
        }
        Advance();

        Log?.Invoke($"{Strings.Optimize_Console_SettingAnimations}\n");

        foreach (var setting in OperationLedger.AnimationKeys)
        {
            await RunAdbAsync($"shell settings put global {setting} 0.5", ct);
            Advance();
        }

        Log?.Invoke($"✓ {Strings.Optimize_Console_SystemOptimized}\n");
    }

    private async Task CompilePackagesAsync(CancellationToken ct)
    {
        var mode = "speed";
        if (Extreme)
        {
            var result = await Pro.ExecuteAsync(ProCommandIds.ExtremeCompilationMode, ct: ct);
            if (result.Success) mode = result.Message;
        }

        StepChanged?.Invoke(Extreme ? Strings.Optimize_Status_ExtremeCompilation : Strings.Optimize_Status_CompilingPackages);
        Log?.Invoke($"{string.Format(Extreme ? Strings.Optimize_Console_ExtremeCompilationStart : Strings.Optimize_Console_CompilationStart, mode)}\n");

        GuardRunning(ct);
        await AdbExecutor.ExecuteCommandAsync(
            $"shell cmd package compile -m {mode} -f -a",
            ct,
            line =>
            {
                Log?.Invoke($"{line}\n");
                var match = CompileOutputRegex().Match(line);
                if (match.Success)
                    StepChanged?.Invoke(string.Format(Strings.Optimize_Status_Compiling, match.Groups[1].Value.Split('.').Last()));
            },
            serial: _serial);
        GuardRunning(ct);

        Advance();
        Report.CompilationMode = mode;
        OperationLedger.Track(_serial, OperationLedger.Ops.Compilation);
    }

    private async Task OptimizeDexAsync(CancellationToken ct)
    {
        try
        {
            StepChanged?.Invoke(Strings.Optimize_Status_DexOptimization);
            Log?.Invoke($"{Strings.Optimize_Console_SpoofingBattery}\n");
            OperationLedger.Track(_serial, OperationLedger.Ops.Battery);
            await RunAdbAsync("shell dumpsys battery set level 100", ct);

            Log?.Invoke($"{Strings.Optimize_Console_RunningDex}\n");
            await RunAdbAsync("shell cmd package bg-dexopt-job", ct);
            Advance();
            Report.DexOptimized = true;
        }
        finally
        {
            if (DeviceOnline)
            {
                Log?.Invoke($"{Strings.Optimize_Console_ResettingBattery}\n");
                try
                {
                    await AdbExecutor.ExecuteCommandAsync("shell dumpsys battery reset", CancellationToken.None, serial: _serial);
                    OperationLedger.Forget(_serial, OperationLedger.Ops.Battery);
                    Log?.Invoke($"✓ {Strings.Optimize_Console_BatteryReset}\n");
                }
                catch { }
            }
        }
    }

    #endregion

    #region Memory Analysis Helpers

    [GeneratedRegex(@"(com\.[\w\.]+|org\.[\w\.]+|net\.[\w\.]+).*?(?:lastPss|pss|mem)[=:\s]*([\d,]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ActivityProcessRegex();

    [GeneratedRegex(@"on\s+([\w\.]+)$")]
    private static partial Regex CompileOutputRegex();

    private async Task<List<(string Package, long MemoryKb)>> GetHeavyAppsAsync(CancellationToken ct)
    {
        var memInfo = await RunAdbAsync("shell dumpsys meminfo", ct);
        var result = MemInfoParser.ParseMemInfo(memInfo);

        if (result.Count == 0)
            result = ParseActivityProcesses(await RunAdbAsync("shell dumpsys activity processes", ct));

        if (result.Count == 0)
            result = ParsePsOutput(await RunAdbAsync("shell ps -A -o RSS,NAME", ct));

        return result
            .Where(x => x.MemoryKb > MemoryThresholdKb && !CloudIntelligenceManager.IsCriticalPackage(x.Package))
            .OrderByDescending(x => x.MemoryKb)
            .ToList();
    }

    private static List<(string Package, long MemoryKb)> ParseActivityProcesses(string output)
    {
        var result = new List<(string, long)>();

        foreach (Match match in ActivityProcessRegex().Matches(output))
        {
            var pkg = match.Groups[1].Value;
            if (long.TryParse(match.Groups[2].Value.Replace(",", ""), out var kb))
            {
                var existing = result.FindIndex(r => r.Item1 == pkg);
                if (existing >= 0)
                    result[existing] = (pkg, Math.Max(result[existing].Item2, kb));
                else
                    result.Add((pkg, kb));
            }
        }
        return result;
    }

    private static List<(string Package, long MemoryKb)> ParsePsOutput(string output) =>
        output.Split('\n')
            .Skip(1)
            .Select(line => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 &&
                           long.TryParse(parts[0], out _) &&
                           parts[^1].Contains('.') &&
                           (parts[^1].StartsWith("com.") || parts[^1].StartsWith("org.") || parts[^1].StartsWith("net.")))
            .Select(parts => (parts[^1], long.Parse(parts[0])))
            .ToList();

    #endregion

    #region Utility Methods

    private bool DeviceOnline => _serial is null
        ? DeviceManager.Instance.IsConnected
        : DeviceManager.Instance.Devices.Any(d => d.Serial == _serial && d.IsAuthorized);

    private void GuardRunning(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!DeviceOnline)
            throw new OperationCanceledException(Strings.Optimize_Console_DeviceDisconnected, ct);
    }

    private void Advance() => ProgressChanged?.Invoke(Math.Min(++_currentStep, TotalSteps));

    private async Task<string> RunAdbAsync(string command, CancellationToken ct)
    {
        GuardRunning(ct);
        var output = await AdbExecutor.ExecuteCommandAsync(command, ct, serial: _serial);
        GuardRunning(ct);
        return output;
    }

    private async Task<long> GetDirectorySizeAsync(string path, CancellationToken ct)
    {
        try
        {
            var output = await RunAdbAsync($"shell du -sk {path} 2>/dev/null", ct);
            var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && long.TryParse(parts[0], out var kb) ? kb : 0;
        }
        catch { return 0; }
    }

    private async Task<long> GetAvailableStorageAsync(CancellationToken ct)
    {
        try
        {
            var output = await RunAdbAsync("shell df /data | tail -1", ct);
            var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 4 && long.TryParse(parts[3], out var kb) ? kb : 0;
        }
        catch { return 0; }
    }

    #endregion
}
