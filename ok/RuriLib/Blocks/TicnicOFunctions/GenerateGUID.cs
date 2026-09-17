using RuriLib.Attributes;
using RuriLib.Functions.Time;
using RuriLib.Models.Bots;
using System;
using System.Text;

namespace RuriLib.Blocks.TicnicOFunctions
{
    /// <summary>
    /// Enum defining the type of identifier to generate.
    /// </summary>
    public enum IdentifierType
    {
        GUID,
        UUID,
        DEVICEID
    }

    [BlockCategory("TicnicOFunctions", "Blocks that generates random stuff", "#FFFF5C")]
    public static class BlockGenerateGUID
    {
        /// <summary>
        /// Generates a random string of specified length from a sample character set.
        /// </summary>
        private static string RandomStr(int len, string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(len);
            var random = new Random((int)TimeConverter.ToUnixTime(DateTime.Now, false));

            for (int i = 0; i < len; i++)
            {
                sb.Append(input[random.Next(0, input.Length)]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates a random GUID, UUID, or DeviceID based on the specified type.
        /// </summary>
        [Block("Random GUID/UUID/DeviceID Generator")]
        public static string GenerateGUID(
            BotData data,
            IdentifierType type,
            bool KeepDashes = true,
            int deviceidLength = 16,
            string deviceidSample = "0a1b2c3d4e5f6879")
        {
            string text;

            switch (type)
            {
                case IdentifierType.GUID:
                case IdentifierType.UUID:
                    text = Guid.NewGuid().ToString();
                    break;
                case IdentifierType.DEVICEID:
                    text = RandomStr(deviceidLength, deviceidSample);
                    break;
                default:
                    text = string.Empty;
                    break;
            }

            if (!KeepDashes && (type == IdentifierType.GUID || type == IdentifierType.UUID))
            {
                text = text.Replace("-", "");
            }

            data.Logger.LogHeader("Generate");
            data.Logger.Log($"Generated Random {type} : {text}", "#fff", false);

            return text;

        }
    }
}

