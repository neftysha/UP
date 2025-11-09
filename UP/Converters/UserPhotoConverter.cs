using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UP.Converters
{
    public class UserPhotoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string path = value as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return new BitmapImage(new Uri(path, UriKind.Absolute));
                }

                // Фото нет — используем дефолтную картинку
                return new BitmapImage(new Uri("pack://application:,,,/Resources/default_user.png"));
            }
            catch
            {
                return new BitmapImage(new Uri("pack://application:,,,/Resources/default_user.png"));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
