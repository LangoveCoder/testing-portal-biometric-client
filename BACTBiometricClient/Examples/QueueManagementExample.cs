using System;
using System.Threading.Tasks;
using BACTBiometricClient.Models;
using BACTBiometricClient.Services;

namespace BACTBiometricClient.Examples
{
    /// <summary>
    /// Example usage of the queue management system for offline operations
    /// Demonstrates Requirements 4.3, 9.2, 9.3 implementation
    /// </summary>
    public class QueueManagementExample
    {
        private readonly IQueueManagementService _queueService;
        private readonly BackgroundQueueProcessor _backgroundProcessor;

        public QueueManagementExample()
        {
            _queueService = ServiceConfiguration.GetQueueManagementService();
            _backgroundProcessor = ServiceConfiguration.GetBackgroundQueueProcessor();
        }

        /// <summary>
        /// Example: Queue a fingerprint registration when offline
        /// </summary>
        public async Task ExampleQueueRegistrationAsync()
        {
            try
            {
                // Create a registration object (typically from fingerprint capture)
                var registration = new Registration
                {
                    StudentId = 12345,
                    RollNumber = "2024-CS-001",
                    FingerprintTemplate = "base64_encoded_template_data",
                    FingerprintImage = new byte[] { /* image data */ },
                    QualityScore = 85,
                    CapturedAt = DateTime.Now,
                    OperatorId = 1,
                    OperatorName = "John Operator",
                    SyncStatus = "pending"
                };

                // Queue the registration for later synchronization
                var operationId = await _queueService.QueueFingerprintRegistrationAsync(registration);
                
                Console.WriteLine($"Registration queued with operation ID: {operationId}");
                
                // The background processor will automatically sync this when network is available
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error queuing registration: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Queue a fingerprint verification when offline
        /// </summary>
        public async Task ExampleQueueVerificationAsync()
        {
            try
            {
                // Create a verification object (typically from fingerprint matching)
                var verification = new Verification
                {
                    StudentId = 12345,
                    RollNumber = "2024-CS-001",
                    MatchResult = "Match",
                    ConfidenceScore = 92.5,
                    EntryAllowed = true,
                    VerifiedAt = DateTime.Now,
                    VerifierId = 2,
                    VerifierName = "Jane Admin",
                    Notes = "Successful verification",
                    SyncStatus = "pending"
                };

                // Queue the verification for later synchronization
                var operationId = await _queueService.QueueFingerprintVerificationAsync(verification);
                
                Console.WriteLine($"Verification queued with operation ID: {operationId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error queuing verification: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Monitor queue statistics
        /// </summary>
        public async Task ExampleMonitorQueueAsync()
        {
            try
            {
                var stats = await _queueService.GetQueueStatisticsAsync();
                
                Console.WriteLine("=== Queue Statistics ===");
                Console.WriteLine($"Pending Registrations: {stats.PendingRegistrations}");
                Console.WriteLine($"Pending Verifications: {stats.PendingVerifications}");
                Console.WriteLine($"Total Pending Operations: {stats.TotalPendingOperations}");
                Console.WriteLine($"Failed Operations: {stats.FailedOperations}");
                Console.WriteLine($"Last Updated: {stats.LastUpdated}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting queue statistics: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Manual queue processing (for testing or manual sync)
        /// </summary>
        public async Task ExampleManualProcessingAsync()
        {
            try
            {
                Console.WriteLine("Starting manual queue processing...");
                
                // Trigger immediate processing
                await _backgroundProcessor.ProcessNowAsync();
                
                Console.WriteLine("Manual processing completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during manual processing: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Handle failed operations
        /// </summary>
        public async Task ExampleHandleFailedOperationsAsync()
        {
            try
            {
                // Get operations that can be retried
                var retryableOps = await _queueService.GetRetryableOperationsAsync();
                
                Console.WriteLine($"Found {retryableOps.Count} operations that can be retried");
                
                foreach (var operation in retryableOps)
                {
                    Console.WriteLine($"Operation {operation.Id}: {operation.OperationType}");
                    Console.WriteLine($"  Attempts: {operation.SyncAttempts}/{operation.MaxAttempts}");
                    Console.WriteLine($"  Last Error: {operation.LastError}");
                    Console.WriteLine($"  Last Attempt: {operation.LastSyncAttempt}");
                    
                    // Optionally reset for retry
                    if (operation.SyncAttempts < operation.MaxAttempts)
                    {
                        await _queueService.ResetOperationForRetryAsync(operation.Id);
                        Console.WriteLine($"  Reset for retry");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling failed operations: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Cleanup old operations
        /// </summary>
        public async Task ExampleCleanupAsync()
        {
            try
            {
                // Clean up operations older than 7 days
                var deletedCount = await _queueService.CleanupCompletedOperationsAsync(7);
                
                Console.WriteLine($"Cleaned up {deletedCount} old operations");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Setup background processor event handlers
        /// </summary>
        public void ExampleSetupEventHandlers()
        {
            // Subscribe to processing events for UI updates
            _backgroundProcessor.ProcessingStarted += (sender, e) =>
            {
                Console.WriteLine($"Background processing started at {e.StartedAt}");
            };

            _backgroundProcessor.ProcessingCompleted += (sender, e) =>
            {
                Console.WriteLine($"Background processing completed:");
                Console.WriteLine($"  Duration: {e.CompletedAt - e.StartedAt}");
                Console.WriteLine($"  Total Processed: {e.TotalProcessed}");
                Console.WriteLine($"  Success: {e.SuccessCount}");
                Console.WriteLine($"  Failed: {e.FailedCount}");
            };

            _backgroundProcessor.ProcessingError += (sender, e) =>
            {
                Console.WriteLine($"Background processing error: {e.Error.Message}");
            };
        }

        /// <summary>
        /// Example: Complete workflow demonstration
        /// </summary>
        public async Task ExampleCompleteWorkflowAsync()
        {
            try
            {
                Console.WriteLine("=== Queue Management Workflow Example ===");
                
                // 1. Setup event handlers
                ExampleSetupEventHandlers();
                
                // 2. Check initial queue status
                await ExampleMonitorQueueAsync();
                
                // 3. Queue some operations (simulating offline work)
                await ExampleQueueRegistrationAsync();
                await ExampleQueueVerificationAsync();
                
                // 4. Check queue status after adding operations
                await ExampleMonitorQueueAsync();
                
                // 5. Process queue manually (simulating network coming back online)
                await ExampleManualProcessingAsync();
                
                // 6. Check for any failed operations
                await ExampleHandleFailedOperationsAsync();
                
                // 7. Final queue status
                await ExampleMonitorQueueAsync();
                
                // 8. Cleanup old operations
                await ExampleCleanupAsync();
                
                Console.WriteLine("=== Workflow Example Complete ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in complete workflow: {ex.Message}");
            }
        }
    }
}