namespace ZephyrsElixir.Licensing;

public sealed partial class LicenseViewModel : ObservableObject, IDisposable
{
    #region Observable Properties

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
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ExpirationText))]
    [NotifyPropertyChangedFor(nameof(OfflineStatusText))]
    [NotifyPropertyChangedFor(nameof(ShowOfflineWarning))]
    [NotifyPropertyChangedFor(nameof(ShowExpiration))]
    [NotifyPropertyChangedFor(nameof(ShowStatusWarning))]
    [NotifyPropertyChangedFor(nameof(MaskedLicenseKey))]
    [NotifyPropertyChangedFor(nameof(HasActiveLicense))]
    [NotifyPropertyChangedFor(nameof(ShowDeactivateButton))]
    private LicenseState _currentState;

    #endregion

    #region Computed Properties

    public bool IsPro => CurrentState.EffectiveTier >= LicenseTier.Pro;
    public bool IsFree => !IsPro;
    public string TierDisplayName => IsPro ? "Pro" : "Free";
    
    public bool HasActiveLicense => CurrentState.LicenseKey is not null;
    public bool ShowDeactivateButton => HasActiveLicense && CurrentState.IsActive;
    public bool CanActivate => !IsLoading && LicenseKeyHelper.IsValidFormat(LicenseKey);
    
    public string DeviceId => LicenseService.Instance.DeviceFingerprint;
    public string MaskedLicenseKey => CurrentState.MaskedKey;
    
    public bool ShowError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowSuccess => !string.IsNullOrEmpty(SuccessMessage);
    
    public string StatusIcon => CurrentState switch
    {
        { Status: LicenseStatus.PastDue } => "⚠",
        { Status: LicenseStatus.Suspended } => "⛔",
        { Status: LicenseStatus.Expired } => "⏰",
        { IsOffline: true } => "⚠",
        { IsActive: true } => "✓",
        _ => "○"
    };
    
    public string StatusText => CurrentState switch
    {
        { Status: LicenseStatus.PastDue } => "Payment past due",
        { Status: LicenseStatus.Suspended } => "License suspended",
        { Status: LicenseStatus.Expired } => "License expired",
        { Status: LicenseStatus.Canceled } => "Subscription canceled",
        { IsActive: true } => CurrentState.StatusDescription,
        _ => "Free"
    };
    
    public string ExpirationText => CurrentState switch
    {
        { ExpiresAt: null } when IsPro => "Subscription active",
        { ExpiresAt: var exp } when exp > DateTime.UtcNow => $"Renews {exp.Value:MMMM dd, yyyy}",
        { IsExpired: true } => "Expired",
        _ => string.Empty
    };
    
    public string OfflineStatusText => CurrentState switch
    {
        { IsOffline: true, OfflineGraceExpired: true } => "⚠ Offline too long - please reconnect to validate",
        { IsOffline: true } => $"⚠ Offline mode ({LicenseConfig.OfflineGraceDays - (int)(DateTime.UtcNow - CurrentState.LastValidated).TotalDays} days remaining)",
        { IsActive: true } => "✓ License validated",
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

    #endregion

    #region Constructor & Cleanup

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
                SetError($"License revoked: {e.NewState.LastError ?? "Contact support for details"}");
            }
            else if (e.Reason == LicenseChangeReason.Expiration)
            {
                SetError("Your license has expired. Please renew to continue using Pro features.");
            }
            else if (e.Downgraded && e.Reason != LicenseChangeReason.Deactivation)
            {
                SetError(e.NewState.LastError ?? "License status changed");
            }
            else if (e.CameOnline && e.NewState.IsActive)
            {
                ShowSuccessMessage("License validated successfully");
            }
        });
    }

    public void Dispose()
    {
        LicenseService.Instance.StateChanged -= OnLicenseStateChanged;
    }

    public void Cleanup() => Dispose();

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await LicenseService.Instance.ActivateAsync(LicenseKey);
            
            if (result.IsSuccess)
            {
                ShowSuccessMessage("License activated successfully! 🎉");
                LicenseKey = string.Empty;
            }
            else
            {
                SetError(result.Error ?? "Activation failed. Please check your key.");
            }
        });
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (IsLoading || !HasActiveLicense) return;
        
        await ExecuteAsync(async () =>
        {
            var result = await LicenseService.Instance.DeactivateAsync();
            
            if (result.IsSuccess)
                ShowSuccessMessage(result.Message ?? "License deactivated successfully.");
            else
                SetError(result.Error ?? "Deactivation failed.");
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading || !HasActiveLicense) return;
        
        await ExecuteAsync(async () =>
        {
            await LicenseService.Instance.ForceValidateAsync();
            
            if (CurrentState.IsActive)
                ShowSuccessMessage("License status refreshed.");
            else if (CurrentState.LastError is not null)
                SetError(CurrentState.LastError);
        }, "Could not refresh license status");
    }

    [RelayCommand]
    private static void OpenPurchasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LicenseConfig.PurchaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void CopyDeviceId()
    {
        try
        {
            Clipboard.SetText(DeviceId);
            ShowSuccessTemporary("Device ID copied to clipboard", TimeSpan.FromSeconds(2));
        }
        catch { }
    }

    #endregion

    #region Helpers

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

    #endregion
}