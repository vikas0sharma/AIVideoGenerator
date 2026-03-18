namespace AIVideoGenerator.Core.Services
{
    /// <summary>
    /// Interacts with a large language model to generate scripts and search terms.
    /// </summary>
    public interface ILlmService
    {
        /// <summary>
        /// Generate a video script from a subject/topic.
        /// </summary>
        Task<string> GenerateScriptAsync(string subject, string language, int paragraphCount, CancellationToken ct = default);

        /// <summary>
        /// Generate search terms (keywords) from a video script for finding stock footage.
        /// </summary>
        Task<List<string>> GenerateTermsAsync(string script, int count, CancellationToken ct = default);
    }
}
