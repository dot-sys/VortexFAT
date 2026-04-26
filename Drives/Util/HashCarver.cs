using System;
using System.IO;
using System.Security.Cryptography;
using Drives.Core;
using Drives.Models;

namespace Drives.Util
{
    public static class HashCarver
    {
        private const long MAX_FILE_SIZE = 100 * 1024 * 1024;

        public static string CarveHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "N/A";
            if (!File.Exists(filePath))
                return "Deleted";

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                    return "Empty";
                if (fileInfo.Length > MAX_FILE_SIZE)
                    return "Too Large";

                using (var md5 = MD5.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "") + " (MD5)";
                }
            }
            catch (UnauthorizedAccessException) { return "Access Denied"; }
            catch (IOException) { return "I/O Error"; }
            catch (Exception) { return "Error"; }
        }

        public static string CarveHashFromData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "N/A";

            try
            {
                using (var md5 = MD5.Create())
                {
                    byte[] hashBytes = md5.ComputeHash(data);
                    return BitConverter.ToString(hashBytes).Replace("-", "") + " (MD5)";
                }
            }
            catch { return "Error"; }
        }

        public static string ComputeDeletedFileHash(FileEntry fileEntry, string driveLetter, bool isExFAT, bool isFAT16)
        {
            if (fileEntry == null || (!fileEntry.IsDeleted && fileEntry.Status != "Replaced"))
                return "N/A";
            if (fileEntry.IsDirectory || fileEntry.StartCluster < 2 || fileEntry.FileSize == 0)
                return "N/A";
            if (fileEntry.FileSize > MAX_FILE_SIZE)
                return "Too Large";

            try
            {
                if (!Recovery.IsAdministrator())
                    return "Admin Required";

                byte[] fileData = ReadDeletedFileData(fileEntry, driveLetter, isExFAT, isFAT16);
                if (fileData == null || fileData.Length == 0)
                    return "Unrecoverable";

                return CarveHashFromData(fileData);
            }
            catch (UnauthorizedAccessException) { return "Access Denied"; }
            catch { return "Error"; }
        }

        private static byte[] ReadDeletedFileData(FileEntry fileEntry, string driveLetter, bool isExFAT, bool isFAT16)
        {
            string drivePath = $"\\\\.\\{driveLetter.TrimEnd('\\')}";

            using (var driveStream = new FileStream(drivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false))
            {
                var data = new System.Collections.Generic.List<byte>();
                long bytesRemaining = fileEntry.FileSize;
                long clusterSize = 0;

                if (isExFAT)
                {
                    var bootSector = ExFAT.ReadBootSector(driveStream);
                    if (bootSector == null) return null;
                    clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    for (int i = 0; i < clustersNeeded && bytesRemaining > 0; i++)
                    {
                        uint cluster = (uint)(fileEntry.StartCluster + i);
                        long clusterOffset = ExFAT.GetClusterOffset(bootSector, cluster);
                        long bytesToRead = Math.Min(clusterSize, bytesRemaining);
                        byte[] clusterData = new byte[bytesToRead];
                        driveStream.Position = clusterOffset;
                        int bytesRead = driveStream.Read(clusterData, 0, (int)bytesToRead);
                        if (bytesRead > 0)
                        {
                            data.AddRange(clusterData);
                            bytesRemaining -= bytesRead;
                        }
                    }
                }
                else if (isFAT16)
                {
                    var bootSector = FAT16.ReadBootSector(driveStream);
                    if (bootSector == null) return null;
                    clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    for (int i = 0; i < clustersNeeded && bytesRemaining > 0; i++)
                    {
                        uint cluster = (uint)(fileEntry.StartCluster + i);
                        long clusterOffset = FAT16.GetClusterOffset(bootSector, cluster);
                        long bytesToRead = Math.Min(clusterSize, bytesRemaining);
                        byte[] clusterData = new byte[bytesToRead];
                        driveStream.Position = clusterOffset;
                        int bytesRead = driveStream.Read(clusterData, 0, (int)bytesToRead);
                        if (bytesRead > 0)
                        {
                            data.AddRange(clusterData);
                            bytesRemaining -= bytesRead;
                        }
                    }
                }
                else
                {
                    var bootSector = FAT32.ReadBootSector(driveStream);
                    if (bootSector == null) return null;
                    clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    for (int i = 0; i < clustersNeeded && bytesRemaining > 0; i++)
                    {
                        uint cluster = (uint)(fileEntry.StartCluster + i);
                        long clusterOffset = FAT32.GetClusterOffset(bootSector, cluster);
                        long bytesToRead = Math.Min(clusterSize, bytesRemaining);
                        byte[] clusterData = new byte[bytesToRead];
                        driveStream.Position = clusterOffset;
                        int bytesRead = driveStream.Read(clusterData, 0, (int)bytesToRead);
                        if (bytesRead > 0)
                        {
                            data.AddRange(clusterData);
                            bytesRemaining -= bytesRead;
                        }
                    }
                }

                return data.ToArray();
            }
        }
    }
}
