using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;

// Core filesystem analysis and recovery logic
namespace Drives.Core
{
    // Handles FAT16 filesystem operations
    public class FAT16
    {
        // Minimum valid cluster number
        private const uint FAT32_MIN_CLUSTER = 2;
        // End of cluster chain marker
        private const uint FAT16_EOC_MARKER = 0xFFF8;

        // Reads and parses boot sector
        public static FAT16BootSector ReadBootSector(Stream diskStream)
        {
            try
            {
                byte[] sector = new byte[512];
                diskStream.Position = 0;
                diskStream.Read(sector, 0, 512);

                var bootSector = new FAT16BootSector
                {
                    BytesPerSector = BitConverter.ToUInt16(sector, 11),
                    SectorsPerCluster = sector[13],
                    ReservedSectors = BitConverter.ToUInt16(sector, 14),
                    NumberOfFATs = sector[16],
                    RootEntryCount = BitConverter.ToUInt16(sector, 17),
                    TotalSectors16 = BitConverter.ToUInt16(sector, 19),
                    MediaDescriptor = sector[21],
                    SectorsPerFAT = BitConverter.ToUInt16(sector, 22)
                };

                return bootSector;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading FAT16 boot sector: {ex.Message}");
                return null;
                }
            }

            // Scans FAT16 root directory cluster
            public static void ScanRootDirectory(Stream diskStream, FAT16BootSector bootSector, string rootPath, List<FileEntry> deletedFiles, Action<byte[], string, List<FileEntry>, List<uint>, List<string>, bool> parseDirectoryEntries)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Scanning FAT16 root directory: {rootPath}");

                long rootDirOffset = (bootSector.ReservedSectors + (bootSector.NumberOfFATs * bootSector.SectorsPerFAT)) * bootSector.BytesPerSector;
                long rootDirSize = bootSector.RootEntryCount * 32;

                byte[] rootDirData = new byte[rootDirSize];
                diskStream.Position = rootDirOffset;
                int bytesRead = diskStream.Read(rootDirData, 0, (int)rootDirSize);

                if (bytesRead != rootDirSize)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Only read {bytesRead} of {rootDirSize} bytes from root directory");
                }

                var subdirectoryClusters = new List<uint>();
                var subdirectoryNames = new List<string>();
                parseDirectoryEntries(rootDirData, rootPath, deletedFiles, subdirectoryClusters, subdirectoryNames, true);

                for (int i = 0; i < subdirectoryClusters.Count; i++)
                {
                    uint subdirCluster = subdirectoryClusters[i];
                    string subdirName = i < subdirectoryNames.Count ? subdirectoryNames[i] : $"subdir_{subdirCluster}";
                    string subdirPath = rootPath.EndsWith("\\") ? rootPath + subdirName : rootPath + "\\" + subdirName;
                    ScanDirectoryCluster(diskStream, bootSector, subdirCluster, subdirPath, deletedFiles, new HashSet<uint>(), parseDirectoryEntries);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning FAT16 root directory: {ex.Message}");
                }
            }

            // Reads directory cluster recursively
            public static void ScanDirectoryCluster(Stream diskStream, FAT16BootSector bootSector, uint cluster, string dirPath, List<FileEntry> deletedFiles, HashSet<uint> visitedClusters, Action<byte[], string, List<FileEntry>, List<uint>, List<string>, bool> parseDirectoryEntries)
        {
            try
            {
                if (!IsValidCluster(cluster) || visitedClusters.Contains(cluster))
                    return;

                visitedClusters.Add(cluster);

                System.Diagnostics.Debug.WriteLine($"Scanning FAT16 cluster {cluster} for directory: {dirPath}");

                long clusterOffset = GetClusterOffset(bootSector, cluster);
                long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;

                byte[] clusterData = new byte[clusterSize];
                diskStream.Position = clusterOffset;
                int bytesRead = diskStream.Read(clusterData, 0, (int)clusterSize);

                if (bytesRead != clusterSize)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Only read {bytesRead} of {clusterSize} bytes");
                }

                var subdirectoryClusters = new List<uint>();
                var subdirectoryNames = new List<string>();
                parseDirectoryEntries(clusterData, dirPath, deletedFiles, subdirectoryClusters, subdirectoryNames, true);

                uint nextCluster = ReadFATEntry(diskStream, bootSector, cluster);
                if (IsValidCluster(nextCluster))
                {
                    ScanDirectoryCluster(diskStream, bootSector, nextCluster, dirPath, deletedFiles, visitedClusters, parseDirectoryEntries);
                }

                for (int i = 0; i < subdirectoryClusters.Count; i++)
                {
                    uint subdirCluster = subdirectoryClusters[i];
                    string subdirName = i < subdirectoryNames.Count ? subdirectoryNames[i] : $"subdir_{subdirCluster}";
                    string subdirPath = dirPath.EndsWith("\\") ? dirPath + subdirName : dirPath + "\\" + subdirName;
                    ScanDirectoryCluster(diskStream, bootSector, subdirCluster, subdirPath, deletedFiles, visitedClusters, parseDirectoryEntries);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning FAT16 cluster {cluster}: {ex.Message}");
                }
            }

            // Calculates byte offset for cluster
            public static long GetClusterOffset(FAT16BootSector bootSector, uint cluster)
        {
            long rootDirSectors = (bootSector.RootEntryCount * 32 + bootSector.BytesPerSector - 1) / bootSector.BytesPerSector;
            long firstDataSector = bootSector.ReservedSectors + (bootSector.NumberOfFATs * bootSector.SectorsPerFAT) + rootDirSectors;
            long clusterSector = firstDataSector + ((cluster - 2) * bootSector.SectorsPerCluster);
                return clusterSector * bootSector.BytesPerSector;
            }

            // Reads FAT table entry value
            public static uint ReadFATEntry(Stream diskStream, FAT16BootSector bootSector, uint cluster)
        {
            try
            {
                long fatOffset = bootSector.ReservedSectors * bootSector.BytesPerSector;
                long entryOffset = fatOffset + (cluster * 2);

                byte[] entryBytes = new byte[2];
                diskStream.Position = entryOffset;
                diskStream.Read(entryBytes, 0, 2);

                uint value = BitConverter.ToUInt16(entryBytes, 0);
                return value;
            }
            catch
            {
                return 0xFFFF;
                }
            }

            // Validates cluster number range
            public static bool IsValidCluster(uint cluster)
        {
            return cluster >= FAT32_MIN_CLUSTER && cluster < FAT16_EOC_MARKER;
            }
        }

        // Stores FAT16 boot sector data
        public class FAT16BootSector
        {
        // Sector size in bytes
        public ushort BytesPerSector { get; set; }
        // Cluster size in sectors
        public byte SectorsPerCluster { get; set; }
        // Count of reserved sectors
        public ushort ReservedSectors { get; set; }
        // Number of FAT copies
        public byte NumberOfFATs { get; set; }
        // Maximum root directory entries
        public ushort RootEntryCount { get; set; }
        // Total sectors on volume
        public ushort TotalSectors16 { get; set; }
        // Media type descriptor byte
        public byte MediaDescriptor { get; set; }
        // FAT table size in sectors
        public ushort SectorsPerFAT { get; set; }
    }
}
