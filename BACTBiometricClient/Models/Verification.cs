using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents a fingerprint verification attempt that needs to be synced to the server
    /// Stored in pending_verifications table until successfully uploaded
    /// </summary>
    public class Verification
    {
        // Primary key
        public int Id { get; set; }

        // Student identifiers
        public int? StudentId { get; set; }
        public string RollNumber { get; set; }

        // Verification result
        public string MatchResult { get; set; }
        public double ConfidenceScore { get; set; }
        public bool EntryAllowed { get; set; }

        // Verification information
        public DateTime VerifiedAt { get; set; }
        public int? VerifierId { get; set; }
        public string VerifierName { get; set; }
        public string Notes { get; set; }

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
        public bool IsMatch => MatchResult == "match";
        public string ConfidenceDisplay => $"{ConfidenceScore:F2}%";
        public string ResultDisplay => EntryAllowed ? "ALLOWED" : "DENIED";

        public Verification()
        {
            VerifiedAt = DateTime.Now;
            CreatedAt = DateTime.Now;
            SyncStatus = "pending";
            SyncAttempts = 0;
            MatchResult = "no_match";
            EntryAllowed = false;
        }

        /// <summary>
        /// Mark this verification as successfully synced
        /// </summary>
        public void MarkAsSynced()
        {
            SyncStatus = "synced";
            LastSyncAttempt = DateTime.Now;
        }

        /// <summary>
        /// Mark this verification as failed sync with error message
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

        /// <summary>
        /// Set verification result based on match
        /// </summary>
        public void SetResult(bool isMatch, double confidence, bool allowEntry)
        {
            MatchResult = isMatch ? "match" : "no_match";
            ConfidenceScore = confidence;
            EntryAllowed = allowEntry;
        }
    }
}