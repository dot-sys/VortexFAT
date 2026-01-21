using System;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Calculates cluster size information
    public static class ClusterHelper
    {
        // Fallback cluster size value
        private const long DEFAULT_CLUSTER_SIZE = 4096;

        // Retrieves drive cluster size
        public static long GetClusterSize(string driveLetter)
        {
            try
            {
                var drive = driveLetter.TrimEnd('\\');

                if (NativeMethods.GetDiskFreeSpace(drive + "\\",
                    out uint sectorsPerCluster,
                    out uint bytesPerSector,
                    out uint numberOfFreeClusters,
                    out uint totalNumberOfClusters))
                {
                    return sectorsPerCluster * bytesPerSector;
                }
            }
            catch { }

            return DEFAULT_CLUSTER_SIZE;
        }
    }
}
