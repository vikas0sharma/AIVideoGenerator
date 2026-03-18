using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Generates speech audio (voiceover) from the video script using TTS.
    /// Input:  VideoProject (with Script populated)
    /// Output: VideoProject (with AudioFilePath and AudioDurationSeconds populated)
    /// </summary>
    public sealed class AudioGeneratorExecutor : Executor<VideoProject, VideoProject>
    {
        private readonly IAudioService _audioService;

        public AudioGeneratorExecutor(IAudioService audioService)
            : base("AudioGenerator")
        {
            _audioService = audioService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // If user provided a custom audio file, use that
            if (!string.IsNullOrWhiteSpace(project.Params.CustomAudioFile))
            {
                project.AudioFilePath = project.Params.CustomAudioFile;
            }
            else
            {
                var voiceName = string.IsNullOrWhiteSpace(project.Params.VoiceName)
                    ? "en-US-AriaNeural"
                    : project.Params.VoiceName;

                project.AudioFilePath = await _audioService.GenerateSpeechAsync(
                    project.Script,
                    voiceName,
                    project.TaskDir,
                    project.Params.VoiceRate,
                    ct);

                project.SubtitleFilePath = project.AudioFilePath.Replace("voiceover.mp3", "subtitles.vtt");
            }

            project.AudioDurationSeconds = await _audioService.GetAudioDurationAsync(
                project.AudioFilePath, ct);

            return project;
        }
    }
}
