using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Downloads a partition of video materials in parallel.
    /// Multiple instances run concurrently via the fan-out pattern,
    /// each handling a different partition stored in workflow shared state.
    ///
    /// Input:  VideoProject (forwarded from MaterialSearchExecutor)
    /// Output: VideoProject (download results stored in shared state)
    /// </summary>
    public sealed class VideoDownloadWorker : Executor<VideoProject, VideoProject>
    {
        private readonly int _workerIndex;
        private readonly IMaterialService _materialService;

        public VideoDownloadWorker(int workerIndex, IMaterialService materialService)
            : base($"DownloadWorker_{workerIndex}")
        {
            _workerIndex = workerIndex;
            _materialService = materialService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // Read this worker's partition from shared state
            var partition = await context.ReadStateAsync<List<MaterialInfo>>(
                $"partition_{_workerIndex}", MaterialSearchExecutor.StateScopeName, ct) ?? [];

            // Download all materials in this partition concurrently
            Directory.CreateDirectory(Path.Combine(project.TaskDir, "videos"));
            var downloadTasks = partition.Select(m =>
                _materialService.DownloadVideoAsync(
                    m.Url,
                    Path.Combine(project.TaskDir, "videos"),
                    ct));
            var paths = await Task.WhenAll(downloadTasks);
            var downloadedPaths = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();

            // Store this worker's results in shared state for the merger
            await context.QueueStateUpdateAsync(
                $"result_{_workerIndex}", downloadedPaths,
                MaterialSearchExecutor.StateScopeName, ct);

            return project;
        }
    }
}
