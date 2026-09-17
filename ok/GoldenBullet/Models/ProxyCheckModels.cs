namespace GoldenBullet.Models
{
    public class ProxyInfo
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Raw { get; set; }
        public string Country { get; set; }
        public bool IsWorking { get; set; }
        public long ResponseTime { get; set; }
        public GeoLocationInfo GeoInfo { get; set; }
        public int DbId { get; set; }  // ✅ NEW: Store DB ID for reliable updates
        public override string ToString() => Raw ?? $"{Host}:{Port}";
    }

    public class GeoLocationInfo
    {
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string ISP { get; set; }
        public string Timezone { get; set; }
    }
}
