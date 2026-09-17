namespace GoldenBullet.Licensing.Models
{
    public class LicenseModel
    {
        public string LicenseId { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string ProductName { get; set; } = "GoldenBullet";
        public string ProductVersion { get; set; } = "1.0.0";
        public string Signature { get; set; } = string.Empty;
    }

    public class LicenseFileModel
    {
        public string Data { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }
}
