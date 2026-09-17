using RuriLib.Attributes;
using RuriLib.Models.Bots;
using System;

namespace RuriLib.Blocks.TicnicOFunctions
{
    [BlockCategory("TicnicOFunctions", "Blocks that generates random stuff", "#FF1F96")]
    public static class BlockUnixTimeToDateExtra
    {
        /// <summary>
        /// Converts a unix time to a formatted datetime string.
        /// </summary>
        [Block("Converts a unix time to a formatted datetime string")]
        public static string UnixTimeToDateExtra(
            BotData data,
            [Variable] string unixTime,
            string format = "yyyy-MM-dd:HH-mm-ss",
            TimePrecision precision = TimePrecision.Seconds)
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0);
            DateTime dateTime2;

            switch (precision)
            {
                case TimePrecision.Seconds:
                    dateTime2 = dateTime.AddSeconds(long.Parse(unixTime)).ToUniversalTime();
                    break;
                case TimePrecision.Milliseconds:
                    dateTime2 = dateTime.AddMilliseconds(long.Parse(unixTime)).ToUniversalTime();
                    break;
                case TimePrecision.Microseconds:
                    dateTime2 = dateTime.AddTicks(long.Parse(unixTime) * 10L).ToUniversalTime();
                    break;
                case TimePrecision.Nanoseconds:
                    dateTime2 = dateTime.AddTicks(long.Parse(unixTime) / 100L).ToUniversalTime();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(precision), precision, null);
            }

            string text = dateTime2.ToString(format);
            data.Logger.LogHeader("UnixTimeToDateExtra");
            data.Logger.Log("Formatted datetime: " + text, "#fff", false);
            return text;
        }
    }
}

