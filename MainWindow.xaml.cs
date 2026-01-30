
namespace ZephyrsElixir
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly Dictionary<string, FrameworkElement> _screens;
        private string _deviceStatus;
        private double _batteryPct;
        private IntPtr _handle;
        private HwndSource? _hwnd;
        private bool _maximized;

        public string DeviceStatusText { get => _deviceStatus; set => SetField(ref _deviceStatus, value); }
        public double DeviceBatteryPercentage { get => _batteryPct; set => SetField(ref _batteryPct, value); }

        public MainWindow()
        {
            _deviceStatus = Strings.DeviceStatus_NoDevice;
            _screens = new(StringComparer.OrdinalIgnoreCase);

            InitializeComponent();
            ConfigureChrome();
            BuildScreens();
            ShowScreen("Home");

            Loaded += OnLoad;
            Closed += OnClose;
        }

        private void ConfigureChrome() => WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            CornerRadius = new CornerRadius(8),
            GlassFrameThickness = new Thickness(0),
            NonClientFrameEdges = NonClientFrameEdges.None,
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });

        private void OnSourceInitialized(object? s, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            _hwnd = HwndSource.FromHwnd(_handle);
            if (_hwnd != null) { _hwnd.AddHook(WndProc); ApplyWin11Style(); }
        }

        private void ApplyWin11Style()
        {
            if (_handle == IntPtr.Zero) return;
            try
            {
                int corner = 2, dark = 1, caption = Rgb(12, 21, 40), border = Rgb(26, 34, 56);
                DwmSetWindowAttribute(_handle, 33, ref corner, 4);
                DwmSetWindowAttribute(_handle, 20, ref dark, 4);
                DwmSetWindowAttribute(_handle, 35, ref caption, 4);
                DwmSetWindowAttribute(_handle, 34, ref border, 4);
            }
            catch (Exception ex) { Debug.WriteLine($"Win11 style failed: {ex.Message}"); }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0083:
                    if (wParam.ToInt32() == 1)
                    {
                        var nccsp = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                        if (WindowState == WindowState.Maximized)
                        {
                            var mon = MonitorFromWindow(hwnd, 2);
                            if (mon != IntPtr.Zero)
                            {
                                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                                if (GetMonitorInfo(mon, ref mi)) nccsp.rgrc0 = mi.rcWork;
                            }
                        }
                        Marshal.StructureToPtr(nccsp, lParam, false);
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case 0x0084:
                    var r = HandleHitTest(lParam);
                    if (r != 1) { handled = true; return new IntPtr(r); }
                    break;

                case 0x02E0:
                    var rect = Marshal.PtrToStructure<RECT>(lParam);
                    SetWindowPos(_handle, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0014);
                    InvalidateVisual();
                    UpdateLayout();
                    handled = true;
                    break;

                case 0x0024:
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    var monitor = MonitorFromWindow(_handle, 2);
                    if (monitor != IntPtr.Zero)
                    {
                        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(monitor, ref info))
                        {
                            var w = info.rcWork;
                            mmi.ptMaxPosition = new POINT { X = w.left, Y = w.top };
                            mmi.ptMaxSize = new POINT { X = w.right - w.left, Y = w.bottom - w.top };
                        }
                    }
                    mmi.ptMinTrackSize = new POINT { X = 1400, Y = 900 };
                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        private int HandleHitTest(IntPtr lParam)
        {
            if (WindowState == WindowState.Maximized) return 1;
            var p = PointFromScreen(GetMousePos(lParam));
            const double r = 8, t = 8;

            if (p.Y <= t) return p.X <= r ? 13 : p.X >= ActualWidth - r ? 14 : 12;
            if (p.Y >= ActualHeight - t) return p.X <= r ? 16 : p.X >= ActualWidth - r ? 17 : 15;
            if (p.X <= t) return 10;
            if (p.X >= ActualWidth - t) return 11;
            if (p.Y <= 40) return p.X >= ActualWidth - 138 ? 1 : 2;
            return 1;
        }

        private static Point GetMousePos(IntPtr lParam) => new((short)(lParam.ToInt32() & 0xFFFF), (short)((lParam.ToInt32() >> 16) & 0xFFFF));

        private void OnTitleBarMouseDown(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void OnMinimizeClick(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaximizeClick(object s, RoutedEventArgs e) => ToggleMaximize();
        private void OnCloseClick(object s, RoutedEventArgs e) => Close();
        private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnWindowStateChanged(object? s, EventArgs e)
        {
            _maximized = WindowState == WindowState.Maximized;
            UpdateMaxButton();
            WindowBorder.CornerRadius = new CornerRadius(_maximized ? 0 : 8);
            WindowBorder.BorderThickness = new Thickness(_maximized ? 0 : 1);
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
            _screens["Home"] = new Home(ShowScreen);
            _screens["Optimize"] = new Optimize();
            _screens["Debloat"] = new Debloat();
            _screens["Tools"] = new Tools();
            _screens["Advanced"] = new Advanced();
            _screens["Settings"] = new Settings();
            _screens["Help"] = new HelpView();
            foreach (var s in _screens.Values) s.Visibility = Visibility.Collapsed;
        }

        private void ShowScreen(string key)
        {
            if (!_screens.TryGetValue(key, out var target)) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                foreach (var s in _screens.Values) s.Visibility = s == target ? Visibility.Visible : Visibility.Collapsed;
                if (ContentHost.Content != target) ContentHost.Content = target;
            });
        }

        private void OnSidebarNavigate(object s, RoutedEventArgs e) { if (s is UI.Shell.Sidebar sb) ShowScreen(sb.SelectedKey); }

        private void OnLoad(object s, RoutedEventArgs e)
        {
            DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatus;
            DeviceManager.Instance.DeviceInfoUpdated += OnDeviceInfo;
            DeviceManager.Instance.StartMonitoring();
            UpdateMaxButton();
        }

        private void OnClose(object? s, EventArgs e)
        {
            DeviceManager.Instance.StopMonitoring();
            _hwnd?.RemoveHook(WndProc);
            _hwnd?.Dispose();
        }

        private void OnDeviceStatus(object? s, bool on) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, () => { DeviceStatusText = DeviceManager.Instance.StatusText; if (!on) DeviceBatteryPercentage = 0; });
        private void OnDeviceInfo(object? s, (string Name, int Bat) e) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, () => { DeviceStatusText = e.Name; DeviceBatteryPercentage = e.Bat; });

        public static async Task<string> ExecuteAdbCommandWithOutputAsync(string args)
        {
            try
            {
                using var p = new Process { StartInfo = new ProcessStartInfo { FileName = "adb", Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
                p.Start();
                var output = await p.StandardOutput.ReadToEndAsync();
                var error = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            }
            catch (Exception ex) { return $"Failed: {ex.Message}"; }
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private static int Rgb(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }
    }
}