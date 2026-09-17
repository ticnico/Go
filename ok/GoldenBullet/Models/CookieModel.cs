using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GoldenBullet.Models
{
    public class CookieModel : INotifyPropertyChanged
    {
        private string _name;
        private string _value;
        private string _domain;
        private string _path;
        private DateTime? _expires;
        private bool _httpOnly;
        private bool _secure;
        private string _sameSite;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public string Domain
        {
            get => _domain;
            set { _domain = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public DateTime? Expires
        {
            get => _expires;
            set { _expires = value; OnPropertyChanged(); }
        }

        public bool HttpOnly
        {
            get => _httpOnly;
            set { _httpOnly = value; OnPropertyChanged(); }
        }

        public bool Secure
        {
            get => _secure;
            set { _secure = value; OnPropertyChanged(); }
        }

        public string SameSite
        {
            get => _sameSite;
            set { _sameSite = value; OnPropertyChanged(); }
        }

        public bool IsSession => !Expires.HasValue;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{Name}={Value}";
        }
    }
}
