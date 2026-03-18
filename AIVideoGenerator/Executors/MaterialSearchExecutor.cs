using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Searches for stock video materials from external providers (e.g. Pexels).
    /// Partitions the found materials across parallel download workers and stores
    /// the partition assignments in workflow shared state.
    ///
    /// Input:  VideoProject (with SearchTerms populated)
    /// Output: VideoProject (with Materials populated; partitions stored in state)
    /// </summary>
    public sealed class MaterialSearchExecutor : Executor<VideoProject, VideoProject>
    {
        public const int WorkerCount = 3;
        public const string StateScopeName = "Downloads";

        private readonly IMaterialService _materialService;

        public MaterialSearchExecutor(IMaterialService materialService)
            : base("MaterialSearch")
        {
            _materialService = materialService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // Use user-provided materials or search Pexels
            if (project.Params.VideoMaterials?.Count > 0)
            {
                project.Materials = project.Params.VideoMaterials;
            }
            else
            {
                project.Materials = await _materialService.SearchVideosAsync(
                    project.SearchTerms,
                    project.Params.VideoAspect,
                    project.Params.VideoClipDuration,
                    ct);
            }

            // Partition materials round-robin across download workers
            for (int i = 0; i < WorkerCount; i++)
            {
                var partition = project.Materials
                    .Where((_, idx) => idx % WorkerCount == i)
                    .ToList();

                await context.QueueStateUpdateAsync(
                    $"partition_{i}", partition, StateScopeName, ct);
            }

            // Store the project in state for the merger to retrieve
            await context.QueueStateUpdateAsync("project", project, StateScopeName, ct);

            return project;
        }
    }
}
