using RuriLib.Attributes;
using RuriLib.Models.Bots;
using System;
using System.Globalization;

namespace RuriLib.Blocks.TicnicOFunctions
{
    /// <summary>
    /// Enum defining the precision level for Unix timestamp conversions.
    /// </summary>
    public enum TimePrecision
    {
        Seconds,
        Milliseconds,
        Microseconds,
        Nanoseconds
    }

    [BlockCategory("TicnicOFunctions", "Blocks that generates random stuff", "#FF871F")]
    public static class BlockDateToUnixExtra
    {
        /// <summary>
        /// Parses a unix time from a formatted datetime string.
        /// </summary>
        [Block("Parses a unix time from a formatted datetime string")]
        public static string DateToUnixExtra(
            BotData data,
            [Variable] string datetime,
            string format = "yyyy-MM-dd HH-mm-ss",
            string cultureInfo = "en-US",
            TimePrecision precision = TimePrecision.Seconds)
        {
            DateTime dateTime = DateTime.ParseExact(datetime, format, new CultureInfo(cultureInfo), DateTimeStyles.AllowWhiteSpaces);
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long unixTime;

            switch (precision)
            {
                case TimePrecision.Seconds:
                    unixTime = (long)dateTime.Subtract(epoch).TotalSeconds;
                    break;
                case TimePrecision.Milliseconds:
                    unixTime = (long)dateTime.Subtract(epoch).TotalMilliseconds;
                    break;
                case TimePrecision.Microseconds:
                    unixTime = dateTime.Subtract(new DateTime(1970, 1, 1)).Ticks / 10L;
                    break;
                case TimePrecision.Nanoseconds:
                    unixTime = dateTime.Subtract(new DateTime(1970, 1, 1)).Ticks * 100L;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(precision), precision, null);
            }

            data.Logger.LogHeader("DateToUnixExtra");
            data.Logger.Log($"Unix time: {unixTime}", "#fff", false);
            return unixTime.ToString();
        }
    }
}

