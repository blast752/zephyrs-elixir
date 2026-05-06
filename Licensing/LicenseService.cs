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
    private readonly HttpClient _downloadHttp;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _dllLock = new(1, 1);
    private readonly string _cachePath;
    private readonly string _deviceId;
    private readonly JsonSerializerOptions _jsonOptions;
    
    private LicenseState _state = LicenseState.Free;
    private CancellationTokenSource? _validationCts;
    private Timer? _validationTimer;
    private bool _disposed;
    private bool _initialized;
    private string? _cachedProDllVersion;
    private string? _cachedProDllHash;

    #endregion

    #region Events & Properties

    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;
    public event EventHandler<ProDllProgressEventArgs>? DllDownloadProgress;

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
                old.Status != value.Status ||
                old.DllState != value.DllState)
            {
                Log("StateChange", $"{old.EffectiveTier}→{value.EffectiveTier}, Status: {value.Status}, Offline: {value.IsOffline}, Dll: {value.DllState}");
                StateChanged?.Invoke(this, new LicenseStateChangedEventArgs(old, value, reason));
            }
        }
    }

    public bool IsPro => CurrentState.EffectiveTier >= LicenseTier.Pro;
    public string DeviceFingerprint => _deviceId;
    public bool IsInitialized => _initialized;
    public string? LocalProDllVersion => _cachedProDllVersion;

    #endregion

    #region Constructor

    private LicenseService()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(ProDllConfig.DownloadConnectTimeoutSeconds),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(LicenseConfig.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(LicenseConfig.RequestTimeoutSeconds)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", $"ZephyrsElixir/{AppVersion}");

        var dlHandler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(ProDllConfig.DownloadConnectTimeoutSeconds),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };

        _downloadHttp = new HttpClient(dlHandler)
        {
            Timeout = TimeSpan.FromSeconds(ProDllConfig.DownloadReadTimeoutSeconds)
        };
        _downloadHttp.DefaultRequestHeaders.Add("User-Agent", $"ZephyrsElixir/{AppVersion}");

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
        
        var dllState = VerifyLocalProDll();
        if (dllState == ProDllState.Ready && CurrentState.Tier >= LicenseTier.Pro)
        {
            CurrentState = CurrentState with { DllState = ProDllState.Ready };
            Log("Init", $"Local Pro DLL verified, version: {_cachedProDllVersion}");
        }
        else if (CurrentState.Tier >= LicenseTier.Pro && dllState != ProDllState.Ready)
        {
            CurrentState = CurrentState with { DllState = dllState };
            Log("Init", $"Pro tier but DLL state: {dllState}");
        }
        
        if (CurrentState.LicenseKey is not null)
        {
            Log("Init", "License key found, validating...");
            await ValidateAsync(silent: true);
        }
        
        StartPeriodicValidation();
        
        _initialized = true;
        Log("Init", $"Initialization complete. IsPro: {IsPro}, Status: {CurrentState.Status}, DllState: {CurrentState.DllState}");
    }

    public async Task<LicenseResult> ActivateAsync(string licenseKey, IProgress<ActivationProgress>? progress = null)
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
            progress?.Report(new ActivationProgress(ActivationPhase.Contacting, 0));

            var request = CreateRequest(cleanKey);
            var response = await PostAsync<LicenseResponse>("?action=activate", request);
            
            if (response is null)
            {
                Log("Activate", "Server unavailable");
                return LicenseResult.Failure(Strings.License_Error_ServerUnavailable);
            }

            if (!response.Valid)
            {
                Log("Activate", $"Activation failed: {response.Error}");
                return LicenseResult.Failure(response.FullError ?? Strings.License_Activation_Failed);
            }

            if (!ValidateTimestamp(response.Timestamp))
            {
                Log("Activate", "Invalid timestamp");
                return LicenseResult.Failure(Strings.License_Error_InvalidResponse);
            }

            var newState = CreateStateFromResponse(response, normalizedKey, offline: false);
            
            if (newState.Tier >= LicenseTier.Pro && response.ProDll?.IsValid == true)
            {
                progress?.Report(new ActivationProgress(ActivationPhase.Downloading, 0));
                
                CurrentState = newState with { DllState = ProDllState.Downloading };

                var dllResult = await DownloadAndInstallProDllAsync(
                    response.ProDll, 
                    normalizedKey,
                    p => progress?.Report(new ActivationProgress(ActivationPhase.Downloading, p)));

                if (dllResult.Success)
                {
                    newState = newState with { DllState = ProDllState.Ready };
                    progress?.Report(new ActivationProgress(ActivationPhase.Installing, 100));
                    Log("Activate", $"Pro DLL installed: v{response.ProDll.Version}");
                }
                else
                {
                    newState = newState with { DllState = ProDllState.DownloadFailed };
                    Log("Activate", $"Pro DLL download failed: {dllResult.Error}");
                }
            }
            else if (newState.Tier >= LicenseTier.Pro)
            {
                var localDll = VerifyLocalProDll();
                newState = newState with { DllState = localDll };
            }

            CurrentState = newState;
            await SaveCacheAsync(newState, response.Signature);
            
            progress?.Report(new ActivationProgress(ActivationPhase.Complete, 100));
            
            Log("Activate", $"SUCCESS! Tier: {newState.Tier}, Status: {newState.Status}, Dll: {newState.DllState}");

            if (newState.Tier >= LicenseTier.Pro && newState.DllState == ProDllState.DownloadFailed)
                return LicenseResult.Success("License activated! Pro module download failed — it will retry automatically.", newState);
            
            return LicenseResult.Success("License activated successfully!", newState);
        }
        catch (TaskCanceledException)
        {
            Log("Activate", "Request timeout");
            return LicenseResult.Failure(Strings.License_Error_Timeout);
        }
        catch (Exception ex)
        {
            Log("Activate", $"Exception: {ex.Message}");
            return LicenseResult.Failure(string.Format(Strings.License_Error_Connection, ex.Message));
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

            DeleteProDllFiles();
            CurrentState = LicenseState.Free;
            DeleteCache();
            _cachedProDllVersion = null;
            _cachedProDllHash = null;
            
            Log("Deactivate", "License deactivated locally, Pro DLL removed");
            return LicenseResult.Success(Strings.License_Deactivated);
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
                
                var currentDllState = VerifyLocalProDll();
                newState = newState with { DllState = currentDllState };
                
                if (newState.Tier >= LicenseTier.Pro && response.ProDll?.IsValid == true)
                {
                    if (currentDllState != ProDllState.Ready)
                    {
                        _ = Task.Run(() => BackgroundDownloadProDllAsync(response.ProDll, CurrentState.NormalizedKey));
                    }
                    else if (IsNewerVersion(response.ProDll.Version, _cachedProDllVersion))
                    {
                        Log("Validate", $"New Pro DLL version available: {response.ProDll.Version} (current: {_cachedProDllVersion})");
                        newState = newState with { DllState = ProDllState.UpdateAvailable };
                        _ = Task.Run(() => BackgroundDownloadProDllAsync(response.ProDll, CurrentState.NormalizedKey)); 
                    }
                }
                
                CurrentState = newState;
                await SaveCacheAsync(newState, response.Signature);
                Log("Validate", $"Valid! Status: {newState.Status}, Dll: {newState.DllState}");
            }
            else if (response?.Valid == false)
            {
                Log("Validate", $"License invalid: {response.Error}, Status: {response.Status}");
                
                var status = response.ParsedStatus;
                
                if (status is LicenseStatus.Refunded or LicenseStatus.Blocked or LicenseStatus.Invalid)
                {
                    DeleteProDllFiles();
                    CurrentState = LicenseState.Free with { LastError = response.Error };
                    DeleteCache();
                    _cachedProDllVersion = null;
                    _cachedProDllHash = null;
                    Log("Validate", "License permanently revoked, cache and Pro DLL cleared");
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
                        LastError = Strings.License_Error_PaymentPastDue,
                        LastValidated = DateTime.UtcNow
                    };
                    await SaveCacheAsync(CurrentState, null);
                    Log("Validate", "Payment past due");
                }
                else
                {
                    DeleteProDllFiles();
                    CurrentState = LicenseState.Free with { LastError = response.Error };
                    DeleteCache();
                    _cachedProDllVersion = null;
                    _cachedProDllHash = null;
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
                        LastError = Strings.License_Error_ServerUnreachable
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

    public async Task RetryProDllDownloadAsync(IProgress<ActivationProgress>? progress = null)
    {
        ThrowIfDisposed();
        
        if (CurrentState.LicenseKey is null || CurrentState.Tier < LicenseTier.Pro)
            return;

        await _lock.WaitAsync();
        try
        {
            progress?.Report(new ActivationProgress(ActivationPhase.Contacting, 0));
            
            var request = CreateRequest(LicenseKeyHelper.Format(CurrentState.LicenseKey));
            var response = await PostAsync<LicenseResponse>("?action=validate", request);

            if (response?.Valid != true || response.ProDll?.IsValid != true)
            {
                Log("RetryDll", "Server did not provide DLL info");
                return;
            }

            progress?.Report(new ActivationProgress(ActivationPhase.Downloading, 0));
            CurrentState = CurrentState with { DllState = ProDllState.Downloading };

            var result = await DownloadAndInstallProDllAsync(
                response.ProDll,
                CurrentState.NormalizedKey,
                p => progress?.Report(new ActivationProgress(ActivationPhase.Downloading, p)));

            if (result.Success)
            {
                CurrentState = CurrentState with { DllState = ProDllState.Ready };
                await SaveCacheAsync(CurrentState, response.Signature);
                progress?.Report(new ActivationProgress(ActivationPhase.Complete, 100));
                Log("RetryDll", "Pro DLL downloaded successfully on retry");
            }
            else
            {
                CurrentState = CurrentState with { DllState = ProDllState.DownloadFailed };
                Log("RetryDll", $"Retry failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Log("RetryDll", $"Exception: {ex.Message}");
            CurrentState = CurrentState with { DllState = ProDllState.DownloadFailed };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> EnsureProDllAsync()
    {
        ThrowIfDisposed();

        if (CurrentState.LicenseKey is null) return false;
        if (CurrentState.Tier < LicenseTier.Pro) return false;
        if (CurrentState.OfflineGraceExpired) return false;

        var localState = VerifyLocalProDll();
        if (localState == ProDllState.Ready)
        {
            if (CurrentState.DllState != ProDllState.Ready)
                CurrentState = CurrentState with { DllState = ProDllState.Ready };
            return true;
        }

        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            Log("Ensure", "No network — cannot download");
            return false;
        }

        try
        {
            await RetryProDllDownloadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log("Ensure", $"RetryProDllDownloadAsync threw: {ex.Message}");
            return false;
        }

        return CurrentState.DllState == ProDllState.Ready;
    }

    public async Task<bool> CleanupProDllAsync()
    {
        ThrowIfDisposed();

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var hadFiles =
                File.Exists(ProDllConfig.EncryptedDllPath) ||
                File.Exists(ProDllConfig.FingerprintPath) ||
                File.Exists(ProDllConfig.TempDllPath);

            DeleteProDllFiles();
            _cachedProDllVersion = null;
            _cachedProDllHash = null;

            if (CurrentState.DllState != ProDllState.NotPresent)
                CurrentState = CurrentState with { DllState = ProDllState.NotPresent };

            return hadFiles;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string? GetDecryptedDllForLoading()
    {
        try
        {
            if (!File.Exists(ProDllConfig.EncryptedDllPath))
                return null;

            var fp = LoadFingerprint();
            if (fp is null || !fp.Verify(_deviceId))
            {
                Log("DllLoad", "Fingerprint invalid for loading");
                return null;
            }

            var encryptedBytes = File.ReadAllBytes(ProDllConfig.EncryptedDllPath);
            var dllBytes = DecryptDll(encryptedBytes, CurrentState.NormalizedKey);
            
            if (dllBytes is null)
            {
                Log("DllLoad", "Decryption failed");
                return null;
            }

            var hash = ComputeSha256(dllBytes);
            if (!string.Equals(hash, fp.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log("DllLoad", "Hash mismatch after decryption");
                return null;
            }

            var tempPath = ProDllConfig.TempDllPath;
            var dir = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(tempPath, dllBytes);
            Log("DllLoad", $"Decrypted DLL written to temp path ({dllBytes.Length} bytes)");
            return tempPath;
        }
        catch (Exception ex)
        {
            Log("DllLoad", $"Failed to prepare DLL for loading: {ex.Message}");
            return null;
        }
    }

    public void CleanupTempDll()
    {
        try
        {
            if (File.Exists(ProDllConfig.TempDllPath))
            {
                var bytes = new byte[new FileInfo(ProDllConfig.TempDllPath).Length];
                RandomNumberGenerator.Fill(bytes);
                File.WriteAllBytes(ProDllConfig.TempDllPath, bytes);
                File.Delete(ProDllConfig.TempDllPath);
                Log("DllLoad", "Temp DLL securely deleted");
            }
        }
        catch (Exception ex)
        {
            Log("DllLoad", $"Temp cleanup failed: {ex.Message}");
            try { File.Delete(ProDllConfig.TempDllPath); } catch { }
        }
    }

    #endregion

    #region Pro DLL Download & Installation

    private async Task<(bool Success, string? Error)> DownloadAndInstallProDllAsync(
        ProDllInfo dllInfo, string normalizedKey, Action<double>? onProgress = null)
    {
        await _dllLock.WaitAsync();
        try
        {
            if (dllInfo.IsUrlExpired)
                return (false, Strings.License_Error_DownloadExpired);

            if (!ValidateDownloadUrl(dllInfo.DownloadUrl!))
                return (false, Strings.License_Error_InvalidSource);

            if (!IsNewerVersion(dllInfo.Version, _cachedProDllVersion) && 
                VerifyLocalProDll() == ProDllState.Ready &&
                !string.IsNullOrEmpty(_cachedProDllVersion))
            {
                if (!dllInfo.AllowRollback)
                {
                    Log("DllDownload", $"Skipping: local v{_cachedProDllVersion} >= server v{dllInfo.Version}");
                    return (true, null);
                }
            }

            var diskCheck = CheckDiskSpace(dllInfo.SizeBytes);
            if (!diskCheck.Ok)
                return (false, diskCheck.Error);

            EnsureProDirectory();

            byte[] dllBytes;
            try
            {
                dllBytes = await DownloadWithProgressAsync(dllInfo.DownloadUrl!, dllInfo.SizeBytes, onProgress);
            }
            catch (Exception ex)
            {
                Log("DllDownload", $"Download failed: {ex.Message}");
                return (false, string.Format(Strings.License_Error_DownloadFailed, ex.Message));
            }

            if (dllBytes.Length < ProDllConfig.MinDllSizeBytes)
                return (false, Strings.License_Error_FileTooSmall);

            var hash = ComputeSha256(dllBytes);
            if (!string.Equals(hash, dllInfo.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log("DllDownload", $"Hash mismatch: expected {dllInfo.Sha256?[..12]}..., got {hash[..12]}...");
                return (false, Strings.License_Error_IntegrityFailed);
            }

            var encrypted = EncryptDll(dllBytes, normalizedKey);
            if (encrypted is null)
                return (false, Strings.License_Error_SecureFailed);

            try
            {
                await File.WriteAllBytesAsync(ProDllConfig.EncryptedDllPath, encrypted);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, Strings.License_Error_PermissionDenied);
            }
            catch (IOException ex) when (ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) ||
                                          ex.HResult == unchecked((int)0x80070020))
            {
                return (false, Strings.License_Error_FileLocked);
            }

            var fingerprint = ProDllFingerprint.Create(hash, dllInfo.Version!, _deviceId, dllBytes.Length);
            SaveFingerprint(fingerprint);

            _cachedProDllVersion = dllInfo.Version;
            _cachedProDllHash = hash;

            Log("DllDownload", $"Installed v{dllInfo.Version}, {dllBytes.Length} bytes, hash: {hash[..12]}...");
            return (true, null);
        }
        catch (Exception ex)
        {
            Log("DllDownload", $"Unexpected error: {ex.Message}");
            return (false, Strings.License_Error_Unexpected);
        }
        finally
        {
            _dllLock.Release();
        }
    }

    private async Task BackgroundDownloadProDllAsync(ProDllInfo dllInfo, string normalizedKey)
    {
        try
        {
            Log("BgDownload", $"Starting background download of v{dllInfo.Version}");
            
            CurrentState = CurrentState with { DllState = ProDllState.Downloading };

            var result = await DownloadAndInstallProDllAsync(dllInfo, normalizedKey);
            
            if (result.Success)
            {
                CurrentState = CurrentState with { DllState = ProDllState.Ready };
                Log("BgDownload", "Background download complete");
            }
            else
            {
                var prevState = VerifyLocalProDll();
                CurrentState = CurrentState with { DllState = prevState == ProDllState.Ready ? ProDllState.Ready : ProDllState.DownloadFailed };
                Log("BgDownload", $"Background download failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Log("BgDownload", $"Exception: {ex.Message}");
        }
    }

    private async Task<byte[]> DownloadWithProgressAsync(string url, long expectedSize, Action<double>? onProgress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? expectedSize;
        if (contentLength > ProDllConfig.MaxDllSizeBytes)
            throw new InvalidOperationException("File exceeds maximum allowed size.");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream(checked((int)Math.Min(contentLength, ProDllConfig.MaxDllSizeBytes)));
        
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > ProDllConfig.MaxDllSizeBytes)
                throw new InvalidOperationException("Download exceeded maximum allowed size.");
            
            ms.Write(buffer, 0, bytesRead);
            
            if (contentLength > 0)
                onProgress?.Invoke((double)totalRead / contentLength * 100.0);
        }

        DllDownloadProgress?.Invoke(this, new ProDllProgressEventArgs(100, totalRead, contentLength));
        return ms.ToArray();
    }

    private static bool ValidateDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return ProDllConfig.AllowedDownloadDomains.Contains(uri.Host);
    }

    private static (bool Ok, string? Error) CheckDiskSpace(long requiredBytes)
    {
        try
        {
            var dir = ProDllConfig.ProDirectory;
            var root = Path.GetPathRoot(dir);
            if (string.IsNullOrEmpty(root)) return (true, null);

            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < requiredBytes + ProDllConfig.MinDiskSpaceBytes)
                return (false, "Not enough disk space. Please free up some space and try again.");
            
            return (true, null);
        }
        catch
        {
            return (true, null);
        }
    }

    private static void EnsureProDirectory()
    {
        var dir = ProDllConfig.ProDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    #endregion

    #region Pro DLL Crypto

    private byte[]? EncryptDll(byte[] dllBytes, string normalizedKey)
    {
        try
        {
            var keyMaterial = DeriveEncryptionKey(normalizedKey);
            var nonce = new byte[ProDllConfig.AesNonceSize];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[dllBytes.Length];
            var tag = new byte[ProDllConfig.AesTagSize];

            using var aes = new AesGcm(keyMaterial, ProDllConfig.AesTagSize);
            aes.Encrypt(nonce, dllBytes, ciphertext, tag);

            var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

            CryptographicOperations.ZeroMemory(keyMaterial);
            return result;
        }
        catch (Exception ex)
        {
            Log("Crypto", $"Encrypt failed: {ex.Message}");
            return null;
        }
    }

    private byte[]? DecryptDll(byte[] encrypted, string normalizedKey)
    {
        try
        {
            if (encrypted.Length < ProDllConfig.AesNonceSize + ProDllConfig.AesTagSize + 1)
                return null;

            var keyMaterial = DeriveEncryptionKey(normalizedKey);

            var nonce = encrypted.AsSpan(0, ProDllConfig.AesNonceSize);
            var tag = encrypted.AsSpan(ProDllConfig.AesNonceSize, ProDllConfig.AesTagSize);
            var ciphertext = encrypted.AsSpan(ProDllConfig.AesNonceSize + ProDllConfig.AesTagSize);

            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(keyMaterial, ProDllConfig.AesTagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            CryptographicOperations.ZeroMemory(keyMaterial);
            return plaintext;
        }
        catch (Exception ex)
        {
            Log("Crypto", $"Decrypt failed: {ex.Message}");
            return null;
        }
    }

    private byte[] DeriveEncryptionKey(string normalizedKey)
    {
        var ikm = Encoding.UTF8.GetBytes($"{_deviceId}:{normalizedKey}:{ProDllConfig.EncryptionEntropy}");
        var key = new byte[ProDllConfig.AesKeySize / 8];
        
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm,
            key,
            Encoding.UTF8.GetBytes("ZephyrsElixir.ProDll.AES"),
            Encoding.UTF8.GetBytes(_deviceId));

        CryptographicOperations.ZeroMemory(ikm);
        return key;
    }

    private static byte[] ProtectKeyMaterial(byte[] data)
    {
        try
        {
            return ProtectedData.Protect(data, Encoding.UTF8.GetBytes(ProDllConfig.EncryptionEntropy), DataProtectionScope.CurrentUser);
        }
        catch
        {
            var hash = SHA256.HashData(data);
            return hash;
        }
    }

    #endregion

    #region Pro DLL Fingerprint

    private ProDllState VerifyLocalProDll()
    {
        try
        {
            if (!File.Exists(ProDllConfig.EncryptedDllPath))
            {
                _cachedProDllVersion = null;
                _cachedProDllHash = null;
                return ProDllState.NotPresent;
            }

            var fp = LoadFingerprint();
            if (fp is null || !fp.Verify(_deviceId))
            {
                Log("DllVerify", "Fingerprint verification failed");
                _cachedProDllVersion = null;
                _cachedProDllHash = null;
                return ProDllState.Corrupted;
            }

            var fileInfo = new FileInfo(ProDllConfig.EncryptedDllPath);
            if (fileInfo.Length < ProDllConfig.MinDllSizeBytes)
            {
                Log("DllVerify", "Encrypted file too small");
                return ProDllState.Corrupted;
            }

            _cachedProDllVersion = fp.Version;
            _cachedProDllHash = fp.Sha256;
            return ProDllState.Ready;
        }
        catch (Exception ex)
        {
            Log("DllVerify", $"Verification error: {ex.Message}");
            _cachedProDllVersion = null;
            _cachedProDllHash = null;
            return ProDllState.Corrupted;
        }
    }

    private ProDllFingerprint? LoadFingerprint()
    {
        try
        {
            if (!File.Exists(ProDllConfig.FingerprintPath))
                return null;

            var encrypted = File.ReadAllBytes(ProDllConfig.FingerprintPath);
            var json = Decrypt(encrypted);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<ProDllFingerprint>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log("Fingerprint", $"Load failed: {ex.Message}");
            return null;
        }
    }

    private void SaveFingerprint(ProDllFingerprint fp)
    {
        try
        {
            var json = JsonSerializer.Serialize(fp, _jsonOptions);
            var encrypted = Encrypt(json);
            File.WriteAllBytes(ProDllConfig.FingerprintPath, encrypted);
            Log("Fingerprint", $"Saved: v{fp.Version}");
        }
        catch (Exception ex)
        {
            Log("Fingerprint", $"Save failed: {ex.Message}");
        }
    }

    private void DeleteProDllFiles()
    {
        try
        {
            if (File.Exists(ProDllConfig.EncryptedDllPath))
                File.Delete(ProDllConfig.EncryptedDllPath);
            if (File.Exists(ProDllConfig.FingerprintPath))
                File.Delete(ProDllConfig.FingerprintPath);
            if (File.Exists(ProDllConfig.TempDllPath))
                File.Delete(ProDllConfig.TempDllPath);
            Log("DllCleanup", "Pro DLL files deleted");
        }
        catch (Exception ex)
        {
            Log("DllCleanup", $"Delete error: {ex.Message}");
        }
    }

    private static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static bool IsNewerVersion(string? serverVersion, string? localVersion)
    {
        if (string.IsNullOrEmpty(serverVersion)) return false;
        if (string.IsNullOrEmpty(localVersion)) return true;
        
        if (Version.TryParse(serverVersion, out var sv) && Version.TryParse(localVersion, out var lv))
            return sv > lv;
        
        return !string.Equals(serverVersion, localVersion, StringComparison.OrdinalIgnoreCase);
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
                Log("Cache", "Cache expired (offline grace period exceeded) — purging Pro DLL");
                DeleteCache();
                DeleteProDllFiles();
                _cachedProDllVersion = null;
                _cachedProDllHash = null;
                return false;
            }

            _cachedProDllVersion = cached.ProDllVersion;
            _cachedProDllHash = cached.ProDllHash;

            CurrentState = new LicenseState
            {
                LicenseKey = cached.NormalizedKey,
                Tier = cached.Tier,
                Status = cached.Status,
                ExpiresAt = cached.ExpiresAt,
                LastValidated = cached.CachedAt,
                IsOffline = true,
                DllState = ProDllState.NotPresent
            };
            
            Log("Cache", $"Loaded successfully: Tier={cached.Tier}, Status={cached.Status}, CachedAt={cached.CachedAt}, DllVersion={cached.ProDllVersion}");
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
                ProDllVersion = _cachedProDllVersion,
                ProDllHash = _cachedProDllHash,
                Checksum = CachedLicense.ComputeChecksum(
                    normalizedKey, state.Tier, state.Status, state.ExpiresAt, signature, _deviceId, _cachedProDllVersion)
            };

            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(cached, _jsonOptions);
            var encrypted = Encrypt(json);
            
            await File.WriteAllBytesAsync(_cachePath, encrypted);
            
            Log("Cache", $"Saved: Tier={state.Tier}, Status={state.Status}, DllVersion={_cachedProDllVersion}");
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
        _validationCts?.Dispose();
        _validationCts = new CancellationTokenSource();

        var initialDelay = CurrentState.IsOffline
            ? TimeSpan.FromSeconds(LicenseConfig.QuickValidationDelaySeconds)
            : TimeSpan.FromHours(LicenseConfig.ValidationIntervalHoursOnline);

        _validationTimer?.Dispose();
        _validationTimer = new Timer(_ => _ = PeriodicValidationCallback(), null, initialDelay, Timeout.InfiniteTimeSpan);
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
        AppVersion = AppVersion,
        ProDllVersion = _cachedProDllVersion
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
        if (old.DllState != newState.DllState) return LicenseChangeReason.ProDllChanged;
        if (old.IsOffline != newState.IsOffline) return LicenseChangeReason.NetworkChange;
        return LicenseChangeReason.Validation;
    }

    private static string AppVersion
    {
        get
        {
            try 
            { 
                return Assembly.GetExecutingAssembly()
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
        _dllLock.Dispose();
        _http.Dispose();
        _downloadHttp.Dispose();
        
        CleanupTempDll();
        
        Log("Dispose", "Service disposed");
    }

    #endregion
}

#region Activation Progress

public enum ActivationPhase
{
    Contacting,
    Downloading,
    Installing,
    Complete
}

public sealed class ActivationProgress
{
    public ActivationPhase Phase { get; }
    public double Percent { get; }

    public ActivationProgress(ActivationPhase phase, double percent)
    {
        Phase = phase;
        Percent = Math.Clamp(percent, 0, 100);
    }

    public string DisplayMessage => Phase switch
    {
        ActivationPhase.Contacting => Strings.License_Progress_Contacting,
        ActivationPhase.Downloading => $"{Strings.License_Progress_Downloading} {Percent:F0}%",
        ActivationPhase.Installing => Strings.License_Progress_Installing,
        ActivationPhase.Complete => Strings.License_Progress_Complete,
        _ => Strings.License_Dialog_PleaseWait
    };
}

public sealed class ProDllProgressEventArgs : EventArgs
{
    public double Percent { get; }
    public long BytesDownloaded { get; }
    public long TotalBytes { get; }

    public ProDllProgressEventArgs(double percent, long bytesDownloaded, long totalBytes)
    {
        Percent = percent;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }
}

#endregion
