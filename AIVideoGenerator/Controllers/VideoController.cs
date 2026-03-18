using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Models;
using AIVideoGenerator.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIVideoGenerator.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class VideoController : ControllerBase
    {
        private readonly VideoWorkflowService _workflowService;
        private readonly ILogger<VideoController> _logger;

        public VideoController(
            VideoWorkflowService workflowService,
            ILogger<VideoController> logger)
        {
            _workflowService = workflowService;
            _logger = logger;
        }

        /// <summary>
        /// Generate a video from a topic/subject using the AI video generation pipeline.
        /// </summary>
        [HttpPost("generate")]
        public async Task<ActionResult<VideoGenerationResponse>> GenerateVideo(
            [FromBody] VideoParams request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.VideoSubject))
            {
                return BadRequest(new VideoGenerationResponse
                {
                    Status = "error",
                    Message = "Either video_subject or video_script must be provided."
                });
            }

            try
            {
                _logger.LogInformation("Received video generation request for: {Subject}", request.VideoSubject);

                var result = await _workflowService.GenerateVideoAsync(request, ct);

                if (result is null)
                {
                    return StatusCode(500, new VideoGenerationResponse
                    {
                        Status = "failed",
                        Message = "Video generation workflow produced no output."
                    });
                }

                return Ok(new VideoGenerationResponse
                {
                    TaskId = result.TaskId,
                    Status = "completed",
                    VideoPath = result.OutputVideoPath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video generation failed");
                return StatusCode(500, new VideoGenerationResponse
                {
                    Status = "failed",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Generate a video with streaming progress updates via Server-Sent Events.
        /// Emits TaskStarted (with task_id), per-executor progress events, and TaskCompleted.
        /// </summary>
        [HttpPost("generate/stream")]
        public async Task GenerateVideoStream(
            [FromBody] VideoParams request,
            CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            var project = new VideoProject { Params = request };

            // Send initial event so the client knows the task ID immediately
            await WriteSseEventAsync(new { type = "TaskStarted", task_id = project.TaskId }, ct);

            try
            {
                await foreach (var evt in _workflowService.GenerateVideoStreamingAsync(project, ct))
                {
                    string executorId = evt switch
                    {
                        ExecutorEvent ee => ee.ExecutorId,
                        WorkflowOutputEvent oe => oe.ExecutorId,
                        _ => "workflow"
                    };

                    if (evt is WorkflowOutputEvent)
                    {
                        await WriteSseEventAsync(new { type = "TaskCompleted", executor = executorId, task_id = project.TaskId }, ct);
                    }
                    else
                    {
                        await WriteSseEventAsync(new { type = evt.GetType().Name, executor = executorId }, ct);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await WriteSseEventAsync(new { type = "TaskFailed", message = ex.Message }, ct);
            }
        }

        /// <summary>
        /// Serve the generated video file for playback / download.
        /// </summary>
        [HttpGet("tasks/{taskId}/video")]
        public IActionResult GetVideo(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId) || taskId.Contains("..") || taskId.Contains('/') || taskId.Contains('\\'))
                return BadRequest();

            var path = Path.Combine("storage", "tasks", taskId, "final_video.mp4");
            if (!System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(Path.GetFullPath(path), "video/mp4", enableRangeProcessing: true);
        }

        private async Task WriteSseEventAsync(object data, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(data);
            await Response.WriteAsync($"data: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
