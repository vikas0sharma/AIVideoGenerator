using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Generates subtitles from the audio and script, then writes an SRT file.
    /// Input:  VideoProject (with AudioFilePath and Script populated)
    /// Output: VideoProject (with SubtitleFilePath and Subtitles populated)
    /// </summary>
    public sealed class SubtitleGeneratorExecutor : Executor<VideoProject, VideoProject>
    {
        private readonly ISubtitleService _subtitleService;

        public SubtitleGeneratorExecutor(ISubtitleService subtitleService)
            : base("SubtitleGenerator")
        {
            _subtitleService = subtitleService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            if (!project.Params.SubtitleEnabled)
            {
                return project;
            }

            project.Subtitles = await _subtitleService.GenerateSubtitlesAsync(
                project.AudioFilePath,
                project.Script,
                ct);

            project.SubtitleFilePath = await _subtitleService.WriteSrtFileAsync(
                project.Subtitles,
                project.TaskDir,
                ct);

            return project;
        }
    }
}
