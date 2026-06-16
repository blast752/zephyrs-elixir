
namespace ZephyrsElixir.UI.Views;

/// <summary>
/// Base for view-models that refresh localized members when the UI language changes.
/// Wires a single weak handler to <see cref="TranslationManager.LanguageChanged"/> so derived
/// view-models only override <see cref="OnLanguageChanged"/>.
/// </summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    protected LocalizedObservableObject()
    {
        WeakEventManager<TranslationManager, EventArgs>.AddHandler(
            TranslationManager.Instance, nameof(TranslationManager.LanguageChanged), OnLanguageChangedInternal);
    }

    private void OnLanguageChangedInternal(object? sender, EventArgs e) => OnLanguageChanged();

    protected virtual void OnLanguageChanged() { }
}

public sealed class AppInfoViewModel : LocalizedObservableObject, IEquatable<AppInfoViewModel>
{
    public event Action<bool>? IsSelectedChanged;

    private bool _isSelected;
    private AppState _state;
    private BitmapImage? _icon;
    private bool _isLoadingIcon;
    private SafetyRiskLevel _riskLevel = SafetyRiskLevel.Unknown;
    private double _safetyScore;
    private string _aiDescription = Strings.Debloat_Risk_Analyzing;
    private string? _warningMessage;

    protected override void OnLanguageChanged()
    {
        if (_aiDescription == Strings.Debloat_Risk_Analyzing)
            OnPropertyChanged(nameof(AiDescription));
        OnPropertyChanged(nameof(RiskDisplay));
    }

    public string Name { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    public AppState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) IsSelectedChanged?.Invoke(_isSelected); }
    }

    public BitmapImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsLoadingIcon
    {
        get => _isLoadingIcon;
        set => SetProperty(ref _isLoadingIcon, value);
    }

    public SafetyRiskLevel RiskLevel
    {
        get => _riskLevel;
        set
        {
            if (!SetProperty(ref _riskLevel, value)) return;
            OnPropertyChanged(nameof(RiskDisplay));
            OnPropertyChanged(nameof(RiskColor));
            OnPropertyChanged(nameof(RiskBadgeBackground));
            OnPropertyChanged(nameof(CanSafelyRemove));
        }
    }

    public double SafetyScore
    {
        get => _safetyScore;
        set { if (SetProperty(ref _safetyScore, value)) OnPropertyChanged(nameof(SafetyScoreDisplay)); }
    }

    public string AiDescription
    {
        get => _aiDescription;
        set => SetProperty(ref _aiDescription, value);
    }

    public string? WarningMessage
    {
        get => _warningMessage;
        set
        {
            if (!SetProperty(ref _warningMessage, value)) return;
            OnPropertyChanged(nameof(RiskDisplay));
            OnPropertyChanged(nameof(HasWarning));
        }
    }

    public string SafetyScoreDisplay => $"{SafetyScore:F0}%";
    public bool HasWarning => !string.IsNullOrEmpty(WarningMessage);
    public bool CanSafelyRemove => RiskLevel == SafetyRiskLevel.Safe;

    public string RiskDisplay => !string.IsNullOrEmpty(WarningMessage) 
    ? WarningMessage.ToUpperInvariant() 
    : RiskLevel switch
    {
        SafetyRiskLevel.Safe => Strings.Debloat_Risk_Safe,
        SafetyRiskLevel.Caution => Strings.Debloat_Risk_Caution,
        SafetyRiskLevel.Critical => Strings.Debloat_Risk_Critical,
        _ => Strings.Debloat_Risk_Analyzing
    };

    public Brush RiskColor => GetBrush(RiskLevel, false);
    public Brush RiskBadgeBackground => GetBrush(RiskLevel, true);

    private static Brush GetBrush(SafetyRiskLevel level, bool background) => (level, background) switch
    {
        (SafetyRiskLevel.Safe, false) => SafeBrush,
        (SafetyRiskLevel.Safe, true) => SafeBackgroundBrush,
        (SafetyRiskLevel.Caution, false) => CautionBrush,
        (SafetyRiskLevel.Caution, true) => CautionBackgroundBrush,
        (SafetyRiskLevel.Critical, false) => CriticalBrush,
        (SafetyRiskLevel.Critical, true) => CriticalBackgroundBrush,
        (_, false) => UnknownBrush,
        _ => UnknownBackgroundBrush
    };

    private static readonly SolidColorBrush SafeBrush = AppBrushes.Green;
    private static readonly SolidColorBrush CautionBrush = AppBrushes.Caution;
    private static readonly SolidColorBrush CriticalBrush = AppBrushes.Critical;
    private static readonly SolidColorBrush UnknownBrush = UIHelpers.FrozenSolid(128, 128, 128, 80);
    private static readonly SolidColorBrush SafeBackgroundBrush = UIHelpers.FrozenSolid(50, 205, 50, 30);
    private static readonly SolidColorBrush CautionBackgroundBrush = UIHelpers.FrozenSolid(255, 190, 0, 30);
    private static readonly SolidColorBrush CriticalBackgroundBrush = UIHelpers.FrozenSolid(220, 20, 60, 30);
    private static readonly SolidColorBrush UnknownBackgroundBrush = UIHelpers.FrozenSolid(128, 128, 128, 20);

    public void ApplyIntelligence(PackageIntelligenceData data)
    {
        RiskLevel = data.RiskLevel;
        SafetyScore = data.SafetyScore;
        AiDescription = data.Description;
        WarningMessage = data.WarningMessage;
    }

    public bool Equals(AppInfoViewModel? other) => other is not null && PackageName == other.PackageName;
    public override bool Equals(object? obj) => Equals(obj as AppInfoViewModel);
    public override int GetHashCode() => PackageName.GetHashCode();
}

public sealed class HistoryAppViewModel : LocalizedObservableObject
{
    private bool _isSelected;
    private BitmapImage? _icon;

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(StatusDisplay));

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public BitmapImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string Name { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTime UninstallDate { get; init; }
    public string? LocalApkPath { get; init; }
    public bool IsSystemApp { get; init; }

    public bool HasBackup => !string.IsNullOrEmpty(LocalApkPath) && File.Exists(LocalApkPath);
    public string DateDisplay => UninstallDate.ToString("g");
    public string StatusDisplay => HasBackup 
        ? Strings.Debloat_History_Status_Backup 
        : Strings.Debloat_History_Status_Uninstalled;

    public string? DeviceSerial { get; init; }
    public bool IsLegacyEntry => string.IsNullOrEmpty(DeviceSerial);
}

public sealed class AppDetailsViewModel : LocalizedObservableObject
{
    private readonly AppInfoViewModel _app;
    private StandbyBucket _selectedBucket;
    private bool _isLoading;

    public AppInfoViewModel App => _app;
    public ObservableCollection<PermissionItem> Permissions { get; } = new();

    public Dictionary<StandbyBucket, string> BucketOptions => new()
    {
        { StandbyBucket.Active, Strings.Debloat_Bucket_Active },
        { StandbyBucket.WorkingSet, Strings.Debloat_Bucket_WorkingSet },
        { StandbyBucket.Frequent, Strings.Debloat_Bucket_Frequent },
        { StandbyBucket.Rare, Strings.Debloat_Bucket_Rare },
        { StandbyBucket.Restricted, Strings.Debloat_Bucket_Restricted }
    };

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public StandbyBucket SelectedBucket
    {
        get => _selectedBucket;
        set { if (SetProperty(ref _selectedBucket, value)) _ = PermissionManager.SetAppStandbyBucketAsync(_app.PackageName, value); }
    }

    public int GrantedPermissionsCount => Permissions.Count(p => p.IsGranted);
    public bool HasGrantedPermissions => Permissions.Any(p => p.IsGranted);

    public AppDetailsViewModel(AppInfoViewModel app)
    {
        _app = app;
    }

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(BucketOptions));

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            Permissions.Clear();
            foreach (var p in await PermissionManager.GetAppPermissionsAsync(_app.PackageName))
            {
                p.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(PermissionItem.IsGranted))
                    {
                        await PermissionManager.SetPermissionAsync(_app.PackageName, p.PermissionKey, p.IsGranted);
                        OnPropertyChanged(nameof(GrantedPermissionsCount)); OnPropertyChanged(nameof(HasGrantedPermissions));
                    }
                };
                Permissions.Add(p);
            }
            _selectedBucket = await PermissionManager.GetAppStandbyBucketAsync(_app.PackageName);
            OnPropertyChanged(nameof(SelectedBucket));
            OnPropertyChanged(nameof(GrantedPermissionsCount)); OnPropertyChanged(nameof(HasGrantedPermissions));
        }
        finally { IsLoading = false; }
    }

    public async Task<int> RevokeAllPermissionsAsync()
    {
        var granted = Permissions.Where(p => p.IsGranted).ToList();
        int count = 0;
        foreach (var p in granted)
        {
            try
            {
                await PermissionManager.SetPermissionAsync(_app.PackageName, p.PermissionKey, false);
                p.IsGranted = false;
                count++;
            }
            catch (Exception ex) { AdbLogger.Instance.LogWarning("RevokeAll", $"Failed: {p.PermissionKey}: {ex.Message}"); }
        }
        OnPropertyChanged(nameof(GrantedPermissionsCount)); OnPropertyChanged(nameof(HasGrantedPermissions));
        return count;
    }
}

public sealed class DnsProviderViewModel : LocalizedObservableObject
{
    private int _pingMs = -1;
    private bool _isPinging;

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(PingDisplay));

    public string Name { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;

    public int PingMs
    {
        get => _pingMs;
        set { if (SetProperty(ref _pingMs, value)) OnPropertyChanged(nameof(PingDisplay)); }
    }

    public bool IsPinging
    {
        get => _isPinging;
        set { if (SetProperty(ref _isPinging, value)) OnPropertyChanged(nameof(PingDisplay)); }
    }

    public string PingDisplay => IsPinging ? "..." : PingMs switch
    {
        < 0 => "",
        0 => Strings.Advanced_DNS_Ping_Timeout,
        < 50 => $"{PingMs}ms ⚡",
        < 100 => $"{PingMs}ms ✓",
        < 200 => $"{PingMs}ms",
        _ => $"{PingMs}ms ⚠"
    };
}

public class MenuItemBase : ObservableObject
{
    private readonly Func<string> _titleAccessor;
    private readonly Func<string> _descriptionAccessor;

    public string Key { get; }
    public string Icon { get; }
    public string Title => _titleAccessor();
    public string Description => _descriptionAccessor();
    public Brush IconBrush { get; }
    public Color GlowColor { get; }

    public MenuItemBase(string key, string icon, Func<string> titleAccessor, Func<string> descriptionAccessor,
                           Brush iconBrush)
    {
        Key = key;
        Icon = icon;
        _titleAccessor = titleAccessor;
        _descriptionAccessor = descriptionAccessor;
        IconBrush = iconBrush;
        // The glow accent is always the icon brush's leading colour — derive it so the two
        // can never drift apart, instead of every call site repeating the colour by hand.
        GlowColor = UIHelpers.LeadingColor(iconBrush);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}
