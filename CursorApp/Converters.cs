using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HololiveCursorApp
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBooleanToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class HasFileToBorderBrushConverter : IValueConverter
    {
        public static readonly HasFileToBorderBrushConverter Instance = new();
        private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x89, 0xB4, 0xFA)); // Primary Accent
        private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0x31, 0x32, 0x44)); // Dim Border

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return ActiveBrush;
            return InactiveBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class HasFileToTextBrushConverter : IValueConverter
    {
        public static readonly HasFileToTextBrushConverter Instance = new();
        private static readonly SolidColorBrush ActiveText = new(Color.FromRgb(0xA6, 0xE3, 0xA1)); // Success Green
        private static readonly SolidColorBrush InactiveText = new(Color.FromRgb(0x6C, 0x70, 0x86)); // Gray

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return ActiveText;
            return InactiveText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
