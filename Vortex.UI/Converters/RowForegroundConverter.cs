using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

// Value converters for XAML bindings
namespace Vortex.UI.Converters
{
    // Converts file status to row foreground color
    public class RowForegroundConverter : IMultiValueConverter
    {
        // White brush for normal files
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Colors.White);
        // Red brush for deleted files
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Colors.Red);
        // Gold brush for unsigned files
        private static readonly SolidColorBrush DarkGoldenrodBrush = new SolidColorBrush(Color.FromRgb(184, 134, 11));
        // Blue brush for hidden files
        private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237));

        // Converts multiple values to foreground brush
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 6)
                return WhiteBrush;

            bool unmarkDeleted = values[0] is bool b0 && b0;
            string status = values[1]?.ToString() ?? string.Empty;
            bool unmarkUnsigned = values[2] is bool b2 && b2;
            string signature = values[3]?.ToString() ?? string.Empty;
            bool unmarkHidden = values[4] is bool b4 && b4;
            bool isHidden = values[5] is bool b5 && b5;

            if (!unmarkDeleted && IsDeletedStatus(status))
                return RedBrush;

            if (!unmarkHidden && isHidden && IsPresentStatus(status))
                return BlueBrush;

            if (!unmarkUnsigned && IsUnsignedSignature(signature))
                return DarkGoldenrodBrush;

                return WhiteBrush;
            }

            // Not supported for this converter
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException("RowForegroundConverter does not support two-way binding");
            }

            // Checks if status indicates deleted file
            private static bool IsDeletedStatus(string status)
            {
                return !string.IsNullOrEmpty(status) &&
                       (status.Equals("Deleted", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("Replaced", StringComparison.OrdinalIgnoreCase));
            }

                    // Checks if status indicates present file
                    private static bool IsPresentStatus(string status)
                    {
                        return status.Equals("Present", StringComparison.OrdinalIgnoreCase);
                    }

                    // Checks if signature indicates unsigned file
                    private static bool IsUnsignedSignature(string signature)
                {
                    return !string.IsNullOrEmpty(signature) &&
                           signature.Equals("Unsigned", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
