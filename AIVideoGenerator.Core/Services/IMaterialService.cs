using AIVideoGenerator.Core.Enums;
using AIVideoGenerator.Core.Models;

namespace AIVideoGenerator.Core.Services
{
    /// <summary>
    /// Fetches stock video materials from external providers (Pexels, Pixabay, etc.).
    /// </summary>
    public interface IMaterialService
    {
        /// <summary>
        /// Search for and download video clips matching the given search terms.
        /// </summary>
        Task<List<MaterialInfo>> SearchVideosAsync(
            List<string> searchTerms,
            VideoAspect aspect,
            int minDurationSeconds,
            CancellationToken ct = default);

        /// <summary>
        /// Download a video from a URL to a local path.
        /// </summary>
        Task<string> DownloadVideoAsync(string url, string outputDir, CancellationToken ct = default);
    }
}
