using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Converts a full file path to just the filename for display
    /// </summary>
    public class FilenameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fullPath && !string.IsNullOrEmpty(fullPath))
            {
                try
                {
                    return Path.GetFileName(fullPath);
                }
                catch
                {
                    return fullPath;
                }
            }

            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
