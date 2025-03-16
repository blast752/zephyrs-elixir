using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SonicsElixir
{
    public class Program : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            if (Environment.OSVersion.Version.Major >= 6)
                SetProcessDPIAware();

            Program app = new Program();
            app.Run(new MainWindow());
        }
    }

    public partial class MainWindow : Window
    {
        #region Fields

        // Layout elements
        private Grid rootGrid;
        private Grid mainGrid;
        private StackPanel leftPanel;
        private Grid contentGrid;

        // Navigation buttons
        private Button btnNavHome;
        private Button btnNavOptimize;
        private Button btnNavSettings;
        private Button btnNavHelp;

        // Social/Donate icon buttons
        private Button btnPaypal;
        private Button btnGithub;
        private Button btnBuyMeACoffee;
        private Button btnTelegram;

        // Screens
        private Grid homeScreen;
        private Grid optimizeScreen;
        private Grid settingsScreen;
        private Grid helpScreen;

        // Optimize screen elements
        private Button btnRunOptimization;
        private Button btnDeviceInfo;
        private ProgressBar progressBar;
        private Label lblProgressPercentage;
        private Label lblTimer;
        private TextBox terminalBox;

        // Settings screen elements
        private Button btnCheckUpdates = null;
        private Button btnExportLogs;
        private Button btnLicense;
        private Button btnResetDefaults;
        private ComboBox comboLanguage;

        // Device status text (for left panel)
        private TextBlock deviceStatusText;

        // Timers
        private DispatcherTimer optimizationTimer;
        private DispatcherTimer deviceCheckTimer;

        // Optimization tracking
        private DateTime optimizationStartTime;
        private int totalSteps = 102;
        private int currentStep = 0;
        private bool isOptimizationRunning = false;
        private bool shouldStopOptimization = false;
        private Process currentAdbProcess;

        // Device connection state
        private bool isDeviceConnected = false;

        #endregion

        #region Constructor and Initialization

        public MainWindow()
        {
            Title = "Sonic's Elixir";
            Width = 1200;
            Height = 800;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            InitializeStyles();
            BuildRootUI();
            BuildUI();
            BuildScreens();
            InitializeTimers();
            StartDeviceMonitor();
        }

        private void InitializeTimers()
        {
            optimizationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            optimizationTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - optimizationStartTime;
                lblTimer.Content = $"Optimization Duration: {elapsed:hh\\:mm\\:ss}";
            };
        }

        #endregion

        #region Styles

        private void InitializeStyles()
        {
            var buttonBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF007FFF"));
            var buttonHoverBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF008FFF"));
            var buttonPressedBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF005F7F"));
            var buttonBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFC400"));

            var buttonShadowEffect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 320,
                ShadowDepth = 3,
                Opacity = 0.4,
                BlurRadius = 6
            };

            this.Resources["ButtonBackgroundBrush"] = buttonBackground;
            this.Resources["ButtonHoverBackgroundBrush"] = buttonHoverBackground;
            this.Resources["ButtonPressedBackgroundBrush"] = buttonPressedBackground;
            this.Resources["ButtonBorderBrush"] = buttonBorder;
            this.Resources["ButtonShadowEffect"] = buttonShadowEffect;

            var buttonStyle = new Style(typeof(Button));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            borderFactory.SetValue(Border.BorderBrushProperty, buttonBorder);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(3));
            borderFactory.SetValue(Border.BackgroundProperty, buttonBackground);
            borderFactory.SetValue(Border.EffectProperty, buttonShadowEffect);

            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenterFactory);

            var controlTemplate = new ControlTemplate(typeof(Button))
            {
                VisualTree = borderFactory
            };

            var hoverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, buttonHoverBackground, "border"));

            var pressedTrigger = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, buttonPressedBackground, "border"));

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), "border"));
            disabledTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromArgb(120, 100, 100, 100)), "border"));
            disabledTrigger.Setters.Add(new Setter(Border.EffectProperty, null, "border"));

            controlTemplate.Triggers.Add(hoverTrigger);
            controlTemplate.Triggers.Add(pressedTrigger);
            controlTemplate.Triggers.Add(disabledTrigger);

            buttonStyle.Setters.Add(new Setter(Button.TemplateProperty, controlTemplate));
            buttonStyle.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            buttonStyle.Setters.Add(new Setter(Button.FontSizeProperty, 18.0));
            buttonStyle.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            buttonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(20, 10, 20, 10)));
            buttonStyle.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            buttonStyle.Setters.Add(new Setter(Button.MarginProperty, new Thickness(8)));
            buttonStyle.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            buttonStyle.Setters.Add(new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            buttonStyle.Setters.Add(new Setter(Button.MinWidthProperty, 150.0));
            buttonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 45.0));

            this.Resources[typeof(Button)] = buttonStyle;

            var windowControlButtonStyle = new Style(typeof(Button))
            {
                BasedOn = (Style)this.Resources[typeof(Button)]
            };
            windowControlButtonStyle.Setters.Add(new Setter(Button.MinWidthProperty, 40.0));
            windowControlButtonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 30.0));
            this.Resources["WindowControlButtonStyle"] = windowControlButtonStyle;

            var progressBarStyle = new Style(typeof(ProgressBar));
            var pbTemplate = new ControlTemplate(typeof(ProgressBar));

            var outerBorder = new FrameworkElementFactory(typeof(Border));
            outerBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            outerBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 50, 50)));
            outerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outerBorder.SetValue(Border.BorderBrushProperty, buttonBorder);

            var grid = new FrameworkElementFactory(typeof(Grid));

            var track = new FrameworkElementFactory(typeof(Border));
            track.Name = "PART_Track";
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            track.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(80, 80, 80)));
            grid.AppendChild(track);

            var indicator = new FrameworkElementFactory(typeof(Border));
            indicator.Name = "PART_Indicator";
            indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            indicator.SetValue(Border.BackgroundProperty, buttonBackground);
            grid.AppendChild(indicator);

            outerBorder.AppendChild(grid);
            pbTemplate.VisualTree = outerBorder;
            progressBarStyle.Setters.Add(new Setter(Control.TemplateProperty, pbTemplate));

            this.Resources[typeof(ProgressBar)] = progressBarStyle;

            var iconButtonStyle = new Style(typeof(Button), (Style)this.Resources[typeof(Button)]);
            iconButtonStyle.Setters.Add(new Setter(Button.MinWidthProperty, 40.0));
            iconButtonStyle.Setters.Add(new Setter(Button.MinHeightProperty, 40.0));
            iconButtonStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(0)));
            this.Resources["IconButtonStyle"] = iconButtonStyle;

            var comboBoxStyle = new Style(typeof(ComboBox))
            {
                Setters =
                {
                    new Setter(ComboBox.ForegroundProperty, Brushes.White),
                    new Setter(ComboBox.FontSizeProperty, 16.0),
                    new Setter(ComboBox.CursorProperty, Cursors.Hand),
                    new Setter(ComboBox.TemplateProperty, CreateThemedComboBoxTemplate())
                }
            };
            this.Resources["ThemedComboBox"] = comboBoxStyle;
        }

        private ControlTemplate CreateThemedComboBoxTemplate()
        {
            var backgroundBrush = new TemplateBindingExtension(ComboBox.BackgroundProperty);
            ControlTemplate template = new ControlTemplate(typeof(ComboBox));
            template.VisualTree = new FrameworkElementFactory(typeof(Grid), "RootGrid");

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Border";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ComboBox.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(ComboBox.BorderThicknessProperty));
            border.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("ButtonBackgroundBrush"));
            border.SetValue(Border.EffectProperty, new DynamicResourceExtension("ButtonShadowEffect"));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            var gridInside = new FrameworkElementFactory(typeof(Grid));

            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton));
            toggleButton.Name = "ToggleButton";
            toggleButton.SetValue(ToggleButton.IsCheckedProperty,
                new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            toggleButton.SetValue(ToggleButton.PaddingProperty, new Thickness(0));
            toggleButton.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
            toggleButton.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
            toggleButton.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
            toggleButton.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            toggleButton.SetValue(FrameworkElement.WidthProperty, 40.0);

            var arrowPath = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrowPath.Name = "Arrow";
            arrowPath.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0 Z"));
            arrowPath.SetValue(System.Windows.Shapes.Path.FillProperty, Brushes.White);
            arrowPath.SetValue(WidthProperty, 12.0);
            arrowPath.SetValue(HeightProperty, 12.0);
            arrowPath.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            arrowPath.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrowPath.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            toggleButton.AppendChild(arrowPath);
            gridInside.AppendChild(toggleButton);

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.Name = "ContentSite";
            contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(MarginProperty, new Thickness(10, 3, 40, 3));
            contentPresenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            gridInside.AppendChild(contentPresenter);

            var editableTextBox = new FrameworkElementFactory(typeof(TextBox));
            editableTextBox.Name = "PART_EditableTextBox";
            editableTextBox.SetValue(VisibilityProperty, Visibility.Hidden);
            editableTextBox.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            editableTextBox.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            editableTextBox.SetValue(MarginProperty, new Thickness(10, 3, 40, 3));
            editableTextBox.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            gridInside.AppendChild(editableTextBox);

            border.AppendChild(gridInside);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);
            popup.SetValue(Popup.StaysOpenProperty, false);

            var dropDownGrid = new FrameworkElementFactory(typeof(Grid));
            dropDownGrid.Name = "DropDown";
            dropDownGrid.SetValue(MinWidthProperty, new TemplateBindingExtension(FrameworkElement.ActualWidthProperty));
            dropDownGrid.SetValue(MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));
            dropDownGrid.SetValue(SnapsToDevicePixelsProperty, true);

            var dropDownBorder = new FrameworkElementFactory(typeof(Border));
            dropDownBorder.Name = "DropDownBorder";
            dropDownBorder.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("ButtonBackgroundBrush"));
            dropDownBorder.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("ButtonBorderBrush"));
            dropDownBorder.SetValue(Border.BorderThicknessProperty, new Thickness(3));
            dropDownBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));

            dropDownGrid.AppendChild(dropDownBorder);

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(MarginProperty, new Thickness(4));
            scrollViewer.SetValue(ScrollViewer.SnapsToDevicePixelsProperty, true);

            var stackPanel = new FrameworkElementFactory(typeof(StackPanel));
            stackPanel.SetValue(System.Windows.Controls.Panel.IsItemsHostProperty, true);
            stackPanel.SetValue(System.Windows.Input.KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Contained);

            scrollViewer.AppendChild(stackPanel);
            dropDownGrid.AppendChild(scrollViewer);

            popup.AppendChild(dropDownGrid);

            var root = template.VisualTree;
            root.AppendChild(border);
            root.AppendChild(popup);

            template.Triggers.Add(new Trigger
            {
                Property = ComboBox.HasItemsProperty,
                Value = false,
                Setters =
                {
                    new Setter
                    {
                        TargetName = "DropDownBorder",
                        Property = FrameworkElement.MinHeightProperty,
                        Value = 30.0
                    }
                }
            });

            template.Triggers.Add(new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false,
                Setters =
                {
                    new Setter
                    {
                        TargetName = "Border",
                        Property = Border.BackgroundProperty,
                        Value = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0))
                    },
                    new Setter
                    {
                        Property = Control.ForegroundProperty,
                        Value = new SolidColorBrush(Color.FromRgb(136,136,136))
                    }
                }
            });

            template.Triggers.Add(new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter
                    {
                        TargetName = "Border",
                        Property = Border.BackgroundProperty,
                        Value = new DynamicResourceExtension("ButtonHoverBackgroundBrush")
                    }
                }
            });

            template.Triggers.Add(new Trigger
            {
                Property = ComboBox.IsDropDownOpenProperty,
                Value = true,
                Setters =
                {
                    new Setter
                    {
                        TargetName = "Border",
                        Property = Border.BackgroundProperty,
                        Value = new DynamicResourceExtension("ButtonPressedBackgroundBrush")
                    }
                }
            });

            return template;
        }

        #endregion


        #region UI Building

        private void BuildRootUI()
        {
            rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            this.Content = rootGrid;

            Grid titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);
        }

        private Grid BuildTitleBar()
        {
            Grid titleBar = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
            };
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                    ToggleWindowState();
                else
                    DragMove();
            };

            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleText = new TextBlock
            {
                Text = "Sonic's Elixir",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(titleText, 0);
            titleBar.Children.Add(titleText);

            StackPanel windowButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(windowButtons, 2);
            titleBar.Children.Add(windowButtons);

            Button btnMinimize = CreateWindowButton("_", "Minimize");
            btnMinimize.Click += (s, e) => { this.WindowState = WindowState.Minimized; };
            windowButtons.Children.Add(btnMinimize);

            Button btnMaximize = CreateWindowButton("☐", "Maximize/Restore");
            btnMaximize.Click += (s, e) => { ToggleWindowState(); };
            windowButtons.Children.Add(btnMaximize);

            Button btnClose = CreateWindowButton("X", "Close");
            btnClose.Click += (s, e) => { this.Close(); };
            btnClose.MouseEnter += (s, e) => { btnClose.Background = Brushes.Red; };
            btnClose.MouseLeave += (s, e) => { btnClose.Background = Brushes.Transparent; };
            windowButtons.Children.Add(btnClose);

            return titleBar;
        }

        private Button CreateWindowButton(string content, string toolTip)
        {
            Button btn = new Button
            {
                Content = content,
                ToolTip = toolTip,
                Width = 40,
                Height = 30,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                FontSize = 14,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("WindowControlButtonStyle")
            };

            btn.MouseEnter += (s, e) => { btn.Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)); };
            btn.MouseLeave += (s, e) => { btn.Background = Brushes.Transparent; };
            return btn;
        }

        private void ToggleWindowState()
        {
            this.WindowState = (this.WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
        }

        private void BuildUI()
        {
            mainGrid = new Grid
            {
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            Grid.SetRow(mainGrid, 1);
            rootGrid.Children.Add(mainGrid);

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            this.Background = new SolidColorBrush(Color.FromRgb(43, 43, 43));

            leftPanel = new StackPanel
            {
                SnapsToDevicePixels = true,
                Orientation = Orientation.Vertical
            };

            var leftGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            leftGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF007FFF"), 0.0));
            leftGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF005FFF"), 1.0));
            leftPanel.Background = leftGradient;

            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            contentGrid = new Grid
            {
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            Grid.SetColumn(contentGrid, 1);
            mainGrid.Children.Add(contentGrid);

            BuildLeftPanel();
        }

        private void BuildLeftPanel()
        {
            Border logoBorder = new Border
            {
                Height = 140,
                Margin = new Thickness(0, 15, 0, 15)
            };
            Image logoImage = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Images/logo.png", UriKind.RelativeOrAbsolute)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(logoImage, BitmapScalingMode.HighQuality);
            logoBorder.Child = logoImage;
            leftPanel.Children.Add(logoBorder);

            TextBlock appName = new TextBlock
            {
                Text = "Sonic's Elixir",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            leftPanel.Children.Add(appName);

            TextBlock subTitle = new TextBlock
            {
                Text = "Tune your device at supersonic speed",
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 25),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            leftPanel.Children.Add(subTitle);

            btnNavHome = CreateNavButton("Home", "Overview and quick start");
            btnNavHome.Click += (s, e) => ShowScreen(homeScreen);
            leftPanel.Children.Add(btnNavHome);

            btnNavOptimize = CreateNavButton("Optimize", "Advanced ADB commands to speed up your device");
            btnNavOptimize.Click += (s, e) => ShowScreen(optimizeScreen);
            leftPanel.Children.Add(btnNavOptimize);

            btnNavSettings = CreateNavButton("Settings", "Configure language, updates, logs, etc.");
            btnNavSettings.Click += (s, e) => ShowScreen(settingsScreen);
            leftPanel.Children.Add(btnNavSettings);

            btnNavHelp = CreateNavButton("Help & Info", "FAQs, usage guide, and more");
            btnNavHelp.Click += (s, e) =>
            {
                HelpWindow helpWindow = new HelpWindow();
                helpWindow.Owner = this; 
                helpWindow.ShowDialog();
            };
            leftPanel.Children.Add(btnNavHelp);

            leftPanel.Children.Add(new StackPanel { Height = 20 });

            StackPanel donationPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            leftPanel.Children.Add(donationPanel);

            btnPaypal = CreateIconButton("pack://application:,,,/Images/paypal.png", "Donate via PayPal", (s, e) => OpenUrl("https://www.paypal.com/"));
            donationPanel.Children.Add(btnPaypal);

            btnGithub = CreateIconButton("pack://application:,,,/Images/github.png", "Visit GitHub", (s, e) => OpenUrl("https://github.com/blast752/sonic-s-elixir"));
            donationPanel.Children.Add(btnGithub);

            btnBuyMeACoffee = CreateIconButton("pack://application:,,,/Images/coffee.png", "Support via BuyMeACoffee", (s, e) => OpenUrl("https://www.buymeacoffee.com/BodmLNnMs"));
            donationPanel.Children.Add(btnBuyMeACoffee);

            btnTelegram = CreateIconButton("pack://application:,,,/Images/telegram.png", "Join Telegram", (s, e) => OpenUrl("https://t.me/sonicselixir"));
            donationPanel.Children.Add(btnTelegram);

            deviceStatusText = new TextBlock
            {
                Text = "No device connected",
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(10, 40, 0, 10)
            };
            leftPanel.Children.Add(deviceStatusText);
        }

        private Button CreateIconButton(string imageUri, string toolTip, RoutedEventHandler clickHandler)
        {
            Button btn = new Button
            {
                ToolTip = toolTip,
                Width = 40,
                Height = 40,
                Margin = new Thickness(5),
                Style = (Style)FindResource("IconButtonStyle")
            };

            Image img = new Image
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            try
            {
                img.Source = new BitmapImage(new Uri(imageUri, UriKind.RelativeOrAbsolute));
            }
            catch
            {
                
            }
            btn.Content = img;
            btn.Click += clickHandler;
            return btn;
        }

        private Button CreateNavButton(string text, string toolTip)
        {
            Button btn = new Button
            {
                Content = text,
                ToolTip = toolTip
            };
            return btn;
        }

        private void BuildScreens()
        {
            homeScreen = BuildHomeScreen();
            contentGrid.Children.Add(homeScreen);

            optimizeScreen = BuildOptimizeScreen();
            optimizeScreen.Visibility = Visibility.Collapsed;
            contentGrid.Children.Add(optimizeScreen);

            settingsScreen = BuildSettingsScreen();
            settingsScreen.Visibility = Visibility.Collapsed;
            contentGrid.Children.Add(settingsScreen);

            helpScreen = BuildHelpScreen();
            helpScreen.Visibility = Visibility.Collapsed;
            contentGrid.Children.Add(helpScreen);
        }

        private Grid BuildHomeScreen()
        {
            Grid screen = new Grid();
            StackPanel stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(60)
            };
            screen.Children.Add(stack);

            TextBlock title = new TextBlock
            {
                Text = "Sonic's Elixir",
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#FF007FFF"),
                Margin = new Thickness(0, 0, 0, 15),
                TextAlignment = TextAlignment.Center
            };
            stack.Children.Add(title);

            TextBlock subtitle = new TextBlock
            {
                Text = "Optimize your Android device at the speed of sound!",
                FontSize = 20,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 30),
                TextAlignment = TextAlignment.Center
            };
            stack.Children.Add(subtitle);

            TextBlock bulletPoints = new TextBlock
            {
                FontSize = 18,
                Foreground = Brushes.White,
                Text =
                    "• Cache cleaning\n" +
                    "• Package compilation\n" +
                    "• Dex optimization\n" +
                    "• Performance boost",
                Margin = new Thickness(0, 0, 0, 40),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(bulletPoints);

            Button btnStartOptimization = new Button
            {
                Name = "btnStartOptimization",
                Content = "START OPTIMIZATION",
                FontSize = 22,
                Padding = new Thickness(30, 12, 30, 12),
                IsEnabled = false
            };
            btnStartOptimization.Click += (s, e) => ShowScreen(optimizeScreen);
            stack.Children.Add(btnStartOptimization);

            TextBlock whatsNew = new TextBlock
            {
                Text = "\nWhat's new in v0.1.0 beta – Initial Public Version:\n\n" +
                       "• Engaging and intuitive UI/UX\n" +
                       "• Streamlined optimization process with real-time feedback\n" +
                       "• Improved ADB command execution and interruption handling\n" +
                       "• Enhanced log export functionality\n",
                FontSize = 16,
                Margin = new Thickness(0, 30, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            };
            stack.Children.Add(whatsNew);

            return screen;
        }

        private Grid BuildOptimizeScreen()
        {
            Grid screen = new Grid();
            screen.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            WrapPanel topWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20)
            };
            Grid.SetRow(topWrap, 0);
            screen.Children.Add(topWrap);

            btnRunOptimization = new Button
            {
                Content = "Run Optimization",
                IsEnabled = false
            };
            btnRunOptimization.Click += BtnRunOptimization_Click;
            topWrap.Children.Add(btnRunOptimization);

            btnDeviceInfo = new Button
            {
                Content = "Device Information",
                IsEnabled = true
            };
            btnDeviceInfo.Click += BtnDeviceInfo_Click;
            topWrap.Children.Add(btnDeviceInfo);

            StackPanel progressStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(20, 0, 20, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            topWrap.Children.Add(progressStack);

            progressBar = new ProgressBar
            {
                Height = 30,
                Minimum = 0,
                Maximum = totalSteps,
                Value = 0,
                Margin = new Thickness(0, 5, 0, 5),
                Width = 250
            };
            progressStack.Children.Add(progressBar);

            lblProgressPercentage = new Label
            {
                Content = "0%",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            progressStack.Children.Add(lblProgressPercentage);

            lblTimer = new Label
            {
                Content = "Optimization Duration: 00:00:00",
                FontSize = 16,
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            topWrap.Children.Add(lblTimer);

            terminalBox = new TextBox
            {
                Margin = new Thickness(20),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14
            };
            Grid.SetRow(terminalBox, 1);
            screen.Children.Add(terminalBox);

            return screen;
        }

        private Grid BuildSettingsScreen()
        {
            Grid screen = new Grid();
            StackPanel stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40)
            };
            screen.Children.Add(stack);

            TextBlock title = new TextBlock
            {
                Text = "Settings",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 25),
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White
            };
            stack.Children.Add(title);

            StackPanel langPanel = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock lblLang = new TextBlock
            {
                Text = "Language: ",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5),
                Foreground = Brushes.White
            };
            langPanel.Children.Add(lblLang);

            comboLanguage = new ComboBox
            {
                Width = 140,
                Margin = new Thickness(5),
                ItemsSource = new string[] { "English", "Italiano" },
                SelectedIndex = 0,
                FontSize = 16,
                Style = (Style)FindResource("ThemedComboBox"),
                BorderBrush = (Brush)this.Resources["ButtonBorderBrush"],
                BorderThickness = new Thickness(3)
            };
            langPanel.Children.Add(comboLanguage);
            stack.Children.Add(langPanel);

            btnCheckUpdates = new Button
            {
                Content = "Check Updates",
                Width = 220
            };
            btnCheckUpdates.Click += async (s, e) =>
            {
                try
                {
                    var updateInfo = await Updater.GetUpdateInfoAsync();
                    if (Updater.IsNewVersionAvailable(updateInfo))
                    {
                        var result = MessageBox.Show(
                            $"A new version is available ({updateInfo.Version}).\n\nRelease notes:\n{updateInfo.ReleaseNotes}\n\nUpdate now?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            await Updater.DownloadAndUpdateAsync(updateInfo);
                        }
                    }
                    else
                    {
                        MessageBox.Show("The application is already up-to-date.", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error checking for updates: " + ex.Message,
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            stack.Children.Add(btnCheckUpdates);

            btnExportLogs = new Button
            {
                Content = "Export Logs",
                Width = 220
            };
            btnExportLogs.Click += (s, e) =>
            {
                SaveFileDialog dlg = new SaveFileDialog
                {
                    FileName = "log",
                    DefaultExt = ".txt",
                    Filter = "Text documents (.txt)|*.txt"
                };
                bool? result = dlg.ShowDialog();
                if (result == true)
                {
                    string filename = dlg.FileName;
                    File.WriteAllText(filename, terminalBox.Text);
                    MessageBox.Show("Logs exported successfully.", "Export Logs",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            stack.Children.Add(btnExportLogs);

            btnLicense = new Button
            {
                Content = "License",
                Width = 220
            };
            btnLicense.Click += (s, e) =>
            {
                MessageBox.Show("Sonic's Elixir - MIT License\n\nCopyright (c) 2023...",
                    "License Info", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            stack.Children.Add(btnLicense);

            btnResetDefaults = new Button
            {
                Content = "Reset to Defaults",
                Width = 220
            };
            btnResetDefaults.Click += (s, e) =>
            {
                comboLanguage.SelectedIndex = 0;
                MessageBox.Show("Settings restored to default.", "Reset Defaults",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            };
            stack.Children.Add(btnResetDefaults);

            return screen;
        }

        private Grid BuildHelpScreen()
        {
            Grid screen = new Grid();
            StackPanel stack = new StackPanel
            {
                Margin = new Thickness(50)
            };
            screen.Children.Add(stack);

            TextBlock title = new TextBlock
            {
                Text = "Help & Info",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 25),
                Foreground = Brushes.White
            };
            stack.Children.Add(title);

            TextBlock faq = new TextBlock
            {
                Text =
                    "FAQ:\n\n" +
                    "1) How does Sonic's Elixir work?\n" +
                    "   - It executes a series of ADB commands to optimize your device.\n\n" +
                    "2) What commands are executed?\n" +
                    "   - adb shell pm trim-caches 1000G (executed 100 times)\n" +
                    "   - adb shell cmd package compile -m speed -f -a\n" +
                    "   - adb shell \"cmd package bg-dexopt-job\"\n\n" +
                    "3) How can I export the logs?\n" +
                    "   - Use the 'Export Logs' option in Settings.\n\n" +
                    "4) Who can use this program?\n" +
                    "   - It is designed for non-technical users who want to simplify ADB commands.\n",
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            };
            stack.Children.Add(faq);

            TextBlock usageGuide = new TextBlock
            {
                Text = "\nUsage Guide:\n\n" +
                       "- Connect your Android device via USB (ensure that ADB is installed).\n" +
                       "- Navigate to the 'Optimize' screen and click 'Run Optimization'.\n" +
                       "- Monitor the logs in real-time.\n" +
                       "- Use 'Device Information' to view system details.\n",
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            };
            stack.Children.Add(usageGuide);

            TextBlock link = new TextBlock
            {
                Text = "\nFor more details, visit: https://elixirsite.vercel.app/guide.html",
                FontSize = 14,
                Foreground = Brushes.LightBlue,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(link);

            return screen;
        }

        private void ShowScreen(Grid screenToShow)
        {
            homeScreen.Visibility = Visibility.Collapsed;
            optimizeScreen.Visibility = Visibility.Collapsed;
            settingsScreen.Visibility = Visibility.Collapsed;
            helpScreen.Visibility = Visibility.Collapsed;

            screenToShow.Visibility = Visibility.Visible;

            if (screenToShow == homeScreen)
            {
                terminalBox.Clear();
                progressBar.Value = 0;
                currentStep = 0;
                lblProgressPercentage.Content = "0%";
                lblTimer.Content = "Optimization Duration: 00:00:00";

                isOptimizationRunning = false;
                shouldStopOptimization = false;
                btnRunOptimization.Content = "Run Optimization";

                Button btnStartHome = FindButtonInVisualTree(homeScreen, "btnStartOptimization");
                if (btnStartHome != null)
                    btnStartHome.IsEnabled = isDeviceConnected;
            }
        }

        #endregion

        #region Event Handlers and Optimization Logic

        private async void BtnRunOptimization_Click(object sender, RoutedEventArgs e)
        {
            if (!isOptimizationRunning)
            {
                isOptimizationRunning = true;
                shouldStopOptimization = false;
                btnRunOptimization.Content = "Stop Optimization";

                Button btnStartHome = FindButtonInVisualTree(homeScreen, "btnStartOptimization");
                if (btnStartHome != null)
                    btnStartHome.IsEnabled = false;

                AppendTerminal("Starting optimization...\n");

                optimizationStartTime = DateTime.Now;
                optimizationTimer.Start();

                for (int i = 1; i <= 100; i++)
                {
                    if (shouldStopOptimization) break;
                    Dispatcher.Invoke(() => terminalBox.Clear());
                    AppendTerminal($"Clearing cache... ({i}/100)\n");
                    await ExecuteAdbCommandAsync("shell", "pm trim-caches 1000G");
                    currentStep++;
                    UpdateProgressBar();
                }

                if (!shouldStopOptimization)
                {
                    Dispatcher.Invoke(() => terminalBox.Clear());
                    AppendTerminal("Compiling packages for speed optimization...\n");
                    await ExecuteAdbCommandAsync("shell", "cmd package compile -m speed -f -a");
                    currentStep++;
                    UpdateProgressBar();
                }

                if (!shouldStopOptimization)
                {
                    Dispatcher.Invoke(() => terminalBox.Clear());
                    AppendTerminal("Optimizing background dex files...\n");
                    await ExecuteAdbCommandAsync("shell", "cmd package bg-dexopt-job");
                    currentStep++;
                    UpdateProgressBar();
                }

                if (shouldStopOptimization)
                    AppendTerminal("Optimization interrupted by user.\n");
                else
                    AppendTerminal("Optimization completed successfully!\n");

                optimizationTimer.Stop();
                isOptimizationRunning = false;
                btnRunOptimization.Content = "Run Optimization";

                if (isDeviceConnected)
                {
                    btnRunOptimization.IsEnabled = true;
                    if (btnStartHome != null)
                        btnStartHome.IsEnabled = true;
                }
            }
            else
            {
                var result = MessageBox.Show("Are you sure you want to interrupt the optimization?",
                    "Stop Optimization", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    shouldStopOptimization = true;
                    AppendTerminal("Interrupting optimization...\n");
                    if (currentAdbProcess != null && !currentAdbProcess.HasExited)
                    {
                        try
                        {
                            currentAdbProcess.Kill();
                            AppendTerminal("Current operation terminated.\n");
                        }
                        catch (Exception ex)
                        {
                            AppendTerminal("Failed to terminate current operation: " + ex.Message + "\n");
                        }
                    }
                }
            }
        }

        private async void BtnDeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() => terminalBox.Clear());
            AppendTerminal("Retrieving device information...\n");
            await ExecuteAdbCommandAsync("shell", "getprop");
            AppendTerminal("Device information retrieved.\n");
        }

        private void UpdateProgressBar()
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = currentStep;
                double percentage = (progressBar.Value / progressBar.Maximum) * 100;
                lblProgressPercentage.Content = $"{percentage:0}%";
            });
        }

        private void AppendTerminal(string text)
        {
            Dispatcher.Invoke(() =>
            {
                terminalBox.AppendText(text);
                terminalBox.ScrollToEnd();
            });
        }

        #endregion

        #region Helper Methods and Device Monitoring

        private Button FindButtonInVisualTree(DependencyObject parent, string buttonName)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button b && b.Name == buttonName)
                    return b;
                var result = FindButtonInVisualTree(child, buttonName);
                if (result != null)
                    return result;
            }
            return null;
        }

        private async Task<bool> CheckDeviceConnectedAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("adb", "devices")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length <= 1)
                        return false;

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (line.EndsWith("device"))
                            return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        private void StartDeviceMonitor()
        {
            deviceCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            deviceCheckTimer.Tick += async (s, e) =>
            {
                bool wasConnected = isDeviceConnected;
                bool nowConnected = await CheckDeviceConnectedAsync();
                if (wasConnected != nowConnected)
                {
                    isDeviceConnected = nowConnected;
                    UpdateDeviceStatusUI(nowConnected);
                }
            };
            deviceCheckTimer.Start();
        }

        private void UpdateDeviceStatusUI(bool connected)
        {
            if (deviceStatusText != null)
                deviceStatusText.Text = connected ? "Device connected" : "No device connected";

            if (!isOptimizationRunning && btnRunOptimization != null)
                btnRunOptimization.IsEnabled = connected;

            Button btnStartHome = FindButtonInVisualTree(homeScreen, "btnStartOptimization");
            if (btnStartHome != null && !isOptimizationRunning)
                btnStartHome.IsEnabled = connected;
        }

        private async Task ExecuteAdbCommandAsync(string adbArgument, string commandArgument)
        {
            try
            {
                var psi = new ProcessStartInfo("adb", $"{adbArgument} {commandArgument}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    currentAdbProcess = process;
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            AppendTerminal(e.Data + "\n");
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            AppendTerminal("Error: " + e.Data + "\n");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    currentAdbProcess = null;
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("Exception: " + ex.Message + "\n");
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open the link: " + ex.Message);
            }
        }

        #endregion
    }
}
