using Drives.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Core filesystem analysis and recovery logic
namespace Drives.Core
{
    // Detects and enumerates available drives
    public class DriveDetector
    {
        // Retrieves all system drives list
        public static List<Models.DriveInfo> GetAllDrives()
        {
            var driveList = new List<Models.DriveInfo>();

            try
            {
                var systemDrives = System.IO.DriveInfo.GetDrives();

                foreach (var drive in systemDrives)
                {
                    try
                    {
                        if (!drive.IsReady)
                            continue;

                        var driveInfo = new Models.DriveInfo
                        {
                            DriveLetter = drive.Name.TrimEnd('\\'),
                            FileSystem = drive.DriveFormat,
                            Label = drive.VolumeLabel,
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace
                        };

                        driveList.Add(driveInfo);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
            }

                    return driveList;
                }

                // Returns only FAT filesystem drives
                public static List<Models.DriveInfo> GetFATDrives()
        {
            return GetAllDrives()
                .Where(d => d.IsSupported)
                .ToList();
        }
    }
}
