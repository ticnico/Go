using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GoldenBullet.Models
{
    public class CookieJarModel : INotifyPropertyChanged
    {
        private string _name;
        private string _filePath;
        private DateTime _lastModified;
        private ObservableCollection<CookieModel> _cookies;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CookieModel> Cookies
        {
            get => _cookies;
            set { _cookies = value; OnPropertyChanged(); }
        }

        public int CookieCount => Cookies?.Count ?? 0;

        public CookieJarModel()
        {
            Cookies = new ObservableCollection<CookieModel>();
            LastModified = DateTime.Now;
        }

        public void AddCookie(CookieModel cookie)
        {
            Cookies.Add(cookie);
            LastModified = DateTime.Now;
            OnPropertyChanged(nameof(CookieCount));
        }

        public void RemoveCookie(CookieModel cookie)
        {
            Cookies.Remove(cookie);
            LastModified = DateTime.Now;
            OnPropertyChanged(nameof(CookieCount));
        }

        public void ClearCookies()
        {
            Cookies.Clear();
            LastModified = DateTime.Now;
            OnPropertyChanged(nameof(CookieCount));
        }

        public async Task SaveToFileAsync(string path = null)
        {
            var savePath = path ?? FilePath;
            if (string.IsNullOrEmpty(savePath))
                throw new InvalidOperationException("No file path specified");

            var json = JsonSerializer.Serialize(Cookies, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(savePath, json);
            FilePath = savePath;
            LastModified = DateTime.Now;
        }

        public async Task LoadFromFileAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Cookie file not found", path);

            var json = await File.ReadAllTextAsync(path);
            var cookies = JsonSerializer.Deserialize<ObservableCollection<CookieModel>>(json);

            Cookies = cookies ?? new ObservableCollection<CookieModel>();
            FilePath = path;
            LastModified = File.GetLastWriteTime(path);
            OnPropertyChanged(nameof(CookieCount));
        }

        public string ExportToNetscapeFormat()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Netscape HTTP Cookie File");
            sb.AppendLine("# https://curl.se/rfc/cookie_spec.html");
            sb.AppendLine();

            foreach (var cookie in Cookies)
            {
                var domain = cookie.Domain.StartsWith(".") ? cookie.Domain : "." + cookie.Domain;
                var includeSubdomains = cookie.Domain.StartsWith(".") ? "TRUE" : "FALSE";
                var path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path;
                var secure = cookie.Secure ? "TRUE" : "FALSE";
                var expires = cookie.Expires.HasValue
                    ? ((DateTimeOffset)cookie.Expires.Value).ToUnixTimeSeconds().ToString()
                    : "0";

                sb.AppendLine($"{domain}\t{includeSubdomains}\t{path}\t{secure}\t{expires}\t{cookie.Name}\t{cookie.Value}");
            }

            return sb.ToString();
        }

        public void ImportFromNetscapeFormat(string content)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length >= 7)
                {
                    var cookie = new CookieModel
                    {
                        Domain = parts[0].TrimStart('.'),
                        Path = parts[2],
                        Secure = parts[3] == "TRUE",
                        Expires = parts[4] != "0"
                            ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[4])).DateTime
                            : null,
                        Name = parts[5],
                        Value = parts[6]
                    };
                    Cookies.Add(cookie);
                }
            }

            LastModified = DateTime.Now;
            OnPropertyChanged(nameof(CookieCount));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
