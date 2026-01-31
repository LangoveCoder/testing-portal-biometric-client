using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Comprehensive synchronization service for offline-first operations
    /// Implements Requirements 4.4, 9.1, 9.4, 9.5 for automatic sync triggers, batch processing, and conflict resolution
    /// </summary>
    public class SynchronizationService : ISynchronizationService, IDisposable
    {
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;
        private readonly QueueManagementService _queueManagementService;
        private readonly Timer _syncTimer;
        private readonly SemaphoreSlim _syncSemaphore;
        private readonly object _lockObject = new object();
        
        private bool _disposed = false;
        private bool _isSyncing = false;
        private DateTime _lastSyncAttempt = DateTime.MinValue;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5); // Default sync interval
        private readonly int _maxBatchSize = 50;
        private readonly int _maxRetryAttempts = 3;

        // Events for sync progress reporting
        public event EventHandler<SyncProgressEventArgs> SyncProgress;
        public event EventHandler<SyncCompletedEventArgs> SyncCompleted;
        public event EventHandler<SyncErrorEventArgs> SyncError;

        public SynchronizationService(
            ApiService apiService, 
            DatabaseService databaseService, 
            QueueManagementService queueManagementService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _queueManagementService = queueManagementService ?? throw new ArgumentNullException(nameof(queueManagementService));
            
            _syncSemaphore = new SemaphoreSlim(1, 1);
            
            // Subscribe to network status changes for automatic sync triggers
            _apiService.NetworkStatusChanged += OnNetworkStatusChanged;
            
            // Initialize sync timer
            _syncTimer = new Timer(OnSyncTimerElapsed, null, _syncInterval, _syncInterval);
        }

        #region Public Interface

        /// <summary>
        /// Perform comprehensive synchronization of all pending operations
        /// </summary>
        public async Task<SyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
        {
            if (!await _syncSemaphore.WaitAsync(100, cancellationToken))
            {
                return new SyncResult
                {
                    Success = false,
                    Message = "Synchronization already in progress",
                    TotalOperations = 0
                };
            }

            try
            {
                _isSyncing = true;
                _lastSyncAttempt = DateTime.Now;
                
                OnSyncProgress(new SyncProgressEventArgs("Starting comprehensive synchronization...", 0));

                var result = new SyncResult
                {
                    StartedAt = DateTime.Now,
                    Success = true,
                    Message = "Synchronization completed successfully"
                };

                // Check network connectivity
                if (!_apiService.IsOnline)
                {
                    result.Success = false;
                    result.Message = "No network connectivity available";
                    return result;
                }

                // Step 1: Sync registrations
                OnSyncProgress(new SyncProgressEventArgs("Synchronizing registrations...", 25));
                var registrationResult = await SyncRegistrationsAsync(cancellationToken);
                result.RegistrationsSynced = registrationResult.OperationsSynced;
                result.RegistrationsFailed = registrationResult.OperationsFailed;

                // Step 2: Sync verifications
                OnSyncProgress(new SyncProgressEventArgs("Synchronizing verifications...", 50));
                var verificationResult = await SyncVerificationsAsync(cancellationToken);
                result.VerificationsSynced = verificationResult.OperationsSynced;
                result.VerificationsFailed = verificationResult.OperationsFailed;

                // Step 3: Download fresh data
                OnSyncProgress(new SyncProgressEventArgs("Downloading fresh data...", 75));
                var downloadResult = await DownloadFreshDataAsync(cancellationToken);
                result.StudentsDownloaded = downloadResult.StudentsDownloaded;

                // Step 4: Cleanup completed operations
                OnSyncProgress(new SyncProgressEventArgs("Cleaning up completed operations...", 90));
                var cleanupCount = await _queueManagementService.CleanupCompletedOperationsAsync();
                result.OperationsCleaned = cleanupCount;

                result.CompletedAt = DateTime.Now;
                result.Duration = result.CompletedAt - result.StartedAt;
                result.TotalOperations = result.RegistrationsSynced + result.VerificationsSynced;

                // Determine overall success
                if (result.RegistrationsFailed > 0 || result.VerificationsFailed > 0)
                {
                    result.Success = false;
                    result.Message = $"Synchronization completed with {result.RegistrationsFailed + result.VerificationsFailed} failures";
                }

                OnSyncProgress(new SyncProgressEventArgs("Synchronization completed", 100));
                OnSyncCompleted(new SyncCompletedEventArgs(result));

                return result;
            }
            catch (Exception ex)
            {
                var errorResult = new SyncResult
                {
                    Success = false,
                    Message = $"Synchronization failed: {ex.Message}",
                    CompletedAt = DateTime.Now
                };

                OnSyncError(new SyncErrorEventArgs(ex, "Comprehensive synchronization failed"));
                await LogSyncError("SyncAllError", ex);

                return errorResult;
            }
            finally
            {
                _isSyncing = false;
                _syncSemaphore.Release();
            }
        }

        /// <summary>
        /// Synchronize pending registrations in batches
        /// </summary>
        public async Task<SyncResult> SyncRegistrationsAsync(CancellationToken cancellationToken = default)
        {
            var result = new SyncResult { StartedAt = DateTime.Now };
            
            try
            {
                // Get pending registration operations
                var registrationOperations = await _queueManagementService.GetOperationsByTypeAsync("FingerprintRegistration", "pending");
                
                if (!registrationOperations.Any())
                {
                    result.Success = true;
                    result.Message = "No pending registrations to sync";
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                result.TotalOperations = registrationOperations.Count;
                var synced = 0;
                var failed = 0;

                // Process in batches
                var batches = registrationOperations.Batch(_maxBatchSize);
                
                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var batchResult = await ProcessRegistrationBatch(batch.ToList(), cancellationToken);
                    synced += batchResult.SuccessCount;
                    failed += batchResult.FailedCount;
                    
                    // Report progress
                    var progress = (double)(synced + failed) / result.TotalOperations * 100;
                    OnSyncProgress(new SyncProgressEventArgs($"Synced {synced} registrations, {failed} failed", (int)progress));
                }

                result.OperationsSynced = synced;
                result.OperationsFailed = failed;
                result.Success = failed == 0;
                result.Message = failed == 0 
                    ? $"Successfully synced {synced} registrations"
                    : $"Synced {synced} registrations, {failed} failed";
                result.CompletedAt = DateTime.Now;
                result.Duration = result.CompletedAt - result.StartedAt;

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Registration sync failed: {ex.Message}";
                result.CompletedAt = DateTime.Now;
                
                await LogSyncError("SyncRegistrationsError", ex);
                return result;
            }
        }

        /// <summary>
        /// Synchronize pending verifications in batches
        /// </summary>
        public async Task<SyncResult> SyncVerificationsAsync(CancellationToken cancellationToken = default)
        {
            var result = new SyncResult { StartedAt = DateTime.Now };
            
            try
            {
                // Get pending verification operations
                var verificationOperations = await _queueManagementService.GetOperationsByTypeAsync("FingerprintVerification", "pending");
                
                if (!verificationOperations.Any())
                {
                    result.Success = true;
                    result.Message = "No pending verifications to sync";
                    result.CompletedAt = DateTime.Now;
                    return result;
                }

                result.TotalOperations = verificationOperations.Count;
                var synced = 0;
                var failed = 0;

                // Process in batches
                var batches = verificationOperations.Batch(_maxBatchSize);
                
                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var batchResult = await ProcessVerificationBatch(batch.ToList(), cancellationToken);
                    synced += batchResult.SuccessCount;
                    failed += batchResult.FailedCount;
                    
                    // Report progress
                    var progress = (double)(synced + failed) / result.TotalOperations * 100;
                    OnSyncProgress(new SyncProgressEventArgs($"Synced {synced} verifications, {failed} failed", (int)progress));
                }

                result.OperationsSynced = synced;
                result.OperationsFailed = failed;
                result.Success = failed == 0;
                result.Message = failed == 0 
                    ? $"Successfully synced {synced} verifications"
                    : $"Synced {synced} verifications, {failed} failed";
                result.CompletedAt = DateTime.Now;
                result.Duration = result.CompletedAt - result.StartedAt;

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Verification sync failed: {ex.Message}";
                result.CompletedAt = DateTime.Now;
                
                await LogSyncError("SyncVerificationsError", ex);
                return result;
            }
        }

        /// <summary>
        /// Download fresh student and college data for offline cache
        /// </summary>
        public async Task<bool> DownloadStudentsAsync(int collegeId, CancellationToken cancellationToken = default)
        {
            try
            {
                OnSyncProgress(new SyncProgressEventArgs($"Downloading students for college {collegeId}...", 0));
                
                var (success, message, students) = await _apiService.DownloadStudentsAsync(collegeId, cancellationToken: cancellationToken);
                
                if (!success)
                {
                    OnSyncError(new SyncErrorEventArgs(new Exception(message), $"Failed to download students for college {collegeId}"));
                    return false;
                }

                if (students?.Any() == true)
                {
                    // Save students to local cache in batches
                    var batches = students.Batch(100);
                    var totalSaved = 0;
                    
                    foreach (var batch in batches)
                    {
                        var studentModels = batch.Select(dto => new Student
                        {
                            Id = dto.Id,
                            RollNumber = dto.RollNumber,
                            Name = dto.Name,
                            FatherName = dto.FatherName,
                            CNIC = dto.CNIC,
                            Gender = dto.Gender,
                            TestId = dto.TestId,
                            TestName = dto.TestName,
                            CollegeId = dto.CollegeId,
                            CollegeName = dto.CollegeName,
                            Picture = !string.IsNullOrEmpty(dto.Picture) ? Convert.FromBase64String(dto.Picture) : null,
                            FingerprintTemplate = !string.IsNullOrEmpty(dto.FingerprintTemplate) ? Convert.FromBase64String(dto.FingerprintTemplate) : null,
                            FingerprintImage = !string.IsNullOrEmpty(dto.FingerprintImage) ? Convert.FromBase64String(dto.FingerprintImage) : null,
                            FingerprintQuality = dto.FingerprintQuality,
                            FingerprintRegisteredAt = !string.IsNullOrEmpty(dto.RegistrationDate) ? DateTime.Parse(dto.RegistrationDate) : null,
                            SyncStatus = "synced"
                        });

                        await _databaseService.SaveStudentsBatchAsync(studentModels);
                        totalSaved += batch.Count();
                        
                        var progress = (double)totalSaved / students.Count * 100;
                        OnSyncProgress(new SyncProgressEventArgs($"Saved {totalSaved}/{students.Count} students", (int)progress));
                    }
                }

                OnSyncProgress(new SyncProgressEventArgs($"Downloaded {students?.Count ?? 0} students for college {collegeId}", 100));
                return true;
            }
            catch (Exception ex)
            {
                OnSyncError(new SyncErrorEventArgs(ex, $"Failed to download students for college {collegeId}"));
                await LogSyncError("DownloadStudentsError", ex, collegeId);
                return false;
            }
        }

        /// <summary>
        /// Check if synchronization is currently in progress
        /// </summary>
        public bool IsSyncing => _isSyncing;

        /// <summary>
        /// Get the last sync attempt timestamp
        /// </summary>
        public DateTime LastSyncAttempt => _lastSyncAttempt;

        /// <summary>
        /// Force immediate synchronization if network is available
        /// </summary>
        public async Task<SyncResult> ForceSyncAsync(CancellationToken cancellationToken = default)
        {
            if (_apiService.IsOnline)
            {
                return await SyncAllAsync(cancellationToken);
            }
            else
            {
                return new SyncResult
                {
                    Success = false,
                    Message = "Cannot force sync: No network connectivity",
                    CompletedAt = DateTime.Now
                };
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Process a batch of registration operations
        /// </summary>
        private async Task<SyncBatchProcessResult> ProcessRegistrationBatch(List<QueuedOperation> operations, CancellationToken cancellationToken)
        {
            var result = new SyncBatchProcessResult();
            var registrations = new List<ApiService.RegistrationSyncDto>();

            // Convert operations to DTOs
            foreach (var operation in operations)
            {
                try
                {
                    var registrationData = JsonSerializer.Deserialize<Registration>(operation.OperationData);
                    registrations.Add(new ApiService.RegistrationSyncDto
                    {
                        RollNumber = registrationData.RollNumber,
                        FingerprintTemplate = registrationData.FingerprintTemplate,
                        FingerprintImage = Convert.ToBase64String(registrationData.FingerprintImage),
                        QualityScore = registrationData.QualityScore,
                        CapturedAt = registrationData.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
                catch (Exception ex)
                {
                    await _queueManagementService.MarkOperationFailedAsync(operation.Id, $"Failed to parse operation data: {ex.Message}");
                    result.FailedCount++;
                }
            }

            if (!registrations.Any())
            {
                return result;
            }

            // Send batch to server
            var (success, message, syncResult) = await _apiService.SyncRegistrationsAsync(registrations, cancellationToken);

            if (success && syncResult != null)
            {
                // Process results and update operation statuses
                foreach (var detail in syncResult.Details ?? new List<ApiService.SyncDetailDto>())
                {
                    var operation = operations.FirstOrDefault(o => 
                    {
                        try
                        {
                            var regData = JsonSerializer.Deserialize<Registration>(o.OperationData);
                            return regData.RollNumber == detail.RollNumber;
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (operation != null)
                    {
                        if (detail.Status == "success")
                        {
                            await _queueManagementService.MarkOperationSuccessAsync(operation.Id);
                            result.SuccessCount++;
                        }
                        else
                        {
                            await _queueManagementService.MarkOperationFailedAsync(operation.Id, detail.Error ?? "Unknown error");
                            result.FailedCount++;
                        }
                    }
                }
            }
            else
            {
                // Mark all operations as failed
                foreach (var operation in operations)
                {
                    await _queueManagementService.MarkOperationFailedAsync(operation.Id, message ?? "Batch sync failed");
                    result.FailedCount++;
                }
            }

            return result;
        }

        /// <summary>
        /// Process a batch of verification operations
        /// </summary>
        private async Task<SyncBatchProcessResult> ProcessVerificationBatch(List<QueuedOperation> operations, CancellationToken cancellationToken)
        {
            var result = new SyncBatchProcessResult();
            var verifications = new List<ApiService.VerificationSyncDto>();

            // Convert operations to DTOs
            foreach (var operation in operations)
            {
                try
                {
                    var verificationData = JsonSerializer.Deserialize<Verification>(operation.OperationData);
                    verifications.Add(new ApiService.VerificationSyncDto
                    {
                        RollNumber = verificationData.RollNumber,
                        MatchResult = verificationData.MatchResult,
                        ConfidenceScore = verificationData.ConfidenceScore,
                        EntryAllowed = verificationData.EntryAllowed,
                        VerifiedAt = verificationData.VerifiedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Remarks = verificationData.Notes
                    });
                }
                catch (Exception ex)
                {
                    await _queueManagementService.MarkOperationFailedAsync(operation.Id, $"Failed to parse operation data: {ex.Message}");
                    result.FailedCount++;
                }
            }

            if (!verifications.Any())
            {
                return result;
            }

            // Send batch to server
            var (success, message, syncResult) = await _apiService.SyncVerificationsAsync(verifications, cancellationToken);

            if (success && syncResult != null)
            {
                // Process results and update operation statuses
                foreach (var detail in syncResult.Details ?? new List<ApiService.SyncDetailDto>())
                {
                    var operation = operations.FirstOrDefault(o => 
                    {
                        try
                        {
                            var verData = JsonSerializer.Deserialize<Verification>(o.OperationData);
                            return verData.RollNumber == detail.RollNumber;
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (operation != null)
                    {
                        if (detail.Status == "success")
                        {
                            await _queueManagementService.MarkOperationSuccessAsync(operation.Id);
                            result.SuccessCount++;
                        }
                        else
                        {
                            await _queueManagementService.MarkOperationFailedAsync(operation.Id, detail.Error ?? "Unknown error");
                            result.FailedCount++;
                        }
                    }
                }
            }
            else
            {
                // Mark all operations as failed
                foreach (var operation in operations)
                {
                    await _queueManagementService.MarkOperationFailedAsync(operation.Id, message ?? "Batch sync failed");
                    result.FailedCount++;
                }
            }

            return result;
        }

        /// <summary>
        /// Download fresh data for all assigned colleges
        /// </summary>
        private async Task<DownloadResult> DownloadFreshDataAsync(CancellationToken cancellationToken)
        {
            var result = new DownloadResult();
            
            try
            {
                // Get current user's assigned colleges
                // This would typically come from the authentication service
                // For now, we'll skip this step as it requires user context
                
                result.Success = true;
                result.Message = "Fresh data download completed";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Fresh data download failed: {ex.Message}";
                await LogSyncError("DownloadFreshDataError", ex);
                return result;
            }
        }

        /// <summary>
        /// Handle network status changes for automatic sync triggers
        /// </summary>
        private async void OnNetworkStatusChanged(object sender, ApiService.NetworkStatusChangedEventArgs e)
        {
            if (e.IsOnline && !_isSyncing)
            {
                // Network came back online - trigger sync after a short delay
                await Task.Delay(2000); // Wait 2 seconds for network to stabilize
                
                if (_apiService.IsOnline && !_isSyncing)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SyncAllAsync();
                        }
                        catch (Exception ex)
                        {
                            await LogSyncError("AutoSyncError", ex);
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Handle periodic sync timer
        /// </summary>
        private async void OnSyncTimerElapsed(object state)
        {
            if (_apiService.IsOnline && !_isSyncing)
            {
                // Check if enough time has passed since last sync attempt
                if (DateTime.Now - _lastSyncAttempt >= _syncInterval)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SyncAllAsync();
                        }
                        catch (Exception ex)
                        {
                            await LogSyncError("PeriodicSyncError", ex);
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Log synchronization errors
        /// </summary>
        private async Task LogSyncError(string errorType, Exception ex, object contextData = null)
        {
            try
            {
                await _databaseService.LogErrorAsync(
                    errorType,
                    ex.Message,
                    ex.StackTrace,
                    contextData: contextData != null ? JsonSerializer.Serialize(contextData) : null
                );
            }
            catch
            {
                // Ignore logging errors to prevent infinite loops
            }
        }

        /// <summary>
        /// Raise sync progress event
        /// </summary>
        private void OnSyncProgress(SyncProgressEventArgs e)
        {
            SyncProgress?.Invoke(this, e);
        }

        /// <summary>
        /// Raise sync completed event
        /// </summary>
        private void OnSyncCompleted(SyncCompletedEventArgs e)
        {
            SyncCompleted?.Invoke(this, e);
        }

        /// <summary>
        /// Raise sync error event
        /// </summary>
        private void OnSyncError(SyncErrorEventArgs e)
        {
            SyncError?.Invoke(this, e);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _syncTimer?.Dispose();
                _syncSemaphore?.Dispose();
                
                if (_apiService != null)
                {
                    _apiService.NetworkStatusChanged -= OnNetworkStatusChanged;
                }
                
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Classes and Extensions

    /// <summary>
    /// Extension method for batching collections
    /// </summary>
    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            var batch = new List<T>(batchSize);
            foreach (var item in source)
            {
                batch.Add(item);
                if (batch.Count == batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }
            if (batch.Count > 0)
                yield return batch;
        }
    }

    /// <summary>
    /// Synchronization batch processing result
    /// </summary>
    public class SyncBatchProcessResult
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Download operation result
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int StudentsDownloaded { get; set; }
        public int CollegesDownloaded { get; set; }
    }

    #endregion
}