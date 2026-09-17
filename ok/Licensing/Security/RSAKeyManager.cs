// 📁 Licensing/Security/RSAKeyManager.cs

using System.Security.Cryptography;
using System.Text;

namespace GoldenBullet.Licensing.Security
{
    public static class RSAKeyManager
    {
        // ✅ PASTE YOUR CONVERTED XML KEY HERE
        private const string PublicKey = @"<RSAKeyValue><Modulus>9py7/x1gJeVfp5kz4vEJAQ2m9tDExMFrt8N1P+O4iVRQ3RQneJGockRN6/+R6HghO3v378vTK9AFI1f0vhZn0Sc+ux+JZr/p9EGt1xa2mgXF6REEi8gAd2Tee1dw+pgV5TYuWBodlqQ60HnyTeFqWoLfaemuOwHuJ/NlcFIRbul5oAppEam2oYKkrL8pxd2M3471iwNnf32F3QNgNuU9ai5t7gejbDSLxn16tCxdt0f26cYJlLqs0RynkYnp/1ZpXYUTztNkzX/Gh/DSOi0llZl0z52u5SgbeWomI1H2jXJhUyfCD89s3FbZXzsSVvMi4pGzlTZss1KuXWsfNHSe+Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool VerifyData(string data, byte[] signature)
        {
            try
            {
                using RSA rsa = RSA.Create();
                rsa.FromXmlString(PublicKey);

                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                return rsa.VerifyData(dataBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }
    }
}
