using System.Net.Http.Headers;
using System.Text.Json;
using AIVideoGenerator.Configuration;
using AIVideoGenerator.Core.Enums;
using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Extensions.Options;

namespace AIVideoGenerator.Services
{
    /// <summary>
    /// Fetches stock video materials from the Pexels API.
    /// </summary>
    public class PexelsMaterialService : IMaterialService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PexelsMaterialService> _logger;

        public PexelsMaterialService(
            IHttpClientFactory httpClientFactory,
            IOptions<VideoGeneratorSettings> settings,
            ILogger<PexelsMaterialService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("Pexels");
            _httpClient.BaseAddress = new Uri("https://api.pexels.com/");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(settings.Value.PexelsApiKey);
            _logger = logger;
        }

        public async Task<List<MaterialInfo>> SearchVideosAsync(
            List<string> searchTerms,
            VideoAspect aspect,
            int minDurationSeconds,
            CancellationToken ct = default)
        {
            var materials = new List<MaterialInfo>();
            var orientation = aspect.ToOrientationString();

            foreach (var term in searchTerms)
            {
                var url = $"videos/search?query={Uri.EscapeDataString(term)}&orientation={orientation}&per_page=5&size=medium";
                _logger.LogInformation("Searching Pexels for: {Term}", term);

                try
                {
                    var response = await _httpClient.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(response);

                    if (doc.RootElement.TryGetProperty("videos", out var videos))
                    {
                        foreach (var video in videos.EnumerateArray())
                        {
                            var duration = video.GetProperty("duration").GetSingle();
                            if (duration < minDurationSeconds)
                                continue;

                            // Get the best quality video file
                            var videoFiles = video.GetProperty("video_files");
                            var bestFile = videoFiles.EnumerateArray()
                                .Where(f => f.GetProperty("quality").GetString() == "hd")
                                .OrderByDescending(f => f.GetProperty("width").GetInt32())
                                .FirstOrDefault();

                            if (bestFile.ValueKind != JsonValueKind.Undefined)
                            {
                                materials.Add(new MaterialInfo
                                {
                                    Provider = "pexels",
                                    Url = bestFile.GetProperty("link").GetString() ?? "",
                                    Duration = duration
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to search Pexels for term: {Term}", term);
                }
            }

            _logger.LogInformation("Found {Count} video materials", materials.Count);
            return materials;
        }

        public async Task<string> DownloadVideoAsync(string url, string outputDir, CancellationToken ct = default)
        {
            try
            {
                var fileName = $"{Guid.NewGuid():N}.mp4";
                var filePath = Path.Combine(outputDir, fileName);

                _logger.LogInformation("Downloading video from {Url}", url);

                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                await using var fileStream = File.Create(filePath);
                await response.Content.CopyToAsync(fileStream, ct);

                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download video from {Url}", url);
                return "";
            }
        }
    }
}
