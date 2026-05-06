namespace ZephyrsElixir.Localization;

public sealed class TranslationManager : INotifyPropertyChanged
{
    private static readonly Lazy<TranslationManager> LazyInstance = new(() => new TranslationManager());
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture = null!;

    public static TranslationManager Instance => LazyInstance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private TranslationManager()
    {
        _resourceManager = new ResourceManager("ZephyrsElixir.Localization.Strings", Assembly.GetExecutingAssembly());
        SetCultureCore(CultureInfo.GetCultureInfo("en-US"), raiseEvents: false);
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
        
        OnPropertyChanged(Binding.IndexerName);
        OnPropertyChanged(nameof(CurrentCulture));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => _resourceManager.GetString(key, _currentCulture) ?? $"[{key}]";

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