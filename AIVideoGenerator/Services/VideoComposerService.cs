using AIVideoGenerator.Configuration;
using AIVideoGenerator.Core.Enums;
using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xabe.FFmpeg;

namespace AIVideoGenerator.Services
{


    /// <summary>
    /// VideoComposerService implemented using Xabe.FFmpeg (no manual Process).
    /// Mirrors the Python logic: subclip -> normalize (scale+pad) -> loop -> concat -> mix audio -> subtitles.
    /// </summary>
    public class VideoComposerService : IVideoComposerService
    {
        private readonly VideoGeneratorSettings _settings;
        private readonly ILogger<VideoComposerService> _logger;
        private const double BgmFadeOutSeconds = 3.0;
        private const double TransitionDurationSeconds = 0.5;

        public VideoComposerService(IOptions<VideoGeneratorSettings> settings, ILogger<VideoComposerService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            string path = @"C:\ffmpeg\bin";
            if (!string.IsNullOrWhiteSpace(path))
            {
                FFmpeg.SetExecutablesPath(path);
                _logger.LogDebug("FFmpeg executables path set to {Path}", path);
            }
        }

        public async Task<string> ComposeVideoAsync(VideoProject project, CancellationToken ct = default)
        {
            Directory.CreateDirectory(project.TaskDir);
            var outputPath = Path.Combine(project.TaskDir, "final_video.mp4");

            if (project.DownloadedVideoPaths == null || project.DownloadedVideoPaths.Count == 0)
            {
                _logger.LogWarning("No video clips to compose");
                return string.Empty;
            }

            var (targetW, targetH) = project.Params.VideoAspect.ToResolution();
            var maxClipDuration = await GetEffectiveMaxClipDurationAsync(project,ct);

            var tempDir = Path.Combine(project.TaskDir, "tmp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1) Build subclip list (only full-sized slices)
                var subclips = await BuildSubclipsListAsync(project.DownloadedVideoPaths, maxClipDuration, ct);
                if (project.Params.VideoConcatMode == VideoConcatMode.Random)
                {
                    var rnd = new Random();
                    subclips = subclips.OrderBy(_ => rnd.Next()).ToList();
                }

                // 2) Process subclips (trim + scale + pad) into temp files
                var processedClips = new List<string>();
                double processedTotalSec = 0.0;
                var audioLen = await GetAudioDurationSafeAsync(project.AudioFilePath, ct);

                foreach (var sc in subclips)
                {
                    if (processedTotalSec >= audioLen)
                        break;

                    var outClip = Path.Combine(tempDir, $"clip_{processedClips.Count + 1}.mp4");
                    await CreateTrimmedPaddedClip_XabeAsync(sc.SourcePath, sc.Start, sc.Duration, outClip, targetW, targetH, ct);
                    processedClips.Add(outClip);

                    var info = await FFmpeg.GetMediaInfo(outClip, ct);
                    processedTotalSec += info.Duration.TotalSeconds;
                }

                if (processedClips.Count == 0)
                    throw new InvalidOperationException("No processed clips created. Verify source durations and MaxClipDuration.");

                // 3) Loop processed clips (by duplicating references) to cover audio length
                if (processedTotalSec < audioLen)
                {
                    var baseList = processedClips.ToList();
                    var idx = 0;
                    while (processedTotalSec < audioLen)
                    {
                        processedClips.Add(baseList[idx % baseList.Count]);
                        var d = (await FFmpeg.GetMediaInfo(baseList[idx % baseList.Count], ct)).Duration.TotalSeconds;
                        processedTotalSec += d;
                        idx++;
                        if (idx > 5000) break; // guard
                    }
                }

                // 4) Merge clips — with transitions or simple concat
                var mergedPath = Path.Combine(tempDir, "merged.mp4");

                if (project.Params.VideoTransitionMode != VideoTransitionMode.None)
                {
                    var clipDurations = new List<double>();
                    foreach (var clip in processedClips)
                    {
                        var info = await FFmpeg.GetMediaInfo(clip, ct);
                        clipDurations.Add(info.Duration.TotalSeconds);
                    }
                    await MergeWithTransitions_XabeAsync(processedClips, clipDurations, project.Params.VideoTransitionMode, mergedPath, ct);
                }
                else
                {
                    var concatList = Path.Combine(tempDir, "concat.txt");
                    await File.WriteAllLinesAsync(concatList, processedClips.Select(p => $"file '{Path.GetFullPath(p).Replace("'", "'\\''")}'"), ct);

                    var ok = await TryConcatCopy_XabeAsync(concatList, mergedPath, project.TaskDir, ct);
                    if (!ok)
                    {
                        _logger.LogInformation("Concat copy failed; performing re-encode concat");
                        await ConcatReencode_XabeAsync(concatList, mergedPath, targetW, targetH, ct);
                    }
                }

                // 6) Mix audio and attach to merged video
                var finalWithAudio = Path.Combine(tempDir, "final_with_audio.mp4");
                await MixAudio_XabeAsync(mergedPath, project.AudioFilePath, project, finalWithAudio, processedTotalSec, ct);

                // 7) Overlay subtitles if present
                if (project.Params.SubtitleEnabled && !string.IsNullOrEmpty(project.SubtitleFilePath) && File.Exists(project.SubtitleFilePath))
                {
                    await OverlaySubtitles_XabeAsync(finalWithAudio, project.SubtitleFilePath,  outputPath, ct);
                }
                else
                {
                    // copy final
                    File.Copy(finalWithAudio, outputPath, true);
                }

                _logger.LogInformation("Compose complete: {0}", outputPath);
                return outputPath;
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        #region Xabe-based helpers

        private async Task CreateTrimmedPaddedClip_XabeAsync(string source, double startSeconds, double durationSeconds, string outPath, int targetW, int targetH, CancellationToken ct)
        {
            // Use Xabe to run ffmpeg args: -ss <start> -t <dur> -i src -vf "scale=...,pad=...,setsar=1" -c:v libx264 -c:a aac -shortest out
            var ss = startSeconds.ToString(CultureInfo.InvariantCulture);
            var t = durationSeconds.ToString(CultureInfo.InvariantCulture);

            var vf = $"scale={targetW}:{targetH}:force_original_aspect_ratio=increase,crop={targetW}:{targetH}:(in_w-out_w)/2:(in_h-out_h)/2,setsar=1";

            // Build argument string. Use PreInput -ss and -t for faster trimming.
            var args = new List<string>
            {
                "-y",
                "-ss", ss,
                "-t", t,
                "-i", $"\"{EscapePath(source)}\"",
                "-vf", $"\"{vf}\"",
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "23",
                "-r", "30",    // 1. Force a consistent 30 FPS across all clips to prevent timebase drift
                "-an",         // 2. STRIP all audio. This guarantees every file in concat.txt has exactly 1 video stream and 0 audio streams.
    
                // "-c:a", "aac", // REMOVE this
                // "-shortest",   // REMOVE this
                $"\"{EscapePath(outPath)}\""
            };

            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(string.Join(' ', args));
            conversion.SetOverwriteOutput(true);

            // Progress logging
            conversion.OnProgress += (s, e) =>
            {
                _logger.LogDebug("Trim+Pad progress: {Percent}% Time:{Time}", e.Percent, e.Duration);
            };

            await conversion.Start(ct);
        }

        private async Task<bool> TryConcatCopy_XabeAsync(string concatListPath, string outputPath, string workingDir, CancellationToken ct)
        {
            // Try fast concat copy: -f concat -safe 0 -i list -c copy out
            var args = $"-y -f concat -safe 0 -i \"{EscapePath(concatListPath)}\" -c copy \"{EscapePath(outputPath)}\"";
            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(args);
            conversion.SetOverwriteOutput(true);
            //conversion.SetWorkingDirectory(workingDir);

            bool success = false;
            try
            {
                conversion.OnProgress += (s, e) => _logger.LogDebug("Concat copy progress: {Percent}% Time:{Time}", e.Percent, e.Duration);
                await conversion.Start(ct);
                success = File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Concat copy attempt failed");
                success = false;
            }

            return success;
        }

        private async Task ConcatReencode_XabeAsync(string concatListPath, string outputPath, int width, int height, CancellationToken ct)
        {
            // Re-encode concat to ensure consistent codecs/resolution
            var vf = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1";
            var args = $"-y -f concat -safe 0 -i \"{EscapePath(concatListPath)}\" -vf \"{vf}\" -c:v libx264 -preset medium -crf 23 -c:a aac -shortest \"{EscapePath(outputPath)}\"";

            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(args);
            conversion.SetOverwriteOutput(true);

            conversion.OnProgress += (s, e) =>
            {
                _logger.LogDebug("Concat re-encode progress: {Percent}% Time:{Time}", e.Percent, e.Duration);
            };

            await conversion.Start(ct);
        }

        private async Task MixAudio_XabeAsync(string videoPath, string voicePath, VideoProject project, string outPath, double videoDurationSeconds, CancellationToken ct)
        {
            var hasVoice = !string.IsNullOrEmpty(voicePath) && File.Exists(voicePath);
            var bgm = ResolveBgmFile(project);
            var hasBgm = !string.IsNullOrEmpty(bgm) && File.Exists(bgm);

            if (!hasVoice && !hasBgm)
            {
                // No audio: copy video
                File.Copy(videoPath, outPath, true);
                return;
            }

            // Build arguments for Xabe conversion; we still use AddParameter with constructed args for full control
            // Inputs:
            //  -i videoPath [ -i voicePath ] [ -stream_loop -1 -i bgm ]
            var sb = new StringBuilder();
            sb.Append("-y ");
            sb.Append($"-i \"{EscapePath(videoPath)}\" ");

            if (hasVoice)
                sb.Append($"-i \"{EscapePath(voicePath)}\" ");

            if (hasBgm)
                sb.Append($"-stream_loop -1 -i \"{EscapePath(bgm)}\" ");

            var filterParts = new List<string>();
            string audioMap = string.Empty;

            if (hasVoice && hasBgm)
            {
                var voiceVol = project.Params.VoiceVolume.ToString(CultureInfo.InvariantCulture);
                var bgmVol = project.Params.BgmVolume.ToString(CultureInfo.InvariantCulture);
                var fadeStart = Math.Max(0, videoDurationSeconds - BgmFadeOutSeconds).ToString(CultureInfo.InvariantCulture);

                filterParts.Add($"[1:a]volume={voiceVol}[voice]");
                filterParts.Add($"[2:a]volume={bgmVol},afade=t=out:st={fadeStart}:d={BgmFadeOutSeconds}[bgm]");

                // Add normalize=0 here
                filterParts.Add("[voice][bgm]amix=inputs=2:duration=first:dropout_transition=2:normalize=0[aout]");
                audioMap = "-map \"[aout]\"";
            }
            else if (hasVoice && !hasBgm)
            {
                audioMap = "-map 1:a";
            }
            else if (!hasVoice && hasBgm)
            {
                var bgmVol = project.Params.BgmVolume.ToString(CultureInfo.InvariantCulture);
                filterParts.Add($"[1:a]volume={bgmVol}[aout]");
                audioMap = "-map \"[aout]\"";
            }

            if (filterParts.Count > 0)
            {
                sb.Append($"-filter_complex \"{string.Join(";", filterParts)}\" ");
            }

            sb.Append("-map 0:v ");
            if (!string.IsNullOrEmpty(audioMap))
                sb.Append($"{audioMap} ");

            sb.Append("-c:v libx264 -preset medium -crf 23 ");
            if (!string.IsNullOrEmpty(audioMap))
                sb.Append("-c:a aac -b:a 192k ");

            sb.Append("-shortest ");
            sb.Append($"\"{EscapePath(outPath)}\"");

            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(sb.ToString());
            conversion.SetOverwriteOutput(true);

            conversion.OnProgress += (s, e) =>
            {
                _logger.LogDebug("MixAudio progress: {Percent}% Time:{Time}", e.Percent, e.Duration);
            };

            await conversion.Start(ct);
        }

        private async Task OverlaySubtitles_XabeAsync(string inputVideo, string subtitlePath, /* ProjectParams @params, */ string outputPath, CancellationToken ct)
        {
            var subsEsc = EscapeFilterPath(Path.GetFullPath(subtitlePath));
            var fontName = "Arial";// string.IsNullOrWhiteSpace(@params.FontName) ? "Arial" : @params.FontName.Replace("'", "\\'");
            var fontSize = 24;// @params.FontSize > 0 ? @params.FontSize : 24;
            var primaryColour = "&H00FFFFFF";// ToFfmpegColor(@params.TextForeColor);

            var filter = $"subtitles='{subsEsc}'";
            if (Path.GetExtension(subtitlePath).Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                filter += $":force_style='FontName={fontName},FontSize={fontSize},PrimaryColour={primaryColour}'";
            }

            var args = $"-y -i \"{EscapePath(inputVideo)}\" -vf \"{filter}\" -c:v libx264 -preset medium -crf 23 -c:a copy \"{EscapePath(outputPath)}\"";
            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(args);
            conversion.SetOverwriteOutput(true);

            conversion.OnProgress += (s, e) => _logger.LogDebug("Subtitle overlay progress: {Percent}% Time:{Time}", e.Percent, e.Duration);

            await conversion.Start(ct);
        }

        private async Task<double> GetAudioDurationSafeAsync(string path, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0.0;

            try
            {
                var info = await FFmpeg.GetMediaInfo(path, ct);
                return info.Duration.TotalSeconds;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ffprobe failed for audio duration; defaulting to 30s");
                return 30.0;
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// If project.Params.MaxClipDuration > 0, returns that. Otherwise computes a heuristic
        /// based on audio length and number of source files so clips will match narration length.
        /// </summary>
        private async Task<int> GetEffectiveMaxClipDurationAsync(VideoProject project, CancellationToken ct)
        {
            // configured value takes precedence
            //if (project?.Params != null && project.Params.MaxClipDuration > 0)
            //    return Math.Max(1, project.Params.MaxClipDuration);

            // ensure sources present
            var sources = project?.DownloadedVideoPaths?.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).ToArray() ?? Array.Empty<string>();
            if (sources.Length == 0)
                return 1;

            // audio length (seconds) — fallback to a default if unknown
            var audioLen = 0.0;
            if (!string.IsNullOrEmpty(project.AudioFilePath) && File.Exists(project.AudioFilePath))
            {
                try
                {
                    var mi = await FFmpeg.GetMediaInfo(project.AudioFilePath, ct);
                    audioLen = mi.Duration.TotalSeconds;
                }
                catch
                {
                    audioLen = 30.0; // fallback
                }
            }
            else
            {
                // no audio -> reasonable clip length
                audioLen = 30.0;
            }

            // Estimate how many slices we might reasonably want:
            // Start with one slice per source. If audio is long, allow more slices per source, but cap.
            // This avoids extremely short clips if there are many sources.
            const int minSlicesPerSource = 1;
            const int maxSlicesPerSource = 4; // allow up to this many pieces per source
                                              // targetSlices = audioLen (s) / preferredClipSeconds (start guess). We'll invert to compute clip length.
                                              // We'll choose preferredClipSeconds = 4 as a starting point for slices count estimation
            var preferredClipSeconds = 4.0;
            var estimatedTotalSlices = Math.Clamp((int)Math.Round(audioLen / preferredClipSeconds), minSlicesPerSource * sources.Length, maxSlicesPerSource * sources.Length);

            // Finally compute clip duration
            var computed = (int)Math.Max(1, Math.Round(audioLen / Math.Max(1, estimatedTotalSlices)));

            // clamp into allowed bounds
            computed = Math.Clamp(computed, 1, 30);

            _logger.LogInformation("Computed MaxClipDuration={Computed} (audioLen={AudioLen}s, sources={Sources}, estSlices={Est})",
                computed, Math.Round(audioLen, 1), sources.Length, estimatedTotalSlices);

            return computed;
        }

        private async Task<List<Subclip>> BuildSubclipsListAsync(IEnumerable<string> sources, int maxClipDuration, CancellationToken ct)
        {
            var list = new List<Subclip>();

            foreach (var src in sources)
            {
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
                var info = await FFmpeg.GetMediaInfo(src, ct);
                var dur = info.Duration.TotalSeconds;
                double start = 0;
                while (start + maxClipDuration - 1e-6 <= dur)
                {
                    list.Add(new Subclip { SourcePath = src, Start = start, Duration = maxClipDuration });
                    start += maxClipDuration;
                }
            }

            return list;
        }

        private string ResolveBgmFile(VideoProject project)
        {
            if (project.Params.BgmType?.ToLowerInvariant() == "none")
                return null;

            if (!string.IsNullOrEmpty(project.Params.BgmFile) && File.Exists(project.Params.BgmFile))
                return project.Params.BgmFile;

            if (!string.IsNullOrEmpty(_settings.SongsPath) && Directory.Exists(_settings.SongsPath))
            {
                var cand = Directory.GetFiles(_settings.SongsPath, "*.mp3");
                if (cand.Length > 0)
                    return cand[Random.Shared.Next(cand.Length)];
            }

            return null;
        }

        private static string ToFfmpegColor(string cssHex)
        {
            if (string.IsNullOrWhiteSpace(cssHex)) return "&H00FFFFFF";
            var s = cssHex.Trim();
            if (s.StartsWith("#")) s = s[1..];
            if (s.Length != 6) return "&H00FFFFFF";
            return "&H00" + s.ToUpperInvariant();
        }

        private static string EscapeFilterPath(string path)
        {
            // Use forward slashes, escape single quotes and colons for ffmpeg filter contexts.
            // Examples:
            //   C:\foo\bar.srt  ->  C\/foo\/bar.srt  then escape colon -> C\:\/foo\/bar.srt
            // but we prefer forward slashes and escape ':' as '\:'
            var p = path.Replace("\\", "/");
            // escape single quote
            p = p.Replace("'", "\\'");
            // escape colon for windows drive letters and any other colons
            p = p.Replace(":", "\\:");
            return p;
        }

        private static string EscapePath(string p) => p.Replace("\"", "\\\"");

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // ignore
            }
        }

        private async Task MergeWithTransitions_XabeAsync(
            List<string> clips,
            List<double> durations,
            VideoTransitionMode mode,
            string outputPath,
            CancellationToken ct)
        {
            if (clips.Count == 1)
            {
                File.Copy(clips[0], outputPath, true);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("-y ");

            for (int i = 0; i < clips.Count; i++)
                sb.Append($"-i \"{EscapePath(clips[i])}\" ");

            var filterParts = new List<string>();
            var transitionNames = GetTransitionNames(mode, clips.Count - 1);

            double cumulativeDuration = durations[0];
            string prevLabel = "[0:v]";

            for (int i = 1; i < clips.Count; i++)
            {
                double offset = Math.Max(0, cumulativeDuration - TransitionDurationSeconds);
                string outLabel = i < clips.Count - 1 ? $"[v{i}]" : "[vout]";
                string transition = transitionNames[i - 1];

                filterParts.Add(
                    $"{prevLabel}[{i}:v]xfade=transition={transition}" +
                    $":duration={TransitionDurationSeconds.ToString(CultureInfo.InvariantCulture)}" +
                    $":offset={offset.ToString(CultureInfo.InvariantCulture)}{outLabel}");

                prevLabel = outLabel;
                cumulativeDuration = offset + durations[i];
            }

            sb.Append($"-filter_complex \"{string.Join(";", filterParts)}\" ");
            sb.Append("-map \"[vout]\" ");
            sb.Append("-c:v libx264 -preset medium -crf 23 -an ");
            sb.Append($"\"{EscapePath(outputPath)}\"");

            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(sb.ToString());
            conversion.SetOverwriteOutput(true);

            conversion.OnProgress += (s, e) =>
            {
                _logger.LogDebug("Transition merge progress: {Percent}% Time:{Time}", e.Percent, e.Duration);
            };

            _logger.LogInformation("Merging {Count} clips with {Mode} transitions", clips.Count, mode);
            await conversion.Start(ct);
        }

        private static List<string> GetTransitionNames(VideoTransitionMode mode, int count)
        {
            if (mode == VideoTransitionMode.Shuffle)
            {
                var options = new[] { "fade", "fadeblack", "fadewhite", "slideleft", "slideright", "wipeleft", "wiperight", "circleopen", "circleclose" };
                var rnd = new Random();
                return Enumerable.Range(0, count).Select(_ => options[rnd.Next(options.Length)]).ToList();
            }

            var name = mode switch
            {
                VideoTransitionMode.FadeIn => "fade",
                VideoTransitionMode.FadeOut => "fadeblack",
                VideoTransitionMode.SlideIn => "slideleft",
                VideoTransitionMode.SlideOut => "slideright",
                _ => "fade"
            };

            return Enumerable.Repeat(name, count).ToList();
        }

        private sealed class Subclip
        {
            public string SourcePath { get; init; }
            public double Start { get; init; }
            public double Duration { get; init; }
        }

        #endregion
    }

    /*
    /// <summary>
    /// Composes the final video using FFmpeg by combining video clips, audio, subtitles, and BGM.
    /// </summary>
    public class VideoComposerService : IVideoComposerService
    {
        private readonly VideoGeneratorSettings _settings;
        private readonly ILogger<VideoComposerService> _logger;

        public VideoComposerService(
            IOptions<VideoGeneratorSettings> settings,
            ILogger<VideoComposerService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<string> ComposeVideoAsync(VideoProject project, CancellationToken ct = default)
        {
            var outputPath = Path.Combine(project.TaskDir, "final_video.mp4");
            Directory.CreateDirectory(project.TaskDir);

            if (project.DownloadedVideoPaths.Count == 0)
            {
                _logger.LogWarning("No video clips available for composition");
                return "";
            }

            var (width, height) = project.Params.VideoAspect.ToResolution();

            // Step 1: Create a concatenation file for FFmpeg
            var concatFilePath = Path.Combine(project.TaskDir, "concat.txt");
            var concatContent = new StringBuilder();
            foreach (var videoPath in project.DownloadedVideoPaths)
            {
                concatContent.AppendLine($"file '{Path.GetFullPath(videoPath)}'");
            }
            await File.WriteAllTextAsync(concatFilePath, concatContent.ToString(), ct);

            // Step 2: Build the FFmpeg command
            var ffmpegArgs = BuildFfmpegArgs(project, concatFilePath, outputPath, width, height);

            _logger.LogInformation("Running FFmpeg to compose video: {Args}", ffmpegArgs);

            var psi = new ProcessStartInfo
            {
                FileName = _settings.FfmpegPath,
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath(project.TaskDir) // ensures -report and -progress write here
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();
            var stdoutTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    stdoutTcs.TrySetResult(stdoutSb.ToString());
                }
                else
                {
                    stdoutSb.AppendLine(e.Data);
                    _logger.LogDebug("ffmpeg stdout: {Line}", e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    stderrTcs.TrySetResult(stderrSb.ToString());
                }
                else
                {
                    stderrSb.AppendLine(e.Data);
                    // Use Information for stderr so you can observe progress/warnings in production logs
                    _logger.LogInformation("ffmpeg stderr: {Line}", e.Data);
                }
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start FFmpeg process");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Ensure child is killed on cancellation
            using (ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        _logger.LogWarning("Cancellation requested — killing ffmpeg process (pid={Pid})", process.Id);
                        process.Kill(true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while killing ffmpeg process");
                }
            }))
            {
                await process.WaitForExitAsync(ct);
            }

            // wait for stream readers to finish
            var finalStdout = await stdoutTcs.Task;
            var finalStderr = await stderrTcs.Task;

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg failed with exit code {ExitCode}. stderr start: {StderrPreview}",
                    process.ExitCode,
                    finalStderr?.Split('\n').FirstOrDefault());
                throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
            }

            _logger.LogInformation("Video composed at {Path}. stderr length={Len}", outputPath, finalStderr?.Length ?? 0);
            return outputPath;
        }

        private string BuildFfmpegArgs(
            VideoProject project,
            string concatFilePath,
            string outputPath,
            int width,
            int height)
        {
            var sb = new StringBuilder();

            // Use absolute path for progress file and quote it
            var progressPath = Path.GetFullPath(Path.Combine(project.TaskDir, "prog.txt"));
            sb.Append($"-hide_banner -progress \"{progressPath}\" -report ");

            // Input: concatenated video clips
            sb.Append($"-f concat -safe 0 -i \"{Path.GetFullPath(concatFilePath)}\" ");

            // Input: audio voiceover
            var hasVoice = false;
            if (!string.IsNullOrEmpty(project.AudioFilePath) && File.Exists(project.AudioFilePath))
            {
                hasVoice = true;
                sb.Append($"-i \"{Path.GetFullPath(project.AudioFilePath)}\" ");
            }

            // Input: background music
            var bgmFile = ResolveBgmFile(project);
            var hasBgm = false;
            if (!string.IsNullOrEmpty(bgmFile) && File.Exists(bgmFile))
            {
                hasBgm = true;
                sb.Append($"-i \"{Path.GetFullPath(bgmFile)}\" ");
            }

            // Video filter: scale + pad to target resolution
            var filters = new List<string>
            {
                $"scale={width}:{height}:force_original_aspect_ratio=decrease",
                $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
            };

            // Subtitle filter (safe defaults & escaping)
            if (project.Params.SubtitleEnabled && !string.IsNullOrEmpty(project.SubtitleFilePath) && File.Exists(project.SubtitleFilePath))
            {
                var fontName = string.IsNullOrWhiteSpace(project.Params.FontName) ? "Arial" : project.Params.FontName;
                var fontSize = project.Params.FontSize > 0 ? project.Params.FontSize : 24;
                var fontColor = ToFfmpegColor(project.Params.TextForeColor);
                var subsPathEsc = EscapeFilterPath(Path.GetFullPath(project.SubtitleFilePath));
                // use: subtitles=path:force_style='FontName=...,FontSize=...,PrimaryColour=&H00RRGGBB'
                filters.Add($"subtitles='{subsPathEsc}':force_style='FontName={fontName},FontSize={fontSize},PrimaryColour={fontColor}'");
            }

            sb.Append($"-vf \"{string.Join(",", filters)}\" ");

            // Audio mixing and mapping
            if (hasVoice && hasBgm)
            {
                // input indices: 0=video (concat), 1=voice, 2=bgm
                var bgmVol = project.Params.BgmVolume;
                var voiceVol = project.Params.VoiceVolume;
                sb.Append($"-filter_complex \"[1:a]volume={voiceVol}[voice];[2:a]volume={bgmVol}[bgm];[voice][bgm]amix=inputs=2:duration=first[aout]\" -map 0:v -map \"[aout]\" ");
            }
            else if (hasVoice)
            {
                // input indices: 0=video, 1=voice
                sb.Append("-map 0:v -map 1:a ");
            }
            else if (hasBgm)
            {
                // input indices: 0=video, 1=bgm  (if no voice provided but bgm present)
                sb.Append("-map 0:v -map 1:a ");
            }
            else
            {
                // No audio input: produce video-only output
                sb.Append("-map 0:v ");
            }

            // Output settings
            sb.Append("-c:v libx264 -preset medium -crf 23 ");
            // if audio mapped, encode aac, otherwise skip audio settings
            if (hasVoice || hasBgm)
            {
                sb.Append("-c:a aac -b:a 192k ");
            }
            sb.Append($"-shortest -y \"{Path.GetFullPath(outputPath)}\"");

            return sb.ToString();
        }

        // Helpers
        private static string EscapeFilterPath(string path)
        {
            // Use forward slashes and escape colons for ffmpeg filter context
            // Example: C:\foo\bar.srt -> C:/foo/bar.srt  or C\:/foo/bar.srt inside filters
            // We return the form ffmpeg expects inside a filter: C\\:/path
            // Simpler and reliable: replace backslashes with forward and escape any colon
            return path.Replace("\\", "/");//.Replace(":", "\\:");
        }

        private static string ToFfmpegColor(string cssHex)
        {
            // ffmpeg expects colors like &H00RRGGBB (note order RRGGBB)
            if (string.IsNullOrWhiteSpace(cssHex)) return "&H00FFFFFF";
            var s = cssHex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6) return "&H00FFFFFF";
            return "&H00" + s.ToUpperInvariant();
        }


        //public async Task<string> ComposeVideoAsync(VideoProject project, CancellationToken ct = default)
        //{
        //    var outputPath = Path.Combine(project.TaskDir, "final_video.mp4");
        //    Directory.CreateDirectory(project.TaskDir);

        //    if (project.DownloadedVideoPaths.Count == 0)
        //    {
        //        _logger.LogWarning("No video clips available for composition");
        //        return "";
        //    }

        //    var (width, height) = project.Params.VideoAspect.ToResolution();

        //    // Step 1: Create a concatenation file for FFmpeg
        //    var concatFilePath = Path.Combine(project.TaskDir, "concat.txt");
        //    var concatContent = new StringBuilder();
        //    foreach (var videoPath in project.DownloadedVideoPaths)
        //    {
        //        concatContent.AppendLine($"file '{Path.GetFullPath(videoPath)}'");
        //    }
        //    await File.WriteAllTextAsync(concatFilePath, concatContent.ToString(), ct);

        //    // Step 2: Build the FFmpeg command
        //    var ffmpegArgs = BuildFfmpegArgs(project, concatFilePath, outputPath, width, height);

        //    _logger.LogInformation("Running FFmpeg to compose video: {Args}", ffmpegArgs);

        //    var psi = new ProcessStartInfo
        //    {
        //        FileName = _settings.FfmpegPath,
        //        Arguments = ffmpegArgs,
        //        RedirectStandardOutput = true,
        //        RedirectStandardError = true,
        //        UseShellExecute = false,
        //        CreateNoWindow = true
        //    };

        //    using var process = Process.Start(psi)
        //        ?? throw new InvalidOperationException("Failed to start FFmpeg process");

        //    await process.WaitForExitAsync(ct);

        //    if (process.ExitCode != 0)
        //    {
        //        var error = await process.StandardError.ReadToEndAsync(ct);
        //        _logger.LogError("FFmpeg failed: {Error}", error);
        //        throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
        //    }

        //    _logger.LogInformation("Video composed at {Path}", outputPath);
        //    return outputPath;
        //}

        //private string BuildFfmpegArgs(
        //    VideoProject project,
        //    string concatFilePath,
        //    string outputPath,
        //    int width,
        //    int height)
        //{
        //    var sb = new StringBuilder();
        //    sb.Append($"-hide_banner -progress {Path.Combine(project.TaskDir, "prog.txt")} -report ");

        //    // Input: concatenated video clips
        //    sb.Append($"-f concat -safe 0 -i \"{concatFilePath}\" ");

        //    // Input: audio voiceover
        //    if (!string.IsNullOrEmpty(project.AudioFilePath))
        //    {
        //        sb.Append($"-i \"{project.AudioFilePath}\" ");
        //    }

        //    // Input: background music
        //    var bgmFile = ResolveBgmFile(project);
        //    if (!string.IsNullOrEmpty(bgmFile))
        //    {
        //        sb.Append($"-i \"{bgmFile}\" ");
        //    }

        //    // Video filter: scale + pad to target resolution
        //    var filters = new List<string>
        //    {
        //        $"scale={width}:{height}:force_original_aspect_ratio=decrease",
        //        $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
        //    };

        //    // Subtitle filter
        //    if (project.Params.SubtitleEnabled && !string.IsNullOrEmpty(project.SubtitleFilePath))
        //    {
        //        var fontName = project.Params.FontName;
        //        var fontSize = project.Params.FontSize;
        //        var fontColor = project.Params.TextForeColor.Replace("#", "&H00");
        //        filters.Add($"subtitles='{EscapeFilterPath(project.SubtitleFilePath)}':force_style='FontName={fontName},FontSize={fontSize},PrimaryColour={fontColor}'");
        //    }

        //    sb.Append($"-vf \"{string.Join(",", filters)}\" ");

        //    // Audio mixing
        //    if (!string.IsNullOrEmpty(project.AudioFilePath) && !string.IsNullOrEmpty(bgmFile))
        //    {
        //        var bgmVol = project.Params.BgmVolume;
        //        var voiceVol = project.Params.VoiceVolume;
        //        sb.Append($"-filter_complex \"[1:a]volume={voiceVol}[voice];[2:a]volume={bgmVol}[bgm];[voice][bgm]amix=inputs=2:duration=first[aout]\" -map 0:v -map \"[aout]\" ");
        //    }
        //    else if (!string.IsNullOrEmpty(project.AudioFilePath))
        //    {
        //        sb.Append("-map 0:v -map 1:a ");
        //    }

        //    // Output settings
        //    sb.Append("-c:v libx264 -preset medium -crf 23 ");
        //    sb.Append("-c:a aac -b:a 192k ");
        //    sb.Append($"-shortest -y \"{outputPath}\"");

        //    return sb.ToString();
        //}

        private string? ResolveBgmFile(VideoProject project)
        {
            if (project.Params.BgmType == "none")
                return null;

            if (!string.IsNullOrEmpty(project.Params.BgmFile) && File.Exists(project.Params.BgmFile))
                return project.Params.BgmFile;

            // Random BGM from songs directory
            if (Directory.Exists(_settings.SongsPath))
            {
                var songs = Directory.GetFiles(_settings.SongsPath, "*.mp3");
                if (songs.Length > 0)
                    return songs[Random.Shared.Next(songs.Length)];
            }

            return null;
        }

        //private static string EscapeFilterPath(string path)
        //{
        //    return path.Replace("\\", "/").Replace(":", "\\:");
        //}
    }

    */
}
