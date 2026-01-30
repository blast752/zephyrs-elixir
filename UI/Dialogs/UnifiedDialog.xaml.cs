namespace ZephyrsElixir.UI.Dialogs;

public sealed partial class UnifiedDialog : Window
{
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
        HeaderIcon.Text = GetIconForType(config.Type);
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
        DialogType.Info or DialogType.RichContent => "\uE946",
        DialogType.Success => "\uE73E",
        DialogType.Warning => "\uE7BA",
        DialogType.Error => "\uEA39",
        DialogType.Question => "\uE897",
        DialogType.ProRequired => "\uE735",
        _ => "\uE946"
    };

    private static Brush GetIconBrush(DialogType type) => type switch
    {
        DialogType.Info or DialogType.RichContent => CreateGradient("#00BFFF", "#007FFF"),
        DialogType.Success => CreateGradient("#00D68F", "#00B377"),
        DialogType.Warning => CreateGradient("#FFD700", "#FFA500"),
        DialogType.Error => CreateGradient("#FF6B6B", "#FF4757"),
        DialogType.Question => CreateGradient("#A78BFA", "#7C3AED"),
        DialogType.ProRequired => CreateGradient("#FFD700", "#FFA500"),
        _ => new SolidColorBrush(Colors.White)
    };

    private static LinearGradientBrush CreateGradient(string color1, string color2) => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop((Color)ColorConverter.ConvertFromString(color1)!, 0),
            new GradientStop((Color)ColorConverter.ConvertFromString(color2)!, 1)
        }
    };

    private static string GetButtonStyle(ButtonStyle style) => style switch
    {
        ButtonStyle.Primary => "DialogPrimaryButtonStyle",
        ButtonStyle.Secondary => "DialogSecondaryButtonStyle",
        ButtonStyle.Accent => "DialogAccentButtonStyle",
        _ => "DialogSecondaryButtonStyle"
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