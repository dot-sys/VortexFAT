using System;
using System.Linq;

// Data models for file information
namespace Drives.Models
{
    // File or directory entry metadata
    public class FileEntry
    {
        // Name of file
        public string FileName { get; set; }

        // Complete path to file
        public string FullPath { get; set; }

        // Size in bytes
        public long FileSize { get; set; }

        // Human readable size
        public string FileSizeFormatted { get; set; }

        // Current file state
        public string Status { get; set; }

        // Creation timestamp
        public DateTime? CreationTime { get; set; }

        // Modification timestamp
        public DateTime? ModifiedTime { get; set; }

        // Access timestamp
        public DateTime? AccessedTime { get; set; }

        // File system attributes
        public string Attributes { get; set; }

        // First cluster location
        public long StartCluster { get; set; }

        // Unused cluster bytes
        public long SlackSpace { get; set; }

        // Deleted file flag
        public bool IsDeleted { get; set; }

        // Directory type flag
        public bool IsDirectory { get; set; }

        // Sequential cluster flag
        public bool UseContiguousClusters { get; set; }

        // Hidden attribute check
        public bool IsHidden => !string.IsNullOrEmpty(Attributes) && Attributes.ToUpper().Contains("HIDDEN");

        // Type as string
        public string Type => IsDirectory ? "Folder" : "File";

        // Size in kilobytes
        public string FileSizeKB
        {
            get
            {
                if (IsDirectory)
                    return string.Empty;

                double sizeInKB = FileSize / 1024.0;

                if (sizeInKB >= 1.0)
                {
                    return ((long)sizeInKB).ToString("N0");
                }
                else
                {
                    return sizeInKB.ToString("0.##");
                }
                }
            }

            // Abbreviated attribute flags
            public string ShortAttributes
        {
            get
            {
                if (string.IsNullOrEmpty(Attributes))
                    return string.Empty;

                var attrs = new System.Collections.Generic.List<string>();
                var attrStr = Attributes.ToUpper();

                if (attrStr.Contains("READONLY")) attrs.Add("R");
                if (attrStr.Contains("HIDDEN")) attrs.Add("H");
                if (attrStr.Contains("SYSTEM")) attrs.Add("S");
                if (attrStr.Contains("NORMAL")) attrs.Add("N");
                if (attrStr.Contains("TEMPORARY")) attrs.Add("T");
                if (attrStr.Contains("COMPRESSED")) attrs.Add("C");
                if (attrStr.Contains("ENCRYPTED")) attrs.Add("E");
                if (attrStr.Contains("OFFLINE")) attrs.Add("O");
                if (attrStr.Contains("NOTCONTENTINDEXED")) attrs.Add("I");
                if (attrStr.Contains("REPARSEPOINT")) attrs.Add("L");
                if (attrStr.Contains("SPARSEFILE")) attrs.Add("P");
                if (attrStr.Contains("INTEGRITYSTREAM")) attrs.Add("V");
                if (attrStr.Contains("NOSCRUBDATA")) attrs.Add("X");
                if (attrStr.Contains("PINNED")) attrs.Add("P");
                if (attrStr.Contains("UNPINNED")) attrs.Add("U");

                return string.Join(", ", attrs);
                }
            }

            // Display name for UI
            public string DisplayName => IsDeleted && !string.IsNullOrEmpty(ReconstructedFileName)
                ? ReconstructedFileName
                : FileName;

            // Recovered original filename
            public string ReconstructedFileName { get; set; }

            // Recovery confidence level
            public int ReconstructionConfidence { get; set; }

            // Raw slack data bytes
            public byte[] SlackData { get; set; }

            // Source of recovery
            public string ReconstructionSource { get; set; }

            // Recovery process notes
            public string ReconstructionNotes { get; set; }

            // File signature status
            public string Signature { get; set; }

            // Carved hash value
            public string Hash { get; set; }

            // Display timestamp fallback
            public DateTime? DisplayModifiedTime => ModifiedTime ?? CreationTime;

            // Formatted access time
            public string DisplayAccessedTime
        {
            get
            {
                if (Status == "Present" && AccessedTime.HasValue)
                    return AccessedTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                return string.Empty;
                }
            }

            // Formatted creation time
            public string DisplayCreatedTime
        {
            get
            {
                if (Status == "Present" && CreationTime.HasValue)
                    return CreationTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                return string.Empty;
                }
            }

            // Most recent timestamp
            public string LastActivity
        {
            get
            {
                var now = DateTime.Now;
                var times = new System.Collections.Generic.List<DateTime?>();

                if (Status == "Present")
                {
                    times.Add(ModifiedTime);
                    times.Add(CreationTime);
                }
                else
                {
                    times.Add(ModifiedTime);
                    times.Add(CreationTime);
                    times.Add(AccessedTime);
                }

                var validTimes = times
                    .Where(t => t.HasValue && t.Value <= now)
                    .Select(t => t.Value)
                    .ToList();

                if (validTimes.Any())
                {
                    var mostRecent = validTimes.Max();
                    return mostRecent.ToString("yyyy-MM-dd HH:mm:ss");
                }

                return string.Empty;
            }
        }
    }
}
