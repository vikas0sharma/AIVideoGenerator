using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Uses an LLM to generate a video script from the subject/topic provided in VideoParams.
    /// Input:  VideoProject (with Params.VideoSubject populated)
    /// Output: VideoProject (with Script and ScriptParagraphs populated)
    /// </summary>
    public sealed class ScriptGeneratorExecutor : Executor<VideoProject, VideoProject>
    {
        private readonly ILlmService _llmService;

        public ScriptGeneratorExecutor(ILlmService llmService)
            : base("ScriptGenerator")
        {
            _llmService = llmService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // If the user already provided a script, skip generation
            if (!string.IsNullOrWhiteSpace(project.Params.VideoScript))
            {
                project.Script = project.Params.VideoScript;
            }
            else
            {
                project.Script = await _llmService.GenerateScriptAsync(
                    project.Params.VideoSubject,
                    project.Params.VideoLanguage,
                    project.Params.ParagraphNumber,
                    ct);
            }

            project.ScriptParagraphs = project.Script
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            return project;
        }
    }
}
