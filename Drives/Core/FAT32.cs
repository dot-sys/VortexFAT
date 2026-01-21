using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;

// Core filesystem analysis and recovery logic
namespace Drives.Core
{
    // Handles FAT32 filesystem operations
    public class FAT32
    {
        // Minimum valid cluster number
        private const uint FAT32_MIN_CLUSTER = 2;
        // End of cluster chain marker
        private const uint FAT32_EOC_MARKER = 0x0FFFFFF8;

        // Reads and parses boot sector
        public static FAT32BootSector ReadBootSector(Stream diskStream)
        {
            try
            {
                byte[] sector = new byte[512];
                diskStream.Position = 0;
                diskStream.Read(sector, 0, 512);

                var bootSector = new FAT32BootSector
                {
                    BytesPerSector = BitConverter.ToUInt16(sector, 11),
                    SectorsPerCluster = sector[13],
                    ReservedSectors = BitConverter.ToUInt16(sector, 14),
                    NumberOfFATs = sector[16],
                    SectorsPerFAT = BitConverter.ToUInt32(sector, 36),
                    RootCluster = BitConverter.ToUInt32(sector, 44)
                };

                return bootSector;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading boot sector: {ex.Message}");
                return null;
                }
            }

            // Reads directory cluster recursively
            public static void ScanDirectoryCluster(Stream diskStream, FAT32BootSector bootSector, uint cluster, string dirPath, List<FileEntry> deletedFiles, HashSet<uint> visitedClusters, Action<byte[], string, List<FileEntry>, List<uint>, List<string>, bool> parseDirectoryEntries)
        {
            try
            {
                if (!IsValidCluster(cluster) || visitedClusters.Contains(cluster))
                    return;

                visitedClusters.Add(cluster);

                System.Diagnostics.Debug.WriteLine($"Scanning cluster {cluster} for directory: {dirPath}");

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
                parseDirectoryEntries(clusterData, dirPath, deletedFiles, subdirectoryClusters, subdirectoryNames, false);

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
                System.Diagnostics.Debug.WriteLine($"Error scanning cluster {cluster}: {ex.Message}");
                }
            }

            // Calculates byte offset for cluster
            public static long GetClusterOffset(FAT32BootSector bootSector, uint cluster)
        {
            long firstDataSector = bootSector.ReservedSectors + (bootSector.NumberOfFATs * bootSector.SectorsPerFAT);
            long clusterSector = firstDataSector + ((cluster - 2) * bootSector.SectorsPerCluster);
                return clusterSector * bootSector.BytesPerSector;
            }

            // Reads FAT table entry value
            public static uint ReadFATEntry(Stream diskStream, FAT32BootSector bootSector, uint cluster)
        {
            try
            {
                long fatOffset = bootSector.ReservedSectors * bootSector.BytesPerSector;
                long entryOffset = fatOffset + (cluster * 4);

                byte[] entryBytes = new byte[4];
                diskStream.Position = entryOffset;
                diskStream.Read(entryBytes, 0, 4);

                uint value = BitConverter.ToUInt32(entryBytes, 0) & 0x0FFFFFFF;
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
            return cluster >= FAT32_MIN_CLUSTER && cluster < FAT32_EOC_MARKER;
            }
        }

        // Stores FAT32 boot sector data
        public class FAT32BootSector
        {
        // Sector size in bytes
        public ushort BytesPerSector { get; set; }
        // Cluster size in sectors
        public byte SectorsPerCluster { get; set; }
        // Count of reserved sectors
        public ushort ReservedSectors { get; set; }
        // Number of FAT copies
        public byte NumberOfFATs { get; set; }
        // FAT table size in sectors
        public uint SectorsPerFAT { get; set; }
        // Root directory first cluster
        public uint RootCluster { get; set; }
    }
}
