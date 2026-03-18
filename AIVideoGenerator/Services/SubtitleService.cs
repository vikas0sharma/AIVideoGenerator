using System.Globalization;
using System.Text;
using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;

namespace AIVideoGenerator.Services
{
    /// <summary>
    /// Simple subtitle service that creates evenly-timed subtitles from the script.
    /// For production, integrate Whisper or Azure Speech-to-Text for precise word timing.
    /// </summary>
    public class SubtitleService : ISubtitleService
    {
        private readonly ILogger<SubtitleService> _logger;
        private readonly IAudioService _audioService;

        public SubtitleService(IAudioService audioService, ILogger<SubtitleService> logger)
        {
            _audioService = audioService;
            _logger = logger;
        }

        public async Task<List<SubtitleItem>> GenerateSubtitlesAsync(
            string audioFilePath,
            string script,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Generating subtitles from script");

            var totalDuration = await _audioService.GetAudioDurationAsync(audioFilePath, ct);

            // Split script into sentences for subtitle items
            var sentences = script
                .Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 2)
                .ToList();

            if (sentences.Count == 0)
                return [];

            var subtitles = new List<SubtitleItem>();
            var durationPerSentence = totalDuration / sentences.Count;

            for (int i = 0; i < sentences.Count; i++)
            {
                subtitles.Add(new SubtitleItem
                {
                    Index = i + 1,
                    Start = TimeSpan.FromSeconds(i * durationPerSentence),
                    End = TimeSpan.FromSeconds((i + 1) * durationPerSentence),
                    Text = sentences[i].Trim()
                });
            }

            _logger.LogInformation("Generated {Count} subtitle items", subtitles.Count);
            return subtitles;
        }

        public Task<string> WriteSrtFileAsync(
            List<SubtitleItem> subtitles,
            string outputDir,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(outputDir);
            var filePath = Path.Combine(outputDir, "subtitles.vtt");

            //var sb = new StringBuilder();
            //foreach (var sub in subtitles)
            //{
            //    sb.AppendLine(sub.Index.ToString());
            //    sb.AppendLine($"{FormatSrtTime(sub.Start)} --> {FormatSrtTime(sub.End)}");
            //    sb.AppendLine(sub.Text);
            //    sb.AppendLine();
            //}

            //File.WriteAllText(filePath, sb.ToString());
            //_logger.LogInformation("SRT file written to {Path}", filePath);
            return Task.FromResult(filePath);
        }

        private static string FormatSrtTime(TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
        }
    }
}
