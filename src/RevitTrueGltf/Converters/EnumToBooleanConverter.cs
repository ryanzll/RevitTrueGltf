using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RevitTrueGltf.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            string checkValue = value.ToString();
            string targetValue = parameter.ToString();

            return checkValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null || !(bool)value)
                return DependencyProperty.UnsetValue;

            // TargetType is the Enum type
            return Enum.Parse(targetType, parameter.ToString());
        }
    }
}
