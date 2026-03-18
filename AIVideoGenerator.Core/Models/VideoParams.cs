using AIVideoGenerator.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AIVideoGenerator.Core.Models
{
    public class VideoParams
    {
        [JsonPropertyName("video_subject")]
        public string VideoSubject { get; set; } = "";

        [JsonPropertyName("video_script")]
        public string VideoScript { get; set; } = "";

        [JsonPropertyName("video_terms")]
        public List<string>? VideoTerms { get; set; }

        [JsonPropertyName("video_aspect")]
        public VideoAspect VideoAspect { get; set; } = VideoAspect.Portrait;

        [JsonPropertyName("video_concat_mode")]
        public VideoConcatMode VideoConcatMode { get; set; } = VideoConcatMode.Random;

        [JsonPropertyName("video_transition_mode")]
        public VideoTransitionMode VideoTransitionMode { get; set; } = VideoTransitionMode.None;

        [JsonPropertyName("video_clip_duration")]
        public int VideoClipDuration { get; set; } = 5;

        [JsonPropertyName("video_count")]
        public int VideoCount { get; set; } = 1;

        [JsonPropertyName("video_source")]
        public string VideoSource { get; set; } = "pexels";

        [JsonPropertyName("video_materials")]
        public List<MaterialInfo>? VideoMaterials { get; set; }

        [JsonPropertyName("custom_audio_file")]
        public string? CustomAudioFile { get; set; }

        [JsonPropertyName("video_language")]
        public string VideoLanguage { get; set; } = "";

        [JsonPropertyName("voice_name")]
        public string VoiceName { get; set; } = "";

        [JsonPropertyName("voice_volume")]
        public float VoiceVolume { get; set; } = 1.0f;

        [JsonPropertyName("voice_rate")]
        public float VoiceRate { get; set; } = 1.0f;

        [JsonPropertyName("bgm_type")]
        public string BgmType { get; set; } = "random";

        [JsonPropertyName("bgm_file")]
        public string BgmFile { get; set; } = "";

        [JsonPropertyName("bgm_volume")]
        public float BgmVolume { get; set; } = 0.2f;

        [JsonPropertyName("subtitle_enabled")]
        public bool SubtitleEnabled { get; set; } = true;

        [JsonPropertyName("subtitle_position")]
        public string SubtitlePosition { get; set; } = "bottom";

        [JsonPropertyName("font_name")]
        public string FontName { get; set; } = "STHeitiMedium.ttc";

        [JsonPropertyName("text_fore_color")]
        public string TextForeColor { get; set; } = "#FFFFFF";

        [JsonPropertyName("font_size")]
        public int FontSize { get; set; } = 60;

        [JsonPropertyName("stroke_color")]
        public string StrokeColor { get; set; } = "#000000";

        [JsonPropertyName("stroke_width")]
        public float StrokeWidth { get; set; } = 1.5f;

        [JsonPropertyName("n_threads")]
        public int NThreads { get; set; } = 2;

        [JsonPropertyName("paragraph_number")]
        public int ParagraphNumber { get; set; } = 1;
    }
}
