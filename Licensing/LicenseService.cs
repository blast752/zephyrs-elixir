namespace ZephyrsElixir.Licensing;

public sealed class LicenseService : IDisposable
{
    #region Singleton

    private static readonly Lazy<LicenseService> _instance = new(
        () => new LicenseService(), 
        LazyThreadSafetyMode.ExecutionAndPublication);
    
    public static LicenseService Instance => _instance.Value;

    #endregion

    #region Fields

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _cachePath;
    private readonly string _deviceId;
    private readonly JsonSerializerOptions _jsonOptions;
    
    private LicenseState _state = LicenseState.Free;
    private CancellationTokenSource? _validationCts;
    private Timer? _validationTimer;
    private bool _disposed;
    private bool _initialized;

    #endregion

    #region Events & Properties

    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    public LicenseState CurrentState
    {
        get { lock (this) return _state; }
        private set
        {
            LicenseState old;
            LicenseChangeReason reason;
            lock (this) 
            { 
                old = _state; 
                _state = value;
                reason = DetermineChangeReason(old, value);
            }
            
            if (old.EffectiveTier != value.EffectiveTier || 
                old.IsOffline != value.IsOffline ||
                old.Status != value.Status)
            {
                Log("StateChange", $"{old.EffectiveTier}→{value.EffectiveTier}, Status: {value.Status}, Offline: {value.IsOffline}");
                StateChanged?.Invoke(this, new LicenseStateChangedEventArgs(old, value, reason));
            }
        }
    }

    public bool IsPro => CurrentState.EffectiveTier >= LicenseTier.Pro;
    public string DeviceFingerprint => _deviceId;
    public bool IsInitialized => _initialized;

    #endregion

    #region Constructor

    private LicenseService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(LicenseConfig.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(LicenseConfig.RequestTimeoutSeconds)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", $"ZephyrsElixir/{AppVersion}");

        _cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZephyrsElixir",
            LicenseConfig.CacheFileName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        _deviceId = GenerateDeviceId();
        
        Log("Init", $"Service created. DeviceId: {_deviceId[..8]}..., CachePath: {_cachePath}");
    }

    #endregion

    #region Public API

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        
        if (_initialized)
        {
            Log("Init", "Already initialized, skipping");
            return;
        }
        
        Log("Init", "Starting initialization...");
        
        var cacheLoaded = LoadCache();
        Log("Init", $"Cache loaded: {cacheLoaded}, State: {CurrentState.Status}, Key: {(CurrentState.LicenseKey is not null ? "Present" : "None")}");
        
        if (CurrentState.LicenseKey is not null)
        {
            Log("Init", "License key found, validating...");
            await ValidateAsync(silent: true);
        }
        
        StartPeriodicValidation();
        
        _initialized = true;
        Log("Init", $"Initialization complete. IsPro: {IsPro}, Status: {CurrentState.Status}");
    }

    public async Task<LicenseResult> ActivateAsync(string licenseKey)
    {
        ThrowIfDisposed();
        
        var validationError = LicenseKeyHelper.GetValidationError(licenseKey);
        if (validationError is not null)
            return LicenseResult.Failure(validationError);

        var cleanKey = LicenseKeyHelper.Clean(licenseKey);  
        var normalizedKey = LicenseKeyHelper.Normalize(licenseKey);
        Log("Activate", $"Attempting activation: {cleanKey[..8]}...");

        await _lock.WaitAsync();
        try
        {
            var request = CreateRequest(cleanKey);
            var response = await PostAsync<LicenseResponse>("?action=activate", request);
            
            if (response is null)
            {
                Log("Activate", "Server unavailable");
                return LicenseResult.Failure("Server unavailable. Please check your connection and try again.");
            }

            if (!response.Valid)
            {
                Log("Activate", $"Activation failed: {response.Error}");
                return LicenseResult.Failure(response.FullError ?? "Activation failed");
            }

            if (!ValidateTimestamp(response.Timestamp))
            {
                Log("Activate", "Invalid timestamp");
                return LicenseResult.Failure("Invalid server response. Please try again.");
            }

            var newState = CreateStateFromResponse(response, normalizedKey, offline: false);
            CurrentState = newState;
            await SaveCacheAsync(newState, response.Signature);
            
            Log("Activate", $"SUCCESS! Tier: {newState.Tier}, Status: {newState.Status}");
            return LicenseResult.Success("License activated successfully!", newState);
        }
        catch (TaskCanceledException)
        {
            Log("Activate", "Request timeout");
            return LicenseResult.Failure("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            Log("Activate", $"Exception: {ex.Message}");
            return LicenseResult.Failure($"Connection error: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LicenseResult> DeactivateAsync()
    {
        ThrowIfDisposed();
        
        await _lock.WaitAsync();
        try
        {
            if (CurrentState.LicenseKey is not null)
            {
                Log("Deactivate", "Sending deactivation request...");
                try
                {
                    var request = CreateRequest(CurrentState.NormalizedKey);
                    var response = await PostAsync<DeactivateResponse>("?action=deactivate", request);
                    Log("Deactivate", $"Server response: {response?.Success}");
                }
                catch (Exception ex)
                {
                    Log("Deactivate", $"Server error (continuing anyway): {ex.Message}");
                }
            }

            CurrentState = LicenseState.Free;
            DeleteCache();
            
            Log("Deactivate", "License deactivated locally");
            return LicenseResult.Success("License deactivated. You can now use it on another device.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ValidateAsync(bool silent = false)
    {
        ThrowIfDisposed();
        
        await _lock.WaitAsync();
        try
        {
            if (CurrentState.LicenseKey is null) 
            {
                Log("Validate", "No license key, skipping validation");
                return;
            }
            
            var cleanKey = LicenseKeyHelper.Format(CurrentState.LicenseKey);
            
            Log("Validate", $"Validating: {cleanKey[..10]}...");
            
            var request = CreateRequest(cleanKey);
            var response = await PostAsync<LicenseResponse>("?action=validate", request);

            if (response?.Valid == true && ValidateTimestamp(response.Timestamp))
            {
                var newState = CreateStateFromResponse(response, CurrentState.NormalizedKey, offline: false);
                CurrentState = newState;
                await SaveCacheAsync(newState, response.Signature);
                Log("Validate", $"Valid! Status: {newState.Status}");
            }
            else if (response?.Valid == false)
            {
                Log("Validate", $"License invalid: {response.Error}, Status: {response.Status}");
                
                var status = response.ParsedStatus;
                
                if (status is LicenseStatus.Refunded or LicenseStatus.Blocked or LicenseStatus.Invalid)
                {
                    CurrentState = LicenseState.Free with { LastError = response.Error };
                    DeleteCache();
                    Log("Validate", "License permanently revoked, cache cleared");
                }
                else if (status is LicenseStatus.Expired or LicenseStatus.Canceled or LicenseStatus.Suspended)
                {
                    CurrentState = CurrentState with 
                    { 
                        Status = status, 
                        IsOffline = false,
                        LastError = response.Error,
                        LastValidated = DateTime.UtcNow
                    };
                    await SaveCacheAsync(CurrentState, null);
                    Log("Validate", $"License status: {status}");
                }
                else if (status is LicenseStatus.PastDue)
                {
                    CurrentState = CurrentState with 
                    { 
                        Status = status, 
                        IsOffline = false,
                        LastError = "Payment past due - please update your payment method",
                        LastValidated = DateTime.UtcNow
                    };
                    await SaveCacheAsync(CurrentState, null);
                    Log("Validate", "Payment past due");
                }
                else
                {
                    CurrentState = LicenseState.Free with { LastError = response.Error };
                    DeleteCache();
                }
            }
            else
            {
                Log("Validate", "Network error, going offline");
                if (CurrentState.Tier != LicenseTier.Free)
                {
                    CurrentState = CurrentState with 
                    { 
                        IsOffline = true,
                        LastError = "Unable to reach license server"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Log("Validate", $"Exception: {ex.Message}");
            if (CurrentState.Tier != LicenseTier.Free)
            {
                CurrentState = CurrentState with 
                { 
                    IsOffline = true,
                    LastError = ex.Message
                };
            }
            if (!silent) throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ForceValidateAsync()
    {
        Log("ForceValidate", "Triggered");
        await ValidateAsync(silent: true);
        RestartValidationTimer();
    }

    #endregion

    #region HTTP

    private async Task<T?> PostAsync<T>(string endpoint, object request) where T : class
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(endpoint, request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode) 
            {
                Log("HTTP", $"Error status: {response.StatusCode}");
                return null;
            }
            
            var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            Log("HTTP", $"Exception: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Cache Operations

    private bool LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) 
            {
                Log("Cache", "No cache file exists");
                return false;
            }

            var encrypted = File.ReadAllBytes(_cachePath);
            var json = Decrypt(encrypted);
            
            if (string.IsNullOrEmpty(json)) 
            {
                Log("Cache", "Decryption failed or empty");
                DeleteCache();
                return false;
            }

            var cached = JsonSerializer.Deserialize<CachedLicense>(json, _jsonOptions);
            
            if (cached is null) 
            {
                Log("Cache", "Deserialization returned null");
                DeleteCache();
                return false;
            }
            
            if (cached.Version < LicenseConfig.CacheVersion)
            {
                Log("Cache", $"Old cache version {cached.Version}, need {LicenseConfig.CacheVersion}");
                DeleteCache();
                return false;
            }
            
            if (!cached.VerifyChecksum(_deviceId)) 
            {
                Log("Cache", "Checksum verification failed");
                DeleteCache();
                return false;
            }
            
            if (cached.DeviceId != _deviceId)
            {
                Log("Cache", "Device ID mismatch");
                DeleteCache();
                return false;
            }
            
            if ((DateTime.UtcNow - cached.CachedAt).TotalDays > LicenseConfig.OfflineGraceDays)
            {
                Log("Cache", "Cache expired (offline grace period exceeded)");
                DeleteCache();
                return false;
            }

            CurrentState = new LicenseState
            {
                LicenseKey = cached.NormalizedKey,
                Tier = cached.Tier,
                Status = cached.Status,
                ExpiresAt = cached.ExpiresAt,
                LastValidated = cached.CachedAt,
                IsOffline = true
            };
            
            Log("Cache", $"Loaded successfully: Tier={cached.Tier}, Status={cached.Status}, CachedAt={cached.CachedAt}");
            return true;
        }
        catch (Exception ex)
        {
            Log("Cache", $"Load exception: {ex.Message}");
            DeleteCache();
            return false;
        }
    }

    private async Task SaveCacheAsync(LicenseState state, string? signature)
    {
        try
        {
            var normalizedKey = state.NormalizedKey;
            
            var cached = new CachedLicense
            {
                NormalizedKey = normalizedKey,
                Tier = state.Tier,
                Status = state.Status,
                ExpiresAt = state.ExpiresAt,
                CachedAt = DateTime.UtcNow,
                Signature = signature,
                DeviceId = _deviceId,
                Version = LicenseConfig.CacheVersion,
                Checksum = CachedLicense.ComputeChecksum(
                    normalizedKey, state.Tier, state.Status, state.ExpiresAt, signature, _deviceId)
            };

            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(cached, _jsonOptions);
            var encrypted = Encrypt(json);
            
            await File.WriteAllBytesAsync(_cachePath, encrypted);
            
            Log("Cache", $"Saved: Tier={state.Tier}, Status={state.Status}");
        }
        catch (Exception ex)
        {
            Log("Cache", $"Save exception: {ex.Message}");
        }
    }

    private void DeleteCache()
    {
        try 
        { 
            if (File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
                Log("Cache", "Deleted");
            }
        }
        catch (Exception ex)
        {
            Log("Cache", $"Delete exception: {ex.Message}");
        }
    }

    #endregion

    #region Crypto & Validation

    private static string GenerateDeviceId()
    {
        var data = string.Join("|",
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessorCount,
            Environment.OSVersion.Version,
            Environment.SystemDirectory,
            Environment.Is64BitOperatingSystem);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash, 0, 16)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static bool ValidateTimestamp(long timestamp)
    {
        var serverTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var diff = DateTimeOffset.UtcNow - serverTime;
        return diff.TotalMinutes > -LicenseConfig.TimestampToleranceMinutesPast 
            && diff.TotalMinutes < LicenseConfig.TimestampToleranceMinutesFuture;
    }

    private static byte[] Encrypt(string text)
    {
        try
        {
            return ProtectedData.Protect(
                Encoding.UTF8.GetBytes(text),
                Encoding.UTF8.GetBytes(LicenseConfig.CacheEntropy),
                DataProtectionScope.CurrentUser);
        }
        catch
        {
            return Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
        }
    }

    private static string Decrypt(byte[] data)
    {
        try
        {
            var decrypted = ProtectedData.Unprotect(
                data,
                Encoding.UTF8.GetBytes(LicenseConfig.CacheEntropy),
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            try
            {
                var base64 = Encoding.UTF8.GetString(data);
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    #endregion

    #region Background Validation

    private void StartPeriodicValidation()
    {
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        
        var initialDelay = CurrentState.IsOffline 
            ? TimeSpan.FromSeconds(LicenseConfig.QuickValidationDelaySeconds)
            : TimeSpan.FromHours(LicenseConfig.ValidationIntervalHoursOnline);
        
        _validationTimer = new Timer(
            async _ => await PeriodicValidationCallback(),
            null,
            initialDelay,
            Timeout.InfiniteTimeSpan
        );
        
        Log("Validation", $"Started periodic validation, initial delay: {initialDelay}");
    }
    
    private async Task PeriodicValidationCallback()
    {
        if (_disposed || _validationCts?.IsCancellationRequested == true) return;
        
        try
        {
            if (CurrentState.LicenseKey is not null)
            {
                Log("Validation", "Running periodic validation...");
                await ValidateAsync(silent: true);
            }
        }
        catch (Exception ex)
        {
            Log("Validation", $"Periodic validation error: {ex.Message}");
        }
        finally
        {
            RestartValidationTimer();
        }
    }
    
    private void RestartValidationTimer()
    {
        if (_disposed || _validationTimer is null) return;
        
        var interval = CurrentState.IsOffline 
            ? TimeSpan.FromHours(LicenseConfig.ValidationIntervalHoursOffline)
            : TimeSpan.FromHours(LicenseConfig.ValidationIntervalHoursOnline);
        
        _validationTimer.Change(interval, Timeout.InfiniteTimeSpan);
        Log("Validation", $"Next validation in {interval.TotalHours:F1} hours");
    }

    #endregion

    #region Helpers

    private LicenseRequest CreateRequest(string key) => new()
    {
        LicenseKey = key,
        DeviceFingerprint = _deviceId,
        AppVersion = AppVersion
    };

    private static LicenseState CreateStateFromResponse(LicenseResponse r, string normalizedKey, bool offline) => new()
    {
        LicenseKey = normalizedKey,
        Tier = (LicenseTier)r.Tier,
        Status = r.ParsedStatus,
        ExpiresAt = r.ExpiresAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(r.ExpiresAt.Value).UtcDateTime
            : null,
        LastValidated = DateTime.UtcNow,
        IsOffline = offline
    };
    
    private static LicenseChangeReason DetermineChangeReason(LicenseState old, LicenseState newState)
    {
        if (old.LicenseKey is null && newState.LicenseKey is not null) return LicenseChangeReason.Activation;
        if (old.LicenseKey is not null && newState.LicenseKey is null) return LicenseChangeReason.Deactivation;
        if (old.Status != newState.Status && newState.Status is LicenseStatus.Expired) return LicenseChangeReason.Expiration;
        if (old.Status != newState.Status && newState.Status is LicenseStatus.Refunded or LicenseStatus.Blocked) return LicenseChangeReason.Revocation;
        if (old.IsOffline != newState.IsOffline) return LicenseChangeReason.NetworkChange;
        return LicenseChangeReason.Validation;
    }

    private static string AppVersion
    {
        get
        {
            try 
            { 
                return System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString(3) ?? "1.0.0"; 
            }
            catch { return "1.0.0"; }
        }
    }

    private static void Log(string context, string message)
    {
        var fullMessage = $"[License.{context}] {message}";
        try 
        { 
            AdbLogger.Instance.LogInfo("License", fullMessage); 
        }
        catch 
        { 
            Debug.WriteLine(fullMessage); 
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LicenseService));
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationTimer?.Dispose();
        _lock.Dispose();
        _http.Dispose();
        
        Log("Dispose", "Service disposed");
    }

    #endregion
}