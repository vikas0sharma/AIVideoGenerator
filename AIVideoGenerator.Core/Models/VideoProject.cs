using AIVideoGenerator.Core.Enums;

namespace AIVideoGenerator.Core.Models
{
    /// <summary>
    /// Accumulated state that flows through the video generation workflow pipeline.
    /// Each executor enriches this object with its output.
    /// </summary>
    public class VideoProject
    {
        // ── Input ──
        public VideoParams Params { get; set; } = new();

        // ── Script generation ──
        public string Script { get; set; } = "";
        public List<string> ScriptParagraphs { get; set; } = [];

        // ── Search terms ──
        public List<string> SearchTerms { get; set; } = [];

        // ── Materials ──
        public List<MaterialInfo> Materials { get; set; } = [];
        public List<string> DownloadedVideoPaths { get; set; } = [];

        // ── Audio ──
        public string AudioFilePath { get; set; } = "";
        public double AudioDurationSeconds { get; set; }

        // ── Subtitles ──
        public string SubtitleFilePath { get; set; } = "";
        public List<SubtitleItem> Subtitles { get; set; } = [];

        // ── Final output ──
        public string OutputVideoPath { get; set; } = "";

        // ── Task tracking ──
        public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
        public string TaskDir => Path.Combine("storage", "tasks", TaskId);
    }
}
