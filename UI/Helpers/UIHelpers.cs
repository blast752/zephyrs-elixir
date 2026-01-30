
namespace ZephyrsElixir.UI.Helpers;

public static class UIHelpers
{
    public static BitmapImage? LoadImage(string uriString)
    {
        if (string.IsNullOrEmpty(uriString))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(uriString, UriKind.RelativeOrAbsolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 0;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogWarning("UI", $"Failed to load image from {uriString}: {ex.Message}");
            return null;
        }
    }

    public static LinearGradientBrush CreateGradientBrush(string hexColor1, string hexColor2) => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop((Color)ColorConverter.ConvertFromString(hexColor1), 0),
            new GradientStop((Color)ColorConverter.ConvertFromString(hexColor2), 1)
        }
    };

    public static string ToPascalCase(string snakeCase) =>
        string.Concat(snakeCase.Split('_')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}



#region Custom Controls

public class SettingsSection : ContentControl
{
    public static readonly DependencyProperty AnimationDelayProperty =
        DependencyProperty.Register(nameof(AnimationDelay), typeof(int), typeof(SettingsSection), new PropertyMetadata(0));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsSection), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsSection), new PropertyMetadata(string.Empty));

    public int AnimationDelay
    {
        get => (int)GetValue(AnimationDelayProperty);
        set => SetValue(AnimationDelayProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public SettingsSection()
    {
        Loaded += OnSettingsSectionLoaded;
    }

    private void OnSettingsSectionLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnSettingsSectionLoaded;

        if (Template.FindName("Container", this) is not UIElement container)
            return;

        var fadeInAnimation = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            BeginTime = TimeSpan.FromMilliseconds(AnimationDelay)
        };

        var slideInAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            BeginTime = TimeSpan.FromMilliseconds(AnimationDelay),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeInAnimation);
        storyboard.Children.Add(slideInAnimation);

        Storyboard.SetTarget(fadeInAnimation, container);
        Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath("Opacity"));

        Storyboard.SetTarget(slideInAnimation, container);
        Storyboard.SetTargetProperty(slideInAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        storyboard.Begin();
    }
}

public class SettingItem : ContentControl
{
    static SettingItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingItem), new FrameworkPropertyMetadata(typeof(SettingItem)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

}

#endregion

#region Value Converters

public sealed class StringEqualityToBoolConverter : IValueConverter
{
    private static readonly Lazy<StringEqualityToBoolConverter> _instance = new(() => new());
    public static StringEqualityToBoolConverter Instance => _instance.Value;
    
    private StringEqualityToBoolConverter() { }
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var valueString = value?.ToString() ?? string.Empty;
        var parameterString = parameter?.ToString() ?? string.Empty;

        return string.Equals(valueString, parameterString, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked)
            return parameter?.ToString() ?? string.Empty;
        
        return Binding.DoNothing;
    }
}

[MarkupExtensionReturnType(typeof(string))]
public class TranslateExtension : MarkupExtension
{
    private readonly string _key;

    public TranslateExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{_key}]")
        {
            Source = TranslationManager.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

public sealed class IntToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count > 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IntToTimeSpanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int ms ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PercentageToScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double percentage ? percentage / 100.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryLevelToBrushConverter : IValueConverter
{
    private static readonly Brush LowBatteryBrush = new SolidColorBrush(Color.FromRgb(210, 4, 45));
    private static readonly Brush MediumBatteryBrush = new SolidColorBrush(Color.FromRgb(245, 166, 35));
    private static readonly Brush HighBatteryBrush = new SolidColorBrush(Color.FromRgb(126, 211, 33));

    static BatteryLevelToBrushConverter()
    {
        LowBatteryBrush.Freeze();
        MediumBatteryBrush.Freeze();
        HighBatteryBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double level) 
            return Brushes.Transparent;
            
        return level switch
        {
            <= 15 => LowBatteryBrush,
            <= 40 => MediumBatteryBrush,
            _ => HighBatteryBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values?.Length >= 2 && 
            values[0] is double percentage && 
            values[1] is string maxWidthStr && 
            double.TryParse(maxWidthStr, out var maxWidth))
        {
            return Math.Max(0, Math.Min(maxWidth, (percentage / 100.0) * maxWidth));
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var valueString = value?.ToString() ?? string.Empty;
        var parameterString = parameter?.ToString() ?? string.Empty;

        return string.Equals(valueString, parameterString, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CapitalizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            return char.ToUpper(str[0], culture ?? CultureInfo.CurrentCulture) + str.Substring(1);
        }
        return value ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    private static NullToVisibilityConverter? _instance;
    public static NullToVisibilityConverter Instance => _instance ??= new NullToVisibilityConverter();
    
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class WidthToColumnsConverter : IValueConverter
{
    private const double MinColumnWidth = 580;
    private const int MaxColumns = 3;
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0)
            return 1;

        int columns = Math.Max(1, (int)(width / MinColumnWidth));
        
        return Math.Min(columns, MaxColumns);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    private static InverseBoolToVisibilityConverter? _instance;
    public static InverseBoolToVisibilityConverter Instance => _instance ??= new InverseBoolToVisibilityConverter();
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CountToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int count || parameter is not string format)
            return string.Empty;

        var parts = format.Split('|');
        if (parts.Length != 2)
            return count.ToString();

        return count == 1 ? $"{count} {parts[0]}" : $"{count} {parts[1]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

#endregion

#region Attached Behaviors

public static class HyperlinkExtensions
{
    public static readonly DependencyProperty OpenInBrowserProperty =
        DependencyProperty.RegisterAttached("OpenInBrowser", typeof(bool), typeof(HyperlinkExtensions),
            new PropertyMetadata(false, OnOpenInBrowserChanged));

    public static void SetOpenInBrowser(DependencyObject obj, bool value)
        => obj.SetValue(OpenInBrowserProperty, value);

    public static bool GetOpenInBrowser(DependencyObject obj)
        => (bool)obj.GetValue(OpenInBrowserProperty);

    private static void OnOpenInBrowserChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Hyperlink hyperlink)
            return;

        if ((bool)e.NewValue)
            hyperlink.RequestNavigate += OnHyperlinkNavigate;
        else
            hyperlink.RequestNavigate -= OnHyperlinkNavigate;
    }

    private static void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogWarning("Navigation", $"Failed to open link {e.Uri}: {ex.Message}");
        }
        e.Handled = true;
    }
}

public class PageHeader : Control
{
    static PageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}

#endregion

#region License Converters & Behaviors

public sealed class LoadingTextConverter : IValueConverter
{
    private static LoadingTextConverter? _instance;
    public static LoadingTextConverter Instance => _instance ??= new LoadingTextConverter();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool isLoading || parameter is not string texts)
            return "OK";

        var parts = texts.Split('|');
        return parts.Length == 2 ? (isLoading ? parts[1] : parts[0]) : texts;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TierToBoolConverter : IValueConverter
{
    private static TierToBoolConverter? _instance;
    public static TierToBoolConverter Instance => _instance ??= new TierToBoolConverter();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LicenseTier currentTier)
            return false;

        if (parameter is LicenseTier requiredTier)
            return currentTier >= requiredTier;

        if (parameter is string tierStr && Enum.TryParse<LicenseTier>(tierStr, true, out var parsed))
            return currentTier >= parsed;

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TierToVisibilityConverter : IValueConverter
{
    private static TierToVisibilityConverter? _instance;
    public static TierToVisibilityConverter Instance => _instance ??= new TierToVisibilityConverter();

    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LicenseTier currentTier)
            return Visibility.Collapsed;

        var requiredTier = LicenseTier.Pro;

        if (parameter is LicenseTier tier)
            requiredTier = tier;
        else if (parameter is string tierStr && Enum.TryParse<LicenseTier>(tierStr, true, out var parsed))
            requiredTier = parsed;

        var hasAccess = currentTier >= requiredTier;
        if (Invert) hasAccess = !hasAccess;

        return hasAccess ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    private static StringToVisibilityConverter? _instance;
    public static StringToVisibilityConverter Instance => _instance ??= new StringToVisibilityConverter();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

#endregion

#region License Guard (Attached Behaviors)

public static class LicenseGuard
{
    public static readonly DependencyProperty RequiredTierProperty =
        DependencyProperty.RegisterAttached("RequiredTier", typeof(LicenseTier?), typeof(LicenseGuard),
            new PropertyMetadata(null, OnRequiredTierChanged));

    public static void SetRequiredTier(DependencyObject obj, LicenseTier? value) 
        => obj.SetValue(RequiredTierProperty, value);
    
    public static LicenseTier? GetRequiredTier(DependencyObject obj) 
        => (LicenseTier?)obj.GetValue(RequiredTierProperty);

    private static void OnRequiredTierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        void UpdateState()
        {
            var required = GetRequiredTier(element);
            if (!required.HasValue) return;

            var hasLicense = LicenseService.Instance.CurrentState.EffectiveTier >= required.Value;
            var hasDevice = DeviceManager.Instance.IsConnected;

            if (element is UIElement ui)
            {
                ui.IsEnabled = hasLicense && hasDevice;
                ui.Opacity = hasLicense ? 1.0 : 0.5;
            }
        }

        UpdateState();
        element.Loaded += (_, _) => UpdateState();
        LicenseService.Instance.StateChanged += (_, _) => element.Dispatcher.BeginInvoke(UpdateState);
        DeviceManager.Instance.DeviceStatusChanged += (_, _) => element.Dispatcher.BeginInvoke(UpdateState);
    }
}

#endregion

#region Placeholder Behavior

public static class PlaceholderBehavior
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(PlaceholderBehavior),
            new PropertyMetadata(null, OnPlaceholderChanged));

    public static void SetPlaceholder(DependencyObject obj, string value)
        => obj.SetValue(PlaceholderProperty, value);

    public static string GetPlaceholder(DependencyObject obj)
        => (string)obj.GetValue(PlaceholderProperty);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        textBox.GotFocus -= RemovePlaceholder;
        textBox.LostFocus -= ShowPlaceholder;

        if (e.NewValue is string placeholder && !string.IsNullOrEmpty(placeholder))
        {
            textBox.GotFocus += RemovePlaceholder;
            textBox.LostFocus += ShowPlaceholder;

            if (string.IsNullOrEmpty(textBox.Text))
            {
                textBox.Text = placeholder;
                textBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130));
            }
        }
    }

    private static void RemovePlaceholder(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var placeholder = GetPlaceholder(textBox);

        if (textBox.Text == placeholder)
        {
            textBox.Text = string.Empty;
            textBox.Foreground = Brushes.White;
        }
    }

    private static void ShowPlaceholder(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var placeholder = GetPlaceholder(textBox);

        if (string.IsNullOrEmpty(textBox.Text))
        {
            textBox.Text = placeholder;
            textBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130));
        }
    }
}

#endregion

#region Smart Truncate Converter
public sealed class SmartTruncateConverter : IValueConverter
{
    private static SmartTruncateConverter? _instance;
    public static SmartTruncateConverter Instance => _instance ??= new SmartTruncateConverter();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return value ?? string.Empty;

        var maxLength = parameter is string p && int.TryParse(p, out var len) ? len : 50;
        
        if (text.Length <= maxLength)
            return text;

        var separators = new[] { '_', '-', '.', ' ', '/', '\\' };
        var truncateAt = maxLength - 3;
        
        var breakPoint = -1;
        for (var i = truncateAt; i > maxLength / 2; i--)
        {
            if (separators.Contains(text[i]))
            {
                breakPoint = i;
                break;
            }
        }

        if (breakPoint == -1)
            breakPoint = truncateAt;

        return text[..breakPoint] + "…";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

#endregion

#region Dialog Behaviors

public static class WindowDrag
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(WindowDrag),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject obj, bool value) 
        => obj.SetValue(EnabledProperty, value);
    
    public static bool GetEnabled(DependencyObject obj) 
        => (bool)obj.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        
        if ((bool)e.NewValue)
            element.MouseLeftButtonDown += OnMouseDown;
        else
            element.MouseLeftButtonDown -= OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            Window.GetWindow((DependencyObject)sender)?.DragMove();
    }
}

public static class WindowClose
{
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.RegisterAttached("Result", typeof(bool?), typeof(WindowClose),
            new PropertyMetadata(null, OnResultChanged));

    public static void SetResult(DependencyObject obj, bool? value) 
        => obj.SetValue(ResultProperty, value);
    
    public static bool? GetResult(DependencyObject obj) 
        => (bool?)obj.GetValue(ResultProperty);

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        
        button.Click -= OnButtonClick;
        if (e.NewValue is not null)
            button.Click += OnButtonClick;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow((DependencyObject)sender) is { } window)
        {
            window.DialogResult = GetResult((DependencyObject)sender);
            window.Close();
        }
    }
}

#endregion
