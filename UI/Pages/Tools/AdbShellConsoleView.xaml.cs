namespace ZephyrsElixir.UI.Pages;

/// <summary>
/// Interactive multi-tab ADB shell console (free tool). Each tab owns an independent,
/// long-lived <c>adb shell</c> process with redirected stdin/stdout/stderr.
/// All feature logic — process wrapper, snippet model, history, autocomplete, syntax
/// highlight — is centralized here; the data models are inline nested classes.
/// </summary>
public sealed partial class AdbShellConsoleView : UserControl
{
    private readonly Action _onClose;
    private readonly DispatcherTimer _flushTimer;
    private readonly FlowDocument _emptyDocument = new();
    private readonly List<SnippetItem> _userSnippets = new();
    private int _tabCounter;
    private bool _viewDisposed;

    public ObservableCollection<TabSession> Sessions { get; } = new();
    public ObservableCollection<SnippetItem> Snippets { get; } = new();

    private static readonly string SnippetsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZephyrsElixir", "adb_snippets.json");

    // Contextual autocomplete table (AC-04). Key = root token, value = sub-commands.
    private static readonly Dictionary<string, string[]> Autocomplete = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pm"]       = new[] { "list packages", "list packages -3", "list packages -s", "install", "install -r",
                               "uninstall", "clear", "disable-user", "enable", "grant", "revoke", "path" },
        ["am"]       = new[] { "start", "start -n", "force-stop", "broadcast", "startservice", "kill", "kill-all" },
        ["settings"] = new[] { "get system", "get secure", "get global", "put system", "put secure", "put global",
                               "list system", "list secure", "list global", "delete" },
        ["dumpsys"]  = new[] { "battery", "meminfo", "package", "activity", "cpuinfo", "wifi", "power", "gfxinfo" },
        ["getprop"]  = new[] { "ro.build.version.release", "ro.build.version.sdk", "ro.product.model",
                               "ro.product.brand", "ro.product.device", "ro.serialno" },
    };

    private static readonly SnippetItem[] DefaultSnippets =
    {
        new("List 3rd-party packages", "pm list packages -3"),
        new("List all packages",       "pm list packages"),
        new("Device model",            "getprop ro.product.model"),
        new("Android version",         "getprop ro.build.version.release"),
        new("Battery status",          "dumpsys battery"),
        new("Memory info",             "dumpsys meminfo"),
        new("Top processes",           "top -n 1 -m 10"),
        new("Screen resolution",       "wm size"),
        new("Screen density",          "wm density"),
        new("Disk free",               "df -h"),
        new("Logcat (dump)",           "logcat -d"),
        new("Reboot device",           "reboot"),
    };

    public AdbShellConsoleView(Action onClose)
    {
        _onClose = onClose;
        InitializeComponent();
        DataContext = this;

        LoadSnippets();

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _flushTimer.Tick += OnFlushTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Dispatcher.ShutdownStarted += OnShutdownStarted;

        AddNewTab();
    }

    private TabSession? Active => Tabs.SelectedItem as TabSession;

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _flushTimer.Start();
        InputBox.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Cleanup();

    private void OnShutdownStarted(object? sender, EventArgs e) => Cleanup();

    private void Cleanup()
    {
        if (_viewDisposed) return;
        _viewDisposed = true;

        _flushTimer.Stop();
        Dispatcher.ShutdownStarted -= OnShutdownStarted;

        foreach (var session in Sessions)
            session.Dispose();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _onClose();

    #endregion

    #region Tabs

    private void AddNewTab()
    {
        var session = new TabSession($"Shell {++_tabCounter}");
        session.Start();
        Sessions.Add(session);
        Tabs.SelectedItem = session;
    }

    private void OnAddTabClick(object sender, RoutedEventArgs e) => AddNewTab();

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabSession session })
            CloseTab(session);
    }

    private void CloseTab(TabSession session)
    {
        var index = Sessions.IndexOf(session);
        if (index < 0) return;

        session.Dispose();
        Sessions.Remove(session);

        if (Sessions.Count == 0)
        {
            _tabCounter = 0; // fresh slate → the replacement tab is "Shell 1" again
            AddNewTab();
        }
        else if (Tabs.SelectedItem == null)
        {
            Tabs.SelectedItem = Sessions[Math.Min(index, Sessions.Count - 1)];
        }
    }

    // Double-click a tab label to rename; single click just selects the tab (keeps input focused).
    private void OnTabLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || sender is not TextBlock label || label.Parent is not Grid grid) return;
        if (grid.Children.OfType<TextBox>().FirstOrDefault() is not TextBox editor) return;

        label.Visibility = Visibility.Collapsed;
        editor.Visibility = Visibility.Visible;
        editor.SelectAll();
        editor.Focus();
        e.Handled = true;
    }

    private void OnTabEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || sender is not TextBox editor) return;
        CommitTabRename(editor);
        InputBox.Focus();
        e.Handled = true;
    }

    private void OnTabEditorCommit(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox editor) CommitTabRename(editor);
    }

    private static void CommitTabRename(TextBox editor)
    {
        if (string.IsNullOrWhiteSpace(editor.Text))
            editor.Text = "Shell"; // never allow a blank tab name

        editor.Visibility = Visibility.Collapsed;
        if (editor.Parent is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock label)
            label.Visibility = Visibility.Visible;
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged is a routed event; ignore bubbles from the inner autocomplete ListBox.
        if (e.OriginalSource is not TabControl) return;

        OutputBox.Document = Active?.Document ?? _emptyDocument;
        OutputBox.ScrollToEnd();
        AutoPopup.IsOpen = false;
        UpdateEmptyHint();
        InputBox?.Focus();
    }

    private void UpdateEmptyHint()
    {
        if (EmptyHint == null) return;
        EmptyHint.Visibility = (Active?.HasOutput ?? false) ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region Input / history / submit

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (AutoPopup.IsOpen) ConfirmAutocomplete();
                else SubmitCommand();
                e.Handled = true;
                break;

            case Key.Up:
                if (AutoPopup.IsOpen) MoveAutocomplete(-1);
                else NavigateHistory(-1);
                e.Handled = true;
                break;

            case Key.Down:
                if (AutoPopup.IsOpen) MoveAutocomplete(1);
                else NavigateHistory(1);
                e.Handled = true;
                break;

            case Key.Tab:
                if (AutoPopup.IsOpen) ConfirmAutocomplete();
                else ShowAutocomplete();
                e.Handled = true; // never let Tab move focus away from the console
                break;

            case Key.Escape:
                if (AutoPopup.IsOpen)
                {
                    AutoPopup.IsOpen = false;
                    e.Handled = true;
                }
                break;

            case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                Active?.ClearOutput();
                UpdateEmptyHint();
                e.Handled = true;
                break;
        }
    }

    private void SubmitCommand()
    {
        var command = InputBox.Text;
        var session = Active;
        if (session == null || string.IsNullOrWhiteSpace(command)) return;

        session.AddHistory(command);
        session.Send(command);
        InputBox.Clear();
        AutoPopup.IsOpen = false;
    }

    private void NavigateHistory(int direction)
    {
        var session = Active;
        if (session == null) return;

        var text = session.NavigateHistory(direction);
        InputBox.Text = text;
        InputBox.CaretIndex = text.Length;
    }

    #endregion

    #region Autocomplete

    private void ShowAutocomplete()
    {
        var text = InputBox.Text ?? string.Empty;
        var suggestions = new List<string>();

        var firstBreak = text.IndexOf(' ');
        if (firstBreak < 0)
        {
            // Still typing the root token → suggest matching roots.
            foreach (var key in Autocomplete.Keys)
                if (key.StartsWith(text, StringComparison.OrdinalIgnoreCase) && !key.Equals(text, StringComparison.OrdinalIgnoreCase))
                    suggestions.Add(key + " ");
        }
        else
        {
            var root = text[..firstBreak];
            if (Autocomplete.TryGetValue(root, out var options))
            {
                var partial = text[(firstBreak + 1)..].TrimStart();
                foreach (var option in options)
                    if (option.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                        suggestions.Add($"{root} {option}");
            }
        }

        if (suggestions.Count == 0)
        {
            AutoPopup.IsOpen = false;
            return;
        }

        AutoList.ItemsSource = suggestions;
        AutoList.SelectedIndex = 0;
        AutoPopup.IsOpen = true;
    }

    private void MoveAutocomplete(int direction)
    {
        if (AutoList.Items.Count == 0) return;
        AutoList.SelectedIndex = Math.Clamp(AutoList.SelectedIndex + direction, 0, AutoList.Items.Count - 1);
        AutoList.ScrollIntoView(AutoList.SelectedItem);
    }

    private void ConfirmAutocomplete()
    {
        if (AutoList.SelectedItem is string value)
        {
            InputBox.Text = value;
            InputBox.CaretIndex = value.Length;
        }
        AutoPopup.IsOpen = false;
        InputBox.Focus();
    }

    private void OnAutoListClick(object sender, MouseButtonEventArgs e) => ConfirmAutocomplete();

    #endregion

    #region Snippets

    private void LoadSnippets()
    {
        foreach (var snippet in DefaultSnippets)
            Snippets.Add(snippet);

        try
        {
            if (!File.Exists(SnippetsPath)) return;
            var json = File.ReadAllText(SnippetsPath);
            var saved = JsonSerializer.Deserialize<List<SnippetItem>>(json);
            if (saved == null) return;

            foreach (var item in saved)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Command)) continue;
                var snippet = item with { IsUserDefined = true };
                _userSnippets.Add(snippet);
                Snippets.Add(snippet);
            }
        }
        catch { /* best-effort load, coherent with project convention */ }
    }

    private void SaveSnippets()
    {
        try
        {
            var dir = Path.GetDirectoryName(SnippetsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SnippetsPath, JsonSerializer.Serialize(_userSnippets));
        }
        catch { /* best-effort save */ }
    }

    private void OnSnippetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command }) return;
        InputBox.Text = command;
        InputBox.CaretIndex = command.Length;
        InputBox.Focus();
        AutoPopup.IsOpen = false;
    }

    private void OnAddSnippetClick(object sender, RoutedEventArgs e)
    {
        var name = SnippetNameBox.Text?.Trim() ?? string.Empty;
        var command = SnippetCmdBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(command)) return;

        var snippet = new SnippetItem(name, command) { IsUserDefined = true };
        Snippets.Add(snippet);
        _userSnippets.Add(snippet);
        SaveSnippets();

        SnippetNameBox.Clear();
        SnippetCmdBox.Clear();
    }

    private void OnDeleteSnippetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SnippetItem { IsUserDefined: true } snippet }) return;
        _userSnippets.Remove(snippet);
        Snippets.Remove(snippet);
        SaveSnippets();
        e.Handled = true;
    }

    #endregion

    #region Toolbar (export / clear) + flush

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var session = Active;
        if (session == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export ADB Session",
            FileName = $"adb_session_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            DefaultExt = ".txt",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, session.RawText);
            AdbLogger.Instance.LogSuccess("AdbConsole", "Session exported");
        }
        catch (Exception ex)
        {
            session.EnqueueSystem($"Export failed: {ex.Message}");
            AdbLogger.Instance.LogError("AdbConsole", $"Export failed: {ex.Message}");
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Active?.ClearOutput();
        UpdateEmptyHint();
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        if (_viewDisposed) return;

        var active = Active;
        var activeChanged = false;

        foreach (var session in Sessions)
        {
            var appended = session.Drain();
            if (appended && ReferenceEquals(session, active))
                activeChanged = true;
        }

        if (activeChanged)
        {
            // Follow the tail only while the user is already there; never yank them back down
            // while they are scrolled up reading earlier output.
            if (OutputBox.VerticalOffset + OutputBox.ViewportHeight >= OutputBox.ExtentHeight - 32)
                OutputBox.ScrollToEnd();
            UpdateEmptyHint();
        }
    }

    #endregion


    /// <summary>A named ADB snippet. Only user-defined ones are persisted and deletable.</summary>
    public sealed record SnippetItem(string Name, string Command)
    {
        [JsonIgnore] public bool IsUserDefined { get; init; }
    }

    /// <summary>
    /// One console tab: owns the long-lived <c>adb shell</c> process, its output document,
    /// raw text buffer (for export), command history and a thread-safe pending-line queue.
    /// </summary>
    public sealed class TabSession : ObservableObject, IDisposable
    {
        private enum LineKind { Stdout, Stderr, Success, System }

        private const int MaxInlines = 12000;
        private const int MaxRawChars = 1_500_000;

        private static readonly SolidColorBrush StdoutBrush  = UIHelpers.FrozenSolid(0xE0, 0xE0, 0xE0);
        private static readonly SolidColorBrush StderrBrush  = UIHelpers.FrozenSolid(0xFF, 0x6B, 0x6B);
        private static readonly SolidColorBrush SuccessBrush = UIHelpers.FrozenSolid(0x00, 0xD6, 0x8F);
        private static readonly SolidColorBrush SystemBrush  = UIHelpers.FrozenSolid(0xB0, 0xB8, 0xC8);

        private static readonly string[] SuccessWords = { "success", "complete", "installed", "ok" };

        private readonly Paragraph _paragraph;
        private readonly StringBuilder _raw = new();
        private readonly ConcurrentQueue<(string Text, LineKind Kind)> _pending = new();
        private readonly List<string> _history = new();
        private int _historyIndex;

        private Process? _process;
        private bool _disposed;

        private string _title;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        public FlowDocument Document { get; }
        public string RawText => _raw.ToString();
        public bool HasOutput => _paragraph.Inlines.Count > 0;

        public TabSession(string title)
        {
            _title = title;
            _paragraph = new Paragraph { Margin = new Thickness(0) };
            Document = new FlowDocument(_paragraph)
            {
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                FontSize = 13,
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent
            };
        }

        public void Start()
        {
            try
            {
                // Pin the session to the device that is active right now: with several phones
                // connected a bare "adb shell" refuses to start ("more than one device/emulator").
                var serial = DeviceManager.Instance.ActiveSerial;
                var arguments = string.IsNullOrEmpty(serial) ? "shell" : $"-s {serial} shell";

                var psi = new ProcessStartInfo(AdbExecutor.GetAdbPath(), arguments)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardInputEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += OnOutputData;
                _process.ErrorDataReceived += OnErrorData;
                _process.Exited += OnProcessExited;

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                Enqueue(string.IsNullOrEmpty(serial)
                    ? "adb shell session started."
                    : $"adb shell session started on {serial}.", LineKind.System);
            }
            catch (Exception ex)
            {
                // adb missing from PATH / Tools\adb, or any spawn failure → graceful message, no crash (AC-07).
                Enqueue($"Failed to start adb: {ex.Message}", LineKind.Stderr);
                Enqueue("Ensure 'adb' is in PATH or bundled in Tools/adb.", LineKind.System);
                AdbLogger.Instance.LogError("AdbConsole", $"adb shell start failed: {ex.Message}");
            }
        }

        private void OnOutputData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
                Enqueue(e.Data, ContainsSuccess(e.Data) ? LineKind.Success : LineKind.Stdout);
        }

        private void OnErrorData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
                Enqueue(e.Data, LineKind.Stderr);
        }

        private void OnProcessExited(object? sender, EventArgs e)
            => Enqueue("[process exited]", LineKind.System);

        public void Send(string command)
        {
            if (_disposed) return;

            // Sessions die when the device disconnects or the user types "exit"; instead of a dead
            // tab, transparently respawn the shell so the next command just works.
            if (_process is not { HasExited: false })
            {
                Enqueue("[shell not running — restarting session]", LineKind.System);
                ReleaseProcess();
                Start();
            }

            Enqueue("$ " + command, LineKind.System);
            try
            {
                if (_process is { HasExited: false } process)
                {
                    process.StandardInput.WriteLine(command);
                    process.StandardInput.Flush();
                }
                else
                {
                    Enqueue("[shell not running]", LineKind.Stderr);
                }
            }
            catch (Exception ex)
            {
                Enqueue($"[input error] {ex.Message}", LineKind.Stderr);
            }
        }

        public void AddHistory(string command)
        {
            _history.Add(command);
            _historyIndex = _history.Count;
        }

        public string NavigateHistory(int direction)
        {
            if (_history.Count == 0) return string.Empty;
            _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
            return _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
        }

        public void EnqueueSystem(string text) => Enqueue(text, LineKind.System);

        private void Enqueue(string text, LineKind kind)
        {
            if (_disposed) return;
            _pending.Enqueue((text, kind));
        }

        /// <summary>Drains queued lines into the document. Must run on the UI thread.</summary>
        public bool Drain()
        {
            var appended = false;
            while (_pending.TryDequeue(out var line))
            {
                appended = true;
                AppendColoredLine(line.Text, line.Kind);
            }
            return appended;
        }

        private void AppendColoredLine(string text, LineKind kind)
        {
            var brush = kind switch
            {
                LineKind.Stderr  => StderrBrush,
                LineKind.Success => SuccessBrush,
                LineKind.System  => SystemBrush,
                _                => StdoutBrush
            };

            _paragraph.Inlines.Add(new Run(text) { Foreground = brush });
            _paragraph.Inlines.Add(new LineBreak());
            _raw.AppendLine(text);
            if (_raw.Length > MaxRawChars)
                _raw.Remove(0, _raw.Length - MaxRawChars);

            if (_paragraph.Inlines.Count > MaxInlines)
            {
                var overflow = _paragraph.Inlines.Take(_paragraph.Inlines.Count - MaxInlines).ToList();
                foreach (var inline in overflow)
                    _paragraph.Inlines.Remove(inline);
            }
        }

        public void ClearOutput()
        {
            _paragraph.Inlines.Clear();
            _raw.Clear();
        }

        // Whole-word match so "ok" doesn't light up "token" or "look" in green.
        private static bool ContainsSuccess(string line)
        {
            foreach (var word in SuccessWords)
            {
                var idx = line.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    var end = idx + word.Length;
                    if ((idx == 0 || !char.IsLetterOrDigit(line[idx - 1])) &&
                        (end >= line.Length || !char.IsLetterOrDigit(line[end])))
                        return true;
                    idx = line.IndexOf(word, end, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseProcess();
        }

        private void ReleaseProcess()
        {
            var process = _process;
            _process = null;
            if (process == null) return;

            process.OutputDataReceived -= OnOutputData;
            process.ErrorDataReceived -= OnErrorData;
            process.Exited -= OnProcessExited;

            try { if (!process.HasExited) { process.CancelOutputRead(); process.CancelErrorRead(); } } catch { }
            try { process.StandardInput.Close(); } catch { }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { process.Dispose(); } catch { }
        }
    }
}
