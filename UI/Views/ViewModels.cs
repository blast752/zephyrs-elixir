
namespace ZephyrsElixir.UI.ViewModels
{
    public abstract class ObservableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void RaiseMultiple(params string[] names)
        {
            foreach (var name in names) OnPropertyChanged(name);
        }
    }

    public sealed class AppInfoViewModel : ObservableBase, IEquatable<AppInfoViewModel>
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

        public AppInfoViewModel()
        {
            TranslationManager.Instance.LanguageChanged += (_, _) => 
            {
                if (_aiDescription == Strings.Debloat_Risk_Analyzing)
                    OnPropertyChanged(nameof(AiDescription));
                RaiseMultiple(nameof(RiskDisplay));
            };
        }

        public string Name { get; init; } = string.Empty;
        public string PackageName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;

        public AppState State
        {
            get => _state;
            set => SetField(ref _state, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (SetField(ref _isSelected, value)) IsSelectedChanged?.Invoke(_isSelected); }
        }

        public BitmapImage? Icon
        {
            get => _icon;
            set => SetField(ref _icon, value);
        }

        public bool IsLoadingIcon
        {
            get => _isLoadingIcon;
            set => SetField(ref _isLoadingIcon, value);
        }

        public SafetyRiskLevel RiskLevel
        {
            get => _riskLevel;
            set { if (SetField(ref _riskLevel, value)) RaiseMultiple(nameof(RiskDisplay), nameof(RiskColor), nameof(RiskBadgeBackground), nameof(CanSafelyRemove)); }
        }

        public double SafetyScore
        {
            get => _safetyScore;
            set { if (SetField(ref _safetyScore, value)) OnPropertyChanged(nameof(SafetyScoreDisplay)); }
        }

        public string AiDescription
        {
            get => _aiDescription;
            set => SetField(ref _aiDescription, value);
        }

        public string? WarningMessage
        {
            get => _warningMessage;
            set { if (SetField(ref _warningMessage, value)) RaiseMultiple(nameof(RiskDisplay), nameof(HasWarning)); }
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

        private static readonly SolidColorBrush SafeBrush = Frozen(50, 205, 50);
        private static readonly SolidColorBrush CautionBrush = Frozen(255, 190, 0);
        private static readonly SolidColorBrush CriticalBrush = Frozen(220, 20, 60);
        private static readonly SolidColorBrush UnknownBrush = Frozen(128, 128, 128, 80);
        private static readonly SolidColorBrush SafeBackgroundBrush = Frozen(50, 205, 50, 30);
        private static readonly SolidColorBrush CautionBackgroundBrush = Frozen(255, 190, 0, 30);
        private static readonly SolidColorBrush CriticalBackgroundBrush = Frozen(220, 20, 60, 30);
        private static readonly SolidColorBrush UnknownBackgroundBrush = Frozen(128, 128, 128, 20);

        private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

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

    public sealed class HistoryAppViewModel : ObservableBase
    {
        private bool _isSelected;
        private BitmapImage? _icon;

        public HistoryAppViewModel()
        {
            TranslationManager.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusDisplay));
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public BitmapImage? Icon
        {
            get => _icon;
            set => SetField(ref _icon, value);
        }

        public string Name { get; init; } = string.Empty;
        public string PackageName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public DateTime UninstallDate { get; init; }
        public string? LocalApkPath { get; init; }
        public bool IsSystemApp { get; init; }

        public bool HasBackup => !string.IsNullOrEmpty(LocalApkPath) && System.IO.File.Exists(LocalApkPath);
        public string DateDisplay => UninstallDate.ToString("g");
        public string StatusDisplay => HasBackup 
            ? Strings.Debloat_History_Status_Backup 
            : Strings.Debloat_History_Status_Uninstalled;
    }

    public sealed class AppDetailsViewModel : ObservableBase
    {
        private readonly AppInfoViewModel _app;
        private StandbyBucket _selectedBucket;
        private bool _isLoading;

        public AppInfoViewModel App => _app;
        public ObservableCollection<PermissionItem> Permissions { get; } = new();

        public Dictionary<StandbyBucket, string> BucketOptions => new()
        {
            { StandbyBucket.Active, $"{Strings.Debloat_Bucket_Active} ({Strings.Advanced_Animation_Slow})" },
            { StandbyBucket.WorkingSet, Strings.Debloat_Bucket_WorkingSet },
            { StandbyBucket.Frequent, Strings.Debloat_Bucket_Frequent },
            { StandbyBucket.Rare, Strings.Debloat_Bucket_Rare },
            { StandbyBucket.Restricted, Strings.Debloat_Bucket_Restricted }
        };

        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value);
        }

        public StandbyBucket SelectedBucket
        {
            get => _selectedBucket;
            set { if (SetField(ref _selectedBucket, value)) _ = PermissionManager.SetAppStandbyBucketAsync(_app.PackageName, value); }
        }

        public int GrantedPermissionsCount => Permissions.Count(p => p.IsGranted);
        public bool HasGrantedPermissions => Permissions.Any(p => p.IsGranted);

        public AppDetailsViewModel(AppInfoViewModel app) 
        {
            _app = app;
            TranslationManager.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(BucketOptions));
        }

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
                            RaiseMultiple(nameof(GrantedPermissionsCount), nameof(HasGrantedPermissions));
                        }
                    };
                    Permissions.Add(p);
                }
                _selectedBucket = await PermissionManager.GetAppStandbyBucketAsync(_app.PackageName);
                OnPropertyChanged(nameof(SelectedBucket));
                RaiseMultiple(nameof(GrantedPermissionsCount), nameof(HasGrantedPermissions));
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
            RaiseMultiple(nameof(GrantedPermissionsCount), nameof(HasGrantedPermissions));
            return count;
        }
    }

    public sealed class DnsProviderViewModel : ObservableBase
    {
        private int _pingMs = -1;
        private bool _isPinging;

        public DnsProviderViewModel()
        {
            TranslationManager.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(PingDisplay));
        }

        public string Name { get; init; } = string.Empty;
        public string Hostname { get; init; } = string.Empty;

        public int PingMs
        {
            get => _pingMs;
            set { if (SetField(ref _pingMs, value)) OnPropertyChanged(nameof(PingDisplay)); }
        }

        public bool IsPinging
        {
            get => _isPinging;
            set { if (SetField(ref _isPinging, value)) OnPropertyChanged(nameof(PingDisplay)); }
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
}