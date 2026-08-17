namespace ZephyrsElixir.UI.Dialogs;

public sealed partial class UnifiedDialog : Window
{
    #region Cached Icon Brushes (shared across all dialog instances)

    // Frozen at class-init to avoid allocating/freezing a new LinearGradientBrush
    // on every dialog open. Keeps identical visuals, saves GC pressure.
    private static readonly Brush InfoIconBrush     = UIHelpers.CreateGradientBrush("#00BFFF", "#007FFF");
    private static readonly Brush SuccessIconBrush  = AppBrushes.GradientGreen; // identical colors — shared
    private static readonly Brush WarningIconBrush  = UIHelpers.CreateGradientBrush("#FFD700", "#FFA500");
    private static readonly Brush ErrorIconBrush    = UIHelpers.CreateGradientBrush("#FF6B6B", "#FF4757");
    private static readonly Brush QuestionIconBrush = UIHelpers.CreateGradientBrush("#A78BFA", "#7C3AED");
    private static readonly Brush ProIconBrush      = WarningIconBrush; // same gold/orange palette

    private static readonly SolidColorBrush DefaultIconBrush;

    static UnifiedDialog()
    {
        DefaultIconBrush = new SolidColorBrush(Colors.White);
        DefaultIconBrush.Freeze();
    }

    #endregion

    #region Dialog Result

    public DialogAction Result { get; private set; } = DialogAction.Cancel;

    #endregion

    #region Private Constructor (use DialogService)

    private UnifiedDialog() => InitializeComponent();

    #endregion

    #region Factory Methods (Internal)

    internal static UnifiedDialog Create(DialogConfig config)
    {
        var dialog = new UnifiedDialog();
        dialog.Configure(config);
        return dialog;
    }

    #endregion

    #region Configuration

    private void Configure(DialogConfig config)
    {
        HeaderTitle.Text = config.Title;
        HeaderIcon.Kind = GetIconForType(config.Type);
        HeaderIcon.Foreground = GetIconBrush(config.Type);

        ConfigureContent(config);
        ConfigureButtons(config);

        if (config.Owner is not null)
            Owner = config.Owner;
    }

    private void ConfigureContent(DialogConfig config)
    {
        if (config.RichContent is not null)
        {
            RichContentDocument.Blocks.Clear();
            RichContentDocument.Blocks.AddRange(config.RichContent);
            RichContentViewer.Visibility = Visibility.Visible;
            ContentText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ContentText.Text = config.Message;
            ContentText.Visibility = Visibility.Visible;
            RichContentViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureButtons(DialogConfig config)
    {
        ButtonPanel.Children.Clear();

        foreach (var buttonConfig in config.Buttons)
        {
            var button = new Button
            {
                Content = buttonConfig.Text,
                Style = (Style)FindResource(GetButtonStyle(buttonConfig.Style)),
                Tag = buttonConfig.Action,
                Margin = new Thickness(4, 0, 0, 0)
            };

            button.Click += OnButtonClick;
            ButtonPanel.Children.Add(button);
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DialogAction action })
        {
            Result = action;
            DialogResult = action is DialogAction.Primary or DialogAction.Yes;
            Close();
        }
    }

    #endregion

    #region Icon & Style Helpers

    private static string GetIconForType(DialogType type) => type switch
    {
        DialogType.Info or DialogType.RichContent => "info",
        DialogType.Success     => "check",
        DialogType.Warning     => "warning",
        DialogType.Error       => "error-circle",
        DialogType.Question    => "question",
        DialogType.ProRequired => "star",
        _                      => "info"
    };

    private static Brush GetIconBrush(DialogType type) => type switch
    {
        DialogType.Info or DialogType.RichContent => InfoIconBrush,
        DialogType.Success     => SuccessIconBrush,
        DialogType.Warning     => WarningIconBrush,
        DialogType.Error       => ErrorIconBrush,
        DialogType.Question    => QuestionIconBrush,
        DialogType.ProRequired => ProIconBrush,
        _                      => DefaultIconBrush
    };

    private static string GetButtonStyle(ButtonStyle style) => style switch
    {
        ButtonStyle.Primary   => "DialogPrimaryButtonStyle",
        ButtonStyle.Secondary => "DialogSecondaryButtonStyle",
        ButtonStyle.Accent    => "DialogAccentButtonStyle",
        _                     => "DialogSecondaryButtonStyle"
    };

    #endregion
}

#region Enums & Configuration Records

public enum DialogType
{
    Info,
    Success,
    Warning,
    Error,
    Question,
    ProRequired,
    RichContent
}

public enum DialogAction
{
    Cancel,
    Primary,
    Secondary,
    Yes,
    No,
    Upgrade
}

public enum ButtonStyle
{
    Primary,
    Secondary,
    Accent
}

public sealed record DialogButton(string Text, DialogAction Action, ButtonStyle Style = ButtonStyle.Primary);

public sealed record DialogConfig
{
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DialogType Type { get; init; } = DialogType.Info;
    public IReadOnlyList<DialogButton> Buttons { get; init; } = Array.Empty<DialogButton>();
    public Window? Owner { get; init; }

    public IEnumerable<Block>? RichContent { get; init; }
}

#endregion
