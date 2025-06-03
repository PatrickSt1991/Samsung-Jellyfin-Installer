using System.Globalization;
using System.Windows.Data;

namespace Samsung_Jellyfin_Installer.Converters
{
    public class ContentWidthConverter : IValueConverter
    {
        public double Padding { get; set; } = 10;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double actualWidth)
            {
                return actualWidth + Padding;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}