using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents a fingerprint registration that needs to be synced to the server
    /// Stored in pending_registrations table until successfully uploaded
    /// </summary>
    public class Registration
    {
        // Primary key
        public int Id { get; set; }

        // Student identifiers
        public int StudentId { get; set; }
        public string RollNumber { get; set; }

        // Biometric data
        public string FingerprintTemplate { get; set; }
        public byte[] FingerprintImage { get; set; }
        public int QualityScore { get; set; }

        // Capture information
        public DateTime CapturedAt { get; set; }
        public int? OperatorId { get; set; }
        public string OperatorName { get; set; }

        // Sync tracking
        public int SyncAttempts { get; set; }
        public string SyncStatus { get; set; }
        public DateTime? LastSyncAttempt { get; set; }
        public string SyncError { get; set; }
        public DateTime CreatedAt { get; set; }

        // Computed properties
        public bool IsPending => SyncStatus == "pending";
        public bool IsError => SyncStatus == "error";
        public bool IsSynced => SyncStatus == "synced";
        public string StatusDisplay => SyncStatus.ToUpper();

        public Registration()
        {
            CapturedAt = DateTime.Now;
            CreatedAt = DateTime.Now;
            SyncStatus = "pending";
            SyncAttempts = 0;
        }

        /// <summary>
        /// Mark this registration as successfully synced
        /// </summary>
        public void MarkAsSynced()
        {
            SyncStatus = "synced";
            LastSyncAttempt = DateTime.Now;
        }

        /// <summary>
        /// Mark this registration as failed sync with error message
        /// </summary>
        public void MarkAsError(string errorMessage)
        {
            SyncStatus = "error";
            SyncError = errorMessage;
            SyncAttempts++;
            LastSyncAttempt = DateTime.Now;
        }

        /// <summary>
        /// Retry sync attempt
        /// </summary>
        public void IncrementSyncAttempt()
        {
            SyncAttempts++;
            LastSyncAttempt = DateTime.Now;
        }
    }
}