using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Core filesystem analysis and recovery logic
namespace Drives.Core
{
    // Analyzes and recovers files from FAT filesystems
    public class FATAnalyzer
    {
        // Size of directory entry in bytes
        private const int DIRECTORY_ENTRY_SIZE = 32;
        // Marker byte for deleted files
        private const byte DELETED_MARKER = 0xE5;
        // Marks end of directory entries
        private const byte DIRECTORY_END_MARKER = 0x00;
        // Long filename attribute identifier
        private const byte LFN_ATTRIBUTE = 0x0F;
        // Volume label entry attribute
        private const byte VOLUME_LABEL_ATTRIBUTE = 0x08;
        // Directory entry attribute identifier
        private const byte DIRECTORY_ATTRIBUTE = 0x10;

        // Target drive letter for analysis
        private readonly string _driveLetter;
        // Metadata about target drive
        private readonly Models.DriveInfo _driveInfo;
        // Indicates exFAT filesystem type
        private readonly bool _isExFAT;
        // Indicates FAT16 filesystem type
        private readonly bool _isFAT16;

        // Initializes analyzer for target drive
        public FATAnalyzer(string driveLetter)
        {
            _driveLetter = driveLetter.TrimEnd('\\');
            _driveInfo = DriveDetector.GetAllDrives()
                .FirstOrDefault(d => d.DriveLetter.Equals(_driveLetter, StringComparison.OrdinalIgnoreCase));

            if (_driveInfo == null || !_driveInfo.IsSupported)
            {
                throw new ArgumentException($"Drive {driveLetter} is not a supported FAT file system.");
            }

            _isExFAT = _driveInfo.FileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
            _isFAT16 = _driveInfo.FileSystem.Equals("FAT", StringComparison.OrdinalIgnoreCase);
        }

        // Retrieves all active files from drive
        public List<FileEntry> GetExistingFiles()
        {
            var files = new List<FileEntry>();

            try
            {
                var rootPath = _driveLetter + "\\";
                EnumerateFilesRecursive(rootPath, files);
                CarveHashesForFiles(files);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error analyzing drive {_driveLetter}: {ex.Message}");
            }

            return files;
        }

        // Recursively scans directory for files
        private void EnumerateFilesRecursive(string path, List<FileEntry> files)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);

                var directories = dirInfo.GetDirectories();
                foreach (var directory in directories)
                {
                    try
                    {
                        var dirEntry = new FileEntry
                        {
                            FileName = directory.Name,
                            FullPath = directory.FullName,
                            FileSize = 0,
                            FileSizeFormatted = "<DIR>",
                            Status = "Present",
                            CreationTime = directory.CreationTime,
                            ModifiedTime = directory.LastWriteTime,
                            AccessedTime = directory.LastAccessTime,
                            Attributes = directory.Attributes.ToString(),
                            IsDeleted = false,
                            IsDirectory = true,
                            SlackSpace = 0,
                            Signature = string.Empty
                        };

                        files.Add(dirEntry);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing directory info {directory.FullName}: {ex.Message}");
                    }
                }

                var fileInfos = dirInfo.GetFiles();

                foreach (var fileInfo in fileInfos)
                {
                    try
                    {
                        var entry = new FileEntry
                        {
                            FileName = fileInfo.Name,
                            FullPath = fileInfo.FullName,
                            FileSize = fileInfo.Length,
                            FileSizeFormatted = Util.FormatHelper.FormatBytes(fileInfo.Length),
                            Status = "Present",
                            CreationTime = fileInfo.CreationTime,
                            ModifiedTime = fileInfo.LastWriteTime,
                            AccessedTime = fileInfo.LastAccessTime,
                            Attributes = fileInfo.Attributes.ToString(),
                            IsDeleted = false,
                            IsDirectory = false,
                            Signature = Util.SigVerifier.CheckSignature(fileInfo.FullName)
                        };

                        files.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing file {fileInfo.FullName}: {ex.Message}");
                    }
                }

                foreach (var directory in directories)
                {
                    try
                    {
                        EnumerateFilesRecursive(directory.FullName, files);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing directory {directory.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating path {path}: {ex.Message}");
            }
        }

        // Scans for deleted files on drive
        public List<FileEntry> GetDeletedFiles()
        {
            var deletedFiles = new List<FileEntry>();

            try
            {
                if (!Util.Recovery.IsAdministrator())
                {
                    throw new UnauthorizedAccessException("Administrator privileges required for low-level disk access.");
                }

                System.Diagnostics.Debug.WriteLine($"Starting deleted file scan on {_driveLetter} (File System: {_driveInfo.FileSystem})");

                string drivePath = $"\\\\.\\{_driveLetter}";
                string rootPath = _driveLetter + "\\";

                using (var driveStream = new FileStream(drivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.None))
                {
                    if (_isExFAT)
                    {
                        var exFatBootSector = ExFAT.ReadBootSector(driveStream);
                        if (exFatBootSector == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to read exFAT boot sector");
                            return deletedFiles;
                        }

                        System.Diagnostics.Debug.WriteLine($"exFAT Boot Sector Info:");
                        System.Diagnostics.Debug.WriteLine($"  Bytes per sector: {exFatBootSector.BytesPerSector}");
                        System.Diagnostics.Debug.WriteLine($"  Sectors per cluster: {exFatBootSector.SectorsPerCluster}");
                        System.Diagnostics.Debug.WriteLine($"  Cluster heap offset: {exFatBootSector.ClusterHeapOffset}");
                        System.Diagnostics.Debug.WriteLine($"  Root directory cluster: {exFatBootSector.RootDirectoryCluster}");

                        ExFAT.ScanDirectoryCluster(driveStream, exFatBootSector, exFatBootSector.RootDirectoryCluster, rootPath, deletedFiles, new HashSet<uint>(), ParseExFATFileEntry);
                    }
                    else if (_isFAT16)
                    {
                        var bootSector = FAT16.ReadBootSector(driveStream);
                        if (bootSector == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to read FAT16 boot sector");
                            return deletedFiles;
                        }

                        System.Diagnostics.Debug.WriteLine($"FAT16 Boot Sector Info:");
                        System.Diagnostics.Debug.WriteLine($"  Bytes per sector: {bootSector.BytesPerSector}");
                        System.Diagnostics.Debug.WriteLine($"  Sectors per cluster: {bootSector.SectorsPerCluster}");
                        System.Diagnostics.Debug.WriteLine($"  Reserved sectors: {bootSector.ReservedSectors}");
                        System.Diagnostics.Debug.WriteLine($"  FAT count: {bootSector.NumberOfFATs}");
                        System.Diagnostics.Debug.WriteLine($"  Sectors per FAT: {bootSector.SectorsPerFAT}");
                        System.Diagnostics.Debug.WriteLine($"  Root entry count: {bootSector.RootEntryCount}");

                        FAT16.ScanRootDirectory(driveStream, bootSector, rootPath, deletedFiles, ParseDirectoryEntries);
                    }
                    else
                    {
                        var bootSector = FAT32.ReadBootSector(driveStream);
                        if (bootSector == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to read boot sector");
                            return deletedFiles;
                        }

                        System.Diagnostics.Debug.WriteLine($"Boot Sector Info:");
                        System.Diagnostics.Debug.WriteLine($"  Bytes per sector: {bootSector.BytesPerSector}");
                        System.Diagnostics.Debug.WriteLine($"  Sectors per cluster: {bootSector.SectorsPerCluster}");
                        System.Diagnostics.Debug.WriteLine($"  Reserved sectors: {bootSector.ReservedSectors}");
                        System.Diagnostics.Debug.WriteLine($"  FAT count: {bootSector.NumberOfFATs}");
                        System.Diagnostics.Debug.WriteLine($"  Sectors per FAT: {bootSector.SectorsPerFAT}");
                        System.Diagnostics.Debug.WriteLine($"  Root cluster: {bootSector.RootCluster}");

                        FAT32.ScanDirectoryCluster(driveStream, bootSector, bootSector.RootCluster, rootPath, deletedFiles, new HashSet<uint>(), ParseDirectoryEntries);
                    }
                }

                CarveHashesForFiles(deletedFiles);

                System.Diagnostics.Debug.WriteLine($"Deleted file scan complete. Found {deletedFiles.Count} deleted items.");
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning for deleted files: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }

            return deletedFiles;
        }

        // Builds complete file path
        private string BuildFullFilePath(string dirPath, string fileName)
        {
            string basePath = dirPath.EndsWith("\\") ? dirPath : dirPath + "\\";
            return basePath + fileName;
        }

        // Parses directory cluster for entries
        private void ParseDirectoryEntries(byte[] clusterData, string dirPath, List<FileEntry> deletedFiles,
            List<uint> subdirectoryClusters, List<string> subdirectoryNames, bool isFAT16)
        {
            List<byte[]> lfnEntries = new List<byte[]>();

            for (int offset = 0; offset < clusterData.Length; offset += DIRECTORY_ENTRY_SIZE)
            {
                byte[] entryBuffer = new byte[DIRECTORY_ENTRY_SIZE];
                Array.Copy(clusterData, offset, entryBuffer, 0, DIRECTORY_ENTRY_SIZE);

                byte firstByte = entryBuffer[0];

                if (firstByte == DIRECTORY_END_MARKER)
                    break;

                if (firstByte == 0x20)
                    continue;

                byte attributes = entryBuffer[11];
                bool isLFN = (attributes == LFN_ATTRIBUTE);
                bool isDeleted = (firstByte == DELETED_MARKER);
                bool isVolumeLabel = (attributes & VOLUME_LABEL_ATTRIBUTE) != 0;

                if (isLFN)
                {
                    byte[] lfnCopy = new byte[DIRECTORY_ENTRY_SIZE];
                    Array.Copy(entryBuffer, lfnCopy, DIRECTORY_ENTRY_SIZE);
                    lfnEntries.Add(lfnCopy);
                }
                else if (!isVolumeLabel && firstByte != 0x2E)
                {
                    var entry = ParseSingleEntry(entryBuffer, isDeleted, lfnEntries);

                    if (entry != null)
                    {
                        if (entry.IsDeleted)
                        {
                            string fileName = entry.LongName ?? entry.ShortName ?? "[Unknown]";
                            var fileEntry = CreateFileEntry(entry, dirPath, fileName);
                            deletedFiles.Add(fileEntry);
                            string fsType = isFAT16 ? "FAT16" : "FAT32";
                            System.Diagnostics.Debug.WriteLine($"Found deleted {fsType} file: {fileEntry.ReconstructedFileName} ({fileEntry.FileSizeFormatted})");
                        }

                        bool isValidCluster = isFAT16 ? FAT16.IsValidCluster(entry.FirstCluster) : FAT32.IsValidCluster(entry.FirstCluster);
                        if (!entry.IsDeleted && entry.IsDirectory && isValidCluster)
                        {
                            subdirectoryClusters.Add(entry.FirstCluster);
                            subdirectoryNames.Add(entry.LongName ?? entry.ShortName);
                        }
                    }

                    lfnEntries.Clear();
                }
            }
        }

        // Extracts metadata from directory entry
        private RawDirectoryEntry ParseSingleEntry(byte[] buffer, bool isDeleted, List<byte[]> lfnEntries)
        {
            try
            {
                var entry = new RawDirectoryEntry
                {
                    IsDeleted = isDeleted
                };

                byte[] nameBytes = new byte[8];
                byte[] extBytes = new byte[3];
                Array.Copy(buffer, 0, nameBytes, 0, 8);
                Array.Copy(buffer, 8, extBytes, 0, 3);

                string name = Encoding.ASCII.GetString(nameBytes).TrimEnd();
                string ext = Encoding.ASCII.GetString(extBytes).TrimEnd();

                if (isDeleted && name.Length > 0)
                {
                    name = "?" + name.Substring(1);
                }

                entry.ShortName = string.IsNullOrEmpty(ext) ? name : $"{name}.{ext}";

                entry.Attributes = ((FileAttributes)buffer[11]).ToString();
                entry.IsDirectory = (buffer[11] & 0x10) != 0;

                entry.CreationTime = Util.DateTimeParser.ParseFatDateTime(buffer, 16, 14);
                entry.AccessTime = Util.DateTimeParser.ParseFatDate(buffer, 18);
                entry.ModifiedTime = Util.DateTimeParser.ParseFatDateTime(buffer, 24, 22);

                ushort clusterLow = BitConverter.ToUInt16(buffer, 26);
                ushort clusterHigh = BitConverter.ToUInt16(buffer, 20);
                entry.FirstCluster = ((uint)clusterHigh << 16) | clusterLow;
                entry.FileSize = BitConverter.ToUInt32(buffer, 28);

                if (lfnEntries.Count > 0)
                {
                    string reconstructedLFN = Util.LFNParser.ReconstructLongFileName(lfnEntries.ToArray());

                    if (reconstructedLFN != null)
                    {
                        if (isDeleted)
                        {
                            byte[] shortNameBuffer = new byte[11];
                            Array.Copy(nameBytes, 0, shortNameBuffer, 0, 8);
                            Array.Copy(extBytes, 0, shortNameBuffer, 8, 3);

                            byte expectedChecksum = Util.LFNParser.CalculateLFNChecksum(shortNameBuffer);
                            byte lfnChecksum = lfnEntries.Count > 0 ? lfnEntries[0][13] : (byte)0;

                            System.Diagnostics.Debug.WriteLine($"LFN Checksum: {lfnChecksum:X2}, Expected: {expectedChecksum:X2}, LFN: {reconstructedLFN}");
                        }

                        if (reconstructedLFN.StartsWith("?"))
                        {
                            entry.LongName = Util.LFNParser.RecoverFirstCharacter(reconstructedLFN, entry.ShortName);
                        }
                        else
                        {
                            entry.LongName = reconstructedLFN;
                        }
                    }
                }

                return entry;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing entry: {ex.Message}");
                return null;
            }
        }

        // Calculates slack space for files
        public void AnalyzeFileSlack(List<FileEntry> files)
        {
            try
            {
                var clusterSize = Util.ClusterHelper.GetClusterSize(_driveLetter);

                foreach (var file in files)
                {
                    if (file.FileSize > 0 && !file.IsDirectory)
                    {
                        var remainder = file.FileSize % clusterSize;
                        if (remainder > 0)
                        {
                            file.SlackSpace = clusterSize - remainder;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error analyzing file slack: {ex.Message}");
            }
        }

        // Attempts file recovery to output path
        public bool RecoverFile(FileEntry fileEntry, string outputPath)
        {
            return Util.Recovery.RecoverFile(fileEntry, outputPath, _driveLetter, _isExFAT, _isFAT16);
        }

        // Creates FileEntry from raw data
        private FileEntry CreateFileEntry(RawDirectoryEntry entry, string dirPath, string fileName)
        {
            return new FileEntry
            {
                FileName = entry.ShortName,
                ReconstructedFileName = entry.LongName ?? entry.ShortName,
                FullPath = BuildFullFilePath(dirPath, fileName),
                FileSize = entry.FileSize,
                FileSizeFormatted = Util.FormatHelper.FormatBytes(entry.FileSize),
                Status = "Deleted",
                CreationTime = entry.CreationTime,
                ModifiedTime = entry.ModifiedTime,
                AccessedTime = entry.AccessTime,
                Attributes = entry.Attributes,
                IsDeleted = true,
                IsDirectory = entry.IsDirectory,
                StartCluster = entry.FirstCluster,
                ReconstructionConfidence = CalculateConfidence(entry),
                ReconstructionSource = entry.LongName != null ? "LFN Entry" : "Short Name",
                ReconstructionNotes = BuildReconstructionNotes(entry)
            };
        }

        // Parses exFAT specific file entry
        private FileEntry ParseExFATFileEntry(byte[] clusterData, int fileEntryOffset, bool isDeleted, string dirPath)
        {
            return ExFAT.ParseFileEntry(clusterData, fileEntryOffset, isDeleted, dirPath);
        }

        // Computes filename recovery confidence level
        private int CalculateConfidence(RawDirectoryEntry entry)
        {
            int confidence = 30;

            if (entry.LongName != null)
                confidence += 30;

            if (entry.CreationTime != null)
                confidence += 20;

            if (entry.FileSize > 0 && !entry.IsDirectory)
                confidence += 10;

            if (entry.FirstCluster >= 2 && entry.FirstCluster < 0x0FFFFFF8)
                confidence += 10;

            return Math.Min(confidence, 100);
        }

        // Generates notes about recovery process
        private string BuildReconstructionNotes(RawDirectoryEntry entry)
        {
            var notes = new List<string>
                                                {
                                                    "First character of filename replaced with '?'"
                                                };

            if (entry.LongName != null)
                notes.Add("Long filename recovered from LFN entries");
            else
                notes.Add("Only 8.3 short name available");

            if (entry.FirstCluster < 2)
                notes.Add("WARNING: Invalid cluster - data may be unrecoverable");

            return string.Join("; ", notes);
        }

        // Carves hash values from file content
        private void CarveHashesForFiles(List<FileEntry> files)
        {
            foreach (var file in files)
            {
                if (file.IsDirectory)
                {
                    file.Hash = "N/A";
                }
                else if (file.IsDeleted || file.Status == "Replaced")
                {
                    file.Hash = "Deleted";
                }
                else if (File.Exists(file.FullPath))
                {
                    file.Hash = Util.HashCarver.CarveHash(file.FullPath);
                }
                else
                {
                    file.Hash = "N/A";
                }
            }
        }

        // Updates status to Replaced for duplicates
        public void DetectReplacedFiles(List<FileEntry> presentFiles, List<FileEntry> deletedFiles)
        {
            var fileNameGroups = deletedFiles
                .Where(f => !f.IsDirectory)
                .GroupBy(f => Path.GetFileName(f.FullPath), StringComparer.OrdinalIgnoreCase);

            foreach (var group in fileNameGroups)
            {
                var matchingPresent = presentFiles
                    .FirstOrDefault(p => !p.IsDirectory &&
                        Path.GetFileName(p.FullPath).Equals(group.Key, StringComparison.OrdinalIgnoreCase));

                if (matchingPresent != null)
                {
                    foreach (var deletedFile in group)
                    {
                        if (deletedFile.FileSize != matchingPresent.FileSize)
                        {
                            deletedFile.Status = "Replaced";
                        }
                    }
                }
            }
        }
    }

    // Holds raw directory entry data
    internal class RawDirectoryEntry
    {
        // DOS 8dot3 filename format
        public string ShortName { get; set; }
        // Full long filename
        public string LongName { get; set; }
        // Entry marked as deleted
        public bool IsDeleted { get; set; }
        // Entry is directory type
        public bool IsDirectory { get; set; }
        // File system attributes string
        public string Attributes { get; set; }
        // File creation timestamp
        public DateTime? CreationTime { get; set; }
        // Last modification timestamp
        public DateTime? ModifiedTime { get; set; }
        // Last accessed timestamp
        public DateTime? AccessTime { get; set; }
        // Starting cluster number
        public uint FirstCluster { get; set; }
        // File size in bytes
        public uint FileSize { get; set; }
    }
}
