
namespace ZephyrsElixir.Core;
public static class AppIconLoader
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> IconCache = new();
    private static readonly SemaphoreSlim LoadSemaphore = new(Environment.ProcessorCount);

    private static string CacheKey(string packageName) =>
        $"{DeviceManager.SharedActiveSerial}|{packageName}";

    public static async Task<BitmapImage?> LoadIconAsync(
        string packageName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        var key = CacheKey(packageName);
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        await LoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IconCache.TryGetValue(key, out cached))
                return cached;

            var icon = await ExtractIconAsync(packageName, cancellationToken);
            icon?.Freeze();
            IconCache.TryAdd(key, icon);
            return icon;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            IconCache.TryAdd(key, null);
            return null;
        }
        finally
        {
            LoadSemaphore.Release();
        }
    }

    private static async Task<BitmapImage?> ExtractIconAsync(
        string packageName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ZephyrsAgent.HttpClient.GetAsync(
                $"{ZephyrsAgent.BaseUri}/icon/{packageName}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (imageBytes is not { Length: > 0 })
                return null;

            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
