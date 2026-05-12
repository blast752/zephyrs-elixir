namespace ZephyrsElixir.Licensing;

#region Configuration

public static class LicenseConfig
{
    public const string ApiBaseUrl = "https://zephyrselixir.com/api/license";
    public const string PurchaseUrl = "https://whop.com/zephyr-s-elixir";
    
    public const int RequestTimeoutSeconds = 15;
    public const int ValidationIntervalHoursOnline = 4;  
    public const int ValidationIntervalHoursOffline = 1;     
    public const int OfflineGraceDays = 7;
    public const int QuickValidationDelaySeconds = 30;    
    
    public const string CacheFileName = ".zephyr_license";
    public const string CacheEntropy = "ZephyrsElixir.v3";   
    public const int CacheVersion = 4;
    
    public const int FreeAiAnalysisQuotaDaily = 25;
    
    public const string KeyPrefix = "Z";
    public const int KeyMinLength = 22;
    public const int KeyMaxLength = 30;
    public const string KeyPlaceholder = "Z-XXXXXX-XXXXXXXX-XXXXXXX";
    public const string KeyPattern = @"^Z-?[A-Z0-9]{6}-?[A-Z0-9]{8}-?[A-Z0-9]{6,7}[A-Z0-9]?$";
    
    private static readonly Lazy<Regex> _keyRegex = new(() => 
        new Regex(KeyPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
    
    public static Regex KeyRegex => _keyRegex.Value;
    
    public const int TimestampToleranceMinutesPast = 5;
    public const int TimestampToleranceMinutesFuture = 10;
}

#endregion

#region Pro DLL Configuration

public static class ProDllConfig
{
    public static readonly string ProDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZephyrsElixir", "pro");

    public const string EncryptedDllFileName = "module.bin";
    public const string FingerprintFileName = "module.fp";
    public const string TempDllFileName = "module.tmp";

    public static string EncryptedDllPath => Path.Combine(ProDirectory, EncryptedDllFileName);
    public static string FingerprintPath => Path.Combine(ProDirectory, FingerprintFileName);
    public static string TempDllPath => Path.Combine(ProDirectory, TempDllFileName);

    public static readonly IReadOnlySet<string> AllowedDownloadDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.zephyrselixir.com",
        "zephyrselixir.com",
        "dl.zephyrselixir.com"
    };

    public const int DownloadConnectTimeoutSeconds = 10;
    public const int DownloadReadTimeoutSeconds = 60;
    public const int MaxDllSizeBytes = 50 * 1024 * 1024;
    public const int MinDllSizeBytes = 1024;
    public const long MinDiskSpaceBytes = 100 * 1024 * 1024;

    public const string EncryptionEntropy = "ZephyrsElixir.ProDll.v1";
    public const int AesKeySize = 256;
    public const int AesNonceSize = 12;
    public const int AesTagSize = 16;

    public const int GuardianStartupDelaySeconds = 3;
    public const int GuardianPeriodicCheckMinutes = 30;
    public const int GuardianNetworkDebounceSeconds = 4;
    public const int GuardianFileSystemDebounceSeconds = 2;

    public static readonly TimeSpan[] GuardianBackoffSchedule =
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

#region Pro DLL State

public enum ProDllState
{
    NotPresent = 0,
    Downloading = 1,
    Verifying = 2,
    Ready = 3,
    Corrupted = 4,
    UpdateAvailable = 5,
    DownloadFailed = 6
}

#endregion

#region Enums

public enum LicenseTier
{
    Free = 0,
    Pro = 1
}

public enum LicenseStatus
{
    Unknown = 0,
    Active = 1,
    Trialing = 2,
    Completed = 3,
    Expired = 10,
    Canceled = 11,
    PastDue = 12,
    Suspended = 13,
    Refunded = 14,
    Blocked = 15,
    Invalid = 20
}

public enum SubscriptionPlan
{
    None = 0,
    Monthly = 1,
    Annual = 2,
    Lifetime = 3
}

#endregion

#region License Key Validation

public static class LicenseKeyHelper
{
    public static bool IsValidFormat(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = Normalize(key);
        return normalized.Length >= LicenseConfig.KeyMinLength && 
               LicenseConfig.KeyRegex.IsMatch(key.Trim());
    }
    
    public static string Clean(string? key) => 
        (key ?? string.Empty).Trim().ToUpperInvariant();
    
    public static string Normalize(string? key) => 
        Clean(key).Replace("-", "").Replace(" ", "");
    
    public static string Format(string? key)
    {
        var normalized = Normalize(key);
        if (normalized.Length < LicenseConfig.KeyMinLength) return normalized;
        
        return $"{normalized[..1]}-{normalized[1..7]}-{normalized[7..15]}-{normalized[15..]}";
    }
    
    public static string Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var formatted = Format(key);
        if (formatted.Length < 20) return new string('*', formatted.Length);
        
        return $"{formatted[..6]}**-********-*****{formatted[^2..]}";
    }
    
    public static string? GetValidationError(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Please enter a license key";
            
        var cleaned = Clean(key);
        
        if (!cleaned.StartsWith(LicenseConfig.KeyPrefix))
            return "License key must start with 'Z-'";
            
        if (Normalize(key).Length < LicenseConfig.KeyMinLength)
            return "License key is too short";
            
        if (!LicenseConfig.KeyRegex.IsMatch(cleaned))
            return "Invalid license key format";
            
        return null;
    }
    
    public static bool Equals(string? key1, string? key2) =>
        string.Equals(Normalize(key1), Normalize(key2), StringComparison.OrdinalIgnoreCase);
}

#endregion

#region License State

public sealed record LicenseState
{
    public LicenseTier Tier { get; init; } = LicenseTier.Free;
    public LicenseStatus Status { get; init; } = LicenseStatus.Unknown;
    public string? LicenseKey { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime LastValidated { get; init; } = DateTime.MinValue;
    public bool IsOffline { get; init; }
    public SubscriptionPlan Plan { get; init; } = SubscriptionPlan.None;
    public string? LastError { get; init; }
    public ProDllState DllState { get; init; } = ProDllState.NotPresent;
    
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    
    public bool OfflineGraceExpired => IsOffline && 
        (DateTime.UtcNow - LastValidated).TotalDays > LicenseConfig.OfflineGraceDays;
    
    public bool IsActive => Status is LicenseStatus.Active or LicenseStatus.Trialing or LicenseStatus.Completed
                            or LicenseStatus.Canceled or LicenseStatus.PastDue
                        && Tier != LicenseTier.Free
                        && !IsExpired
                        && !OfflineGraceExpired;
    
    public LicenseTier EffectiveTier => IsActive ? Tier : LicenseTier.Free;
    
    public bool NeedsValidation
    {
        get
        {
            if (LicenseKey is null) return false;
            var hours = IsOffline 
                ? LicenseConfig.ValidationIntervalHoursOffline 
                : LicenseConfig.ValidationIntervalHoursOnline;
            return (DateTime.UtcNow - LastValidated).TotalHours > hours;
        }
    }
    
    public int? DaysUntilExpiration => ExpiresAt.HasValue && !IsExpired 
        ? (int)Math.Ceiling((ExpiresAt.Value - DateTime.UtcNow).TotalDays) 
        : null;
    
    public string NormalizedKey => LicenseKeyHelper.Normalize(LicenseKey);
    
    public string MaskedKey => LicenseKeyHelper.Mask(LicenseKey);
    
    public string StatusDescription => Status switch
    {
        LicenseStatus.Active => "Active",
        LicenseStatus.Trialing => "Trial",
        LicenseStatus.Completed => "Lifetime",
        LicenseStatus.Expired => "Expired",
        LicenseStatus.Canceled => "Canceled",
        LicenseStatus.PastDue => "Payment Past Due",
        LicenseStatus.Suspended => "Suspended",
        LicenseStatus.Refunded => "Refunded",
        LicenseStatus.Blocked => "Blocked",
        LicenseStatus.Invalid => "Invalid",
        _ => "Unknown"
    };
    
    public static LicenseState Free => new();
}

public sealed class LicenseStateChangedEventArgs : EventArgs
{
    public LicenseState OldState { get; }
    public LicenseState NewState { get; }
    public LicenseChangeReason Reason { get; }
    
    public bool TierChanged => OldState.EffectiveTier != NewState.EffectiveTier;
    public bool Upgraded => OldState.EffectiveTier < NewState.EffectiveTier;
    public bool Downgraded => OldState.EffectiveTier > NewState.EffectiveTier;
    public bool WentOffline => !OldState.IsOffline && NewState.IsOffline;
    public bool CameOnline => OldState.IsOffline && !NewState.IsOffline;

    public LicenseStateChangedEventArgs(LicenseState oldState, LicenseState newState, LicenseChangeReason reason = LicenseChangeReason.Validation)
    {
        OldState = oldState;
        NewState = newState;
        Reason = reason;
    }
}

public enum LicenseChangeReason
{
    CacheLoad,
    Activation,
    Deactivation,
    Validation,
    Expiration,
    Revocation,
    NetworkChange,
    ProDllChanged
}

#endregion

#region API DTOs

public sealed record LicenseRequest
{
    [JsonPropertyName("license_key")] 
    public string LicenseKey { get; init; } = "";
    
    [JsonPropertyName("device_fingerprint")] 
    public string DeviceFingerprint { get; init; } = "";
    
    [JsonPropertyName("app_version")] 
    public string? AppVersion { get; init; }
    
    [JsonPropertyName("pro_dll_version")]
    public string? ProDllVersion { get; init; }
}

public sealed record ProDllInfo
{
    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("url_expires_at")]
    public long UrlExpiresAt { get; init; }

    [JsonPropertyName("allow_rollback")]
    public bool AllowRollback { get; init; }

    public bool IsValid => !string.IsNullOrEmpty(DownloadUrl) 
        && !string.IsNullOrEmpty(Sha256) 
        && !string.IsNullOrEmpty(Version)
        && SizeBytes > 0;

    public bool IsUrlExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= UrlExpiresAt;
}

public sealed record LicenseResponse
{
    [JsonPropertyName("valid")] 
    public bool Valid { get; init; }
    
    [JsonPropertyName("tier")] 
    public int Tier { get; init; }
    
    [JsonPropertyName("status")]
    public string? Status { get; init; }
    
    [JsonPropertyName("expires_at")] 
    public long? ExpiresAt { get; init; }
    
    [JsonPropertyName("license_key")] 
    public string? LicenseKey { get; init; }
    
    [JsonPropertyName("timestamp")] 
    public long Timestamp { get; init; }
    
    [JsonPropertyName("signature")] 
    public string? Signature { get; init; }
    
    [JsonPropertyName("error")] 
    public string? Error { get; init; }
    
    [JsonPropertyName("hint")] 
    public string? Hint { get; init; }

    [JsonPropertyName("pro_dll")]
    public ProDllInfo? ProDll { get; init; }
    
    public string? FullError => string.IsNullOrEmpty(Hint) ? Error : $"{Error}\n{Hint}";
    
    public LicenseStatus ParsedStatus => Status?.ToLowerInvariant() switch
    {
        "active" => LicenseStatus.Active,
        "trialing" => LicenseStatus.Trialing,
        "completed" => LicenseStatus.Completed,
        "expired" => LicenseStatus.Expired,
        "canceled" or "cancelled" => LicenseStatus.Canceled,
        "past_due" => LicenseStatus.PastDue,
        "suspended" => LicenseStatus.Suspended,
        "refunded" => LicenseStatus.Refunded,
        "blocked" => LicenseStatus.Blocked,
        "invalid" => LicenseStatus.Invalid,
        _ => Valid ? LicenseStatus.Active : LicenseStatus.Invalid
    };
}

public sealed record DeactivateResponse
{
    [JsonPropertyName("success")] 
    public bool Success { get; init; }
    
    [JsonPropertyName("message")] 
    public string? Message { get; init; }
    
    [JsonPropertyName("error")] 
    public string? Error { get; init; }
}

internal sealed record CachedLicense
{
    public string? NormalizedKey { get; init; }   
    public LicenseTier Tier { get; init; }
    public LicenseStatus Status { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime CachedAt { get; init; }
    public string? Signature { get; init; }
    public string? Checksum { get; init; }
    public int Version { get; init; } = LicenseConfig.CacheVersion;
    public string? DeviceId { get; init; }
    public string? ProDllVersion { get; init; }
    public string? ProDllHash { get; init; }
    
    public static string ComputeChecksum(string? normalizedKey, LicenseTier tier, LicenseStatus status, DateTime? expiresAt, string? signature, string? deviceId, string? proDllVersion = null)
    {
        var data = $"{normalizedKey}:{(int)tier}:{(int)status}:{expiresAt?.Ticks}:{signature}:{deviceId}:{proDllVersion}:v{LicenseConfig.CacheVersion}";
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
    
    public bool VerifyChecksum(string? deviceId) =>
        Version >= LicenseConfig.CacheVersion &&
        Checksum == ComputeChecksum(NormalizedKey, Tier, Status, ExpiresAt, Signature, deviceId, ProDllVersion);
}

#endregion

#region Pro DLL Fingerprint

internal sealed record ProDllFingerprint
{
    public string Sha256 { get; init; } = "";
    public string Version { get; init; } = "";
    public long Timestamp { get; init; }
    public string DeviceId { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Checksum { get; init; } = "";

    public static ProDllFingerprint Create(string sha256, string version, string deviceId, long sizeBytes)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fp = new ProDllFingerprint
        {
            Sha256 = sha256,
            Version = version,
            Timestamp = ts,
            DeviceId = deviceId,
            SizeBytes = sizeBytes
        };
        return fp with { Checksum = fp.ComputeChecksum() };
    }

    public string ComputeChecksum()
    {
        var data = $"{Sha256}:{Version}:{Timestamp}:{DeviceId}:{SizeBytes}:fp.v1";
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    public bool Verify(string deviceId) =>
        DeviceId == deviceId &&
        !string.IsNullOrEmpty(Sha256) &&
        !string.IsNullOrEmpty(Version) &&
        Checksum == ComputeChecksum();
}

#endregion

#region Result Type

public readonly record struct LicenseResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public LicenseState? NewState { get; init; }
    
    public static LicenseResult Success(string? message = null, LicenseState? state = null) => 
        new() { IsSuccess = true, Message = message, NewState = state };
        
    public static LicenseResult Failure(string error) => 
        new() { IsSuccess = false, Error = error };
    
    public void Deconstruct(out bool success, out string? error)
    {
        success = IsSuccess;
        error = Error;
    }
}

#endregion

#region Features

public static class Features
{
    public const string Optimization = "optimization";
    public const string Debloat = "debloat";
    public const string AIAnalysis = "ai.analysis";
    public const string ApkInstaller = "tools.apk_installer";
    public const string PowerMenu = "tools.power_menu";
    public const string WirelessAdb = "connection.wireless";
    public const string PrivateDns = "privacy.dns";
    public const string AnimationSpeed = "advanced.animations";
    
    public const string ExtremeMode = "optimization.extreme";
    public const string ScreenMirror = "tools.screen_mirror";
    public const string ScreenRecording = "tools.screen_recording";
    public const string ApkBackup = "debloat.backup";
    public const string MultiApkInstall = "tools.apk_multi_install";
    public const string AIAnalysisUnlimited = "ai.analysis.unlimited";
    public const string PrivacySafetyCore = "privacy.safety_core";
    public const string PrivacyAdId = "privacy.ad_id";
    public const string PrivacyCaptivePortal = "privacy.captive_portal";
    public const string PrivacyGoogleCore = "privacy.google_core";
    public const string PrivacyRamExpansion = "privacy.ram_expansion";
    public const string PrioritySupport = "support.priority";
    
    private static readonly Lazy<Dictionary<string, LicenseTier>> _requirements = new(() =>
        new Dictionary<string, LicenseTier>(StringComparer.OrdinalIgnoreCase)
        {
            [Optimization] = LicenseTier.Free,
            [Debloat] = LicenseTier.Free,
            [AIAnalysis] = LicenseTier.Free,
            [ApkInstaller] = LicenseTier.Free,
            [PowerMenu] = LicenseTier.Free,
            [WirelessAdb] = LicenseTier.Free,
            [PrivateDns] = LicenseTier.Free,
            [AnimationSpeed] = LicenseTier.Free,
            
            [ExtremeMode] = LicenseTier.Pro,
            [ScreenMirror] = LicenseTier.Pro,
            [ScreenRecording] = LicenseTier.Pro,
            [ApkBackup] = LicenseTier.Pro,
            [MultiApkInstall] = LicenseTier.Pro,
            [AIAnalysisUnlimited] = LicenseTier.Pro,
            [PrivacySafetyCore] = LicenseTier.Pro,
            [PrivacyAdId] = LicenseTier.Pro,
            [PrivacyCaptivePortal] = LicenseTier.Pro,
            [PrivacyGoogleCore] = LicenseTier.Pro,
            [PrivacyRamExpansion] = LicenseTier.Pro,
            [PrioritySupport] = LicenseTier.Pro
        });

    public static bool IsAvailable(string featureId) =>
        _requirements.Value.TryGetValue(featureId, out var required) &&
        LicenseService.Instance.CurrentState.EffectiveTier >= required &&
        (required < LicenseTier.Pro || ProLoader.IsLoaded);

    public static LicenseTier? GetRequiredTier(string featureId) =>
        _requirements.Value.TryGetValue(featureId, out var tier) ? tier : null;

    public static bool RequiresPro(string featureId) =>
        _requirements.Value.TryGetValue(featureId, out var tier) && tier == LicenseTier.Pro;

    public static void IfAvailable(string featureId, Action action, Action? fallback = null)
    {
        if (IsAvailable(featureId)) action();
        else fallback?.Invoke();
    }

    public static T Choose<T>(string featureId, T whenAvailable, T whenLocked) =>
        IsAvailable(featureId) ? whenAvailable : whenLocked;
    
    public static async Task<bool> IfAvailableAsync(string featureId, Func<Task> action)
    {
        if (!IsAvailable(featureId)) return false;
        await action();
        return true;
    }

    public static readonly IReadOnlyList<string> PrivacySuite = new[]
    {
        PrivacySafetyCore, PrivacyAdId, PrivacyCaptivePortal, 
        PrivacyGoogleCore, PrivacyRamExpansion
    };

    public static readonly IReadOnlyList<string> ProFeatures = new[]
    {
        ExtremeMode, ScreenMirror, ScreenRecording, ApkBackup,
        MultiApkInstall, AIAnalysisUnlimited, PrivacySafetyCore,
        PrivacyAdId, PrivacyCaptivePortal, PrivacyGoogleCore,
        PrivacyRamExpansion, PrioritySupport
    };

    public static readonly IReadOnlyList<string> FreeFeatures = new[]
    {
        Optimization, Debloat, AIAnalysis, ApkInstaller,
        PowerMenu, WirelessAdb, PrivateDns, AnimationSpeed
    };
}

#endregion
