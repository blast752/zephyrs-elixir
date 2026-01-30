namespace ZephyrsElixir.Licensing;

#region Configuration

public static class LicenseConfig
{
    // API Endpoints
    public const string ApiBaseUrl = "https://zephyrselixir.com/api/license";
    public const string PurchaseUrl = "https://whop.com/zephyr-s-elixir";
    
    // Timeouts & Intervals
    public const int RequestTimeoutSeconds = 15;
    public const int ValidationIntervalHoursOnline = 4;  
    public const int ValidationIntervalHoursOffline = 1;     
    public const int OfflineGraceDays = 7;
    public const int QuickValidationDelaySeconds = 30;    
    
    public const string CacheFileName = ".zephyr_license";
    public const string CacheEntropy = "ZephyrsElixir.v3";   
    public const int CacheVersion = 3;
    
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
        
        // Format: Z-XXXXXX-XXXXXXXX-XXXXXXX
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
    
    // Computed properties
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    
    public bool OfflineGraceExpired => IsOffline && 
        (DateTime.UtcNow - LastValidated).TotalDays > LicenseConfig.OfflineGraceDays;
    
    public bool IsActive => Status is LicenseStatus.Active or LicenseStatus.Trialing or LicenseStatus.Completed 
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
    NetworkChange
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
    
    public static string ComputeChecksum(string? normalizedKey, LicenseTier tier, LicenseStatus status, DateTime? expiresAt, string? signature, string? deviceId)
    {
        var data = $"{normalizedKey}:{(int)tier}:{(int)status}:{expiresAt?.Ticks}:{signature}:{deviceId}:v{LicenseConfig.CacheVersion}";
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
    
    public bool VerifyChecksum(string? deviceId) =>
        Version >= LicenseConfig.CacheVersion &&
        Checksum == ComputeChecksum(NormalizedKey, Tier, Status, ExpiresAt, Signature, deviceId);
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
    // Free tier features
    public const string Optimization = "optimization";
    public const string Debloat = "debloat";
    public const string AIAnalysis = "ai.analysis";
    public const string ApkInstaller = "tools.apk_installer";
    public const string PowerMenu = "tools.power_menu";
    public const string WirelessAdb = "connection.wireless";
    public const string PrivateDns = "privacy.dns";
    public const string AnimationSpeed = "advanced.animations";
    
    // Pro tier features
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
            // Free tier
            [Optimization] = LicenseTier.Free,
            [Debloat] = LicenseTier.Free,
            [AIAnalysis] = LicenseTier.Free,
            [ApkInstaller] = LicenseTier.Free,
            [PowerMenu] = LicenseTier.Free,
            [WirelessAdb] = LicenseTier.Free,
            [PrivateDns] = LicenseTier.Free,
            [AnimationSpeed] = LicenseTier.Free,
            
            // Pro tier
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
        LicenseService.Instance.CurrentState.EffectiveTier >= required;

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