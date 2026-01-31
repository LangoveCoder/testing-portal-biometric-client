using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Queue management service for offline operations with retry logic and error handling
    /// Implements Requirements 4.3, 9.2, 9.3 for offline operation queuing and synchronization
    /// </summary>
    public class QueueManagementService : IQueueManagementService
    {
        private readonly DatabaseService _databaseService;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public QueueManagementService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        #region Queue Operations

        /// <summary>
        /// Add fingerprint registration to queue with high priority
        /// </summary>
        public async Task<int> QueueFingerprintRegistrationAsync(Registration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            try
            {
                var operationData = new
                {
                    StudentId = registration.StudentId,
                    RollNumber = registration.RollNumber,
                    FingerprintTemplate = registration.FingerprintTemplate,
                    FingerprintImage = registration.FingerprintImage,
                    QualityScore = registration.QualityScore,
                    CapturedAt = registration.CapturedAt,
                    OperatorId = registration.OperatorId,
                    OperatorName = registration.OperatorName
                };

                var operationId = await _databaseService.AddQueuedOperationAsync(
                    "FingerprintRegistration", 
                    operationData, 
                    priority: 2 // High priority for registrations
                );

                await _databaseService.LogSyncOperationAsync(
                    "QueueRegistration", 
                    "queue", 
                    1, 
                    1, 
                    0, 
                    null, 
                    DateTime.Now, 
                    TimeSpan.Zero
                );

                return operationId;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "QueueRegistrationError", 
                    ex.Message, 
                    ex.StackTrace, 
                    registration.OperatorId,
                    JsonSerializer.Serialize(new { registration.RollNumber, registration.StudentId })
                );
                throw;
            }
        }

        /// <summary>
        /// Add fingerprint verification to queue with normal priority
        /// </summary>
        public async Task<int> QueueFingerprintVerificationAsync(Verification verification)
        {
            if (verification == null)
                throw new ArgumentNullException(nameof(verification));

            try
            {
                var operationData = new
                {
                    StudentId = verification.StudentId,
                    RollNumber = verification.RollNumber,
                    MatchResult = verification.MatchResult,
                    ConfidenceScore = verification.ConfidenceScore,
                    EntryAllowed = verification.EntryAllowed,
                    VerifiedAt = verification.VerifiedAt,
                    VerifierId = verification.VerifierId,
                    VerifierName = verification.VerifierName,
                    Notes = verification.Notes
                };

                var operationId = await _databaseService.AddQueuedOperationAsync(
                    "FingerprintVerification", 
                    operationData, 
                    priority: 1 // Normal priority for verifications
                );

                // Also save to verification results cache
                await _databaseService.SaveVerificationResultAsync(verification);

                await _databaseService.LogSyncOperationAsync(
                    "QueueVerification", 
                    "queue", 
                    1, 
                    1, 
                    0, 
                    null, 
                    DateTime.Now, 
                    TimeSpan.Zero
                );

                return operationId;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "QueueVerificationError", 
                    ex.Message, 
                    ex.StackTrace, 
                    verification.VerifierId,
                    JsonSerializer.Serialize(new { verification.RollNumber, verification.StudentId })
                );
                throw;
            }
        }

        /// <summary>
        /// Add student data update to queue with low priority
        /// </summary>
        public async Task<int> QueueStudentUpdateAsync(Student student, int? userId = null)
        {
            if (student == null)
                throw new ArgumentNullException(nameof(student));

            try
            {
                var operationData = new
                {
                    StudentId = student.Id,
                    RollNumber = student.RollNumber,
                    Name = student.Name,
                    FatherName = student.FatherName,
                    CNIC = student.CNIC,
                    Gender = student.Gender,
                    TestId = student.TestId,
                    TestName = student.TestName,
                    CollegeId = student.CollegeId,
                    CollegeName = student.CollegeName,
                    Picture = student.Picture,
                    FingerprintTemplate = student.FingerprintTemplate,
                    FingerprintImage = student.FingerprintImage,
                    FingerprintQuality = student.FingerprintQuality,
                    FingerprintRegisteredAt = student.FingerprintRegisteredAt,
                    UpdatedBy = userId
                };

                var operationId = await _databaseService.AddQueuedOperationAsync(
                    "StudentUpdate", 
                    operationData, 
                    priority: 0 // Low priority for updates
                );

                await _databaseService.LogSyncOperationAsync(
                    "QueueStudentUpdate", 
                    "queue", 
                    1, 
                    1, 
                    0, 
                    null, 
                    DateTime.Now, 
                    TimeSpan.Zero
                );

                return operationId;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "QueueStudentUpdateError", 
                    ex.Message, 
                    ex.StackTrace, 
                    userId,
                    JsonSerializer.Serialize(new { student.RollNumber, student.Id })
                );
                throw;
            }
        }

        #endregion

        #region Queue Processing

        /// <summary>
        /// Get next batch of operations ready for processing
        /// </summary>
        public async Task<List<QueuedOperation>> GetNextBatchAsync(int batchSize = 50)
        {
            try
            {
                var operations = await _databaseService.GetPendingOperationsAsync(batchSize);
                
                // Mark operations as syncing to prevent duplicate processing
                foreach (var operation in operations)
                {
                    await _databaseService.UpdateOperationStatusAsync(operation.Id, "syncing");
                }

                return operations;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "GetNextBatchError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        /// <summary>
        /// Mark operation as successfully processed
        /// </summary>
        public async Task MarkOperationSuccessAsync(int operationId)
        {
            try
            {
                await _databaseService.UpdateOperationStatusAsync(operationId, "synced");
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "MarkOperationSuccessError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: JsonSerializer.Serialize(new { operationId })
                );
                throw;
            }
        }

        /// <summary>
        /// Mark operation as failed with error details
        /// </summary>
        public async Task MarkOperationFailedAsync(int operationId, string errorMessage)
        {
            try
            {
                await _databaseService.UpdateOperationStatusAsync(operationId, "failed", errorMessage);
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "MarkOperationFailedError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: JsonSerializer.Serialize(new { operationId, originalError = errorMessage })
                );
                throw;
            }
        }

        /// <summary>
        /// Reset operation status to pending for retry
        /// </summary>
        public async Task ResetOperationForRetryAsync(int operationId)
        {
            try
            {
                await _databaseService.UpdateOperationStatusAsync(operationId, "pending");
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "ResetOperationError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: JsonSerializer.Serialize(new { operationId })
                );
                throw;
            }
        }

        /// <summary>
        /// Get operations that can be retried
        /// </summary>
        public async Task<List<QueuedOperation>> GetRetryableOperationsAsync()
        {
            try
            {
                return await _databaseService.GetRetryableOperationsAsync();
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "GetRetryableOperationsError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        #endregion

        #region Queue Monitoring

        /// <summary>
        /// Get comprehensive queue statistics
        /// </summary>
        public async Task<QueueStatistics> GetQueueStatisticsAsync()
        {
            try
            {
                var (registrations, verifications, totalOperations) = await _databaseService.GetPendingCountsAsync();
                var retryableOperations = await _databaseService.GetRetryableOperationsAsync();

                return new QueueStatistics
                {
                    PendingRegistrations = registrations,
                    PendingVerifications = verifications,
                    TotalPendingOperations = totalOperations,
                    FailedOperations = retryableOperations.Count,
                    LastUpdated = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "GetQueueStatisticsError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        /// <summary>
        /// Get operations by type and status
        /// </summary>
        public async Task<List<QueuedOperation>> GetOperationsByTypeAsync(string operationType, string status = null)
        {
            try
            {
                var allOperations = await _databaseService.GetPendingOperationsAsync(1000);
                
                var filteredOperations = allOperations.Where(o => o.OperationType == operationType);
                
                if (!string.IsNullOrEmpty(status))
                {
                    filteredOperations = filteredOperations.Where(o => o.SyncStatus == status);
                }

                return filteredOperations.ToList();
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "GetOperationsByTypeError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: JsonSerializer.Serialize(new { operationType, status })
                );
                throw;
            }
        }

        /// <summary>
        /// Clean up old completed operations
        /// </summary>
        public async Task<int> CleanupCompletedOperationsAsync(int olderThanDays = 7)
        {
            try
            {
                var deletedCount = await _databaseService.CleanupCompletedOperationsAsync(olderThanDays);
                
                await _databaseService.LogSyncOperationAsync(
                    "QueueCleanup", 
                    "cleanup", 
                    deletedCount, 
                    deletedCount, 
                    0, 
                    null, 
                    DateTime.Now, 
                    TimeSpan.Zero
                );

                return deletedCount;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "CleanupOperationsError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: JsonSerializer.Serialize(new { olderThanDays })
                );
                throw;
            }
        }

        #endregion

        #region Batch Processing

        /// <summary>
        /// Process operations in batches with error handling
        /// </summary>
        public async Task<BatchProcessResult> ProcessBatchAsync(List<QueuedOperation> operations, Func<QueuedOperation, Task<bool>> processor)
        {
            if (operations == null || !operations.Any())
                return new BatchProcessResult { TotalProcessed = 0, SuccessCount = 0, FailedCount = 0 };

            var result = new BatchProcessResult
            {
                TotalProcessed = operations.Count,
                StartedAt = DateTime.Now
            };

            var successCount = 0;
            var failedCount = 0;
            var errors = new List<string>();

            foreach (var operation in operations)
            {
                try
                {
                    var success = await processor(operation);
                    
                    if (success)
                    {
                        await MarkOperationSuccessAsync(operation.Id);
                        successCount++;
                    }
                    else
                    {
                        await MarkOperationFailedAsync(operation.Id, "Processing returned false");
                        failedCount++;
                        errors.Add($"Operation {operation.Id} processing failed");
                    }
                }
                catch (Exception ex)
                {
                    await MarkOperationFailedAsync(operation.Id, ex.Message);
                    failedCount++;
                    errors.Add($"Operation {operation.Id}: {ex.Message}");
                    
                    await _databaseService.LogErrorAsync(
                        "BatchProcessError", 
                        ex.Message, 
                        ex.StackTrace,
                        contextData: JsonSerializer.Serialize(new { operation.Id, operation.OperationType })
                    );
                }
            }

            result.SuccessCount = successCount;
            result.FailedCount = failedCount;
            result.CompletedAt = DateTime.Now;
            result.Duration = result.CompletedAt - result.StartedAt;
            result.Errors = errors;

            // Log batch processing results
            await _databaseService.LogSyncOperationAsync(
                "BatchProcess", 
                "upload", 
                result.TotalProcessed, 
                result.SuccessCount, 
                result.FailedCount, 
                errors.Any() ? string.Join("; ", errors.Take(3)) : null,
                result.StartedAt, 
                result.Duration
            );

            return result;
        }

        /// <summary>
        /// Retry failed operations with exponential backoff
        /// </summary>
        public async Task<BatchProcessResult> RetryFailedOperationsAsync(Func<QueuedOperation, Task<bool>> processor, int maxRetries = 3)
        {
            try
            {
                var retryableOperations = await GetRetryableOperationsAsync();
                
                if (!retryableOperations.Any())
                {
                    return new BatchProcessResult 
                    { 
                        TotalProcessed = 0, 
                        SuccessCount = 0, 
                        FailedCount = 0,
                        StartedAt = DateTime.Now,
                        CompletedAt = DateTime.Now
                    };
                }

                // Filter operations that haven't exceeded max retries
                var operationsToRetry = retryableOperations
                    .Where(o => o.SyncAttempts < maxRetries)
                    .ToList();

                return await ProcessBatchAsync(operationsToRetry, processor);
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "RetryFailedOperationsError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Cleanup any managed resources if needed
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Queue statistics for monitoring
    /// </summary>
    public class QueueStatistics
    {
        public int PendingRegistrations { get; set; }
        public int PendingVerifications { get; set; }
        public int TotalPendingOperations { get; set; }
        public int FailedOperations { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Batch processing result
    /// </summary>
    public class BatchProcessResult
    {
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        
        public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed * 100 : 0;
    }
}