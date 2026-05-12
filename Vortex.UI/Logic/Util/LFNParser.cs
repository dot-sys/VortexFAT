using System;
using System.Text;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Handles long filename entry parsing
    public static class LFNParser
    {
        // Rebuilds long filename from entries
        public static string ReconstructLongFileName(byte[][] lfnEntries)
        {
            try
            {
                Array.Reverse(lfnEntries);
                StringBuilder longName = new StringBuilder();

                foreach (var lfnEntry in lfnEntries)
                {
                    longName.Append(ExtractLFNChars(lfnEntry, 1, 10));
                    longName.Append(ExtractLFNChars(lfnEntry, 14, 25));
                    longName.Append(ExtractLFNChars(lfnEntry, 28, 31));
                }

                return longName.ToString().TrimEnd('\0', '\xFF');
            }
            catch
            {
                return null;
                }
            }

            // Extracts characters from LFN entry
            private static string ExtractLFNChars(byte[] buffer, int start, int end)
        {
            StringBuilder chars = new StringBuilder();

            for (int i = start; i <= end; i += 2)
            {
                if (i + 1 < buffer.Length)
                {
                    char c = BitConverter.ToChar(buffer, i);
                    if (c != 0 && c != 0xFFFF)
                    {
                        chars.Append(c);
                    }
                }
            }

                return chars.ToString();
            }

            // Computes checksum for LFN validation
            public static byte CalculateLFNChecksum(byte[] shortNameBytes)
        {
            byte checksum = 0;
            for (int i = 0; i < 11; i++)
            {
                checksum = (byte)(((checksum & 1) << 7) + (checksum >> 1) + shortNameBytes[i]);
            }
                return checksum;
            }

            // Recovers first character from short name
            public static string RecoverFirstCharacter(string lfnWithPlaceholder, string shortName)
        {
            try
            {
                if (string.IsNullOrEmpty(lfnWithPlaceholder))
                    return lfnWithPlaceholder;

                if (!lfnWithPlaceholder.StartsWith("?"))
                    return lfnWithPlaceholder;

                if (string.IsNullOrEmpty(shortName) || shortName.StartsWith("?"))
                    return lfnWithPlaceholder;

                char firstCharFromShort = shortName[0];

                if (lfnWithPlaceholder.Length > 1)
                {
                    char secondChar = lfnWithPlaceholder[1];

                    if (char.IsLower(secondChar) && char.IsUpper(firstCharFromShort))
                    {
                        firstCharFromShort = char.ToLower(firstCharFromShort);
                    }
                }

                return firstCharFromShort + lfnWithPlaceholder.Substring(1);
            }
            catch
            {
                return lfnWithPlaceholder;
            }
        }
    }
}
