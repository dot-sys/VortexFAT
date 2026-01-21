using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Drives.Core;
using Drives.Models;

// Utility classes for recovery operations
namespace Drives.Util
{
    // Computes and extracts hash values
    public static class HashCarver
    {
        // Maximum file size for hash computation
        private const long MAX_FILE_SIZE = 100 * 1024 * 1024;
        // MD5 hash pattern matcher
        private static readonly Regex Md5Regex = new Regex(@"\b[a-fA-F0-9]{32}\b", RegexOptions.Compiled);
        // SHA1 hash pattern matcher
        private static readonly Regex Sha1Regex = new Regex(@"\b[a-fA-F0-9]{40}\b", RegexOptions.Compiled);
        // SHA256 hash pattern matcher
        private static readonly Regex Sha256Regex = new Regex(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);

        // Computes hash for present files only
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

                using (var stream = File.OpenRead(filePath))
                {
                    using (var md5 = MD5.Create())
                    {
                        byte[] hashBytes = md5.ComputeHash(stream);
                        string hashString = BitConverter.ToString(hashBytes).Replace("-", "");
                        return $"{hashString} (MD5)";
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return "Access Denied";
            }
            catch (IOException)
            {
                return "I/O Error";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error computing hash for {filePath}: {ex.Message}");
                return "Error";
            }
        }

        // Computes hash from raw byte data
        public static string CarveHashFromData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "N/A";

            try
            {
                using (var md5 = MD5.Create())
                {
                    byte[] hashBytes = md5.ComputeHash(data);
                    string hashString = BitConverter.ToString(hashBytes).Replace("-", "");
                    return $"{hashString} (MD5)";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error computing hash from data: {ex.Message}");
                return "Error";
            }
        }

        // Computes hash for deleted file on-demand
        public static string ComputeDeletedFileHash(FileEntry fileEntry, string driveLetter, bool isExFAT, bool isFAT16)
        {
            if (fileEntry == null)
                return "N/A";

            if (!fileEntry.IsDeleted && fileEntry.Status != "Replaced")
                return "Not Deleted";

            if (fileEntry.IsDirectory)
                return "N/A";

            if (fileEntry.StartCluster < 2)
                return "Invalid Cluster";

            if (fileEntry.FileSize == 0)
                return "Empty";

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
            catch (UnauthorizedAccessException)
            {
                return "Access Denied";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error computing deleted file hash: {ex.Message}");
                return "Error";
            }
        }

        // Reads deleted file data from disk
        private static byte[] ReadDeletedFileData(FileEntry fileEntry, string driveLetter, bool isExFAT, bool isFAT16)
        {
            string drivePath = $"\\\\.\\{driveLetter.TrimEnd('\\')}";

            using (var driveStream = new FileStream(drivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.None))
            {
                if (isExFAT)
                {
                    var bootSector = ExFAT.ReadBootSector(driveStream);
                    if (bootSector == null)
                        return null;

                    long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    var data = new System.Collections.Generic.List<byte>();
                    long bytesRemaining = fileEntry.FileSize;

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
                            data.AddRange(clusterData.Take(bytesRead));
                            bytesRemaining -= bytesRead;
                        }
                    }

                    return data.ToArray();
                }
                else if (isFAT16)
                {
                    var bootSector = FAT16.ReadBootSector(driveStream);
                    if (bootSector == null)
                        return null;

                    long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    var data = new System.Collections.Generic.List<byte>();
                    long bytesRemaining = fileEntry.FileSize;

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
                            data.AddRange(clusterData.Take(bytesRead));
                            bytesRemaining -= bytesRead;
                        }
                    }

                    return data.ToArray();
                }
                else
                {
                    var bootSector = FAT32.ReadBootSector(driveStream);
                    if (bootSector == null)
                        return null;

                    long clusterSize = bootSector.BytesPerSector * bootSector.SectorsPerCluster;
                    int clustersNeeded = (int)((fileEntry.FileSize + clusterSize - 1) / clusterSize);

                    var data = new System.Collections.Generic.List<byte>();
                    long bytesRemaining = fileEntry.FileSize;

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
                            data.AddRange(clusterData.Take(bytesRead));
                            bytesRemaining -= bytesRead;
                        }
                    }

                    return data.ToArray();
                }
            }
        }
    }
}
