using RuriLib.Attributes;
using RuriLib.Models.Bots;
using System;

namespace RuriLib.Blocks.TicnicOFunctions
{

    [BlockCategory("TicnicOFunctions", "Blocks that generates random stuff", "#FF5C5C")]
    public static class Method
    {
        /// <summary>
        /// Gets the current unix time in different precisions.
        /// </summary>
        /// <param name="data">The BotData instance for logging.</param>
        /// <param name="useUtc">Whether to use UTC time or Local time.</param>
        /// <param name="precision">The desired precision of the output.</param>
        /// <returns>The current Unix timestamp as a string.</returns>
        [Block("Get the current unix time in different precision")]
        public static string CurrentUnixTimeExtra(
            BotData data,
            bool useUtc,
            TimePrecision precision = TimePrecision.Seconds)
        {
            DateTime dateTime = useUtc ? DateTime.UtcNow : DateTime.Now;
            long value = 0L;

            switch (precision)
            {
                case TimePrecision.Seconds:
                    value = (long)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    break;
                case TimePrecision.Milliseconds:
                    value = (long)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;
                    break;
                case TimePrecision.Microseconds:
                    value = dateTime.Subtract(new DateTime(1970, 1, 1)).Ticks / 10L;
                    break;
                case TimePrecision.Nanoseconds:
                    value = dateTime.Subtract(new DateTime(1970, 1, 1)).Ticks * 100L;
                    break;
            }

            data.Logger.LogHeader("CurrentUnixTimeExtra");
            data.Logger.Log($"Current unix time: {value}", "#fff", false);

            return value.ToString();
        }
    }
}
