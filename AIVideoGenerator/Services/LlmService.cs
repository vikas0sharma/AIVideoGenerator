using System.Text.Json;
using AIVideoGenerator.Core.Services;
using Microsoft.Extensions.AI;

namespace AIVideoGenerator.Services
{
    /// <summary>
    /// LLM service implementation using Azure OpenAI via Microsoft.Extensions.AI.
    /// </summary>
    public class LlmService : ILlmService
    {
        private readonly IChatClient _chatClient;
        private readonly ILogger<LlmService> _logger;

        public LlmService(IChatClient chatClient, ILogger<LlmService> logger)
        {
            _chatClient = chatClient;
            _logger = logger;
        }

        public async Task<string> GenerateScriptAsync(string subject, string language, int paragraphCount, CancellationToken ct = default)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? "English" : language;

            var prompt = $"""
                Generate a video script about the topic: "{subject}".
                The script should be written in {lang}.
                Generate exactly {paragraphCount} paragraph(s).
                Each paragraph should be 4-5 sentences suitable for narration in a short video.
                Output ONLY the script text, one paragraph per line. No titles, numbers, or formatting.
                """;

            _logger.LogInformation("Generating script for subject: {Subject}", subject);

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            return response.Text ?? "";
        }

        public async Task<List<string>> GenerateTermsAsync(string script, int count, CancellationToken ct = default)
        {
            var prompt = $"""
                Based on the following video script, generate {count} search terms/keywords
                that would be useful for finding relevant stock video footage on Pexels or similar sites.
                Return the terms as a JSON array of strings. Only output the JSON array, nothing else.

                Script:
                {script}
                """;

            _logger.LogInformation("Generating search terms from script");

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var text = response.Text?.Trim() ?? "[]";

            try
            {
                return JsonSerializer.Deserialize<List<string>>(text) ?? [];
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse LLM response as JSON array, falling back to line split");
                return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.Trim('"', ',', '[', ']', ' '))
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Take(count)
                    .ToList();
            }
        }
    }
}
