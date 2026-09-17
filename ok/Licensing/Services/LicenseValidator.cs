using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using GoldenBullet.Licensing.Models;
using GoldenBullet.Licensing.Security;

namespace GoldenBullet.Licensing.Services
{
    public static class LicenseValidator
    {
        private const string RegistryPath = @"SOFTWARE\GoldenBullet\Native";
        private const string LicenseFileKey = "LicenseFile";

        public static bool IsActivated()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return false;

                string? licensePath = key.GetValue(LicenseFileKey)?.ToString();
                if (string.IsNullOrEmpty(licensePath) || !File.Exists(licensePath))
                    return false;

                return ValidateLicenseFile(licensePath);
            }
            catch
            {
                return false;
            }
        }

        public static bool Activate(string licenseFilePath)
        {
            if (!File.Exists(licenseFilePath)) return false;

            if (!ValidateLicenseFile(licenseFilePath))
                return false;

            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key.SetValue(LicenseFileKey, Path.GetFullPath(licenseFilePath));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ValidateLicenseFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var licenseFile = JsonSerializer.Deserialize<LicenseFileModel>(json);

                if (string.IsNullOrEmpty(licenseFile?.Data) || string.IsNullOrEmpty(licenseFile.Signature))
                    return false;

                if (!RSAKeyManager.VerifyData(licenseFile.Data, Convert.FromBase64String(licenseFile.Signature)))
                    return false;

                var license = JsonSerializer.Deserialize<LicenseModel>(licenseFile.Data);
                if (license == null) return false;

                string currentHwId = HardwareInfo.GetMachineID();
                if (license.HardwareId != currentHwId)
                    return false;

                if (license.ExpiryDate < DateTime.UtcNow)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Deactivate()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            }
            catch { }
        }

        public static LicenseModel? GetLicenseInfo()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return null;

                string? licensePath = key.GetValue(LicenseFileKey)?.ToString();
                if (string.IsNullOrEmpty(licensePath) || !File.Exists(licensePath))
                    return null;

                string json = File.ReadAllText(licensePath);
                var licenseFile = JsonSerializer.Deserialize<LicenseFileModel>(json);
                return JsonSerializer.Deserialize<LicenseModel>(licenseFile?.Data ?? "");
            }
            catch
            {
                return null;
            }
        }
    }
}
