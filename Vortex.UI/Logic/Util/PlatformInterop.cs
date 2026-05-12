using System;

namespace Drives.Util
{
    public static class PlatformInterop
    {
        public delegate bool GetDiskFreeSpaceDelegate(
            string lpRootPathName,
            out uint lpSectorsPerCluster,
            out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters,
            out uint lpTotalNumberOfClusters);

        public static Func<string, string> CheckSignature { get; set; } = (path) => "Unsigned";
        public static GetDiskFreeSpaceDelegate GetDiskFreeSpace { get; set; } = null;
    }
}
