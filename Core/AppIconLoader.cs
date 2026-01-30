
namespace ZephyrsElixir.Core
{
    public static class AppIconLoader
    {
        private static readonly ConcurrentDictionary<string, BitmapImage?> IconCache = new();
        private static readonly int MaxConcurrent = Environment.ProcessorCount;
        private static readonly SemaphoreSlim LoadSemaphore = new(MaxConcurrent);
        private const string AgentIconUri = "http://localhost:8080/icon";

        public static async Task<BitmapImage?> LoadIconAsync(
            string packageName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return null;

            if (IconCache.TryGetValue(packageName, out var cached))
                return cached;

            await LoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (IconCache.TryGetValue(packageName, out cached))
                    return cached;

                var icon = await ExtractIconAsync(packageName, cancellationToken);
                icon?.Freeze();
                IconCache.TryAdd(packageName, icon);

                return icon;
            }
            catch (OperationCanceledException)
            {
                IconCache.TryAdd(packageName, null);
                return null;
            }
            catch
            {
                IconCache.TryAdd(packageName, null);
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
            var uri = $"{AgentIconUri}/{packageName}";
            
            try
            {
                var response = await ZephyrsAgent.HttpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (imageBytes == null || imageBytes.Length == 0)
                    return null;

                using (var stream = new MemoryStream(imageBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 44;
                    bitmap.StreamSource = stream;
                    
                    try
                    {
                        bitmap.EndInit();
                        return bitmap;
                    }
                    catch (NotSupportedException)
                    {
                        return null;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static void ClearCache()
        {
            IconCache.Clear();
        }

        public static async Task PreloadIconsAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken = default)
        {
            var tasks = packageNames
                .Where(p => !IconCache.ContainsKey(p))
                .Select(p => LoadIconAsync(p, cancellationToken));

            await Task.WhenAll(tasks);
        }
    }
}
