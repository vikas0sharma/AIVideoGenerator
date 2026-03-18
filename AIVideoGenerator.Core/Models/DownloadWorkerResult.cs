namespace AIVideoGenerator.Core.Models
{
    public class DownloadWorkerResult
    {
        public int Id { get; set; }
        public List<string> Paths { get; set; } = [];
    }
}
