using System.Text.Json.Serialization;

namespace AIVideoGenerator.Models
{
    public class VideoGenerationResponse
    {
        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("video_path")]
        public string? VideoPath { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
