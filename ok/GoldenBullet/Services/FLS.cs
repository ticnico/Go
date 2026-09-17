using Core.Services;
using System.Diagnostics;

namespace GoldenBullet.Services
{
    public static class FLS
    {
        // ✅ Trial Mode Limits
        public const int TrialMaxConfigs = 3;
        public const int TrialMaxProxies = 100;
        public const int TrialMaxThreads = 10;
        public const int TrialMaxHits = 50;
        public const bool TrialCanExport = false;
        public const bool TrialCanSaveConfig = false;
        public const bool TrialCanUsePlugins = false;
        public const bool TrialCanUseTools = false;
        public const int TrialMaxSessionMinutes = 30;

        // ✅ Activated Mode Limits (Unlimited)
        public const int ActivatedMaxConfigs = int.MaxValue;
        public const int ActivatedMaxProxies = int.MaxValue;
        public const int ActivatedMaxThreads = 1000;
        public const int ActivatedMaxHits = int.MaxValue;
        public const bool ActivatedCanExport = true;
        public const bool ActivatedCanSaveConfig = true;
        public const bool ActivatedCanUsePlugins = true;
        public const bool ActivatedCanUseTools = true;
        public const int ActivatedMaxSessionMinutes = int.MaxValue;

        /// <summary>
        /// Check if the application is activated
        /// </summary>
        public static bool IsActivated()
        {
            return Validate.IsActivated();
        }

        /// <summary>
        /// ✅ Refresh activation status (call after license activation)
        /// </summary>
        public static void Refresh()
        {
            // Force re-check of license status
            // This ensures Validate clears any cached state
            Debug.WriteLine("🔄 FLS.Refresh() called");

            // Re-initialize Validate if it has cached state
            try
            {
                Validate.IsActivated(); // Force re-check
                Debug.WriteLine($"✅ FLS refreshed. IsActivated: {IsActivated()}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ FLS.Refresh() error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get maximum number of configs allowed
        /// </summary>
        public static int GetMaxConfigs()
        {
            return IsActivated() ? ActivatedMaxConfigs : TrialMaxConfigs;
        }

        /// <summary>
        /// Get maximum number of proxies allowed
        /// </summary>
        public static int GetMaxProxies()
        {
            return IsActivated() ? ActivatedMaxProxies : TrialMaxProxies;
        }

        /// <summary>
        /// Get maximum number of threads allowed
        /// </summary>
        public static int GetMaxThreads()
        {
            return IsActivated() ? ActivatedMaxThreads : TrialMaxThreads;
        }

        /// <summary>
        /// Get maximum number of hits allowed
        /// </summary>
        public static int GetMaxHits()
        {
            return IsActivated() ? ActivatedMaxHits : TrialMaxHits;
        }

        /// <summary>
        /// Check if export is allowed
        /// </summary>
        public static bool CanExport()
        {
            return IsActivated() ? ActivatedCanExport : TrialCanExport;
        }

        /// <summary>
        /// Check if saving configs is allowed
        /// </summary>
        public static bool CanSaveConfig()
        {
            return IsActivated() ? ActivatedCanSaveConfig : TrialCanSaveConfig;
        }

        /// <summary>
        /// Check if plugins can be used
        /// </summary>
        public static bool CanUsePlugins()
        {
            return IsActivated() ? ActivatedCanUsePlugins : TrialCanUsePlugins;
        }

        /// <summary>
        /// Check if tools can be used
        /// </summary>
        public static bool CanUseTools()
        {
            return IsActivated() ? ActivatedCanUseTools : TrialCanUseTools;
        }

        /// <summary>
        /// Get maximum session time in minutes
        /// </summary>
        public static int GetMaxSessionMinutes()
        {
            return IsActivated() ? ActivatedMaxSessionMinutes : TrialMaxSessionMinutes;
        }

        /// <summary>
        /// Get limitation message for UI
        /// </summary>
        public static string GetLimitationMessage()
        {
            if (IsActivated())
                return "✅ Full Version - All Features Unlocked";

            return $"⚠️ Trial Mode - Limited Features\n" +
                   $"• Max Configs: {TrialMaxConfigs}\n" +
                   $"• Max Proxies: {TrialMaxProxies}\n" +
                   $"• Max Threads: {TrialMaxThreads}\n" +
                   $"• Max Hits: {TrialMaxHits}\n" +
                   $"• Export: Disabled\n" +
                   $"• Save Config: Disabled\n" +
                   $"• Session Time: {TrialMaxSessionMinutes} minutes";
        }
    }
}
