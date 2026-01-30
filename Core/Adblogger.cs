namespace ZephyrsElixir.Core;

public sealed class AdbLogger
{
    #region Singleton
    
    private static readonly Lazy<AdbLogger> _instance = new(() => new AdbLogger());
    public static AdbLogger Instance => _instance.Value;
    
    private AdbLogger() { }
    
    #endregion

    #region Configuration
    
    private const int MaxEntries = 150;
    private const int MaxDetailLength = 300;
    private const int DedupeWindowSeconds = 5;
    
    #endregion

    #region State
    
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private volatile LogEntry? _lastEntry;
    private int _duplicateCount;
    
    public event EventHandler<LogEntry>? LogEntryAdded;
    
    #endregion

    #region Public Types
    
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
            var time = Timestamp.ToString("HH:mm:ss");
            var repeat = RepeatCount > 1 ? $" (×{RepeatCount})" : "";
            var detail = string.IsNullOrEmpty(Detail) ? "" : $"\n    → {Detail}";
            return $"[{time}] [{Level}] [{Category}] {Message}{repeat}{detail}";
        }
    }
    
    #endregion

    #region Core Logging Methods

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
        var sanitizedCmd = SanitizeCommand(command);
        AddEntry(LogLevel.Command, "ADB", $"adb {sanitizedCmd}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            var cleanOutput = SanitizeOutput(output.Trim());
            if (!string.IsNullOrEmpty(cleanOutput))
            {
                AddEntry(
                    isError ? LogLevel.Error : LogLevel.Info,
                    "ADB",
                    isError ? "Command failed" : "Output",
                    cleanOutput);
            }
        }
    }

    public void LogException(string category, Exception ex)
    {
        var message = $"{ex.GetType().Name}: {SanitizeMessage(ex.Message)}";
        var detail = ex.InnerException != null 
            ? $"Inner: {ex.InnerException.GetType().Name}" 
            : null;
        
        AddEntry(LogLevel.Error, category, message, detail);
    }
    
    #endregion

    #region Entry Management

    private void AddEntry(LogLevel level, string category, string message, string? detail = null)
    {
        var sanitizedDetail = detail != null ? Truncate(SanitizeMessage(detail), MaxDetailLength) : null;
        var now = DateTime.Now;

        var last = _lastEntry;
        if (last != null && 
            last.Level == level && 
            last.Category == category && 
            last.Message == message &&
            (now - last.Timestamp).TotalSeconds < DedupeWindowSeconds)
        {
            Interlocked.Increment(ref _duplicateCount);
            return;
        }

        FlushDuplicates();

        var entry = new LogEntry(now, level, category, message, sanitizedDetail);
        _lastEntry = entry;
        _duplicateCount = 1;

        Enqueue(entry);
    }

    private void FlushDuplicates()
    {
        var last = _lastEntry;
        var count = Interlocked.Exchange(ref _duplicateCount, 0);
        
        if (last != null && count > 1)
        {
            var updated = last with { RepeatCount = count };
            
            if (TryRemoveLast(out _))
                Enqueue(updated);
        }
    }

    private void Enqueue(LogEntry entry)
    {
        _entries.Enqueue(entry);
        
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);

        LogEntryAdded?.Invoke(this, entry);
    }

    private bool TryRemoveLast(out LogEntry? removed)
    {
        var items = _entries.ToArray();
        removed = null;
        
        if (items.Length == 0) return false;

        while (_entries.TryDequeue(out _)) { }
        
        for (int i = 0; i < items.Length - 1; i++)
            _entries.Enqueue(items[i]);
        
        removed = items[^1];
        return true;
    }
    
    #endregion

    #region Export & Query

    public bool HasLogs => !_entries.IsEmpty;
    public int Count => _entries.Count;
    public IReadOnlyList<LogEntry> GetAllEntries() => _entries.ToArray();

    public string GetFormattedLog()
    {
        FlushDuplicates();
        
        var sb = new StringBuilder(4096);
        
        sb.AppendLine("════════════════════════════════════════════════════════════");
        sb.AppendLine("  Zephyr's Elixir - Diagnostic Log");
        sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("════════════════════════════════════════════════════════════");
        sb.AppendLine();

        if (_entries.IsEmpty)
        {
            sb.AppendLine("No operations logged. Perform some actions and try again.");
            return sb.ToString();
        }

        var entries = _entries.ToArray();
        var stats = new Dictionary<LogLevel, int>();
        foreach (var e in entries)
            stats[e.Level] = stats.GetValueOrDefault(e.Level) + e.RepeatCount;

        sb.AppendLine($"Summary: {entries.Length} entries");
        if (stats.TryGetValue(LogLevel.Error, out var errors) && errors > 0)
            sb.AppendLine($"  ⚠ {errors} error(s)");
        if (stats.TryGetValue(LogLevel.Warning, out var warnings) && warnings > 0)
            sb.AppendLine($"  ⚡ {warnings} warning(s)");
        sb.AppendLine();
        sb.AppendLine("────────────────────────────────────────────────────────────");
        sb.AppendLine();

        DateTime? currentBlock = null;
        foreach (var entry in entries)
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

        sb.AppendLine();
        sb.AppendLine("════════════════════════════════════════════════════════════");
        sb.AppendLine("  End of Log");
        sb.AppendLine("════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        _lastEntry = null;
        _duplicateCount = 0;
    }
    
    #endregion

    #region Sanitization (Privacy)

    private static string SanitizeCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return command;

        command = Regex.Replace(command, @"[A-Za-z]:\\[^\s""]+\\([^\s""\\]+)", "$1");
        command = Regex.Replace(command, @"/(?:home|Users|mnt|storage)/[^\s""]+/([^\s""/]+)", "$1");
        
        command = Regex.Replace(command, @"(\d{1,3})\.\d{1,3}\.\d{1,3}\.\d{1,3}", "$1.xxx.xxx.xxx");
        
        return command;
    }

    private static string SanitizeOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return output;

        var result = output;
        
        result = Regex.Replace(result, @"\b[A-Z0-9]{8,}\b", "[SERIAL]");
        
        result = Regex.Replace(result, @"\b[\w.-]+@[\w.-]+\.\w+\b", "[EMAIL]");
        
        result = Regex.Replace(result, @"[A-Za-z]:\\[^\s\r\n]+", "[PATH]");
        result = Regex.Replace(result, @"/(?:data|storage|sdcard)/[^\s\r\n]+", "[PATH]");

        return Truncate(result, MaxDetailLength);
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        
        var result = Regex.Replace(message, @" in [^\r\n]+:line \d+", "");
        result = Regex.Replace(result, @"[A-Za-z]:\\[^\s]+", "[PATH]");
        
        return result;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }
    
    #endregion
}