namespace ZephyrsElixir.UI.Views;

public sealed class HelpViewModel : INotifyPropertyChanged
{
    private static class Icons
    {
        public const string Home = "home";
        public const string Steps = "checklist";
        public const string Book = "book";
        public const string Lightning = "bolt";
        public const string Document = "document";
        public const string Wrench = "wrench";
        public const string Lightbulb = "lightbulb";
        public const string Question = "question";
        public const string Info = "info";
        public const string Keyboard = "keyboard";
        public const string Shield = "shield";
        public const string Star = "star";
        public const string Speed = "gauge";
        public const string Battery = "battery";
        public const string Backup = "save";
        public const string Console = "console";
        public const string Wireless = "wireless";
        public const string Recipe = "library";
        public const string Vial = "vial";
    }

    private readonly TranslationManager _tm = TranslationManager.Instance;

    public HelpViewModel()
    {
        NavItems = BuildNavItems();
        Faq = BuildFaqItems();
        Tips = BuildTipItems();

        _selectedNav = NavItems[0];

        FilteredFaqView = CollectionViewSource.GetDefaultView(Faq);
        FilteredFaqView.Filter = FilterFaq;

        _tm.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<HelpNavItem> NavItems { get; private set; }
    public ObservableCollection<FaqItem> Faq { get; private set; }
    public ObservableCollection<TipItem> Tips { get; private set; }
    public ICollectionView FilteredFaqView { get; private set; }

    private HelpNavItem _selectedNav = null!;
    public HelpNavItem SelectedNav
    {
        get => _selectedNav;
        set => SetField(ref _selectedNav, value);
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetField(ref _searchQuery, value)) return;
            FilteredFaqView.Refresh();
            OnPropertyChanged(nameof(IsFaqEmpty));
            OnPropertyChanged(nameof(SearchResultCount));
        }
    }

    public bool IsFaqEmpty => FilteredFaqView.IsEmpty;

    public int SearchResultCount => FilteredFaqView switch
    {
        ListCollectionView listView => listView.Count,
        { } view => view.Cast<object>().Count(),
    };

    public void SelectByKey(string key)
    {
        var match = NavItems.FirstOrDefault(n => n.Key == key);
        if (match is not null) SelectedNav = match;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var previousKey = _selectedNav?.Key;

        NavItems = BuildNavItems();
        OnPropertyChanged(nameof(NavItems));

        if (previousKey is not null) SelectByKey(previousKey);

        Faq = BuildFaqItems();
        OnPropertyChanged(nameof(Faq));
        FilteredFaqView = CollectionViewSource.GetDefaultView(Faq);
        FilteredFaqView.Filter = FilterFaq;
        OnPropertyChanged(nameof(FilteredFaqView));
        FilteredFaqView.Refresh();

        Tips = BuildTipItems();
        OnPropertyChanged(nameof(Tips));
    }

    private ObservableCollection<HelpNavItem> BuildNavItems() => new()
    {
        new("welcome",         _tm["Help_Nav_Welcome"],         Icons.Home),
        new("first-steps",     _tm["Help_Nav_FirstSteps"],      Icons.Steps),
        new("basics",          _tm["Help_Nav_Basics"],          Icons.Book),
        new("quick-guide",     _tm["Help_Nav_QuickGuide"],      Icons.Lightning),
        new("complete-guide",  _tm["Help_Nav_CompleteGuide"],   Icons.Document),
        new("troubleshooting", _tm["Help_Nav_Troubleshooting"], Icons.Wrench),
        new("tips",            _tm["Help_Nav_Tips"],            Icons.Lightbulb),
        new("faq",             _tm["Help_Nav_Faq"],             Icons.Question),
        new("about",           _tm["Help_Nav_About"],           Icons.Info)
    };

    private ObservableCollection<FaqItem> BuildFaqItems() => new()
    {
        new(_tm["Help_Faq_Safe_Q"],              _tm["Help_Faq_Safe_A"]),
        new(_tm["Help_Faq_Warranty_Q"],          _tm["Help_Faq_Warranty_A"]),
        new(_tm["Help_Faq_Root_Q"],              _tm["Help_Faq_Root_A"]),
        new(_tm["Help_Faq_AndroidVersions_Q"],   _tm["Help_Faq_AndroidVersions_A"]),
        new(_tm["Help_Faq_SystemReq_Q"],         _tm["Help_Faq_SystemReq_A"]),
        new(_tm["Help_Faq_Wireless_Q"],          _tm["Help_Faq_Wireless_A"]),
        new(_tm["Help_Faq_NotDetected_Q"],       _tm["Help_Faq_NotDetected_A"]),
        new(_tm["Help_Faq_WirelessFail_Q"],      _tm["Help_Faq_WirelessFail_A"]),
        new(_tm["Help_Faq_WhatHappens_Q"],       _tm["Help_Faq_WhatHappens_A"]),
        new(_tm["Help_Faq_HowOften_Q"],          _tm["Help_Faq_HowOften_A"]),
        new(_tm["Help_Faq_Customize_Q"],         _tm["Help_Faq_Customize_A"]),
        new(_tm["Help_Faq_ExtremeVsStandard_Q"], _tm["Help_Faq_ExtremeVsStandard_A"]),
        new(_tm["Help_Faq_Recipes_Q"],           _tm["Help_Faq_Recipes_A"]),
        new(_tm["Help_Faq_Community_Q"],         _tm["Help_Faq_Community_A"]),
        new(_tm["Help_Faq_Vials_Q"],             _tm["Help_Faq_Vials_A"]),
        new(_tm["Help_Faq_MultiDevice_Q"],       _tm["Help_Faq_MultiDevice_A"]),
        new(_tm["Help_Faq_SomethingWrong_Q"],    _tm["Help_Faq_SomethingWrong_A"]),
        new(_tm["Help_Faq_Logs_Q"],              _tm["Help_Faq_Logs_A"]),
        new(_tm["Help_Faq_DataCollection_Q"],    _tm["Help_Faq_DataCollection_A"]),
        new(_tm["Help_Faq_ProFeatures_Q"],       _tm["Help_Faq_ProFeatures_A"]),
        new(_tm["Help_Faq_ActivatePro_Q"],       _tm["Help_Faq_ActivatePro_A"]),
        new(_tm["Help_Faq_ApkErrors_Q"],         _tm["Help_Faq_ApkErrors_A"])
    };

    private ObservableCollection<TipItem> BuildTipItems() => new()
    {
        new(Icons.Keyboard, _tm["Help_Tip_Shortcut_Title"],    _tm["Help_Tip_Shortcut_Body"]),
        new(Icons.Speed,    _tm["Help_Tip_Performance_Title"], _tm["Help_Tip_Performance_Body"]),
        new(Icons.Battery,  _tm["Help_Tip_Battery_Title"],     _tm["Help_Tip_Battery_Body"]),
        new(Icons.Console,  _tm["Help_Tip_Console_Title"],     _tm["Help_Tip_Console_Body"]),
        new(Icons.Backup,   _tm["Help_Tip_Backup_Title"],      _tm["Help_Tip_Backup_Body"]),
        new(Icons.Recipe,   _tm["Help_Tip_Recipes_Title"],     _tm["Help_Tip_Recipes_Body"]),
        new(Icons.Vial,     _tm["Help_Tip_Vials_Title"],       _tm["Help_Tip_Vials_Body"]),
        new(Icons.Wireless, _tm["Help_Tip_Wireless_Title"],    _tm["Help_Tip_Wireless_Body"]),
        new(Icons.Shield,   _tm["Help_Tip_Safety_Title"],      _tm["Help_Tip_Safety_Body"]),
        new(Icons.Star,     _tm["Help_Tip_Pro_Title"],         _tm["Help_Tip_Pro_Body"])
    };

    private bool FilterFaq(object item)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return true;
        if (item is not FaqItem faq) return false;

        return faq.Question.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)
            || faq.Answer.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record HelpNavItem(string Key, string Title, string Icon);

public sealed record QuickStep(string Icon, string Text);

public sealed record FaqItem(string Question, string Answer);

public sealed record TipItem(string Icon, string Title, string Description);
