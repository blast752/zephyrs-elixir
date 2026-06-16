namespace ZephyrsElixir;
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> _screens;
    private IntPtr _handle;
    private HwndSource? _hwnd;
    private bool _maximized;
    private bool _adjusting;

    public MainWindow()
    {
        _screens = new(StringComparer.OrdinalIgnoreCase);

        InitializeComponent();

        Width = AppConfiguration.Window.DefaultWidth;
        Height = AppConfiguration.Window.DefaultHeight;
        MinWidth = AppConfiguration.Window.MinWidth;
        MinHeight = AppConfiguration.Window.MinHeight;

        ConfigureChrome();
        BuildScreens();
        ShowScreen(AppConfiguration.Window.SidebarDefaultKey);

        Loaded += OnLoad;
        Closed += OnClose;
    }

    #region Custom chrome / monitor / sizing

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
        if (_handle == IntPtr.Zero) return;

        _hwnd = HwndSource.FromHwnd(_handle);
        if (_hwnd is null) return;

        _hwnd.AddHook(WndProc);

        var margins = new NativeInterop.MARGINS { top = (int)AppConfiguration.Window.GlassFrameThickness };
        NativeInterop.DwmExtendFrameIntoClientArea(_handle, ref margins);

        ApplyWin11Style();
        ApplyMonitorConstraints(initial: true);
    }

    private void ApplyWin11Style()
    {
        try
        {
            int corner = 2, dark = 1;
            var titleBar = AppConfiguration.Window.TitleBarBg;
            var windowBorder = AppConfiguration.Window.BorderColor;
            int caption = NativeInterop.Rgb(titleBar.R, titleBar.G, titleBar.B);
            int border = NativeInterop.Rgb(windowBorder.R, windowBorder.G, windowBorder.B);
            NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmCornerPreference, ref corner, sizeof(int));
            NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmDarkMode, ref dark, sizeof(int));
            NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmCaptionColor, ref caption, sizeof(int));
            NativeInterop.DwmSetWindowAttribute(_handle, AppConfiguration.Common.WindowStyleDwmBorderColor, ref border, sizeof(int));
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
                    if (WindowState == WindowState.Maximized && TryGetWorkArea(hwnd, out var wa))
                    {
                        nccsp.rgrc0 = wa;
                        Marshal.StructureToPtr(nccsp, lParam, false);
                    }
                    handled = true;
                }
                return IntPtr.Zero;

            case AppConfiguration.Common.WmNcHitTest:
                var hit = HitTest(lParam);
                if (hit == AppConfiguration.Common.HtClient) return IntPtr.Zero;
                handled = true;
                return new IntPtr(hit);

            case AppConfiguration.Common.WmGetMinMaxInfo:
                ApplyMinMaxInfo(hwnd, lParam);
                handled = true;
                return IntPtr.Zero;

            case AppConfiguration.Common.WmDpiChanged:
                var suggested = Marshal.PtrToStructure<NativeInterop.RECT>(lParam);
                NativeInterop.SetWindowPos(_handle, IntPtr.Zero,
                    suggested.left, suggested.top,
                    suggested.right - suggested.left, suggested.bottom - suggested.top,
                    AppConfiguration.Common.SwpReposition);
                Dispatcher.BeginInvoke(() => ApplyMonitorConstraints(), DispatcherPriority.ApplicationIdle);
                handled = true;
                return IntPtr.Zero;

            case AppConfiguration.Common.WmDisplayChange:
                // Display geometry changed (RDP session resize, DPI/resolution switch, monitor hot-plug).
                Dispatcher.BeginInvoke(() => ApplyMonitorConstraints(), DispatcherPriority.ApplicationIdle);
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private void ApplyMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<NativeInterop.MINMAXINFO>(lParam);
        var scale = GetDpiScale(hwnd);

        if (TryGetWorkArea(hwnd, out var work))
        {
            var workW = work.right - work.left;
            var workH = work.bottom - work.top;
            mmi.ptMaxPosition = new NativeInterop.POINT { X = work.left, Y = work.top };
            mmi.ptMaxSize = new NativeInterop.POINT { X = workW, Y = workH };
            mmi.ptMaxTrackSize = new NativeInterop.POINT { X = workW, Y = workH };
            mmi.ptMinTrackSize = new NativeInterop.POINT
            {
                X = ClampMinPhysical(AppConfiguration.Window.MinWidth, AppConfiguration.Window.AbsoluteMinWidth, workW, scale),
                Y = ClampMinPhysical(AppConfiguration.Window.MinHeight, AppConfiguration.Window.AbsoluteMinHeight, workH, scale)
            };
        }
        else
        {
            mmi.ptMinTrackSize = new NativeInterop.POINT
            {
                X = (int)(AppConfiguration.Window.MinWidth * scale),
                Y = (int)(AppConfiguration.Window.MinHeight * scale)
            };
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    /// <summary>
    /// Single source of truth for monitor-driven sizing: keeps Window.Min* and the current bounds
    /// inside the active monitor's work area, accounting for DPI. Runs on init, DPI change,
    /// display change, and when the window returns to Normal state.
    /// </summary>
    private void ApplyMonitorConstraints(bool initial = false)
    {
        if (_adjusting) return;
        if (_handle == IntPtr.Zero) return;
        if (!TryGetWorkArea(_handle, out var work)) return;

        _adjusting = true;
        try
        {
            var scale = GetDpiScale(_handle);
            var workWdip = (work.right - work.left) / scale;
            var workHdip = (work.bottom - work.top) / scale;

            var minW = Clamp(AppConfiguration.Window.MinWidth, AppConfiguration.Window.AbsoluteMinWidth, workWdip);
            var minH = Clamp(AppConfiguration.Window.MinHeight, AppConfiguration.Window.AbsoluteMinHeight, workHdip);
            if (Math.Abs(MinWidth - minW) > 0.5) MinWidth = minW;
            if (Math.Abs(MinHeight - minH) > 0.5) MinHeight = minH;

            if (initial)
            {
                if (Width > workWdip) Width = workWdip;
                if (Height > workHdip) Height = workHdip;
                return;
            }

            if (WindowState != WindowState.Normal) return;
            if (!NativeInterop.GetWindowRect(_handle, out var cur)) return;

            var w = cur.right - cur.left;
            var h = cur.bottom - cur.top;
            var x = cur.left;
            var y = cur.top;
            var workW = work.right - work.left;
            var workH = work.bottom - work.top;

            if (w > workW) w = workW;
            if (h > workH) h = workH;
            if (x + w > work.right) x = work.right - w;
            if (y + h > work.bottom) y = work.bottom - h;
            if (x < work.left) x = work.left;
            if (y < work.top) y = work.top;

            if (x == cur.left && y == cur.top && w == cur.right - cur.left && h == cur.bottom - cur.top) return;

            NativeInterop.SetWindowPos(_handle, IntPtr.Zero, x, y, w, h, AppConfiguration.Common.SwpReposition);
        }
        finally { _adjusting = false; }
    }

    private int HitTest(IntPtr lParam)
    {
        if (WindowState == WindowState.Maximized) return AppConfiguration.Common.HtClient;

        var p = PointFromScreen(GetMousePos(lParam));
        var border = AppConfiguration.Window.ResizeBorderThickness;
        var corner = AppConfiguration.Window.CornerRadius;
        var w = ActualWidth;
        var h = ActualHeight;

        var top = p.Y <= border;
        var bottom = p.Y >= h - border;
        var left = p.X <= border;
        var right = p.X >= w - border;
        var nearLeft = p.X <= corner;
        var nearRight = p.X >= w - corner;

        if (top && nearLeft) return AppConfiguration.Common.HtTopLeft;
        if (top && nearRight) return AppConfiguration.Common.HtTopRight;
        if (bottom && nearLeft) return AppConfiguration.Common.HtBottomLeft;
        if (bottom && nearRight) return AppConfiguration.Common.HtBottomRight;
        if (top) return AppConfiguration.Common.HtTop;
        if (bottom) return AppConfiguration.Common.HtBottom;
        if (left) return AppConfiguration.Common.HtLeft;
        if (right) return AppConfiguration.Common.HtRight;

        if (p.Y <= AppConfiguration.Window.CaptionHeight)
            return p.X >= w - AppConfiguration.Window.CaptionButtonsAreaWidth
                ? AppConfiguration.Common.HtClient
                : AppConfiguration.Common.HtCaption;

        return AppConfiguration.Common.HtClient;
    }

    private static bool TryGetWorkArea(IntPtr hwnd, out NativeInterop.RECT workArea)
    {
        workArea = default;
        if (hwnd == IntPtr.Zero) return false;
        var mon = NativeInterop.MonitorFromWindow(hwnd, AppConfiguration.Common.MonitorDefaultToNearest);
        if (mon == IntPtr.Zero) return false;
        var info = new NativeInterop.MONITORINFO { cbSize = Marshal.SizeOf<NativeInterop.MONITORINFO>() };
        if (!NativeInterop.GetMonitorInfo(mon, ref info)) return false;
        workArea = info.rcWork;
        return true;
    }

    private static double GetDpiScale(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 1.0;
        var dpi = NativeInterop.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static int ClampMinPhysical(double desired, double absoluteFloor, int workPhysical, double scale)
    {
        var floor = (int)Math.Min(absoluteFloor * scale, workPhysical);
        return Math.Max(floor, (int)Math.Min(desired * scale, workPhysical));
    }

    private static double Clamp(double desired, double absoluteFloor, double workDip) =>
        Math.Max(Math.Min(absoluteFloor, workDip), Math.Min(desired, workDip));

    private static Point GetMousePos(IntPtr lParam)
    {
        var raw = lParam.ToInt32();
        return new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
    }

    private void OnTitleBarMouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimizeClick(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object s, RoutedEventArgs e) => ToggleMaximize();
    private void OnCloseClick(object s, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowStateChanged(object? s, EventArgs e)
    {
        _maximized = WindowState == WindowState.Maximized;
        UpdateMaxButton();
        WindowBorder.CornerRadius = new CornerRadius(_maximized ? 0 : AppConfiguration.Window.CornerRadius);
        WindowBorder.BorderThickness = new Thickness(_maximized ? 0 : AppConfiguration.Window.GlassFrameThickness);
        if (!_maximized) Dispatcher.BeginInvoke(() => ApplyMonitorConstraints(), DispatcherPriority.ApplicationIdle);
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

    #endregion

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
