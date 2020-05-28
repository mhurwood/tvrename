using Alphaleonis.Win32.Filesystem;

namespace TVRename
{
    public class FileIssue
    {
        public readonly string Message;
        public int? SeasonNumber { get; }
        public int? EpisodeNumber { get; }
        public ShowItem Show { get; }
        public FileInfo File { get; }
        public string Showname => Show.ShowName;
        public string Filename => File.Name;
        public string Directory => File.DirectoryName;

        public FileIssue(ShowItem show, FileInfo file, string message)
        {
            Message = message;
            Show = show;
            File = file;
        }

        public FileIssue(ShowItem show, FileInfo file, string message, int seasonNumber, int episodeNumber) : this(show, file,message)
        {
            SeasonNumber = seasonNumber;
            EpisodeNumber = episodeNumber;
        }

        public FileIssue(ShowItem show, FileInfo file, string message, int seasonNumber) : this(show, file, message)
        {
            SeasonNumber = seasonNumber;
        }
    }
}