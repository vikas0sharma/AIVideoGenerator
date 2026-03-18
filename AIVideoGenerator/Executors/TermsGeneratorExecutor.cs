using AIVideoGenerator.Core.Models;
using AIVideoGenerator.Core.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace AIVideoGenerator.Executors
{
    /// <summary>
    /// Uses an LLM to generate search terms/keywords from the video script
    /// for finding relevant stock footage.
    /// Input:  VideoProject (with Script populated)
    /// Output: VideoProject (with SearchTerms populated)
    /// </summary>
    public sealed class TermsGeneratorExecutor : Executor<VideoProject, VideoProject>
    {
        private readonly ILlmService _llmService;

        public TermsGeneratorExecutor(ILlmService llmService)
            : base("TermsGenerator")
        {
            _llmService = llmService;
        }

        public override async ValueTask<VideoProject> HandleAsync(VideoProject project, IWorkflowContext context, CancellationToken ct = default)
        {
            // If the user already provided terms, use them
            if (project.Params.VideoTerms?.Count > 0)
            {
                project.SearchTerms = project.Params.VideoTerms;
            }
            else
            {
                project.SearchTerms = await _llmService.GenerateTermsAsync(
                    project.Script,
                    count: 5,
                    ct);
            }

            return project;
        }
    }
}
