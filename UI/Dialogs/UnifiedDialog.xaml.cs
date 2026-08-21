namespace ZephyrsElixir.UI.Dialogs;

public sealed partial class UnifiedDialog : Window
{
    private static readonly Brush InfoIconBrush     = AppBrushes.GradientSky;
    private static readonly Brush SuccessIconBrush  = AppBrushes.GradientGreen;
    private static readonly Brush WarningIconBrush  = AppBrushes.GradientGold;
    private static readonly Brush ErrorIconBrush    = AppBrushes.GradientCoral;
    private static readonly Brush QuestionIconBrush = AppBrushes.GradientViolet;
    private static readonly Brush ProIconBrush      = WarningIconBrush; // same gold/orange palette

    private static readonly SolidColorBrush DefaultIconBrush;

    static UnifiedDialog()
    {
        DefaultIconBrush = new SolidColorBrush(Colors.White);
        DefaultIconBrush.Freeze();
    }

    public DialogAction Result { get; private set; } = DialogAction.Cancel;

    private UnifiedDialog() => InitializeComponent();

    internal static UnifiedDialog Create(DialogConfig config)
    {
        var dialog = new UnifiedDialog();
        dialog.Configure(config);
        return dialog;
    }

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

}

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

