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
                throw new ArgumentException("Error Code:9");
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
                            Signature = Util.PlatformInterop.CheckSignature(fileInfo.FullName)
                        };

                        files.Add(entry);
                    }
                    catch (Exception ex)
                    {
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
                    }
                }
            }
            catch (Exception ex)
            {
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
                    throw new UnauthorizedAccessException("Error Code:1");
                }


                string drivePath = $@"\\.\{_driveLetter}";
                string rootPath = _driveLetter + "\\";

                using (var driveStream = new FileStream(drivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false))
                {
                    if (_isExFAT)
                    {
                        var exFatBootSector = ExFAT.ReadBootSector(driveStream);
                        if (exFatBootSector == null)
                        {
                            return deletedFiles;
                        }


                        ExFAT.ScanDirectoryCluster(driveStream, exFatBootSector, exFatBootSector.RootDirectoryCluster, rootPath, deletedFiles, new HashSet<uint>(), ParseExFATFileEntry);
                    }
                    else if (_isFAT16)
                    {
                        var bootSector = FAT16.ReadBootSector(driveStream);
                        if (bootSector == null)
                        {
                            return deletedFiles;
                        }


                        FAT16.ScanRootDirectory(driveStream, bootSector, rootPath, deletedFiles, ParseDirectoryEntries);
                    }
                    else
                    {
                        var bootSector = FAT32.ReadBootSector(driveStream);
                        if (bootSector == null)
                        {
                            return deletedFiles;
                        }


                        FAT32.ScanDirectoryCluster(driveStream, bootSector, bootSector.RootCluster, rootPath, deletedFiles, new HashSet<uint>(), ParseDirectoryEntries);
                    }
                }

                CarveHashesForFiles(deletedFiles);

            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
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
                UseContiguousClusters = false
            };
        }

        // Parses exFAT specific file entry
        private FileEntry ParseExFATFileEntry(byte[] clusterData, int fileEntryOffset, bool isDeleted, string dirPath)
        {
            return ExFAT.ParseFileEntry(clusterData, fileEntryOffset, isDeleted, dirPath);
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
