using System;
using System.Text.Json.Serialization;

namespace RuriLib.Services
{
    public class pipo
    {
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; set; } = string.Empty;

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("hardwareId")]
        public string HardwareId { get; set; } = string.Empty;

        [JsonPropertyName("issueDate")]
        public DateTime IssueDate { get; set; }

        [JsonPropertyName("expiryDate")]
        public DateTime ExpiryDate { get; set; }

        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = "GoldenBullet";

        [JsonPropertyName("productVersion")]
        public string ProductVersion { get; set; } = "2.2";

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;
    }

    public class LicenseFileModel
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;
    }
}
