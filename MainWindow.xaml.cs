using ZephyrsElixir.Core;

namespace ZephyrsElixir
{
    public sealed partial class MainWindow : Window
    {
        private readonly Dictionary<string, FrameworkElement> _screens;
        private IntPtr _handle;
        private HwndSource? _hwnd;
        private bool _maximized;

        public MainWindow()
        {
            _screens = new(StringComparer.OrdinalIgnoreCase);

            InitializeComponent();
            ConfigureChrome();
            BuildScreens();
            ShowScreen(AppConfiguration.Window.SidebarDefaultKey);

            Loaded += OnLoad;
            Closed += OnClose;
        }

        private void ConfigureChrome() => WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = AppConfiguration.Window.CaptionHeight,
            CornerRadius = AppConfiguration.Window.WindowCornerRadius,
            GlassFrameThickness = AppConfiguration.Window.WindowGlassFrame,
            NonClientFrameEdges = NonClientFrameEdges.None,
            ResizeBorderThickness = AppConfiguration.Window.WindowResizeBorder,
            UseAeroCaptionButtons = false
        });

        private void OnSourceInitialized(object? s, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            _hwnd = HwndSource.FromHwnd(_handle);
            if (_hwnd != null)
            {
                _hwnd.AddHook(WndProc);
                var m = new NativeInterop.MARGINS { top = (int)AppConfiguration.Window.GlassFrameThickness };
                NativeInterop.DwmExtendFrameIntoClientArea(_handle, ref m);
                ApplyWin11Style();
            }
        }

        private void ApplyWin11Style()
        {
            if (_handle == IntPtr.Zero) return;
            try
            {
                int corner = 2, dark = 1;
                int caption = NativeInterop.Rgb(AppConfiguration.Colors.RgbTitleBarRed, AppConfiguration.Colors.RgbTitleBarGreen, AppConfiguration.Colors.RgbTitleBarBlue);
                int border = NativeInterop.Rgb(AppConfiguration.Colors.RgbWindowBorderRed, AppConfiguration.Colors.RgbWindowBorderGreen, AppConfiguration.Colors.RgbWindowBorderBlue);
                NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmCornerPreference, ref corner, 4);
                NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmDarkMode, ref dark, 4);
                NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmCaptionColor, ref caption, 4);
                NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmBorderColor, ref border, 4);
            }
            catch (Exception ex) { Debug.WriteLine($"Win11 style failed: {ex.Message}"); }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case AppConfiguration.Common.WmNcCalcSize:
                    if (wParam.ToInt32() == 1)
                    {
                        var nccsp = Marshal.PtrToStructure<NativeInterop.NCCALCSIZE_PARAMS>(lParam);
                        if (WindowState == WindowState.Maximized)
                        {
                            var mon = NativeInterop.MonitorFromWindow(hwnd, AppConfiguration.Common.MonitorDefaultToNearest);
                            if (mon != IntPtr.Zero)
                            {
                                var mi = new NativeInterop.MONITORINFO { cbSize = Marshal.SizeOf<NativeInterop.MONITORINFO>() };
                                if (NativeInterop.GetMonitorInfo(mon, ref mi)) nccsp.rgrc0 = mi.rcWork;
                            }
                        }
                        Marshal.StructureToPtr(nccsp, lParam, false);
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case AppConfiguration.Common.WmNcHitTest:
                    var r = HandleHitTest(lParam);
                    if (r != AppConfiguration.Common.HtClient) { handled = true; return new IntPtr(r); }
                    break;

                case AppConfiguration.Common.WmDpiChanged:
                    var rect = Marshal.PtrToStructure<NativeInterop.RECT>(lParam);
                    NativeInterop.SetWindowPos(_handle, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, AppConfiguration.Common.SetWindowPosNoCopyBits);
                    InvalidateVisual();
                    UpdateLayout();
                    handled = true;
                    break;

                case AppConfiguration.Common.WmGetMinMaxInfo:
                    var mmi = Marshal.PtrToStructure<NativeInterop.MINMAXINFO>(lParam);
                    var monitor = NativeInterop.MonitorFromWindow(_handle, AppConfiguration.Common.MonitorDefaultToNearest);
                    if (monitor != IntPtr.Zero)
                    {
                        var info = new NativeInterop.MONITORINFO { cbSize = Marshal.SizeOf<NativeInterop.MONITORINFO>() };
                        if (NativeInterop.GetMonitorInfo(monitor, ref info))
                        {
                            var w = info.rcWork;
                            mmi.ptMaxPosition = new NativeInterop.POINT { X = w.left, Y = w.top };
                            mmi.ptMaxSize = new NativeInterop.POINT { X = w.right - w.left, Y = w.bottom - w.top };
                        }
                    }
                    mmi.ptMinTrackSize = new NativeInterop.POINT { X = (int)AppConfiguration.Window.MinWidth, Y = (int)AppConfiguration.Window.MinHeight };
                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        private int HandleHitTest(IntPtr lParam)
        {
            if (WindowState == WindowState.Maximized) return AppConfiguration.Common.HtClient;
            var p = PointFromScreen(GetMousePos(lParam));
            var r = AppConfiguration.Window.CornerRadius;
            var t = AppConfiguration.Window.CornerRadius;

            if (p.Y <= t) return p.X <= r ? AppConfiguration.Common.HtTopLeft : p.X >= ActualWidth - r ? AppConfiguration.Common.HtTopRight : AppConfiguration.Common.HtTop;
            if (p.Y >= ActualHeight - t) return p.X <= r ? AppConfiguration.Common.HtBottomLeft : p.X >= ActualWidth - r ? AppConfiguration.Common.HtBottomRight : AppConfiguration.Common.HtBottom;
            if (p.X <= t) return AppConfiguration.Common.HtLeft;
            if (p.X >= ActualWidth - t) return AppConfiguration.Common.HtRight;
            if (p.Y <= AppConfiguration.Window.CaptionHeight) return p.X >= ActualWidth - 138 ? AppConfiguration.Common.HtClient : AppConfiguration.Common.HtCaption;
            return AppConfiguration.Common.HtClient;
        }

        private static Point GetMousePos(IntPtr lParam) => new((short)(lParam.ToInt32() & 0xFFFF), (short)((lParam.ToInt32() >> 16) & 0xFFFF));

        private void OnTitleBarMouseDown(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void OnMinimizeClick(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaximizeClick(object s, RoutedEventArgs e) => ToggleMaximize();
        private void OnCloseClick(object s, RoutedEventArgs e) => Close();
        private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnShowHelp(object sender, ExecutedRoutedEventArgs e)
        {
            NavigateTo(AppConfiguration.Window.HelpScreenKey);
            e.Handled = true;
        }

        private void NavigateTo(string key)
        {
            ShowScreen(key);
            if (AppSidebar is not null) AppSidebar.SelectedKey = key;
        }

        private void OnWindowStateChanged(object? s, EventArgs e)
        {
            _maximized = WindowState == WindowState.Maximized;
            UpdateMaxButton();
            WindowBorder.CornerRadius = new CornerRadius(_maximized ? 0 : AppConfiguration.Window.CornerRadius);
            WindowBorder.BorderThickness = new Thickness(_maximized ? 0 : AppConfiguration.Window.GlassFrameThickness);
        }

        private void UpdateMaxButton()
        {
            if (MaximizeIcon == null) return;
            MaximizeIcon.Children.Clear();
            var brush = (Brush)FindResource("ButtonIcon");

            if (_maximized)
            {
                var back = new Rectangle { Width = 8, Height = 8, Stroke = brush, StrokeThickness = 1 };
                Canvas.SetLeft(back, 2);
                Canvas.SetTop(back, 0);
                var front = new Rectangle { Width = 8, Height = 8, Stroke = brush, StrokeThickness = 1, Fill = (Brush)FindResource("TitleBarBackground") };
                Canvas.SetLeft(front, 0);
                Canvas.SetTop(front, 2);
                MaximizeIcon.Children.Add(back);
                MaximizeIcon.Children.Add(front);
                MaximizeRestoreButton.ToolTip = "Restore";
            }
            else
            {
                MaximizeIcon.Children.Add(new Rectangle { Width = 10, Height = 10, Stroke = brush, StrokeThickness = 1 });
                MaximizeRestoreButton.ToolTip = "Maximize";
            }
        }

        private void BuildScreens()
        {
            _screens["Home"] = new Home(NavigateTo);
            _screens["Optimize"] = new Optimize();
            _screens["Debloat"] = new Debloat();
            _screens["Tools"] = new Tools();
            _screens["Advanced"] = new Advanced();
            _screens["Settings"] = new Settings();
            _screens[AppConfiguration.Window.HelpScreenKey] = new HelpView();
            foreach (var s in _screens.Values) s.Visibility = Visibility.Collapsed;
        }

        private void ShowScreen(string key)
        {
            if (!_screens.TryGetValue(key, out var target)) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                foreach (var s in _screens.Values) s.Visibility = s == target ? Visibility.Visible : Visibility.Collapsed;
                if (ContentHost.Content != target) ContentHost.Content = target;
            });
        }

        private void OnSidebarNavigate(object s, RoutedEventArgs e) { if (s is UI.Shell.Sidebar sb) ShowScreen(sb.SelectedKey); }

        private void OnLoad(object s, RoutedEventArgs e)
        {
            DeviceManager.Instance.StartMonitoring();
            UpdateMaxButton();
        }

        private void OnClose(object? s, EventArgs e)
        {
            DeviceManager.Instance.StopMonitoring();
            _hwnd?.RemoveHook(WndProc);
            _hwnd?.Dispose();
        }
    }
}