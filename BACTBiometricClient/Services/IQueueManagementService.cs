using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Interface for queue management service for offline operations
    /// Defines contract for Requirements 4.3, 9.2, 9.3
    /// </summary>
    public interface IQueueManagementService : IDisposable
    {
        #region Queue Operations

        /// <summary>
        /// Add fingerprint registration to queue with high priority
        /// </summary>
        Task<int> QueueFingerprintRegistrationAsync(Registration registration);

        /// <summary>
        /// Add fingerprint verification to queue with normal priority
        /// </summary>
        Task<int> QueueFingerprintVerificationAsync(Verification verification);

        /// <summary>
        /// Add student data update to queue with low priority
        /// </summary>
        Task<int> QueueStudentUpdateAsync(Student student, int? userId = null);

        #endregion

        #region Queue Processing

        /// <summary>
        /// Get next batch of operations ready for processing
        /// </summary>
        Task<List<QueuedOperation>> GetNextBatchAsync(int batchSize = 50);

        /// <summary>
        /// Mark operation as successfully processed
        /// </summary>
        Task MarkOperationSuccessAsync(int operationId);

        /// <summary>
        /// Mark operation as failed with error details
        /// </summary>
        Task MarkOperationFailedAsync(int operationId, string errorMessage);

        /// <summary>
        /// Reset operation status to pending for retry
        /// </summary>
        Task ResetOperationForRetryAsync(int operationId);

        /// <summary>
        /// Get operations that can be retried
        /// </summary>
        Task<List<QueuedOperation>> GetRetryableOperationsAsync();

        #endregion

        #region Queue Monitoring

        /// <summary>
        /// Get comprehensive queue statistics
        /// </summary>
        Task<QueueStatistics> GetQueueStatisticsAsync();

        /// <summary>
        /// Get operations by type and status
        /// </summary>
        Task<List<QueuedOperation>> GetOperationsByTypeAsync(string operationType, string status = null);

        /// <summary>
        /// Clean up old completed operations
        /// </summary>
        Task<int> CleanupCompletedOperationsAsync(int olderThanDays = 7);

        #endregion

        #region Batch Processing

        /// <summary>
        /// Process operations in batches with error handling
        /// </summary>
        Task<BatchProcessResult> ProcessBatchAsync(List<QueuedOperation> operations, Func<QueuedOperation, Task<bool>> processor);

        /// <summary>
        /// Retry failed operations with exponential backoff
        /// </summary>
        Task<BatchProcessResult> RetryFailedOperationsAsync(Func<QueuedOperation, Task<bool>> processor, int maxRetries = 3);

        #endregion
    }
}