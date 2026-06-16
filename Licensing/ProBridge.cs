namespace ZephyrsElixir.Licensing;

#region IProModule Interface

public interface IProModule : IDisposable
{
    bool Initialize(
        Func<string, CancellationToken, Task<string>> adbExecutor,
        Func<LicenseState> licenseStateProvider,
        string deviceFingerprint);

    Version ModuleVersion { get; }

    IReadOnlySet<string> SupportedCommands { get; }

    IReadOnlySet<string> SupportedPages { get; }

    Task<ProResult> ExecuteAsync(
        string commandId,
        IReadOnlyDictionary<string, object>? args = null,
        CancellationToken ct = default,
        IProgress<string>? progress = null);

    Task<ProResult> RevertAsync(string commandId, CancellationToken ct = default);

    FrameworkElement? CreatePage(string pageId, Action? closeAction = null);
}

#endregion

#region ProResult

public readonly record struct ProResult
{
    public bool Success { get; init; }
    public string Message { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    public static ProResult Ok(string message) => new() { Success = true, Message = message };
    public static ProResult Fail(string message) => new() { Success = false, Message = message };
    public static ProResult Ok(string message, IReadOnlyDictionary<string, object> data) => new() { Success = true, Message = message, Data = data };
}

#endregion

#region ProModuleLoadContext

internal sealed class ProModuleLoadContext : AssemblyLoadContext
{
    private readonly string _directory;

    public ProModuleLoadContext(string dllPath) : base("ProModule", isCollectible: true)
    {
        _directory = Path.GetDirectoryName(dllPath) ?? AppDomain.CurrentDomain.BaseDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var candidate = Path.Combine(_directory, $"{assemblyName.Name}.dll");
        if (File.Exists(candidate))
            return LoadFromAssemblyPath(candidate);

        var appCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(appCandidate))
            return Default.LoadFromAssemblyPath(appCandidate);

        return null;
    }
}

#endregion

#region ProLoader

public static class ProLoader
{
    private static IProModule? _module;
    private static ProModuleLoadContext? _loadContext;
    private static bool _attempted;
    private static readonly object _lock = new();

    public static IProModule? Module
    {
        get
        {
            if (!_attempted) EnsureLoaded();
            return _module;
        }
    }

    public static bool IsLoaded => Module is not null;

    public static void EnsureLoaded()
    {
        if (_attempted) return;
        lock (_lock)
        {
            if (_attempted) return;
            _attempted = true;

            try
            {
                var state = LicenseService.Instance.CurrentState;
                
                if (state.EffectiveTier < LicenseTier.Pro)
                {
                    Log("Not Pro tier — running in Free mode");
                    return;
                }

                if (state.DllState != ProDllState.Ready)
                {
                    Log($"Pro DLL state is {state.DllState} — cannot load");
                    return;
                }

                var dllPath = LicenseService.Instance.GetDecryptedDllForLoading();
                if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                {
                    Log("Failed to prepare Pro DLL for loading");
                    return;
                }

                if (!VerifyDllIntegrity(dllPath))
                {
                    Log("Pro DLL integrity check failed — ignoring");
                    LicenseService.Instance.CleanupTempDll();
                    return;
                }

                _loadContext = new ProModuleLoadContext(dllPath);
                var assembly = _loadContext.LoadFromAssemblyPath(dllPath);

                Type? moduleType = null;
                try
                {
                    moduleType = assembly.GetTypes()
                        .FirstOrDefault(t => typeof(IProModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Log($"ReflectionTypeLoadException: {string.Join("; ", ex.LoaderExceptions?.Select(e => e?.Message) ?? Array.Empty<string?>())}");
                    moduleType = ex.Types?.FirstOrDefault(t => t is not null && typeof(IProModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                }

                if (moduleType is null)
                {
                    Log("Pro DLL does not contain an IProModule implementation");
                    UnloadContext();
                    LicenseService.Instance.CleanupTempDll();
                    return;
                }

                var instance = (IProModule)Activator.CreateInstance(moduleType)!;

                LicenseService.Instance.CleanupTempDll();

                var initialized = instance.Initialize(
                    adbExecutor: (cmd, ct) => AdbExecutor.ExecuteCommandAsync(cmd, ct),
                    licenseStateProvider: () => LicenseService.Instance.CurrentState,
                    deviceFingerprint: LicenseService.Instance.DeviceFingerprint);

                if (!initialized)
                {
                    Log("Pro module initialization failed");
                    instance.Dispose();
                    UnloadContext();
                    return;
                }

                _module = instance;
                Log($"Pro module loaded v{instance.ModuleVersion} — {instance.SupportedCommands.Count} commands, {instance.SupportedPages.Count} pages");
            }
            catch (BadImageFormatException ex)
            {
                Log($"Bad image format: {ex.Message}");
                _module = null;
                UnloadContext();
                LicenseService.Instance.CleanupTempDll();
            }
            catch (FileLoadException ex)
            {
                Log($"File load error: {ex.Message}");
                _module = null;
                UnloadContext();
                LicenseService.Instance.CleanupTempDll();
            }
            catch (Exception ex)
            {
                Log($"Failed to load Pro module: {ex.Message}");
                _module = null;
                UnloadContext();
                LicenseService.Instance.CleanupTempDll();
            }
        }
    }

    public static void ReloadIfNeeded()
    {
        lock (_lock)
        {
            var state = LicenseService.Instance.CurrentState;
            
            if (state.DllState == ProDllState.Ready && !IsLoaded && state.EffectiveTier >= LicenseTier.Pro)
            {
                Log("Reload triggered — DLL is ready but not loaded");
                _attempted = false;
                EnsureLoaded();
            }
        }
    }

    private static bool VerifyDllIntegrity(string path)
    {
        try
        {
            var name = AssemblyName.GetAssemblyName(path);
            var token = name.GetPublicKeyToken();

            if (token is null || token.Length == 0)
            {
                Log("Pro DLL is not strong-name signed");
                return false;
            }

            var expectedToken = ProIntegrity.ExpectedPublicKeyToken;
            if (!token.AsSpan().SequenceEqual(expectedToken.AsSpan()))
            {
                Log("Pro DLL public key token mismatch");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"Integrity check exception: {ex.Message}");
            return false;
        }
    }

    public static void Unload()
    {
        lock (_lock)
        {
            _module?.Dispose();
            _module = null;
            _attempted = false;
            UnloadContext();
            LicenseService.Instance.CleanupTempDll();
        }
    }

    private static void UnloadContext()
    {
        if (_loadContext is not null)
        {
            _loadContext.Unload();
            _loadContext = null;
        }
    }

    private static void Log(string message)
    {
        var full = $"[ProLoader] {message}";
        try { AdbLogger.Instance.LogInfo("ProLoader", full); } catch { Debug.WriteLine(full); }
    }
}

#endregion

#region ProIntegrity

internal static class ProIntegrity
{
    internal static readonly byte[] ExpectedPublicKeyToken = new byte[]
    {
        0xAE, 0x38, 0xA7, 0x84, 0x38, 0xDF, 0x1E, 0x97
    };
}

#endregion

#region Pro Commands Helper

public static class Pro
{
    public static bool IsAvailable => ProLoader.IsLoaded && LicenseService.Instance.IsPro;

    public static async Task<ProResult> ExecuteAsync(
        string commandId,
        IReadOnlyDictionary<string, object>? args = null,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        if (!ProLoader.IsLoaded)
        {
            ProLoader.ReloadIfNeeded();
            if (!ProLoader.IsLoaded)
                return ProResult.Fail("Pro module not available. Please check your license or restart the application.");
        }

        if (!LicenseService.Instance.IsPro)
            return ProResult.Fail("Pro license required.");

        return await ProLoader.Module!.ExecuteAsync(commandId, args, ct, progress);
    }

    public static async Task<ProResult> RevertAsync(string commandId, CancellationToken ct = default)
    {
        if (!ProLoader.IsLoaded)
            return ProResult.Fail("Pro module not available.");

        return await ProLoader.Module!.RevertAsync(commandId, ct);
    }

    public static FrameworkElement? CreatePage(string pageId, Action? closeAction = null)
    {
        if (!ProLoader.IsLoaded || !LicenseService.Instance.IsPro)
            return null;

        return ProLoader.Module!.CreatePage(pageId, closeAction);
    }

    public static bool SupportsCommand(string commandId) =>
        ProLoader.Module?.SupportedCommands.Contains(commandId) == true;

    public static bool SupportsPage(string pageId) =>
        ProLoader.Module?.SupportedPages.Contains(pageId) == true;
}

#endregion

#region Pro Command IDs

public static class ProCommandIds
{
    public const string SafetyCore = "privacy.safety_core";
    public const string ResetAdId = "privacy.ad_id";
    public const string CaptivePortal = "privacy.captive_portal";
    public const string GoogleCoreControl = "privacy.google_core";
    public const string RamExpansion = "privacy.ram_expansion";

    public const string ExtremeOptimization = "optimization.extreme";
    public const string ExtremeCachedAppsFreezer = "extreme_cached_apps_freezer";
    public const string ExtremeMulticoreScheduler = "extreme_multicore_scheduler";
    public const string ExtremeCompilationMode = "extreme_compilation_mode";

    public const string ApkBackup = "debloat.backup";

    public const string MultiApkInstall = "tools.apk_multi_install";

    public const string ScreenMirrorPage = "tools.screen_mirror";
    public const string PerformanceMonitorPage = "performance.monitor";
}

#endregion
