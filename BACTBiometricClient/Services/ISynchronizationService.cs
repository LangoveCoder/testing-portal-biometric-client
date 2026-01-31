using System;
using System.Threading;
using System.Threading.Tasks;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Interface for comprehensive synchronization service
    /// Defines contract for offline-first synchronization operations
    /// </summary>
    public interface ISynchronizationService : IDisposable
    {
        /// <summary>
        /// Perform comprehensive synchronization of all pending operations
        /// </summary>
        Task<SyncResult> SyncAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Synchronize pending registrations in batches
        /// </summary>
        Task<SyncResult> SyncRegistrationsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Synchronize pending verifications in batches
        /// </summary>
        Task<SyncResult> SyncVerificationsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Download fresh student data for offline cache
        /// </summary>
        Task<bool> DownloadStudentsAsync(int collegeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Force immediate synchronization if network is available
        /// </summary>
        Task<SyncResult> ForceSyncAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if synchronization is currently in progress
        /// </summary>
        bool IsSyncing { get; }

        /// <summary>
        /// Get the last sync attempt timestamp
        /// </summary>
        DateTime LastSyncAttempt { get; }

        /// <summary>
        /// Event raised when sync progress updates
        /// </summary>
        event EventHandler<SyncProgressEventArgs> SyncProgress;

        /// <summary>
        /// Event raised when sync operation completes
        /// </summary>
        event EventHandler<SyncCompletedEventArgs> SyncCompleted;

        /// <summary>
        /// Event raised when sync error occurs
        /// </summary>
        event EventHandler<SyncErrorEventArgs> SyncError;
    }

    /// <summary>
    /// Synchronization result containing detailed information about the sync operation
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        
        public int TotalOperations { get; set; }
        public int RegistrationsSynced { get; set; }
        public int RegistrationsFailed { get; set; }
        public int VerificationsSynced { get; set; }
        public int VerificationsFailed { get; set; }
        public int StudentsDownloaded { get; set; }
        public int OperationsCleaned { get; set; }
        
        public int OperationsSynced { get; set; }
        public int OperationsFailed { get; set; }
        
        public double SuccessRate => TotalOperations > 0 ? (double)OperationsSynced / TotalOperations * 100 : 100;
    }

    /// <summary>
    /// Event arguments for sync progress updates
    /// </summary>
    public class SyncProgressEventArgs : EventArgs
    {
        public string Message { get; }
        public int ProgressPercentage { get; }
        public DateTime Timestamp { get; }

        public SyncProgressEventArgs(string message, int progressPercentage)
        {
            Message = message;
            ProgressPercentage = Math.Max(0, Math.Min(100, progressPercentage));
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for sync completion
    /// </summary>
    public class SyncCompletedEventArgs : EventArgs
    {
        public SyncResult Result { get; }
        public DateTime Timestamp { get; }

        public SyncCompletedEventArgs(SyncResult result)
        {
            Result = result;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for sync errors
    /// </summary>
    public class SyncErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public SyncErrorEventArgs(Exception exception, string message)
        {
            Exception = exception;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }
}