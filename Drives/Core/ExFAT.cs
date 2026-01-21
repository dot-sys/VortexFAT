using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Core filesystem analysis and recovery logic
namespace Drives.Core
{
    // Handles exFAT filesystem operations
    public class ExFAT
    {
        // Minimum valid cluster number
        private const uint FAT32_MIN_CLUSTER = 2;
        // End of cluster chain marker
        private const uint EXFAT_EOC_MARKER = 0xFFFFFFF7;
        // Size of directory entry in bytes
        private const int DIRECTORY_ENTRY_SIZE = 32;
        // Marks end of directory entries
        private const byte DIRECTORY_END_MARKER = 0x00;

        // Reads and parses boot sector
        public static ExFATBootSector ReadBootSector(Stream diskStream)
        {
            try
            {
                byte[] sector = new byte[512];
                diskStream.Position = 0;
                diskStream.Read(sector, 0, 512);

                string signature = Encoding.ASCII.GetString(sector, 3, 8);
                if (signature != "EXFAT   ")
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid exFAT signature: {signature}");
                    return null;
                }

                var bootSector = new ExFATBootSector
                {
                    BytesPerSectorShift = sector[108],
                    SectorsPerClusterShift = sector[109],
                    NumberOfFATs = sector[110],
                    PartitionOffset = BitConverter.ToUInt64(sector, 64),
                    VolumeLength = BitConverter.ToUInt64(sector, 72),
                    FATOffset = BitConverter.ToUInt32(sector, 80),
                    FATLength = BitConverter.ToUInt32(sector, 84),
                    ClusterHeapOffset = BitConverter.ToUInt32(sector, 88),
                    ClusterCount = BitConverter.ToUInt32(sector, 92),
                    RootDirectoryCluster = BitConverter.ToUInt32(sector, 96)
                };

                bootSector.BytesPerSector = (uint)(1 << bootSector.BytesPerSectorShift);
                bootSector.SectorsPerCluster = (uint)(1 << bootSector.SectorsPerClusterShift);

                return bootSector;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading exFAT boot sector: {ex.Message}");
                return null;
                }
            }

            // Reads directory cluster recursively
            public static void ScanDirectoryCluster(Stream diskStream, ExFATBootSector bootSector, uint cluster, string dirPath, List<FileEntry> deletedFiles, HashSet<uint> visitedClusters, Func<byte[], int, bool, string, FileEntry> parseExFATFileEntry)
        {
            try
            {
                if (!IsValidCluster(cluster) || visitedClusters.Contains(cluster))
                    return;

                visitedClusters.Add(cluster);

                System.Diagnostics.Debug.WriteLine($"Scanning exFAT cluster {cluster} for directory: {dirPath}");

                long clusterOffset = GetClusterOffset(bootSector, cluster);
                long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;

                byte[] clusterData = new byte[clusterSize];
                diskStream.Position = clusterOffset;
                int bytesRead = diskStream.Read(clusterData, 0, (int)clusterSize);

                if (bytesRead != clusterSize)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Only read {bytesRead} of {clusterSize} bytes");
                }

                List<uint> subdirectoryClusters = new List<uint>();
                List<string> subdirectoryNames = new List<string>();

                for (int offset = 0; offset < clusterData.Length; offset += DIRECTORY_ENTRY_SIZE)
                {
                    byte[] entryBuffer = new byte[DIRECTORY_ENTRY_SIZE];
                    Array.Copy(clusterData, offset, entryBuffer, 0, DIRECTORY_ENTRY_SIZE);

                    byte entryType = entryBuffer[0];

                    if (entryType == DIRECTORY_END_MARKER)
                        break;

                    bool isInUse = (entryType & 0x80) != 0;
                    bool isDeleted = !isInUse;

                    byte actualType = (byte)(entryType & 0x7F);

                    if (actualType == 0x05)
                    {
                        var fileEntry = parseExFATFileEntry(clusterData, offset, isDeleted, dirPath);
                        if (fileEntry != null)
                        {
                            if (fileEntry.IsDeleted)
                            {
                                deletedFiles.Add(fileEntry);
                                System.Diagnostics.Debug.WriteLine($"Found deleted exFAT file: {fileEntry.ReconstructedFileName} ({fileEntry.FileSizeFormatted})");
                            }

                            if (!fileEntry.IsDeleted && fileEntry.IsDirectory && IsValidCluster((uint)fileEntry.StartCluster))
                            {
                                subdirectoryClusters.Add((uint)fileEntry.StartCluster);
                                subdirectoryNames.Add(fileEntry.ReconstructedFileName);
                            }
                        }
                    }
                }

                uint nextCluster = ReadFATEntry(diskStream, bootSector, cluster);
                if (IsValidCluster(nextCluster))
                {
                    ScanDirectoryCluster(diskStream, bootSector, nextCluster, dirPath, deletedFiles, visitedClusters, parseExFATFileEntry);
                }

                for (int i = 0; i < subdirectoryClusters.Count; i++)
                {
                    uint subdirCluster = subdirectoryClusters[i];
                    string subdirName = i < subdirectoryNames.Count ? subdirectoryNames[i] : $"subdir_{subdirCluster}";
                    string subdirPath = dirPath.EndsWith("\\") ? dirPath + subdirName : dirPath + "\\" + subdirName;
                    ScanDirectoryCluster(diskStream, bootSector, subdirCluster, subdirPath, deletedFiles, visitedClusters, parseExFATFileEntry);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning exFAT cluster {cluster}: {ex.Message}");
                }
            }

            // Parses exFAT file entry structure
            public static FileEntry ParseFileEntry(byte[] clusterData, int fileEntryOffset, bool isDeleted, string dirPath)
        {
            try
            {
                byte[] fileEntry = new byte[DIRECTORY_ENTRY_SIZE];
                Array.Copy(clusterData, fileEntryOffset, fileEntry, 0, DIRECTORY_ENTRY_SIZE);

                byte secondaryCount = fileEntry[1];

                if (secondaryCount < 2)
                {
                    return null;
                }

                int streamOffset = fileEntryOffset + DIRECTORY_ENTRY_SIZE;
                if (streamOffset + DIRECTORY_ENTRY_SIZE > clusterData.Length)
                    return null;

                byte[] streamEntry = new byte[DIRECTORY_ENTRY_SIZE];
                Array.Copy(clusterData, streamOffset, streamEntry, 0, DIRECTORY_ENTRY_SIZE);

                byte streamType = (byte)(streamEntry[0] & 0x7F);
                if (streamType != 0x40)
                    return null;

                byte generalSecondaryFlags = streamEntry[1];
                bool noFatChain = (generalSecondaryFlags & 0x02) != 0;

                ushort fileAttributes = BitConverter.ToUInt16(fileEntry, 4);
                bool isDirectory = (fileAttributes & 0x0010) != 0;

                ulong fileSize = BitConverter.ToUInt64(streamEntry, 8);
                uint firstCluster = BitConverter.ToUInt32(streamEntry, 20);
                ulong dataLength = BitConverter.ToUInt64(streamEntry, 16);

                DateTime? creationTime = Util.DateTimeParser.ParseExFATDateTime(fileEntry, 8);
                DateTime? modifiedTime = Util.DateTimeParser.ParseExFATDateTime(fileEntry, 12);
                DateTime? accessedTime = Util.DateTimeParser.ParseExFATDateTime(fileEntry, 16);

                byte nameLength = streamEntry[3];

                StringBuilder fileName = new StringBuilder();
                int filenameEntryCount = secondaryCount - 1;

                for (int i = 0; i < filenameEntryCount && i < 17; i++)
                {
                    int filenameOffset = streamOffset + DIRECTORY_ENTRY_SIZE + (i * DIRECTORY_ENTRY_SIZE);
                    if (filenameOffset + DIRECTORY_ENTRY_SIZE > clusterData.Length)
                        break;

                    byte[] filenameEntry = new byte[DIRECTORY_ENTRY_SIZE];
                    Array.Copy(clusterData, filenameOffset, filenameEntry, 0, DIRECTORY_ENTRY_SIZE);

                    byte filenameType = (byte)(filenameEntry[0] & 0x7F);
                    if (filenameType != 0x41)
                        break;

                    for (int j = 2; j < 32 && fileName.Length < nameLength; j += 2)
                    {
                        char c = BitConverter.ToChar(filenameEntry, j);
                        if (c != 0 && c != 0xFFFF)
                        {
                            fileName.Append(c);
                        }
                    }
                }

                string finalName = fileName.ToString().TrimEnd('\0');
                if (string.IsNullOrEmpty(finalName))
                {
                    finalName = isDirectory ? "[Deleted Directory]" : "[Deleted File]";
                }

                string basePath = dirPath.EndsWith("\\") ? dirPath : dirPath + "\\";
                string fullPath = basePath + finalName;

                var entry = new FileEntry
                {
                    FileName = finalName,
                    ReconstructedFileName = finalName,
                    FullPath = fullPath,
                    FileSize = (long)fileSize,
                    FileSizeFormatted = Util.FormatHelper.FormatBytes((long)fileSize),
                    Status = isDeleted ? "Deleted" : "Present",
                    CreationTime = creationTime,
                    ModifiedTime = modifiedTime,
                    AccessedTime = accessedTime,
                    Attributes = ConvertAttributes(fileAttributes),
                    IsDeleted = isDeleted,
                    IsDirectory = isDirectory,
                    StartCluster = firstCluster,
                    UseContiguousClusters = noFatChain,
                    ReconstructionConfidence = CalculateConfidence(finalName, fileSize, creationTime),
                    ReconstructionSource = "exFAT Directory Entry",
                    ReconstructionNotes = isDeleted ? "Recovered from exFAT deleted entry" : "Active exFAT entry"
                };

                return entry;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing exFAT file entry: {ex.Message}");
                return null;
                }
            }

            // Converts attribute flags to string
            private static string ConvertAttributes(ushort attributes)
        {
            var attrList = new List<string>();

            if ((attributes & 0x0001) != 0) attrList.Add("ReadOnly");
            if ((attributes & 0x0002) != 0) attrList.Add("Hidden");
            if ((attributes & 0x0004) != 0) attrList.Add("System");
            if ((attributes & 0x0010) != 0) attrList.Add("Directory");
            if ((attributes & 0x0020) != 0) attrList.Add("Archive");

                return attrList.Count > 0 ? string.Join(", ", attrList) : "Normal";
            }

            // Computes recovery confidence level
            private static int CalculateConfidence(string fileName, ulong fileSize, DateTime? creationTime)
        {
            int confidence = 50;

            if (!string.IsNullOrEmpty(fileName) && !fileName.StartsWith("["))
                confidence += 30;

            if (creationTime != null)
                confidence += 10;

            if (fileSize > 0)
                confidence += 10;

                    return Math.Min(confidence, 100);
                }

                // Calculates byte offset for cluster
            public static long GetClusterOffset(ExFATBootSector bootSector, uint cluster)
        {
            long clusterHeapOffset = bootSector.ClusterHeapOffset * bootSector.BytesPerSector;
            long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                return clusterHeapOffset + ((cluster - 2) * clusterSize);
            }

            // Reads FAT table entry value
            public static uint ReadFATEntry(Stream diskStream, ExFATBootSector bootSector, uint cluster)
        {
            try
            {
                long fatOffset = bootSector.FATOffset * bootSector.BytesPerSector;
                long entryOffset = fatOffset + (cluster * 4);

                byte[] entryBytes = new byte[4];
                diskStream.Position = entryOffset;
                diskStream.Read(entryBytes, 0, 4);

                uint value = BitConverter.ToUInt32(entryBytes, 0);
                return value;
            }
            catch
            {
                return 0xFFFFFFFF;
                }
            }

            // Validates cluster number range
            public static bool IsValidCluster(uint cluster)
        {
            return cluster >= FAT32_MIN_CLUSTER && cluster < EXFAT_EOC_MARKER;
            }
        }

        // Stores exFAT boot sector data
        public class ExFATBootSector
        {
        // Sector size bit shift value
        public byte BytesPerSectorShift { get; set; }
        // Cluster size bit shift value
        public byte SectorsPerClusterShift { get; set; }
        // Sector size in bytes
        public uint BytesPerSector { get; set; }
        // Cluster size in sectors
        public uint SectorsPerCluster { get; set; }
        // Number of FAT copies
        public byte NumberOfFATs { get; set; }
        // Partition start sector offset
        public ulong PartitionOffset { get; set; }
        // Total volume size in sectors
        public ulong VolumeLength { get; set; }
        // FAT table start sector
        public uint FATOffset { get; set; }
        // FAT table size in sectors
        public uint FATLength { get; set; }
        // Data cluster heap start
        public uint ClusterHeapOffset { get; set; }
        // Total number of clusters
        public uint ClusterCount { get; set; }
        // Root directory first cluster
        public uint RootDirectoryCluster { get; set; }
    }
}
