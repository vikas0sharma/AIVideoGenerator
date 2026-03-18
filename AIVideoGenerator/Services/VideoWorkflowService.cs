using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Executors;
using Microsoft.Agents.AI.Workflows;

namespace AIVideoGenerator.Services
{
    /// <summary>
    /// Builds and manages the video generation workflow using the Microsoft Agent Framework.
    ///
    /// Pipeline:
    ///   ScriptGenerator → TermsGenerator → MaterialSearch
    ///     → Fan-out [DownloadWorker_0, DownloadWorker_1, DownloadWorker_2]  (parallel downloads)
    ///     → Fan-in barrier → DownloadMerger
    ///     → AudioGenerator → SubtitleGenerator → VideoComposer → Output
    /// </summary>
    public class VideoWorkflowService
    {
        private readonly Workflow _workflow;
        private readonly ILogger<VideoWorkflowService> _logger;

        public VideoWorkflowService(
            ScriptGeneratorExecutor scriptGenerator,
            TermsGeneratorExecutor termsGenerator,
            MaterialSearchExecutor materialSearch,
            VideoDownloadWorker[] downloadWorkers,
            DownloadMergerExecutor downloadMerger,
            AudioGeneratorExecutor audioGenerator,
            SubtitleGeneratorExecutor subtitleGenerator,
            VideoComposerExecutor videoComposer,
            ILogger<VideoWorkflowService> logger)
        {
            _logger = logger;

            // Build the video generation pipeline with fan-out for parallel downloads
            var builder = new WorkflowBuilder(scriptGenerator);

            // Sequential: script → terms → search
            builder.AddEdge(scriptGenerator, termsGenerator);
            builder.AddEdge(termsGenerator, materialSearch);
            //builder.AddEdge(materialSearch, downloadMerger); // Forward to merger for fan-in barrier
            //// Fan-out: materialSearch → [worker0, worker1, worker2] (all receive the same VideoProject)
            //var workerBindings = downloadWorkers.Select(w => new ExecutorInstanceBinding(w)).ToArray();
            builder.AddFanOutEdge(materialSearch, [..downloadWorkers]);

            //// Fan-in barrier: wait for all workers, then merge results
            builder.AddFanInBarrierEdge([.. downloadWorkers], downloadMerger);

            // Sequential: merger → audio → subtitles → compose
            builder.AddEdge(downloadMerger, audioGenerator);
            builder.AddEdge(audioGenerator, videoComposer);
            //builder.AddEdge(subtitleGenerator, videoComposer);

            builder.WithOutputFrom(videoComposer);

            _workflow = builder.Build();
        }

        /// <summary>
        /// Run the video generation workflow end-to-end.
        /// </summary>
        public async Task<VideoProject?> GenerateVideoAsync(VideoParams videoParams, CancellationToken ct = default)
        {
            var project = new VideoProject { Params = videoParams };
            Directory.CreateDirectory(project.TaskDir);

            _logger.LogInformation("Starting video generation workflow for task {TaskId}", project.TaskId);

            Run result = await InProcessExecution.RunAsync(_workflow, project, cancellationToken: ct);

            VideoProject? output = null;
            foreach (var evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvt && outputEvt.Data is VideoProject vp)
                {
                    output = vp;
                }
            }

            if (output is not null)
            {
                _logger.LogInformation("Video generation completed: {Path}", output.OutputVideoPath);
            }
            else
            {
                _logger.LogWarning("Video generation workflow produced no output");
            }

            return output;
        }

        /// <summary>
        /// Run the workflow with streaming events for real-time progress.
        /// </summary>
        public async IAsyncEnumerable<WorkflowEvent> GenerateVideoStreamingAsync(
            VideoParams videoParams,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var project = new VideoProject { Params = videoParams };
            Directory.CreateDirectory(project.TaskDir);

            _logger.LogInformation("Starting streaming video generation for task {TaskId}", project.TaskId);

            StreamingRun run = await InProcessExecution.RunStreamingAsync(_workflow, project, cancellationToken: ct);

            await foreach (var evt in run.WatchStreamAsync(ct))
            {
                yield return evt;
            }
        }
    }
}
