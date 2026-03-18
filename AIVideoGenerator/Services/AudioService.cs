using AIVideoGenerator.Configuration;
using AIVideoGenerator.Core.Services;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace AIVideoGenerator.Services
{
    /// <summary>
    /// Audio service that uses edge-tts (a Node/Python CLI tool) or
    /// falls back to a simple process-based approach for TTS.
    /// Replace with Azure Cognitive Services Speech SDK for production use.
    /// </summary>
    public class AudioService : IAudioService
    {
        private readonly VideoGeneratorSettings _settings;
        private readonly ILogger<AudioService> _logger;

        public AudioService(IOptions<VideoGeneratorSettings> settings, ILogger<AudioService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<string> GenerateSpeechAsync(
            string text,
            string voiceName,
            string outputDir,
            float rate = 1.0f,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "voiceover.mp3");

            // Define the VTT path to capture exact timings
            var vttPath = Path.Combine(outputDir, "subtitles.vtt");

            _logger.LogInformation("Generating speech with voice {Voice}", voiceName);

            // Use edge-tts CLI (pip install edge-tts)
            var rateStr = rate >= 1 ? $"+{(int)((rate - 1) * 100)}%" : $"-{(int)((1 - rate) * 100)}%";
            var args = $"--voice \"{voiceName}\" --rate=\"{rateStr}\" --text \"{EscapeForShell(text)}\" --write-media \"{outputPath}\" --write-subtitles \"{vttPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "C:\\Users\\v.sharma\\AppData\\Roaming\\Python\\Python313\\Scripts\\edge-tts.exe", // TODO: Make this configurable or use a more robust method to locate the executable
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start edge-tts process");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                _logger.LogError("edge-tts failed: {Error}", error);
                throw new InvalidOperationException($"edge-tts failed with exit code {process.ExitCode}: {error}");
            }

            _logger.LogInformation("Speech generated at {Path}", outputPath);
            return outputPath;
        }

        public async Task<double> GetAudioDurationAsync(string audioFilePath, CancellationToken ct = default)
        {
            // Use ffprobe to get audio duration
            var args = $"-v error -show_entries format=duration -of csv=p=0 \"{audioFilePath}\"";
            var psi = new ProcessStartInfo
            {
                FileName = "C:\\ffmpeg\\bin\\ffprobe.exe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffprobe process");

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            // FIX: Add InvariantCulture so it doesn't fail on systems using commas for decimals
            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
                return duration;

            _logger.LogWarning("Could not determine audio duration, defaulting to 30s");
            return 30.0;
        }

        private static string EscapeForShell(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", "");
        }
    }
}
