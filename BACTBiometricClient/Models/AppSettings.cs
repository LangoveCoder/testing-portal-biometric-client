using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents application settings and configuration
    /// Stored in app_settings table as key-value pairs
    /// </summary>
    public class AppSettings
    {
        // API Configuration
        public string ApiUrl { get; set; }

        // Scanner Configuration
        public int QualityThreshold { get; set; }
        public int ScannerTimeoutSeconds { get; set; }
        public int MatchThreshold { get; set; }

        // Sync Configuration
        public bool AutoSyncEnabled { get; set; }
        public int SyncIntervalMinutes { get; set; }

        // User Preferences
        public string LastLoginEmail { get; set; }
        public bool RememberCredentials { get; set; }

        // Session Management
        public int InactivityTimeoutMinutes { get; set; }

        // Default constructor with default values
        public AppSettings()
        {
            // Set default values
            ApiUrl = "https://your-domain.com/api";
            QualityThreshold = 50;
            ScannerTimeoutSeconds = 30;
            MatchThreshold = 70;
            AutoSyncEnabled = true;
            SyncIntervalMinutes = 5;
            LastLoginEmail = string.Empty;
            RememberCredentials = false;
            InactivityTimeoutMinutes = 30;
        }

        /// <summary>
        /// Validate settings and return any errors
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(ApiUrl))
                return "API URL is required";

            if (!ApiUrl.StartsWith("http://") && !ApiUrl.StartsWith("https://"))
                return "API URL must start with http:// or https://";

            if (QualityThreshold < 40 || QualityThreshold > 80)
                return "Quality threshold must be between 40 and 80";

            if (MatchThreshold < 50 || MatchThreshold > 100)
                return "Match threshold must be between 50 and 100";

            if (SyncIntervalMinutes < 1 || SyncIntervalMinutes > 60)
                return "Sync interval must be between 1 and 60 minutes";

            if (ScannerTimeoutSeconds < 10 || ScannerTimeoutSeconds > 120)
                return "Scanner timeout must be between 10 and 120 seconds";

            return null; // No errors
        }

        /// <summary>
        /// Check if settings are valid
        /// </summary>
        public bool IsValid()
        {
            return string.IsNullOrEmpty(Validate());
        }
    }

    /// <summary>
    /// Individual setting item from database
    /// </summary>
    public class SettingItem
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public DateTime UpdatedAt { get; set; }

        public SettingItem()
        {
            UpdatedAt = DateTime.Now;
        }
    }
}