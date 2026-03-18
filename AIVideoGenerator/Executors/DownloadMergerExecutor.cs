using AIVideoGenerator.Core.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Fan-in executor that merges download results from all parallel download workers.
    /// Runs after the fan-in barrier ensures all workers have completed.
    /// Reads each worker's results from shared state and populates VideoProject.DownloadedVideoPaths.
    ///
    /// Input:  VideoProject (one per download worker, via fan-in barrier)
    /// Output: VideoProject (with DownloadedVideoPaths merged from all workers)
    /// </summary>
    public sealed class DownloadMergerExecutor : Executor<VideoProject,VideoProject>
    {
        private int _receivedCount;

        public DownloadMergerExecutor()
            : base("DownloadMerger")
        {
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // Fan-in barrier delivers one message per source worker.
            // Only merge and forward on the last message.
            var count = Interlocked.Increment(ref _receivedCount);
            if (count < MaterialSearchExecutor.WorkerCount)
                return null;

            // Reset counter for potential reuse
            Interlocked.Exchange(ref _receivedCount, 0);

            // Merge download results from all workers
            var allPaths = new List<string>();
            for (int i = 0; i < MaterialSearchExecutor.WorkerCount; i++)
            {
                var paths = await context.ReadStateAsync<List<string>>(
                    $"result_{i}", MaterialSearchExecutor.StateScopeName, ct);
                if (paths is not null)
                    allPaths.AddRange(paths);
            }

            project.DownloadedVideoPaths = allPaths;
            
            return project;
        }
    }
}
