using GoldenBullet.Licensing.Services;
using RuriLib.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Core.Services
{
    public enum ActivationStatus { Valid, Invalid, Error }

    public static class Validate
    {
        private static pipo? cachedLicense;

        public static async Task<ActivationStatus> CheckActivationAsync()
        {
            // 1. Check Offline First (Works without internet, faster)
            if (OfflineLicenseManager.IsOfflineActivated())
            {
                cachedLicense = OfflineLicenseManager.GetPayload();
                if (cachedLicense != null)
                {
                    Debug.WriteLine("✅ Offline validation: LICENSE VALID");
                    return ActivationStatus.Valid;
                }
            }

            // 2. Fallback to Online
            Debug.WriteLine("🌐 Checking online license...");
            var result = await LicenseApiClient.ValidateHwidAsync();

            if (result.Success && result.License != null)
            {
                cachedLicense = new pipo
                {
                    LicenseId = result.License.LicenseId,
                    CustomerEmail = result.License.CustomerEmail,
                    CustomerName = result.License.CustomerName,
                    HardwareId = result.License.HardwareId,
                    IssueDate = result.License.IssueDate,
                    ExpiryDate = result.License.ExpiryDate ?? DateTime.MaxValue,
                    ProductName = result.License.ProductName,
                    ProductVersion = result.License.ProductVersion
                };

                Debug.WriteLine("✅ Online validation: LICENSE VALID");
                return ActivationStatus.Valid;
            }

            Debug.WriteLine($"❌ Validation failed: {result.Error}");
            cachedLicense = null;
            return ActivationStatus.Invalid;
        }

        public static async Task<(bool success, string message)> ActivateWithKeyAsync(string licenseKey)
        {
            // Check if it's an offline activation code (JSON format)
            if (licenseKey.Contains("\"data\"") && licenseKey.Contains("\"signature\""))
            {
                bool offlineSuccess = OfflineLicenseManager.ActivateOffline(licenseKey);
                if (offlineSuccess)
                {
                    await CheckActivationAsync(); // Refresh cache
                    return (true, "✅ Offline activation successful!");
                }
                return (false, "❌ Invalid offline activation code or HWID mismatch.");
            }

            // Otherwise, try online activation
            var result = await LicenseApiClient.ActivateLicenseAsync(licenseKey);
            if (result.Success)
            {
                await CheckActivationAsync(); // Refresh cache
                return (true, result.Message);
            }

            return (false, result.Error);
        }

        public static async Task<bool> DeactivateAsync()
        {
            OfflineLicenseManager.DeactivateOffline();
            var result = await LicenseApiClient.DeactivateHwidAsync();
            cachedLicense = null;
            Debug.WriteLine($"🌐 Deactivation: {result.Success}");
            return result.Success;
        }

        public static pipo? GetLicenseInfo() => cachedLicense;
        public static bool IsActivated() => cachedLicense != null;
        public static void ClearCache() => cachedLicense = null;
    }
}
