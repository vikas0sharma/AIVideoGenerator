using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Models;
using AIVideoGenerator.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Mvc;

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
        /// </summary>
        [HttpPost("generate/stream")]
        public async Task GenerateVideoStream(
            [FromBody] VideoParams request,
            CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";

            await foreach (var evt in _workflowService.GenerateVideoStreamingAsync(request, ct))
            {
                string executorId = evt switch
                {
                    ExecutorEvent ee => ee.ExecutorId,
                    WorkflowOutputEvent oe => oe.ExecutorId,
                    _ => "workflow"
                };
                var data = $"data: {{\"executor\": \"{executorId}\", \"type\": \"{evt.GetType().Name}\"}}\n\n";
                await Response.WriteAsync(data, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
    }
}
