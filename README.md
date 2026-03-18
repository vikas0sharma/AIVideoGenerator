# AIVideoGenerator 🎬

An AI-powered short video generator built with **.NET 10** and the **Microsoft Agent Framework**. Provide a topic or keyword and the pipeline automatically generates a script, finds royalty-free stock footage, synthesises a voiceover, adds subtitles and background music, applies transitions, and outputs a finished HD video.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)

<img width="1886" height="905" alt="Image" src="https://github.com/user-attachments/assets/61dc702a-5b13-44d0-b153-1f13e7e012bc" />

<img width="1891" height="924" alt="Image" src="https://github.com/user-attachments/assets/c8f8951b-3f50-4ddb-bd9f-460fe66cb157" />

https://github.com/user-attachments/assets/8cd8bbfe-9004-49b4-99cd-8786e5fdd46b

## Features ✨

- **End-to-end automation** – from topic to finished video in a single API call
- **AI script generation** – LLM-powered narration scripts (Ollama / Azure OpenAI)
- **Royalty-free footage** – automatic search & download from [Pexels](https://www.pexels.com/)
- **Text-to-speech** – edge-tts voice synthesis with 13+ voices across multiple languages
- **Subtitle generation** – auto-timed subtitles with configurable font, size, colour and stroke
- **Background music** – random or custom BGM with adjustable volume and fade-out
- **Video transitions** – `Fade`, `FadeBlack`, `SlideLeft`, `SlideRight`, `Shuffle` (random per cut) via FFmpeg xfade
- **Multiple aspect ratios** – Portrait 9:16 (`1080×1920`), Landscape 16:9 (`1920×1080`), Square 1:1 (`1080×1080`)
- **Parallel downloads** – fan-out/fan-in workflow for concurrent material fetching
- **Streaming progress** – Server-Sent Events for real-time pipeline status
- **Web UI** – built-in dark-themed SPA at the root URL
- **REST API** – OpenAPI 3.1 documentation via [Scalar](https://scalar.com/)
- **Workflow engine** – orchestrated with `Microsoft.Agents.AI.Workflows`

## Architecture 🏗️

```
AIVideoGenerator.sln
├── AIVideoGenerator/           ASP.NET Core Web API + WebUI
│   ├── Controllers/            REST endpoints
│   ├── Executors/              Workflow step implementations
│   ├── Services/               Core service implementations
│   ├── Configuration/          Settings / options
│   ├── Models/                 API response models
│   └── wwwroot/                Static WebUI (index.html)
└── AIVideoGenerator.Core/      Shared interfaces, models & enums
    ├── Enums/                  VideoAspect, VideoConcatMode, VideoTransitionMode
    ├── Models/                 VideoParams, VideoProject, MaterialInfo, SubtitleItem
    └── Services/               ILlmService, IAudioService, IMaterialService, …
```

### Pipeline

```
ScriptGenerator → TermsGenerator → MaterialSearch
  → Fan-out [DownloadWorker × 3]  (parallel)
  → Fan-in barrier → DownloadMerger
  → AudioGenerator → SubtitleGenerator → VideoComposer → Output
```

## Prerequisites 📋

| Dependency | Version | Notes |
|---|---|---|
| [.NET SDK](https://dot.net/download) | **10.0** | Required |
| [FFmpeg](https://ffmpeg.org/download.html) | 6.x+ | Must include `ffmpeg` and `ffprobe` |
| [edge-tts](https://pypi.org/project/edge-tts/) | latest | `pip install edge-tts` |
| [Pexels API key](https://www.pexels.com/api/) | — | Free tier is sufficient |
| LLM provider | — | Ollama (default) or Azure OpenAI |

## Quick Start 🚀

### 1. Clone the repository

```bash
git clone https://github.com/vikas0sharma/AIVideoGenerator.git
cd AIVideoGenerator
```

### 2. Configure settings

Copy the default configuration and update it with your keys:

**appsettings.json** (or use [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)):

```jsonc
{
  "VideoGenerator": {
    "PexelsApiKey": "YOUR_PEXELS_API_KEY",
    "FfmpegPath": "C:\\ffmpeg\\bin",
    "DefaultVoiceName": "en-US-AriaNeural",
    "DefaultLanguage": "en",
    "StoragePath": "storage",
    "FontsPath": "resource/fonts",
    "SongsPath": "resource/songs"
  }
}
```

Or with .NET User Secrets:

```bash
cd AIVideoGenerator
dotnet user-secrets set "VideoGenerator:PexelsApiKey" "YOUR_KEY"
dotnet user-secrets set "OllamaApiKey" "YOUR_OLLAMA_KEY"
```

### 3. Install external tools

```bash
# FFmpeg – download from https://ffmpeg.org/download.html and add to PATH
# or on Windows, extract to C:\ffmpeg\bin

# edge-tts (Python)
pip install edge-tts
```

### 4. Add background music (optional)

Place `.mp3` files in the `resource/songs` directory for random background music selection.

### 5. Run the application

```bash
dotnet run --project AIVideoGenerator
```

The application starts on **http://localhost:5005** by default.

| URL | Description |
|---|---|
| `http://localhost:5005` | **Web UI** |
| `http://localhost:5005/scalar/v1` | **API documentation** (Scalar) |
| `http://localhost:5005/openapi/v1.json` | OpenAPI 3.1 spec |

## Usage 📖

### Web UI

Open `http://localhost:5005` in your browser. The built-in UI provides controls for all video parameters including topic, aspect ratio, voice, transitions, subtitles, and background music. Progress is streamed in real-time as each pipeline step completes.

### REST API

#### Generate a video

```bash
curl -X POST http://localhost:5005/api/v1/video/generate \
  -H "Content-Type: application/json" \
  -d '{
    "video_subject": "Why exercise is important",
    "video_aspect": 1,
    "video_transition_mode": 2,
    "voice_name": "en-US-AriaNeural",
    "subtitle_enabled": true,
    "bgm_type": "random",
    "bgm_volume": 0.2
  }'
```

**Response:**

```json
{
  "task_id": "a1b2c3d4...",
  "status": "completed",
  "video_path": "storage/tasks/a1b2c3d4.../final_video.mp4"
}
```

#### Generate with streaming progress (SSE)

```bash
curl -X POST http://localhost:5005/api/v1/video/generate/stream \
  -H "Content-Type: application/json" \
  -d '{ "video_subject": "Benefits of reading" }'
```

Returns Server-Sent Events with per-step progress:

```
data: {"type":"TaskStarted","task_id":"abc123"}
data: {"type":"ExecutorEvent","executor":"ScriptGenerator"}
data: {"type":"ExecutorEvent","executor":"TermsGenerator"}
...
data: {"type":"TaskCompleted","executor":"VideoComposer","task_id":"abc123"}
```

#### Download the generated video

```
GET /api/v1/video/tasks/{taskId}/video
```

### Video Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `video_subject` | `string` | — | Topic for AI script generation *(required)* |
| `video_script` | `string` | `""` | Custom script (skips AI generation if provided) |
| `video_language` | `string` | `""` | Language code (`en`, `zh`, `es`, `fr`, …) |
| `video_aspect` | `int` | `0` | `0` Portrait · `1` Landscape · `2` Square |
| `video_concat_mode` | `int` | `0` | `0` Random · `1` Sequential |
| `video_transition_mode` | `int` | `0` | `0` None · `1` Shuffle · `2` FadeIn · `3` FadeOut · `4` SlideIn · `5` SlideOut |
| `video_clip_duration` | `int` | `5` | Max seconds per clip |
| `video_count` | `int` | `1` | Number of videos to generate |
| `voice_name` | `string` | `""` | edge-tts voice (e.g. `en-US-AriaNeural`) |
| `voice_volume` | `float` | `1.0` | Voiceover volume multiplier |
| `voice_rate` | `float` | `1.0` | Speech rate multiplier |
| `bgm_type` | `string` | `"random"` | `random` · `none` |
| `bgm_volume` | `float` | `0.2` | Background music volume |
| `subtitle_enabled` | `bool` | `true` | Enable subtitle overlay |
| `subtitle_position` | `string` | `"bottom"` | `bottom` · `top` · `center` |
| `font_size` | `int` | `60` | Subtitle font size |
| `text_fore_color` | `string` | `"#FFFFFF"` | Subtitle text colour (CSS hex) |
| `stroke_color` | `string` | `"#000000"` | Subtitle stroke colour |
| `stroke_width` | `float` | `1.5` | Subtitle stroke width |
| `paragraph_number` | `int` | `1` | Number of script paragraphs |

## Video Transitions 🎞️

Transitions are applied between clips using FFmpeg's `xfade` filter. Set `video_transition_mode` in your request:

| Mode | Enum Value | FFmpeg Effect |
|---|---|---|
| None | `0` | Simple concatenation (fastest) |
| Shuffle | `1` | Random transition per cut (fade, slideLeft, wipe, circleOpen, …) |
| Fade In | `2` | `fade` |
| Fade Out | `3` | `fadeblack` |
| Slide In | `4` | `slideleft` |
| Slide Out | `5` | `slideright` |

Transition duration is **0.5 seconds** between each clip boundary.

## Project Configuration ⚙️

All settings are in the `VideoGenerator` section of `appsettings.json`:

```jsonc
{
  "VideoGenerator": {
    // Azure OpenAI (uncomment in Program.cs to use instead of Ollama)
    "AzureOpenAIEndpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "AzureOpenAIDeployment": "gpt-4o-mini",

    // Pexels stock footage
    "PexelsApiKey": "YOUR_API_KEY",

    // Voice defaults
    "DefaultVoiceName": "en-US-AriaNeural",
    "DefaultLanguage": "en",

    // Paths
    "FfmpegPath": "C:\\ffmpeg\\bin",
    "StoragePath": "storage",
    "FontsPath": "resource/fonts",
    "SongsPath": "resource/songs"
  }
}
```

Sensitive keys (API keys) should be stored in [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or environment variables for non-development environments.

## Tech Stack 🛠️

| Component | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core |
| Workflow Engine | Microsoft.Agents.AI.Workflows |
| LLM Integration | Microsoft.Extensions.AI + OllamaSharp |
| Video Processing | Xabe.FFmpeg (FFmpeg wrapper) |
| Text-to-Speech | edge-tts (CLI) |
| Stock Footage | Pexels API |
| API Docs | Scalar (OpenAPI 3.1) |
| Frontend | Vanilla HTML/CSS/JS (SPA) |

## License 📝

This project is licensed under the [MIT License](LICENSE).
