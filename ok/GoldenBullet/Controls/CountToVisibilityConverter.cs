using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GoldenBullet.Controls
{
    /// <summary>
    /// Converts a count value to Visibility. 
    /// If count is 0, returns Visible (or Collapsed if Inverted).
    /// If count > 0, returns Collapsed (or Visible if Inverted).
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public bool Inverted { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;

            if (value is int intValue)
            {
                count = intValue;
            }
            else if (value is ICollection collection)
            {
                count = collection.Count;
            }
            else if (value is IEnumerable enumerable && value is not string)
            {
                // Count items in IEnumerable
                var enumerator = enumerable.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    count++;
                }
            }

            bool isVisible = count == 0;
            if (Inverted)
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
