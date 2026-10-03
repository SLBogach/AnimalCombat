using System.IO;

namespace AnimalCombat.ReplayViewer.Runtime.Loading
{
    public interface IReplaySource
    {
        string ReadAllText(string replayName);
    }

    public sealed class DirectoryReplaySource : IReplaySource
    {
        readonly string rootDirectory;

        public DirectoryReplaySource(string rootDirectory)
        {
            this.rootDirectory = rootDirectory;
        }

        public string ReadAllText(string replayName)
        {
            if (string.IsNullOrWhiteSpace(replayName) || Path.GetFileName(replayName) != replayName)
                throw new IOException("Replay name must be a file name without directory segments.");

            return File.ReadAllText(Path.Combine(rootDirectory, replayName));
        }
    }
}
