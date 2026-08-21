namespace ZephyrsElixir.Localization;

public sealed class TranslationManager : INotifyPropertyChanged
{
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly Lazy<TranslationManager> LazyInstance = new(() => new TranslationManager());
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture = null!;

    public static TranslationManager Instance => LazyInstance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private TranslationManager()
    {
        _resourceManager = new ResourceManager("ZephyrsElixir.Localization.Strings", Assembly.GetExecutingAssembly());
        SetCultureCore(ReadPersistedCulture() ?? DefaultCulture, raiseEvents: false);
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Equals(value)) return;
            SetCultureCore(value, raiseEvents: true);
        }
    }

    private void SetCultureCore(CultureInfo culture, bool raiseEvents)
    {
        _currentCulture = culture;
        Strings.Culture = culture;

        if (!raiseEvents) return;

        Persist(culture);
        
        OnPropertyChanged(Binding.IndexerName);
        OnPropertyChanged(nameof(CurrentCulture));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The persisted choice, or null when absent, unreadable or no longer offered.</summary>
    private static CultureInfo? ReadPersistedCulture()
    {
        try
        {
            if (!File.Exists(AppConfiguration.Paths.LanguageMarker)) return null;
            var name = File.ReadAllText(AppConfiguration.Paths.LanguageMarker).Trim();
            return AvailableCultures.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static void Persist(CultureInfo culture)
    {
        try
        {
            Directory.CreateDirectory(AppConfiguration.Paths.LocalAppDataRoot);
            File.WriteAllText(AppConfiguration.Paths.LanguageMarker, culture.Name);
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Forgets the persisted choice and returns to the default culture.</summary>
    public void ResetToDefault()
    {
        CurrentCulture = DefaultCulture;
        try { File.Delete(AppConfiguration.Paths.LanguageMarker); } catch { /* non-fatal */ }
    }

    public string this[string key] => _resourceManager.GetString(key, _currentCulture) ?? $"[{key}]";

    /// <summary>
    /// Raw lookup: the translated text, or <c>null</c> when the key is absent. The indexer answers
    /// with a visible <c>[key]</c> marker instead, so callers that enumerate an open-ended run of
    /// numbered keys need this to know where the run ends.
    /// </summary>
    public string? Find(string key) => _resourceManager.GetString(key, _currentCulture);

    public static IEnumerable<CultureInfo> AvailableCultures { get; } = new List<CultureInfo>
    {
        new("en-US"),
        new("it-IT")
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}