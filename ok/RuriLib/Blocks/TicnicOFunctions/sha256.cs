using RuriLib.Attributes;
using RuriLib.Models.Bots;
using System;
using System.Security.Cryptography;
using System.Text;

namespace RuriLib.Blocks.TicnicOFunctions
{
    [BlockCategory("TicnicOFunctions", "Blocks for cryptographic and utility functions", "#29F54B")]
    public static class BlockSHA256
    {
        /// <summary>
        /// SHA256 hash function, input: string, output: raw|base64 string
        /// </summary>
        [Block("sha256 hash function, input: string, output: raw|base64 string", name = "sha256")]
        public static string sha256(
            BotData data,
            [Variable] string input,
            bool base64 = false)
        {
            string result;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha.ComputeHash(inputBytes);

                if (base64)
                {
                    result = Convert.ToBase64String(hashBytes);
                }
                else
                {
                    result = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }

            data.Logger.LogHeader("sha256");
            data.Logger.Log("SHA256 Hash: " + result, "#fff", false);
            return result;
        }
    }
}
