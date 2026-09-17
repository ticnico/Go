using RuriLib.Attributes;
using RuriLib.Models.Bots;
using System;
using System.Security.Cryptography;
using System.Text;

namespace RuriLib.Blocks.TicnicOFunctions
{
    [BlockCategory("TicnicOFunctions", "Blocks for cryptographic and utility functions", "#6DF529")]
    public static class BlockMD5
    {
        /// <summary>
        /// MD5 hash function, input: string, output: raw|base64 string
        /// </summary>
        [Block("md5 hash function, input: string, output: raw|base64 string", name = "md5")]
        public static string md5(
            BotData data,
            [Variable] string input,
            bool base64 = false)
        {
            string result;
            using (MD5 md = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md.ComputeHash(inputBytes);

                if (base64)
                {
                    result = Convert.ToBase64String(hashBytes);
                }
                else
                {
                    result = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }

            data.Logger.LogHeader("md5");
            data.Logger.Log("MD5 Hash: " + result, "#fff", false);
            return result;
        }
    }
}
