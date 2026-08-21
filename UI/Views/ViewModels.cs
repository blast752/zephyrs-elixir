
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

/// <summary>
/// One rendered row of the Debloat grid. WPF has no virtualizing wrap panel, so the page groups the
/// filtered apps into rows itself and lets a <c>VirtualizingStackPanel</c> virtualize by row: the
/// grid keeps its columns while only the visible rows are ever realized.
/// </summary>
/// <param name="Items">The apps in this row, at most <paramref name="Columns"/> of them.</param>
/// <param name="Columns">Column count the row's <c>UniformGrid</c> lays out against, so a partial
/// last row keeps the same cell width as a full one.</param>
public sealed record AppRowViewModel(IReadOnlyList<AppInfoViewModel> Items, int Columns);

public sealed class AppInfoViewModel : LocalizedObservableObject, IEquatable<AppInfoViewModel>
{
    public event Action<bool>? IsSelectedChanged;

    private bool _isSelected;
    private AppState _state;
    private BitmapImage? _icon;
    private SafetyRiskLevel _riskLevel = SafetyRiskLevel.Unknown;
    private double _safetyScore;
    private string _aiDescription = string.Empty;
    private bool _hasIntelligence;
    private string? _warningMessage;

    protected override void OnLanguageChanged()
    {
        // The placeholder has to follow the language, the verdict must not: comparing the text would
        // fail here, because the resource has already switched by the time this runs.
        if (!_hasIntelligence)
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
        get => _hasIntelligence ? _aiDescription : Strings.Debloat_Risk_Analyzing;
        set
        {
            _aiDescription = value;
            _hasIntelligence = true;
            OnPropertyChanged();
        }
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

    public bool HasBackup => UninstallHistoryManager.BackupExists(LocalApkPath);
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
    private bool _supportsStandbyBucket = true;
    private bool _syncingPermissions;

    public AppInfoViewModel App => _app;
    public ObservableCollection<PermissionItem> Permissions { get; } = new();

    public Dictionary<StandbyBucket, string> BucketOptions
    {
        get
        {
            var options = new Dictionary<StandbyBucket, string>
            {
                { StandbyBucket.Active, Strings.Debloat_Bucket_Active },
                { StandbyBucket.WorkingSet, Strings.Debloat_Bucket_WorkingSet },
                { StandbyBucket.Frequent, Strings.Debloat_Bucket_Frequent },
                { StandbyBucket.Rare, Strings.Debloat_Bucket_Rare },
                { StandbyBucket.Restricted, Strings.Debloat_Bucket_Restricted }
            };

            // NEVER is what Android assigns to an app the user has never opened — the state most of
            // the preinstalled bloat is actually in. It is reported, not chosen: it belongs in the
            // list only while it is the current value, or the box would render blank on those apps.
            if (_selectedBucket == StandbyBucket.Never)
                options.Add(StandbyBucket.Never, Strings.Debloat_Bucket_Never);

            return options;
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool SupportsStandbyBucket
    {
        get => _supportsStandbyBucket;
        private set => SetProperty(ref _supportsStandbyBucket, value);
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
            var sdk = await DeviceApi.GetSdkAsync();
            SupportsStandbyBucket = DeviceApi.SupportsStandbyBucket(sdk);

            Permissions.Clear();
            foreach (var p in await PermissionManager.GetAppPermissionsAsync(_app.PackageName))
            {
                p.PropertyChanged += OnPermissionChanged;
                Permissions.Add(p);
            }

            if (SupportsStandbyBucket)
            {
                _selectedBucket = await PermissionManager.GetAppStandbyBucketAsync(_app.PackageName);
                OnPropertyChanged(nameof(BucketOptions));
                OnPropertyChanged(nameof(SelectedBucket));
            }
            NotifyPermissionCounts();
        }
        finally { IsLoading = false; }
    }

    private async void OnPermissionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingPermissions || e.PropertyName != nameof(PermissionItem.IsGranted) || sender is not PermissionItem p) return;
        await PermissionManager.SetPermissionAsync(_app.PackageName, p.PermissionKey, p.IsGranted);
        NotifyPermissionCounts();
    }

    public async Task<int> RevokeAllPermissionsAsync()
    {
        var granted = Permissions.Where(p => p.IsGranted).ToList();
        int count = 0;
        _syncingPermissions = true;
        try
        {
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
        }
        finally { _syncingPermissions = false; }
        NotifyPermissionCounts();
        return count;
    }

    private void NotifyPermissionCounts()
    {
        OnPropertyChanged(nameof(GrantedPermissionsCount));
        OnPropertyChanged(nameof(HasGrantedPermissions));
    }

    public async Task<string> LaunchAsync()
    {
        var result = await AdbExecutor.ExecuteCommandAsync(
            $"shell monkey -p {_app.PackageName} -c android.intent.category.LAUNCHER 1");
        return result.Contains("aborted", StringComparison.OrdinalIgnoreCase) ||
               result.Contains("No activities", StringComparison.OrdinalIgnoreCase)
            ? string.Format(Strings.Debloat_Status_NoLauncher, _app.Name)
            : string.Format(Strings.Debloat_Status_Launched, _app.Name);
    }

    public async Task<string> ForceStopAsync()
    {
        await AdbExecutor.ExecuteCommandAsync($"shell am force-stop {_app.PackageName}");
        return string.Format(Strings.Debloat_Status_Stopped, _app.Name);
    }

    public Task OpenAppInfoAsync() => AdbExecutor.ExecuteCommandAsync(
        $"shell am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:{_app.PackageName}");

    public async Task<bool> ClearDataAsync()
    {
        var result = await AdbExecutor.ExecuteCommandAsync($"shell pm clear {_app.PackageName}");
        return result.Contains("success", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> ExtractApkAsync(string targetDir)
    {
        var paths = await DeviceApi.GetPackagePathsAsync(_app.PackageName);
        if (paths.Count == 0) return null;

        var destRoot = paths.Count == 1
            ? targetDir
            : Directory.CreateDirectory(Path.Combine(targetDir, _app.PackageName)).FullName;

        int pulled = 0;
        foreach (var remote in paths)
        {
            var name = paths.Count == 1 ? $"{_app.PackageName}.apk" : Path.GetFileName(remote);
            var local = Path.Combine(destRoot, name);
            await AdbExecutor.ExecuteTransferAsync($"pull \"{remote}\" \"{local}\"");
            if (File.Exists(local)) pulled++;
        }
        return pulled == paths.Count ? destRoot : null;
    }
}

public sealed class DnsProviderViewModel : LocalizedObservableObject
{
    private int _pingMs = -1;
    private bool _isPinging;

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(PingDisplay));

    public string Name { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
    public bool IsCustom { get; init; }

    public int PingMs
    {
        get => _pingMs;
        set { if (SetProperty(ref _pingMs, value)) { OnPropertyChanged(nameof(PingDisplay)); OnPropertyChanged(nameof(PingQualityIcon)); } }
    }

    public bool IsPinging
    {
        get => _isPinging;
        set { if (SetProperty(ref _isPinging, value)) { OnPropertyChanged(nameof(PingDisplay)); OnPropertyChanged(nameof(PingQualityIcon)); } }
    }

    public string PingDisplay => IsPinging ? "..." : PingMs switch
    {
        < 0 => "",
        0 => Strings.Advanced_DNS_Ping_Timeout,
        _ => $"{PingMs}ms"
    };

    /// <summary>Icon registry key grading the latency alongside <see cref="PingDisplay"/>; empty when there is nothing to grade.</summary>
    public string PingQualityIcon => IsPinging || PingMs <= 0 ? string.Empty : PingMs switch
    {
        < 50 => "bolt",
        < 100 => "check",
        < 200 => string.Empty,
        _ => "warning"
    };
}

public sealed class VialItemViewModel
{
    public SettingsSnapshot Vial { get; }

    public VialItemViewModel(SettingsSnapshot vial) => Vial = vial;

    public string TriggerDisplay => Vial.Trigger switch
    {
        SettingsTimeMachine.TriggerOptimize => Strings.Advanced_Vials_Trigger_Optimize,
        SettingsTimeMachine.TriggerRecipe => string.Format(Strings.Advanced_Vials_Trigger_Recipe, Vial.Label ?? string.Empty),
        SettingsTimeMachine.TriggerAdvanced => Strings.Advanced_Vials_Trigger_Advanced,
        _ => Strings.Advanced_Vials_Trigger_Manual
    };

    public string MetaDisplay =>
        $"{Vial.TakenUtc.ToLocalTime():g}  ·  {string.Format(Strings.Advanced_Vials_Count, Vial.SettingCount)}";
}

public sealed class VialChangeRow : ObservableObject
{
    private static readonly SolidColorBrush GlobalBg = UIHelpers.FrozenSolid(0x00, 0xBF, 0xFF, 0x2A);
    private static readonly SolidColorBrush GlobalFg = UIHelpers.FrozenSolid(0x7F, 0xD4, 0xFF);
    private static readonly SolidColorBrush SecureBg = UIHelpers.FrozenSolid(0xFF, 0xD0, 0x00, 0x2A);
    private static readonly SolidColorBrush SecureFg = UIHelpers.FrozenSolid(0xFF, 0xD8, 0x70);
    private static readonly SolidColorBrush SystemBg = UIHelpers.FrozenSolid(0x00, 0xD6, 0x8F, 0x2A);
    private static readonly SolidColorBrush SystemFg = UIHelpers.FrozenSolid(0x7F, 0xEB, 0xC4);

    private readonly Action _selectionChanged;
    private bool _isSelected;

    public SettingChange Change { get; }

    public VialChangeRow(SettingChange change, Action selectionChanged)
    {
        Change = change;
        _selectionChanged = selectionChanged;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) _selectionChanged(); }
    }

    public bool CanRestore => !Change.IsProtected;
    public bool IsProtected => Change.IsProtected;
    public string Namespace => Change.Namespace;
    public string Key => Change.Key;
    public string CurrentDisplay => Display(Change.CurrentValue);
    public string VialDisplay => Display(Change.VialValue);

    public Brush NamespaceBackground => Change.Namespace switch
    {
        "secure" => SecureBg,
        "system" => SystemBg,
        _ => GlobalBg
    };

    public Brush NamespaceForeground => Change.Namespace switch
    {
        "secure" => SecureFg,
        "system" => SystemFg,
        _ => GlobalFg
    };

    private static string Display(string? value) =>
        value is null ? Strings.Advanced_Vials_Missing : value.Length == 0 ? "\"\"" : value;
}

public static class RecipeAccents
{
    private static readonly Dictionary<string, Brush> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gold"] = AppBrushes.GradientAmber,
        ["red"] = AppBrushes.GradientRed,
        ["cyan"] = AppBrushes.GradientCyan,
        ["purple"] = AppBrushes.GradientPurple,
        ["green"] = AppBrushes.GradientGreen,
        ["blue"] = AppBrushes.GradientBlue
    };

    public static Brush BrushFor(string accent) =>
        Map.TryGetValue(accent, out var brush) ? brush : AppBrushes.GradientBlue;
}

public static class RecipeChips
{
    public static IReadOnlyList<string> For(Recipe recipe)
    {
        var chips = new List<string>();

        if (recipe.HasOptimization)
            chips.Add(recipe.Optimization!.Extreme
                ? $"{Strings.Recipes_Chip_Optimization} · {Strings.Optimize_ExtremeMode_Label}"
                : Strings.Recipes_Chip_Optimization);

        if (recipe.HasDebloat)
            chips.Add(string.Format(
                recipe.Debloat!.Mode == DebloatMode.Uninstall ? Strings.Recipes_Chip_DebloatUninstall : Strings.Recipes_Chip_DebloatDisable,
                recipe.Debloat.Packages.Count));

        if (recipe.Tweaks is { } tweaks)
        {
            if (!string.IsNullOrEmpty(tweaks.DnsHostname)) chips.Add($"DNS · {tweaks.DnsName ?? tweaks.DnsHostname}");
            if (tweaks.AnimationScale is { } scale) chips.Add($"{Strings.Recipes_Chip_Animations} {scale.ToString("0.##", CultureInfo.InvariantCulture)}x");
            if (tweaks.ProPrivacy.Count > 0) chips.Add(string.Format(Strings.Recipes_Chip_Privacy, tweaks.ProPrivacy.Count));
        }

        if (recipe.HasInstall)
            chips.Add(string.Format(Strings.Recipes_Chip_Install, recipe.Install!.Apks.Count));

        return chips;
    }

    public static IReadOnlyList<string> For(CommunityRecipe recipe)
    {
        var chips = new List<string>();
        if (recipe.HasOptimization) chips.Add(Strings.Recipes_Chip_Optimization);
        if (recipe.Packages > 0) chips.Add(string.Format(Strings.Recipes_Chip_DebloatUninstall, recipe.Packages));
        if (recipe.HasTweaks) chips.Add(Strings.Recipes_Chip_Tweaks);
        if (recipe.HasInstall) chips.Add(Strings.Recipes_Chip_InstallGeneric);
        return chips;
    }
}

public sealed class RecipeCardViewModel : LocalizedObservableObject
{
    public Recipe Model { get; }

    public RecipeCardViewModel(Recipe model) => Model = model;

    private static readonly SolidColorBrush ShareIdleBrush = UIHelpers.FrozenSolid(0xC8, 0xD8, 0xE8);

    public string Name => Model.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? Strings.Recipes_NoDescription : Model.Description;
    public string Glyph => RecipeStyle.Normalize(Model.Glyph);
    public Brush IconBrush => RecipeAccents.BrushFor(Model.Accent);
    public bool RequiresPro => Model.RequiresPro;
    public bool IsShared => Model.CommunityId is not null;
    public IReadOnlyList<string> Chips => RecipeChips.For(Model);
    public string AppliedDisplay => string.Format(Strings.Recipes_AppliedCount, Model.TimesApplied);
    public string UpdatedDisplay => Model.UpdatedUtc.ToLocalTime().ToString("d");
    public string AuthorDisplay => string.IsNullOrWhiteSpace(Model.Author) ? string.Empty : string.Format(Strings.Recipes_ByAuthor, Model.Author);

    // The share icon doubles as the publish state: gold when the recipe is live in the community
    // (click to remove), neutral when it can still be shared.
    public Brush ShareBrush => IsShared ? AppBrushes.Caution : ShareIdleBrush;
    public string ShareTooltip => IsShared ? Strings.Recipes_Tooltip_Unpublish : Strings.Recipes_Tooltip_Share;

    public void NotifyShareChanged()
    {
        OnPropertyChanged(nameof(IsShared));
        OnPropertyChanged(nameof(ShareBrush));
        OnPropertyChanged(nameof(ShareTooltip));
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(AppliedDisplay));
        OnPropertyChanged(nameof(AuthorDisplay));
        OnPropertyChanged(nameof(ShareTooltip));
    }
}

public sealed class CommunityRecipeViewModel : LocalizedObservableObject
{
    private int _likes;
    private bool _hasLiked;
    private bool _isBusy;

    public CommunityRecipe Model { get; }
    public int Rank { get; }

    public CommunityRecipeViewModel(CommunityRecipe model, int rank)
    {
        Model = model;
        Rank = rank;
        _likes = model.Likes;
        _hasLiked = RecipeStore.HasLiked(model.Id);
    }

    public string Name => Model.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? Strings.Recipes_NoDescription : Model.Description;
    public string Glyph => RecipeStyle.Normalize(Model.Glyph);
    public Brush IconBrush => RecipeAccents.BrushFor(Model.Accent);
    public bool RequiresPro => Model.RequiresPro;
    public string AuthorDisplay => string.Format(Strings.Recipes_ByAuthor, string.IsNullOrWhiteSpace(Model.Author) ? Strings.Recipes_AnonymousAuthor : Model.Author);
    public IReadOnlyList<string> Chips => RecipeChips.For(Model);
    public string DownloadsDisplay => Model.Downloads.ToString("N0", CultureInfo.CurrentCulture);
    public string AppliedDisplay => Model.Applied.ToString("N0", CultureInfo.CurrentCulture);
    public bool ShowRank => Rank is >= 1 and <= 3;
    public string RankDisplay => $"#{Rank}";

    public int Likes
    {
        get => _likes;
        set { if (SetProperty(ref _likes, value)) OnPropertyChanged(nameof(LikesDisplay)); }
    }

    public string LikesDisplay => Likes.ToString("N0", CultureInfo.CurrentCulture);

    public bool HasLiked
    {
        get => _hasLiked;
        set => SetProperty(ref _hasLiked, value);
    }

    public void BumpDownloads()
    {
        Model.Downloads++;
        OnPropertyChanged(nameof(DownloadsDisplay));
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AuthorDisplay));
        OnPropertyChanged(nameof(Chips));
    }
}

public sealed class RecipeTargetViewModel : ObservableObject
{
    private bool _isSelected;

    public required AndroidDevice Device { get; init; }

    public string Name => Device.Name;
    public string Serial => Device.Serial;
    public string BatteryDisplay => Device.IsAuthorized ? $"{Device.BatteryLevel}%" : Strings.Devices_Unauthorized;
    public bool IsEnabled => Device.IsAuthorized;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

// One row of the "pick which APK" chooser shown when a free user runs a recipe that bundles more
// than one APK: every APK stays in the recipe but a single one is installed per run.
public sealed class RunApkChoiceViewModel : ObservableObject
{
    private bool _isSelected;

    public required RecipeApk Apk { get; init; }

    public string Label => string.IsNullOrWhiteSpace(Apk.Label) ? Apk.FileName : Apk.Label!;
    public bool IsResolved => Apk.IsResolved;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class RecipeDeviceProgressViewModel : ObservableObject
{
    private string _message = string.Empty;
    private double _percent;
    private bool _isDone;
    private bool _isError;

    public required string Serial { get; init; }
    public required string Name { get; init; }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, value);
    }

    public bool IsDone
    {
        get => _isDone;
        set { if (SetProperty(ref _isDone, value)) OnPropertyChanged(nameof(StatusGlyph)); }
    }

    public bool IsError
    {
        get => _isError;
        set { if (SetProperty(ref _isError, value)) { OnPropertyChanged(nameof(StatusGlyph)); OnPropertyChanged(nameof(StatusBrush)); } }
    }

    public string StatusGlyph => !IsDone ? "sync" : IsError ? "error-circle" : "check";
    public Brush StatusBrush => IsError ? AppBrushes.Failed : AppBrushes.Success;
}

/// <summary>
/// The "Alchemist's Journey" ladder: applying recipes ("brews") earns rank. Thresholds and
/// titles live here so the badge, the progress bar and the copy can never disagree.
/// </summary>
public static class RecipeRank
{
    // Cumulative brews required to REACH each level (index 0 = level 1).
    private static readonly int[] Thresholds = { 0, 5, 15, 35, 75 };

    public static int MaxLevel => Thresholds.Length;

    public static int LevelFor(int brews)
    {
        var level = 1;
        for (int i = 1; i < Thresholds.Length; i++)
            if (brews >= Thresholds[i]) level = i + 1;
        return level;
    }

    public static int Floor(int level) => Thresholds[Math.Clamp(level, 1, MaxLevel) - 1];
    public static int? Ceiling(int level) => level >= MaxLevel ? null : Thresholds[level];

    public static string Title(int level) => level switch
    {
        1 => Strings.Recipes_Rank_1,
        2 => Strings.Recipes_Rank_2,
        3 => Strings.Recipes_Rank_3,
        4 => Strings.Recipes_Rank_4,
        _ => Strings.Recipes_Rank_5
    };
}

public static class RecipeTips
{
    public static IReadOnlyList<string> All => new[]
    {
        Strings.Recipes_Tip_1, Strings.Recipes_Tip_2, Strings.Recipes_Tip_3,
        Strings.Recipes_Tip_4, Strings.Recipes_Tip_5
    };
}

public sealed class GamificationViewModel : LocalizedObservableObject
{
    private int _recipes, _brews, _shared;
    private string _tip = string.Empty;

    public void Update(int recipes, int brews, int shared)
    {
        _recipes = recipes;
        _brews = brews;
        _shared = shared;
        OnPropertyChanged(string.Empty);
    }

    public int Level => RecipeRank.LevelFor(_brews);
    public string Title => RecipeRank.Title(Level);
    public string LevelLabel => string.Format(Strings.Recipes_Gamify_Level, Level);
    public bool IsMaxLevel => Level >= RecipeRank.MaxLevel;

    public double ProgressPercent
    {
        get
        {
            var floor = RecipeRank.Floor(Level);
            var ceiling = RecipeRank.Ceiling(Level);
            return ceiling is null ? 100 : Math.Clamp((_brews - floor) * 100.0 / (ceiling.Value - floor), 0, 100);
        }
    }

    public string XpLabel => RecipeRank.Ceiling(Level) is { } ceiling
        ? string.Format(Strings.Recipes_Gamify_Xp, _brews, ceiling)
        : string.Format(Strings.Recipes_Gamify_XpMax, _brews);

    public string NextRankLabel => RecipeRank.Ceiling(Level) is not null
        ? string.Format(Strings.Recipes_Gamify_Next, RecipeRank.Title(Level + 1))
        : Strings.Recipes_Gamify_Legend;

    public string RecipesValue => _recipes.ToString("N0", CultureInfo.CurrentCulture);
    public string BrewsValue => _brews.ToString("N0", CultureInfo.CurrentCulture);
    public string SharedValue => _shared.ToString("N0", CultureInfo.CurrentCulture);

    public string Tip
    {
        get => _tip;
        set => SetProperty(ref _tip, value);
    }

    protected override void OnLanguageChanged() => OnPropertyChanged(string.Empty);
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
