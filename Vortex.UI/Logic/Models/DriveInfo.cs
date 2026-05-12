using System;

// Data models for drive information
namespace Drives.Models
{
    // Physical drive with filesystem info
    public class DriveInfo
    {
        // Drive letter identifier string
        public string DriveLetter { get; set; }

        // Filesystem type name
        public string FileSystem { get; set; }

        // Volume label name string
        public string Label { get; set; }

        // Total capacity in bytes
        public long TotalSize { get; set; }

        // Free space in bytes
        public long FreeSpace { get; set; }

        // Formatted display text string
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Label))
                {
                    return $"{DriveLetter}\\ ({Label}) - {FileSystem}";
                }
                return $"{DriveLetter}\\ - {FileSystem}";
                    }
                }

                // Checks if FAT filesystem
                public bool IsSupported
        {
            get
            {
                return FileSystem == "FAT32" || FileSystem == "exFAT" || FileSystem == "FAT";
            }
        }
    }
}
