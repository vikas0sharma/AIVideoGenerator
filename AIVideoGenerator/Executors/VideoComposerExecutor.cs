using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Final step: composes the video from clips, audio, subtitles, and background music.
    /// Input:  VideoProject (fully populated)
    /// Output: Yields the final VideoProject with OutputVideoPath set.
    /// </summary>
    public sealed class VideoComposerExecutor : Executor<VideoProject, VideoProject>
    {
        private readonly IVideoComposerService _composerService;

        public VideoComposerExecutor(IVideoComposerService composerService)
            : base("VideoComposer", ExecutorOptions.Default)
        {
            _composerService = composerService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            project.OutputVideoPath = await _composerService.ComposeVideoAsync(project, ct);
            await context.YieldOutputAsync(project, ct);
            return project;
        }
    }
}
