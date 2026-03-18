namespace AIVideoGenerator.Core.Services
{
    /// <summary>
    /// Generates speech audio from text using text-to-speech services.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>
        /// Generate speech audio from text and save to an output file.
        /// Returns the path to the generated audio file.
        /// </summary>
        Task<string> GenerateSpeechAsync(
            string text,
            string voiceName,
            string outputDir,
            float rate = 1.0f,
            CancellationToken ct = default);

        /// <summary>
        /// Get the duration of an audio file in seconds.
        /// </summary>
        Task<double> GetAudioDurationAsync(string audioFilePath, CancellationToken ct = default);
    }
}
