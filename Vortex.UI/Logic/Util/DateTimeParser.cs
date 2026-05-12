using System;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Parses FAT filesystem timestamps
    public static class DateTimeParser
    {
        // Converts FAT date and time
        public static DateTime? ParseFatDateTime(byte[] buffer, int dateOffset, int timeOffset)
        {
            try
            {
                ushort date = BitConverter.ToUInt16(buffer, dateOffset);
                ushort time = BitConverter.ToUInt16(buffer, timeOffset);

                if (date == 0 || time == 0xFFFF || date == 0xFFFF)
                    return null;

                int year = 1980 + ((date >> 9) & 0x7F);
                int month = (date >> 5) & 0x0F;
                int day = date & 0x1F;

                int hour = (time >> 11) & 0x1F;
                int minute = (time >> 5) & 0x3F;
                int second = (time & 0x1F) * 2;

                if (month >= 1 && month <= 12 && day >= 1 && day <= 31 && hour <= 23 && minute <= 59 && second <= 59)
                {
                    try
                    {
                        return new DateTime(year, month, day, hour, minute, second);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch { }

                return null;
            }

            // Converts FAT date only
            public static DateTime? ParseFatDate(byte[] buffer, int dateOffset)
        {
            try
            {
                ushort date = BitConverter.ToUInt16(buffer, dateOffset);

                if (date == 0 || date == 0xFFFF)
                    return null;

                int year = 1980 + ((date >> 9) & 0x7F);
                int month = (date >> 5) & 0x0F;
                int day = date & 0x1F;

                if (month >= 1 && month <= 12 && day >= 1 && day <= 31)
                {
                    try
                    {
                        return new DateTime(year, month, day);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch { }

                return null;
            }

            // Converts exFAT timestamp format
            public static DateTime? ParseExFATDateTime(byte[] buffer, int offset)
        {
            try
            {
                uint timestamp = BitConverter.ToUInt32(buffer, offset);

                if (timestamp == 0 || timestamp == 0xFFFFFFFF)
                    return null;

                int year = 1980 + (int)((timestamp >> 25) & 0x7F);
                int month = (int)((timestamp >> 21) & 0x0F);
                int day = (int)((timestamp >> 16) & 0x1F);
                int hour = (int)((timestamp >> 11) & 0x1F);
                int minute = (int)((timestamp >> 5) & 0x3F);
                int second = (int)((timestamp & 0x1F) * 2);

                if (month >= 1 && month <= 12 && day >= 1 && day <= 31 && hour <= 23 && minute <= 59 && second <= 59)
                {
                    try
                    {
                        return new DateTime(year, month, day, hour, minute, second);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
