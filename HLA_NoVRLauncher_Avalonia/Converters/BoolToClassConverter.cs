using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace HLA_NoVRLauncher_Avalonia.Converters
{
    public class BoolToClassConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "open" : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}