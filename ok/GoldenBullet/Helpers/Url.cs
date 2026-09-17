using System.Diagnostics;

namespace GoldenBullet.Helpers
{
    public static class Url
    {
        public static void Open(string url)
        {
            var sInfo = new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            };

            Process.Start(sInfo);
        }
    }
}
