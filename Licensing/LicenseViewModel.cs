namespace ZephyrsElixir.Licensing;

public sealed partial class LicenseViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanActivate))]
    [NotifyPropertyChangedFor(nameof(KeyValidationHint))]
    [NotifyPropertyChangedFor(nameof(ShowKeyValidationHint))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanActivate))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryDllDownloadCommand))]
    private bool _isLoading;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(ShowError))]
    private string? _errorMessage;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuccess))]
    private string? _successMessage;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsPro))]
    [NotifyPropertyChangedFor(nameof(IsFree))]
    [NotifyPropertyChangedFor(nameof(TierDisplayName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ExpirationText))]
    [NotifyPropertyChangedFor(nameof(OfflineStatusText))]
    [NotifyPropertyChangedFor(nameof(OfflineStatusIcon))]
    [NotifyPropertyChangedFor(nameof(ShowOfflineWarning))]
    [NotifyPropertyChangedFor(nameof(ShowExpiration))]
    [NotifyPropertyChangedFor(nameof(ShowStatusWarning))]
    [NotifyPropertyChangedFor(nameof(MaskedLicenseKey))]
    [NotifyPropertyChangedFor(nameof(HasActiveLicense))]
    [NotifyPropertyChangedFor(nameof(ShowDeactivateButton))]
    [NotifyPropertyChangedFor(nameof(ShowDllDownloadFailed))]
    [NotifyPropertyChangedFor(nameof(ShowDllDownloading))]
    [NotifyPropertyChangedFor(nameof(DllStateText))]
    private LicenseState _currentState;

    [ObservableProperty]
    private string? _activationStatusMessage;

    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadProgress))]
    private bool _isDownloading;

    public bool IsPro => CurrentState.EffectiveTier >= LicenseTier.Pro;
    public bool IsFree => !IsPro;
    public string TierDisplayName => IsPro ? "Pro" : Strings.License_Status_Free;
    
    public bool HasActiveLicense => CurrentState.LicenseKey is not null;
    public bool ShowDeactivateButton => HasActiveLicense && CurrentState.IsActive;
    public bool CanActivate => !IsLoading && LicenseKeyHelper.IsValidFormat(LicenseKey);
    
    public string DeviceId => LicenseService.Instance.DeviceFingerprint;
    public string MaskedLicenseKey => CurrentState.MaskedKey;
    
    public bool ShowError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowSuccess => !string.IsNullOrEmpty(SuccessMessage);
    public bool ShowDownloadProgress => IsDownloading;
    
    public bool ShowDllDownloadFailed => CurrentState.DllState == ProDllState.DownloadFailed && CurrentState.IsActive;
    public bool ShowDllDownloading => CurrentState.DllState == ProDllState.Downloading;
    
    public string? DllStateText => CurrentState.DllState switch
    {
        ProDllState.Downloading => Strings.License_Dll_Downloading,
        ProDllState.DownloadFailed => Strings.License_Dll_DownloadFailed,
        ProDllState.Corrupted => Strings.License_Dll_Corrupted,
        ProDllState.UpdateAvailable => Strings.License_Dll_UpdateAvailable,
        _ => null
    };
    
    public string StatusText => CurrentState switch
    {
        { Status: LicenseStatus.PastDue } => Strings.License_Status_PastDue,
        { Status: LicenseStatus.Suspended } => Strings.License_Status_Suspended,
        { Status: LicenseStatus.Expired } => Strings.License_Status_Expired,
        { Status: LicenseStatus.Canceled } => Strings.License_Status_Canceled,
        { IsActive: true, Status: LicenseStatus.Trialing } => Strings.License_Status_Trial,
        { IsActive: true, Status: LicenseStatus.Completed } => Strings.License_Status_Lifetime,
        { IsActive: true } => Strings.License_Status_Active,
        _ => Strings.License_Status_Free
    };
    
    public string ExpirationText => CurrentState switch
    {
        { ExpiresAt: null } when IsPro => Strings.License_Status_SubscriptionActive,
        { ExpiresAt: var exp } when exp > DateTime.UtcNow => string.Format(Strings.License_Status_Renews, exp.Value.ToString("MMMM dd, yyyy")),
        { IsExpired: true } => Strings.License_Status_Expired,
        _ => string.Empty
    };
    
    public string OfflineStatusText => CurrentState switch
    {
        { IsOffline: true, OfflineGraceExpired: true } => Strings.License_Status_OfflineTooLong,
        { IsOffline: true } => string.Format(Strings.License_Status_OfflineMode, LicenseConfig.OfflineGraceDays - (int)(DateTime.UtcNow - CurrentState.LastValidated).TotalDays),
        { IsActive: true } => Strings.License_Status_Validated,
        _ => string.Empty
    };

    /// <summary>Icon registry key matching <see cref="OfflineStatusText"/>; empty when there is no line to show.</summary>
    public string OfflineStatusIcon => CurrentState switch
    {
        { IsOffline: true } => "warning",
        { IsActive: true } => "check",
        _ => string.Empty
    };
    
    public bool ShowOfflineWarning => CurrentState.IsOffline && HasActiveLicense;
    public bool ShowExpiration => IsPro && CurrentState.ExpiresAt.HasValue;
    public bool ShowStatusWarning => CurrentState.Status is LicenseStatus.PastDue or LicenseStatus.Suspended or LicenseStatus.Expired;
    
    public string? KeyValidationHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LicenseKey)) return null;
            if (LicenseKeyHelper.IsValidFormat(LicenseKey)) return null;
            
            var normalized = LicenseKeyHelper.Normalize(LicenseKey);
            if (normalized.Length < 5) return null;
            
            return LicenseKeyHelper.GetValidationError(LicenseKey);
        }
    }
    
    public bool ShowKeyValidationHint => !string.IsNullOrEmpty(KeyValidationHint);

    public LicenseViewModel()
    {
        CurrentState = LicenseService.Instance.CurrentState;
        LicenseService.Instance.StateChanged += OnLicenseStateChanged;
    }

    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CurrentState = e.NewState;
            
            if (e.Reason == LicenseChangeReason.Revocation)
            {
                SetError(string.Format(Strings.License_Revoked, e.NewState.LastError ?? Strings.License_Revoked_ContactSupport));
            }
            else if (e.Reason == LicenseChangeReason.Expiration)
            {
                SetError(Strings.License_Expired_Renew);
            }
            else if (e.Downgraded && e.Reason != LicenseChangeReason.Deactivation)
            {
                SetError(e.NewState.LastError ?? Strings.License_Status_Changed);
            }
            else if (e.CameOnline && e.NewState.IsActive)
            {
                ShowSuccessMessage(Strings.License_Status_Validated);
            }
            else if (e.Reason == LicenseChangeReason.ProDllChanged && e.NewState.DllState == ProDllState.Ready && e.NewState.IsActive)
            {
                ShowSuccessMessage(Strings.License_Progress_Complete);
                ProLoader.ReloadIfNeeded();
            }
        });
    }

    public void Dispose()
    {
        LicenseService.Instance.StateChanged -= OnLicenseStateChanged;
    }

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        await ExecuteAsync(async () =>
        {
            ActivationStatusMessage = null;
            IsDownloading = false;
            DownloadPercent = 0;

            var progress = new Progress<ActivationProgress>(p =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ActivationStatusMessage = p.DisplayMessage;
                    if (p.Phase == ActivationPhase.Downloading)
                    {
                        IsDownloading = true;
                        DownloadPercent = p.Percent;
                    }
                    else if (p.Phase == ActivationPhase.Complete)
                    {
                        IsDownloading = false;
                        DownloadPercent = 100;
                    }
                });
            });

            var result = await LicenseService.Instance.ActivateAsync(LicenseKey, progress);
            
            IsDownloading = false;
            ActivationStatusMessage = null;

            if (result.IsSuccess)
            {
                ShowSuccessMessage(result.Message ?? Strings.License_Progress_Complete);
                LicenseKey = string.Empty;
                
                if (result.NewState?.DllState == ProDllState.Ready)
                    ProLoader.ReloadIfNeeded();
            }
            else
            {
                SetError(result.Error ?? Strings.License_Activation_Failed);
            }
        });
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (IsLoading || !HasActiveLicense) return;
        
        await ExecuteAsync(async () =>
        {
            ProLoader.Unload();
            var result = await LicenseService.Instance.DeactivateAsync();
            
            if (result.IsSuccess)
                ShowSuccessMessage(result.Message ?? Strings.License_Deactivated_Success);
            else
                SetError(result.Error ?? Strings.License_Deactivated_Failed);
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading || !HasActiveLicense) return;
        
        await ExecuteAsync(async () =>
        {
            await LicenseService.Instance.ForceValidateAsync();
            
            if (CurrentState.LastError is not null)
                SetError(CurrentState.LastError);
            else if (CurrentState.IsActive)
                ShowSuccessMessage(Strings.License_Status_Refreshed);
        }, Strings.License_RefreshFailed);
    }

    [RelayCommand]
    private async Task RetryDllDownloadAsync()
    {
        if (IsLoading) return;

        await ExecuteAsync(async () =>
        {
            IsDownloading = true;
            DownloadPercent = 0;
            ActivationStatusMessage = Strings.License_Dll_Downloading;

            var progress = new Progress<ActivationProgress>(p =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ActivationStatusMessage = p.DisplayMessage;
                    if (p.Phase == ActivationPhase.Downloading)
                        DownloadPercent = p.Percent;
                });
            });

            await LicenseService.Instance.RetryProDllDownloadAsync(progress);

            IsDownloading = false;
            ActivationStatusMessage = null;

            if (CurrentState.DllState == ProDllState.Ready)
            {
                ShowSuccessMessage(Strings.License_Progress_Complete);
                ProLoader.ReloadIfNeeded();
            }
            else
            {
                SetError(Strings.License_Dll_DownloadFailed);
            }
        });
    }

    [RelayCommand]
    private static void OpenPurchasePage()
        => ShellUtils.OpenUrl(LicenseConfig.PurchaseUrl);

    [RelayCommand]
    private void CopyDeviceId()
    {
        try
        {
            Clipboard.SetText(DeviceId);
            ShowSuccessTemporary(Strings.License_DeviceIdCopied, TimeSpan.FromSeconds(2));
        }
        catch { }
    }

    private async Task ExecuteAsync(Func<Task> action, string? defaultError = null)
    {
        ClearMessages();
        IsLoading = true;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetError(defaultError ?? $"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            IsDownloading = false;
        }
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    private void SetError(string message) => ErrorMessage = message;
    
    private void ShowSuccessMessage(string message)
    {
        SuccessMessage = message;
        ErrorMessage = null;
    }
    
    private void ShowSuccessTemporary(string message, TimeSpan duration)
    {
        SuccessMessage = message;
        _ = Task.Delay(duration).ContinueWith(_ =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (SuccessMessage == message)
                    SuccessMessage = null;
            });
        }, TaskScheduler.Default);
    }
}
