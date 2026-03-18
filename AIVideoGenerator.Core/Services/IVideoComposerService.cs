using AIVideoGenerator.Core.Models;

namespace AIVideoGenerator.Core.Services
{
    /// <summary>
    /// Composes the final video from video clips, audio, subtitles, and background music.
    /// </summary>
    public interface IVideoComposerService
    {
        /// <summary>
        /// Compose the final video by combining clips, voiceover audio, subtitles, and BGM.
        /// </summary>
        Task<string> ComposeVideoAsync(VideoProject project, CancellationToken ct = default);
    }
}
