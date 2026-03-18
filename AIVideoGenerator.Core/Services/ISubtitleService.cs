using AIVideoGenerator.Core.Models;

namespace AIVideoGenerator.Core.Services
{
    /// <summary>
    /// Generates subtitles from audio or script text.
    /// </summary>
    public interface ISubtitleService
    {
        /// <summary>
        /// Generate subtitle items from an audio file and its corresponding script.
        /// </summary>
        Task<List<SubtitleItem>> GenerateSubtitlesAsync(
            string audioFilePath,
            string script,
            CancellationToken ct = default);

        /// <summary>
        /// Write subtitle items to an SRT file.
        /// </summary>
        Task<string> WriteSrtFileAsync(
            List<SubtitleItem> subtitles,
            string outputDir,
            CancellationToken ct = default);
    }
}
