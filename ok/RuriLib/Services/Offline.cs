using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RuriLib.Services;

namespace GoldenBullet.Licensing.Services
{
    public static class OfflineLicenseManager
    {
        // ✅ YOUR PUBLIC KEY IS NOW SECURELY EMBEDDED
        private const string PUBLIC_KEY_PEM = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA9vcaG51V1M1v3AX3CReO
cH1IT/hNZDZSwUNhxzFFzmA+NWuD1v6oMo7ouoZrbRpQGMqJJScCQE53/pl1w9Ud
orOMKtc3XRsjoQEXu9R1v3VMF4Y9ugFft+T8PAG1XAh1/CDM15BzVTfxcoZaO7Mu
rA9bKgPGYFKMOc6Wrz6CWsxIfnVdNOZBYMc+QBdWwgIWFcxR6tlYGKq3OM2kLXwO
e7QjRQYpSPs/KURMLnJVp8wF9c5EPhGje0oIISAMiFj4fsiTQNldX7xOTzQTdQ8Y
Ngwm/t6Lo3hvsAnT+sJcSfxdpV7R8cW5zpmCvX2puuJpXljXenmDjhfdlca2o6r6
IwIDAQAB
-----END PUBLIC KEY-----";

        private const string LicenseFileName = "gb_license.dat";

        public static bool IsOfflineActivated()
        {
            if (!File.Exists(LicenseFileName)) return false;
            try
            {
                string licenseData = File.ReadAllText(LicenseFileName);
                return VerifyLicense(licenseData);
            }
            catch { return false; }
        }

        public static bool VerifyLicense(string licenseData)
        {
            try
            {
                var licenseFile = JsonSerializer.Deserialize<LicenseFileModel>(licenseData);
                if (licenseFile == null || string.IsNullOrEmpty(licenseFile.Data) || string.IsNullOrEmpty(licenseFile.Signature))
                    return false;

                byte[] signatureBytes = Convert.FromBase64String(licenseFile.Signature);
                byte[] dataBytes = Convert.FromBase64String(licenseFile.Data);

                using var rsa = RSA.Create();
                rsa.ImportFromPem(PUBLIC_KEY_PEM);

                // 1. Verify the cryptographic signature
                if (!rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return false;

                // 2. Deserialize the payload into your 'pipo' class
                var payload = JsonSerializer.Deserialize<pipo>(Encoding.UTF8.GetString(dataBytes));
                if (payload == null) return false;

                // 3. Check if HWID matches current machine
                if (payload.HardwareId != Info.GetMachineID())
                    return false;

                // 4. Check Expiry Date
                if (payload.ExpiryDate < DateTime.UtcNow)
                    return false;

                return true;
            }
            catch { return false; }
        }

        public static bool ActivateOffline(string licenseData)
        {
            if (VerifyLicense(licenseData))
            {
                File.WriteAllText(LicenseFileName, licenseData);
                return true;
            }
            return false;
        }

        public static void DeactivateOffline()
        {
            if (File.Exists(LicenseFileName))
                File.Delete(LicenseFileName);
        }

        public static string GetRequestCode()
        {
            return Info.GetMachineID();
        }

        public static pipo? GetPayload()
        {
            if (!File.Exists(LicenseFileName)) return null;
            try
            {
                string licenseData = File.ReadAllText(LicenseFileName);
                var licenseFile = JsonSerializer.Deserialize<LicenseFileModel>(licenseData);
                if (licenseFile == null) return null;

                byte[] dataBytes = Convert.FromBase64String(licenseFile.Data);
                string dataJson = Encoding.UTF8.GetString(dataBytes);
                return JsonSerializer.Deserialize<pipo>(dataJson);
            }
            catch { return null; }
        }
    }
}
