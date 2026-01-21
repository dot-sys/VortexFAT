using System;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Provides formatting utilities for display
    public static class FormatHelper
    {
        // Converts bytes to readable format
        public static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            if (len >= 1)
            {
                long wholePart = (long)len;
                return $"{wholePart} {sizes[order]}";
            }
            else
            {
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
}
