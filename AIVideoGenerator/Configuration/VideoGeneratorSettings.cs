namespace AIVideoGenerator.Configuration
{
    public class VideoGeneratorSettings
    {
        public const string SectionName = "VideoGenerator";

        // ── Azure OpenAI ──
        public string AzureOpenAIEndpoint { get; set; } = "";
        public string AzureOpenAIDeployment { get; set; } = "gpt-4o-mini";

        // ── Pexels ──
        public string PexelsApiKey { get; set; } = "";

        // ── Voice / TTS ──
        public string DefaultVoiceName { get; set; } = "en-US-AriaNeural";
        public string DefaultLanguage { get; set; } = "en";

        // ── Paths ──
        public string FfmpegPath { get; set; } = "ffmpeg";
        public string StoragePath { get; set; } = "storage";
        public string FontsPath { get; set; } = "resource/fonts";
        public string SongsPath { get; set; } = "resource/songs";
    }
}
