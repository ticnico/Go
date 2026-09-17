using GoldenBullet.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace GoldenBullet.Services
{
    public class CookieManagerService
    {
        private static readonly Lazy<CookieManagerService> _instance =
            new Lazy<CookieManagerService>(() => new CookieManagerService());

        public static CookieManagerService Instance => _instance.Value;

        public ObservableCollection<CookieJarModel> CookieJars { get; private set; }
        public string StorageDirectory { get; set; }

        private CookieManagerService()
        {
            CookieJars = new ObservableCollection<CookieJarModel>();
            StorageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GoldenBullet", "Cookies");

            Directory.CreateDirectory(StorageDirectory);
            LoadSavedJars();
        }

        public CookieJarModel CreateNewJar(string name)
        {
            var jar = new CookieJarModel
            {
                Name = name,
                FilePath = Path.Combine(StorageDirectory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.json")
            };

            CookieJars.Add(jar);
            return jar;
        }

        public void DeleteJar(CookieJarModel jar)
        {
            if (File.Exists(jar.FilePath))
                File.Delete(jar.FilePath);

            CookieJars.Remove(jar);
        }

        public async Task SaveJarAsync(CookieJarModel jar)
        {
            await jar.SaveToFileAsync();
        }

        public async Task<CookieJarModel> ImportJarFromFileAsync(string filePath)
        {
            var jar = new CookieJarModel();

            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await jar.LoadFromFileAsync(filePath);
            }
            else if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                // Assume Netscape format
                var content = await File.ReadAllTextAsync(filePath);
                jar.ImportFromNetscapeFormat(content);
                jar.Name = Path.GetFileNameWithoutExtension(filePath);
                jar.FilePath = Path.Combine(StorageDirectory, $"{jar.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            }

            CookieJars.Add(jar);
            return jar;
        }

        public async Task ExportJarToNetscapeFormatAsync(CookieJarModel jar, string filePath)
        {
            var content = jar.ExportToNetscapeFormat();
            await File.WriteAllTextAsync(filePath, content);
        }

        public async Task ImportFromBrowserAsync(string browserName, string profilePath = null)
        {
            // Implementation for importing from Chrome, Firefox, Edge
            var cookies = await BrowserCookieImporter.ImportAsync(browserName, profilePath);
            var jar = CreateNewJar($"{browserName}_Import_{DateTime.Now:yyyyMMdd_HHmmss}");

            foreach (var cookie in cookies)
            {
                jar.AddCookie(cookie);
            }
        }

        private void LoadSavedJars()
        {
            if (!Directory.Exists(StorageDirectory))
                return;

            var files = Directory.GetFiles(StorageDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var jar = new CookieJarModel();
                    jar.LoadFromFileAsync(file).Wait();
                    CookieJars.Add(jar);
                }
                catch { /* Skip corrupted files */ }
            }
        }
    }

    public static class BrowserCookieImporter
    {
        public static async Task<System.Collections.Generic.List<CookieModel>> ImportAsync(string browserName, string profilePath)
        {
            var cookies = new System.Collections.Generic.List<CookieModel>();

            // Chrome implementation
            if (browserName.ToLower() == "chrome")
            {
                var chromePath = profilePath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data", "Default", "Cookies");

                // Note: Chrome cookies are SQLite encrypted, this is a simplified version
                // You may need to use SQLitePCLRaw or similar to read Chrome's SQLite DB
                // and decrypt using Windows DPAPI
            }

            // Firefox implementation
            else if (browserName.ToLower() == "firefox")
            {
                var firefoxPath = profilePath ?? FindFirefoxProfile();
                if (!string.IsNullOrEmpty(firefoxPath))
                {
                    var cookiesFile = Path.Combine(firefoxPath, "cookies.sqlite");
                    // Read from SQLite database
                }
            }

            await Task.CompletedTask; // Placeholder for async operations
            return cookies;
        }

        private static string FindFirefoxProfile()
        {
            var firefoxDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (Directory.Exists(firefoxDir))
            {
                var profiles = Directory.GetDirectories(firefoxDir, "*.default*");
                return profiles.FirstOrDefault();
            }

            return null;
        }
    }
}
