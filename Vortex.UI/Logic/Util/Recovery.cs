using Drives.Core;
using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Handles file recovery operations
    public static class Recovery
    {
        // Checks administrator privilege status
        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            // Recovers deleted file to path
            public static bool RecoverFile(FileEntry fileEntry, string outputPath, string driveLetter, bool isExFAT, bool isFAT16)
        {
            if (fileEntry == null || fileEntry.IsDirectory)
            {
                throw new ArgumentException("Error Code:4");
            }

            if (fileEntry.StartCluster < 2)
            {
                throw new InvalidOperationException("Error Code:5");
            }

            try
            {
                if (!IsAdministrator())
                {
                    throw new UnauthorizedAccessException("Error Code:2");
                }

                string drivePath = $@"\\.\{driveLetter.TrimEnd('\\')}";

                using (var driveStream = new FileStream(drivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false))
                {
                    byte[] fileData;
                    long clusterSize;

                    if (isExFAT)
                    {
                        var bootSector = ExFAT.ReadBootSector(driveStream) ?? throw new InvalidOperationException("Error Code:6");
                        clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;

                        fileData = RecoverContiguousFile(driveStream, bootSector, (uint)fileEntry.StartCluster, fileEntry.FileSize);

                        if ((fileData == null || fileData.Length < fileEntry.FileSize / 2) && fileEntry.FileSize > clusterSize * 2)
                        {
                            fileData = RecoverFileData(
                                driveStream,
                                (uint)fileEntry.StartCluster,
                                fileEntry.FileSize,
                                clusterSize,
                                "exFAT",
                                cluster => ExFAT.ReadFATEntry(driveStream, bootSector, cluster),
                                cluster => ExFAT.GetClusterOffset(bootSector, cluster),
                                ExFAT.IsValidCluster);
                        }
                    }
                    else if (isFAT16)
                    {
                        var bootSector = FAT16.ReadBootSector(driveStream) ?? throw new InvalidOperationException("Error Code:7");
                        clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                        fileData = RecoverFileData(
                            driveStream,
                            (uint)fileEntry.StartCluster,
                            fileEntry.FileSize,
                            clusterSize,
                            "FAT16",
                            cluster => FAT16.ReadFATEntry(driveStream, bootSector, cluster),
                            cluster => FAT16.GetClusterOffset(bootSector, cluster),
                            FAT16.IsValidCluster,
                            isFAT16: true);
                    }
                    else
                    {
                        var bootSector = FAT32.ReadBootSector(driveStream) ?? throw new InvalidOperationException("Error Code:8");
                        clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                        fileData = RecoverFileData(
                            driveStream,
                            (uint)fileEntry.StartCluster,
                            fileEntry.FileSize,
                            clusterSize,
                            "FAT32",
                            cluster => FAT32.ReadFATEntry(driveStream, bootSector, cluster),
                            cluster => FAT32.GetClusterOffset(bootSector, cluster),
                            FAT32.IsValidCluster);
                    }

                    if (fileData != null && fileData.Length > 0)
                    {
                        File.WriteAllBytes(outputPath, fileData);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                throw;
                }
            }

            // Reads file data from clusters
            private static byte[] RecoverFileData(
            Stream diskStream,
            uint startCluster,
            long fileSize,
            long clusterSize,
            string fileSystemType,
            Func<uint, uint> readFATEntry,
            Func<uint, long> getClusterOffset,
            Func<uint, bool> isValidCluster,
            bool isFAT16 = false)
        {
            var data = new List<byte>();
            int clustersNeeded = (int)((fileSize + clusterSize - 1) / clusterSize);
            long bytesRemaining = fileSize;


            var clusterChain = BuildClusterChain(startCluster, clustersNeeded, readFATEntry, isValidCluster, isFAT16);

            foreach (uint cluster in clusterChain)
            {
                if (bytesRemaining <= 0)
                    break;

                long clusterOffset = getClusterOffset(cluster);
                long bytesToRead = Math.Min(clusterSize, bytesRemaining);

                byte[] clusterData = new byte[bytesToRead];
                diskStream.Position = clusterOffset;
                int bytesRead = diskStream.Read(clusterData, 0, (int)bytesToRead);

                if (bytesRead > 0)
                {
                    data.AddRange(clusterData.Take(bytesRead));
                    bytesRemaining -= bytesRead;
                }
                else
                {
                }
            }

                return data.ToArray();
            }

            // Reads contiguous exFAT file data
            private static byte[] RecoverContiguousFile(Stream diskStream, ExFATBootSector bootSector, uint startCluster, long fileSize)
        {
            long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
            int clustersNeeded = (int)((fileSize + clusterSize - 1) / clusterSize);
            var data = new List<byte>();
            long bytesRemaining = fileSize;


            for (int i = 0; i < clustersNeeded && bytesRemaining > 0; i++)
            {
                uint cluster = startCluster + (uint)i;
                long clusterOffset = ExFAT.GetClusterOffset(bootSector, cluster);
                long bytesToRead = Math.Min(clusterSize, bytesRemaining);

                byte[] clusterData = new byte[bytesToRead];
                diskStream.Position = clusterOffset;
                int bytesRead = diskStream.Read(clusterData, 0, (int)bytesToRead);

                if (bytesRead > 0)
                {
                    data.AddRange(clusterData.Take(bytesRead));
                    bytesRemaining -= bytesRead;
                }
                else
                {
                    break;
                }
            }

                return data.ToArray();
            }

            // Constructs chain of file clusters
            private static List<uint> BuildClusterChain(uint startCluster, int clustersNeeded, Func<uint, uint> readFATEntry, Func<uint, bool> isValidCluster, bool isFAT16 = false)
        {
            var clusterChain = new List<uint>();

            if (isFAT16 && clustersNeeded > 20)
            {
                for (int i = 0; i < clustersNeeded; i++)
                {
                    clusterChain.Add(startCluster + (uint)i);
                }
                return clusterChain;
            }

            var visitedClusters = new HashSet<uint>();
            uint currentCluster = startCluster;

            while (isValidCluster(currentCluster) && clusterChain.Count < clustersNeeded)
            {
                if (visitedClusters.Contains(currentCluster))
                {
                    break;
                }

                visitedClusters.Add(currentCluster);
                clusterChain.Add(currentCluster);

                uint nextCluster = readFATEntry(currentCluster);

                if (nextCluster == 0 || nextCluster == 1)
                {
                    break;
                }

                currentCluster = nextCluster;
            }

            if (clusterChain.Count < clustersNeeded)
            {
                clusterChain.Clear();
                for (int i = 0; i < clustersNeeded; i++)
                {
                    clusterChain.Add(startCluster + (uint)i);
                }
            }

            return clusterChain;
        }
    }
}
