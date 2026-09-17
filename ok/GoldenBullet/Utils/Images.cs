using System.IO;
using System.Windows.Media.Imaging;

namespace GoldenBullet.Utils
{
    public static class Images
    {
        public static BitmapImage Base64ToBitmapImage(string base64)
            => BytesToBitmapImage(Convert.FromBase64String(base64));

        public static BitmapImage BytesToBitmapImage(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();

            return image;
        }
    }
}
