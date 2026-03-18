using AIVideoGenerator.Configuration;
using AIVideoGenerator.Core.Services;
using AIVideoGenerator.Executors;
using AIVideoGenerator.Services;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──
builder.Services.Configure<VideoGeneratorSettings>(
    builder.Configuration.GetSection(VideoGeneratorSettings.SectionName));
builder.Services.AddHttpClient();

// ── Azure OpenAI / IChatClient ──
var settings = builder.Configuration
    .GetSection(VideoGeneratorSettings.SectionName)
    .Get<VideoGeneratorSettings>() ?? new VideoGeneratorSettings();

builder.Services.AddSingleton<IChatClient>(sp =>
{
    //var endpoint = settings.AzureOpenAIEndpoint;
    //var deployment = settings.AzureOpenAIDeployment;

    //var azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    //return azureClient.GetChatClient(deployment).AsIChatClient();
    IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
    HttpClient client = factory.CreateClient("Ollama");
    client.BaseAddress = new Uri("https://ollama.com/");
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", builder.Configuration["OllamaApiKey"]!);

    OllamaApiClient chatClient = new(client, "gpt-oss:20b");

    return chatClient;
});

// ── HttpClient ──
builder.Services.AddHttpClient();

// ── Core Services ──
builder.Services.AddSingleton<ILlmService, LlmService>();
builder.Services.AddSingleton<IMaterialService, PexelsMaterialService>();
builder.Services.AddSingleton<IAudioService, AudioService>();
builder.Services.AddSingleton<ISubtitleService, SubtitleService>();
builder.Services.AddSingleton<IVideoComposerService, VideoComposerService>();

// ── Workflow Executors ──
builder.Services.AddSingleton<ScriptGeneratorExecutor>();
builder.Services.AddSingleton<TermsGeneratorExecutor>();
builder.Services.AddSingleton<MaterialSearchExecutor>();
builder.Services.AddSingleton(sp =>
{
    var materialService = sp.GetRequiredService<IMaterialService>();
    return new VideoDownloadWorker[]
    {
        new(0, materialService),
        new(1, materialService),
        new(2, materialService)
    };
});
builder.Services.AddSingleton<DownloadMergerExecutor>();
builder.Services.AddSingleton<AudioGeneratorExecutor>();
builder.Services.AddSingleton<SubtitleGeneratorExecutor>();
builder.Services.AddSingleton<VideoComposerExecutor>();

// ── Workflow Orchestration ──
builder.Services.AddSingleton<VideoWorkflowService>();

// ── ASP.NET Core ──
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
