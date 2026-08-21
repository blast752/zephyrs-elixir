namespace ZephyrsElixir.Core;

public sealed class AdbLogger
{
    private static readonly Lazy<AdbLogger> _instance = new(() => new AdbLogger());
    public static AdbLogger Instance => _instance.Value;

    private AdbLogger() { }

    private const int MaxEntries = 150;
    private const int MaxDetailLength = 300;
    private const int DedupeWindowSeconds = 5;

    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();
    private LogEntry? _lastEntry;
    private int _duplicateCount;

    public event EventHandler<LogEntry>? LogEntryAdded;

    public enum LogLevel { Info, Success, Warning, Error, Command }

    public sealed record LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        string? Detail = null,
        int RepeatCount = 1)
    {
        public override string ToString()
        {
            var time = Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var repeat = RepeatCount > 1 ? $" (×{RepeatCount})" : "";
            var detail = string.IsNullOrEmpty(Detail) ? "" : $"\n    → {Detail}";
            return $"[{time}] [{Level}] [{Category}] {Message}{repeat}{detail}";
        }
    }

    public void LogInfo(string category, string message, string? details = null)
        => AddEntry(LogLevel.Info, category, message, details);

    public void LogSuccess(string category, string message, string? details = null)
        => AddEntry(LogLevel.Success, category, message, details);

    public void LogWarning(string category, string message, string? details = null)
        => AddEntry(LogLevel.Warning, category, message, details);

    public void LogError(string category, string message, string? details = null)
        => AddEntry(LogLevel.Error, category, message, details);

    public void LogAdbCommand(string command, string output, bool isError = false)
    {
        AddEntry(LogLevel.Command, "ADB", $"adb {SanitizeCommand(command)}");

        // Successful command output is high-volume and low-signal — record it only on failure,
        // where the captured stderr/stdout is what actually helps diagnose the problem.
        if (!isError || string.IsNullOrWhiteSpace(output)) return;

        var cleanOutput = SanitizeOutput(output.Trim());
        if (!string.IsNullOrEmpty(cleanOutput))
            AddEntry(LogLevel.Error, "ADB", "Command failed", cleanOutput);
    }

    public void LogException(string category, Exception ex)
    {
        var message = $"{ex.GetType().Name}: {SanitizeMessage(ex.Message)}";
        var detail = ex.InnerException != null
            ? $"Inner: {ex.InnerException.GetType().Name}"
            : null;

        AddEntry(LogLevel.Error, category, message, detail);
    }

    private void AddEntry(LogLevel level, string category, string message, string? detail = null)
    {
        var sanitizedDetail = detail != null ? Truncate(SanitizeMessage(detail), MaxDetailLength) : null;
        var now = DateTime.Now;

        LogEntry? newEntry;

        lock (_lock)
        {
            // De-duplicate consecutive identical entries within the dedupe window.
            if (_lastEntry is { } last &&
                last.Level == level &&
                last.Category == category &&
                last.Message == message &&
                (now - last.Timestamp).TotalSeconds < DedupeWindowSeconds)
            {
                _duplicateCount++;
                return;
            }

            // Flush any accumulated repeat count onto the previous (same-reference) entry
            // BEFORE appending the new one. Records use value-equality, so identity checks
            // must use ReferenceEquals to avoid false positives against another equal record.
            if (_lastEntry is not null && _duplicateCount > 1 &&
                _entries.Count > 0 && ReferenceEquals(_entries[^1], _lastEntry))
            {
                var merged = _lastEntry with { RepeatCount = _duplicateCount };
                _entries[^1] = merged;
            }

            newEntry = new LogEntry(now, level, category, message, sanitizedDetail);
            _entries.Add(newEntry);
            _lastEntry = newEntry;
            _duplicateCount = 1;

            if (_entries.Count > MaxEntries)
                EvictOldestLowPriority();
        }

        LogEntryAdded?.Invoke(this, newEntry);
    }

    /// <summary>
    /// Capacity eviction that protects diagnostics: drops the oldest low-signal entry
    /// (Info / Success / Command) so a burst of routine logging can never push Warnings or
    /// Errors out of the buffer. Falls back to the oldest entry only when nothing but
    /// Warnings/Errors remain. Never evicts the just-added tail (keeps the dedupe invariant).
    /// </summary>
    private void EvictOldestLowPriority()
    {
        for (var i = 0; i < _entries.Count - 1; i++)
        {
            if (_entries[i].Level is LogLevel.Info or LogLevel.Success or LogLevel.Command)
            {
                _entries.RemoveAt(i);
                return;
            }
        }
        _entries.RemoveAt(0);
    }

    public bool HasLogs
    {
        get { lock (_lock) return _entries.Count > 0; }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public IReadOnlyList<LogEntry> GetAllEntries()
    {
        lock (_lock) return _entries.ToArray();
    }

    public string GetFormattedLog()
    {
        LogEntry[] snapshot;
        lock (_lock)
        {
            // Flush pending duplicate-count into the tail entry, again guarded by identity.
            if (_lastEntry is not null && _duplicateCount > 1 &&
                _entries.Count > 0 && ReferenceEquals(_entries[^1], _lastEntry))
            {
                _entries[^1] = _lastEntry = _lastEntry with { RepeatCount = _duplicateCount };
            }
            snapshot = _entries.ToArray();
        }

        var sb = new StringBuilder(4096);

        sb.AppendLine($"Zephyr's Elixir — Diagnostic Log — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        if (snapshot.Length == 0)
        {
            sb.AppendLine("No operations logged. Perform some actions and try again.");
            return sb.ToString();
        }

        var stats = new Dictionary<LogLevel, int>();
        foreach (var e in snapshot)
            stats[e.Level] = stats.GetValueOrDefault(e.Level) + e.RepeatCount;

        sb.AppendLine($"Summary: {snapshot.Length} entries");
        if (stats.TryGetValue(LogLevel.Error, out var errors) && errors > 0)
            sb.AppendLine($"  ⚠ {errors} error(s)");
        if (stats.TryGetValue(LogLevel.Warning, out var warnings) && warnings > 0)
            sb.AppendLine($"  ⚡ {warnings} warning(s)");
        sb.AppendLine();

        DateTime? currentBlock = null;
        foreach (var entry in snapshot)
        {
            var block = new DateTime(
                entry.Timestamp.Year, entry.Timestamp.Month, entry.Timestamp.Day,
                entry.Timestamp.Hour, entry.Timestamp.Minute / 5 * 5, 0);

            if (currentBlock != block)
            {
                if (currentBlock.HasValue) sb.AppendLine();
                sb.AppendLine($"── {block:HH:mm} ──");
                currentBlock = block;
            }

            sb.AppendLine(entry.ToString());
        }

        return sb.ToString();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _lastEntry = null;
            _duplicateCount = 0;
        }
    }

    private static readonly Regex WinPathRegex = new(@"[A-Za-z]:\\[^\s""]+\\([^\s""\\]+)", RegexOptions.Compiled);
    private static readonly Regex UnixPathRegex = new(@"/(?:home|Users|mnt|storage)/[^\s""]+/([^\s""/]+)", RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"(\d{1,3})\.\d{1,3}\.\d{1,3}\.\d{1,3}", RegexOptions.Compiled);
    private static readonly Regex SerialRegex = new(@"\b[A-Z0-9]{8,}\b", RegexOptions.Compiled);
    private static readonly Regex SerialTargetRegex = new(@"^\s*-s\s+\S+", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"\b[\w.-]+@[\w.-]+\.\w+\b", RegexOptions.Compiled);
    private static readonly Regex WinPathOutputRegex = new(@"[A-Za-z]:\\[^\s\r\n]+", RegexOptions.Compiled);
    private static readonly Regex DataPathRegex = new(@"/(?:data|storage|sdcard)/[^\s\r\n]+", RegexOptions.Compiled);
    private static readonly Regex StackTraceRegex = new(@" in [^\r\n]+:line \d+", RegexOptions.Compiled);
    private static readonly Regex MsgPathRegex = new(@"[A-Za-z]:\\[^\s]+", RegexOptions.Compiled);

    private static string SanitizeCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return command;
        command = SerialTargetRegex.Replace(command, "-s [SERIAL]");
        command = WinPathRegex.Replace(command, "$1");
        command = UnixPathRegex.Replace(command, "$1");
        command = IpRegex.Replace(command, "$1.xxx.xxx.xxx");
        return command;
    }

    private static string SanitizeOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return output;
        var result = output;
        result = SerialRegex.Replace(result, "[SERIAL]");
        result = EmailRegex.Replace(result, "[EMAIL]");
        result = WinPathOutputRegex.Replace(result, "[PATH]");
        result = DataPathRegex.Replace(result, "[PATH]");
        return Truncate(result, MaxDetailLength);
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var result = StackTraceRegex.Replace(message, "");
        result = MsgPathRegex.Replace(result, "[PATH]");
        return result;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }
}
