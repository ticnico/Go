using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GoldenBullet.Licensing.Services
{
    public static class LicenseApiClient
    {
        private const string API_BASE_URL = "http://ticnico.atwebpages.com/";
        private const string API_KEY = "GB-Key-2026-X7k9mP2qR5vL8wN3jH4";

        private static readonly HttpClient _httpClient = new HttpClient();

        static LicenseApiClient()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", API_KEY);
        }

        public static async Task<OnlineValidationResult> ValidateHwidAsync()
        {
            try
            {
                string hwid = Info.GetMachineID();
                var form = new Dictionary<string, string> { { "hwid", hwid } };
                var content = new FormUrlEncodedContent(form);

                var response = await _httpClient.PostAsync(API_BASE_URL + "validate_hwid.php", content);
                var rawResponse = await response.Content.ReadAsStringAsync();

                // 🔍 DEBUG: Log the raw response
                System.Diagnostics.Debug.WriteLine($"🌐 RAW RESPONSE: {rawResponse}");
                System.Diagnostics.Debug.WriteLine($"🌐 STATUS CODE: {response.StatusCode}");

                return JsonSerializer.Deserialize<OnlineValidationResult>(rawResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new OnlineValidationResult { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                return new OnlineValidationResult { Success = false, Error = ex.Message };
            }
        }

        public static async Task<OnlineValidationResult> ActivateLicenseAsync(string licenseKey)
        {
            try
            {
                string hwid = Info.GetMachineID();
                var form = new Dictionary<string, string>
                {
                    { "license_key", licenseKey },
                    { "hwid", hwid }
                };
                var content = new FormUrlEncodedContent(form);

                var response = await _httpClient.PostAsync(API_BASE_URL + "activate_license.php", content);
                var json = await response.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<OnlineValidationResult>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new OnlineValidationResult { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                return new OnlineValidationResult { Success = false, Error = ex.Message };
            }
        }

        public static async Task<OnlineValidationResult> DeactivateHwidAsync()
        {
            try
            {
                string hwid = Info.GetMachineID();
                var form = new Dictionary<string, string> { { "hwid", hwid } };
                var content = new FormUrlEncodedContent(form);

                var response = await _httpClient.PostAsync(API_BASE_URL + "deactivate_license.php", content);
                var json = await response.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<OnlineValidationResult>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new OnlineValidationResult { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                return new OnlineValidationResult { Success = false, Error = ex.Message };
            }
        }
    }

    public class OnlineValidationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("license")]
        public ApiLicenseInfo? License { get; set; }
    }

    public class ApiLicenseInfo
    {
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; set; } = "";

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; set; } = "";

        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = "";

        [JsonPropertyName("hardwareId")]
        public string HardwareId { get; set; } = "";

        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = "GoldenBullet";

        [JsonPropertyName("productVersion")]
        public string ProductVersion { get; set; } = "2.2";

        [JsonPropertyName("issueDate")]
        public DateTime IssueDate { get; set; }

        [JsonPropertyName("expiryDate")]
        public DateTime? ExpiryDate { get; set; }
    }
}
