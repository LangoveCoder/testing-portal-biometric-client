using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using BACTBiometricClient.Models;
using Timer = System.Timers.Timer;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Background service for automatic queue processing when network is available
    /// Implements Requirements 4.4, 9.1 for automatic synchronization
    /// </summary>
    public class BackgroundQueueProcessor : IDisposable
    {
        private readonly IQueueManagementService _queueService;
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;
        private readonly Timer _processingTimer;
        private readonly object _lockObject = new object();
        
        private bool _isProcessing = false;
        private bool _disposed = false;
        private CancellationTokenSource _cancellationTokenSource;

        // Events for monitoring
        public event EventHandler<QueueProcessingEventArgs> ProcessingStarted;
        public event EventHandler<QueueProcessingEventArgs> ProcessingCompleted;
        public event EventHandler<QueueProcessingErrorEventArgs> ProcessingError;

        public BackgroundQueueProcessor(
            IQueueManagementService queueService, 
            ApiService apiService, 
            DatabaseService databaseService)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));

            _cancellationTokenSource = new CancellationTokenSource();

            // Initialize timer for periodic processing
            _processingTimer = new Timer();
            _processingTimer.Elapsed += OnProcessingTimerElapsed;
            _processingTimer.AutoReset = true;
            
            // Subscribe to network status changes
            _apiService.NetworkStatusChanged += OnNetworkStatusChanged;
        }

        #region Public Methods

        /// <summary>
        /// Start the background queue processor
        /// </summary>
        public async Task StartAsync(int intervalMinutes = 5)
        {
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(BackgroundQueueProcessor));

                // Set processing interval
                _processingTimer.Interval = TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds;
                _processingTimer.Start();

                await _databaseService.LogSyncOperationAsync(
                    "BackgroundProcessor", 
                    "start", 
                    0, 
                    0, 
                    0, 
                    $"Started with {intervalMinutes} minute interval", 
                    DateTime.Now, 
                    TimeSpan.Zero
                );

                // Process immediately if network is available
                if (_apiService.IsOnline)
                {
                    _ = Task.Run(async () => await ProcessQueueAsync(_cancellationTokenSource.Token));
                }
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "BackgroundProcessorStartError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        /// <summary>
        /// Stop the background queue processor
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                _processingTimer?.Stop();
                _cancellationTokenSource?.Cancel();

                // Wait for current processing to complete
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (_isProcessing && DateTime.Now - startTime < timeout)
                {
                    await Task.Delay(100);
                }

                await _databaseService.LogSyncOperationAsync(
                    "BackgroundProcessor", 
                    "stop", 
                    0, 
                    0, 
                    0, 
                    "Stopped background processing", 
                    DateTime.Now, 
                    TimeSpan.Zero
                );
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "BackgroundProcessorStopError", 
                    ex.Message, 
                    ex.StackTrace
                );
                throw;
            }
        }

        /// <summary>
        /// Manually trigger queue processing
        /// </summary>
        public async Task ProcessNowAsync()
        {
            if (!_apiService.IsOnline)
            {
                await _databaseService.LogSyncOperationAsync(
                    "ManualProcess", 
                    "skip", 
                    0, 
                    0, 
                    0, 
                    "Skipped - network offline", 
                    DateTime.Now, 
                    TimeSpan.Zero
                );
                return;
            }

            await ProcessQueueAsync(_cancellationTokenSource.Token);
        }

        /// <summary>
        /// Get current processing status
        /// </summary>
        public bool IsProcessing => _isProcessing;

        #endregion

        #region Private Methods

        /// <summary>
        /// Timer elapsed event handler
        /// </summary>
        private async void OnProcessingTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_apiService.IsOnline && !_isProcessing)
            {
                _ = Task.Run(async () => await ProcessQueueAsync(_cancellationTokenSource.Token));
            }
        }

        /// <summary>
        /// Network status changed event handler
        /// </summary>
        private async void OnNetworkStatusChanged(object sender, ApiService.NetworkStatusChangedEventArgs e)
        {
            if (e.IsOnline && !_isProcessing)
            {
                // Network came back online - process queue immediately
                _ = Task.Run(async () => await ProcessQueueAsync(_cancellationTokenSource.Token));
            }
        }

        /// <summary>
        /// Main queue processing logic
        /// </summary>
        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing || cancellationToken.IsCancellationRequested)
                return;

            lock (_lockObject)
            {
                if (_isProcessing)
                    return;
                _isProcessing = true;
            }

            var startTime = DateTime.Now;
            var totalProcessed = 0;
            var totalSuccess = 0;
            var totalFailed = 0;

            try
            {
                ProcessingStarted?.Invoke(this, new QueueProcessingEventArgs { StartedAt = startTime });

                // Get queue statistics
                var stats = await _queueService.GetQueueStatisticsAsync();
                
                if (stats.TotalPendingOperations == 0)
                {
                    return; // Nothing to process
                }

                // Process in batches to prevent memory issues and timeouts
                const int batchSize = 20; // Smaller batches for better reliability
                var hasMoreOperations = true;

                while (hasMoreOperations && !cancellationToken.IsCancellationRequested)
                {
                    var batch = await _queueService.GetNextBatchAsync(batchSize);
                    
                    if (batch.Count == 0)
                    {
                        hasMoreOperations = false;
                        continue;
                    }

                    var batchResult = await _queueService.ProcessBatchAsync(batch, ProcessSingleOperationAsync);
                    
                    totalProcessed += batchResult.TotalProcessed;
                    totalSuccess += batchResult.SuccessCount;
                    totalFailed += batchResult.FailedCount;

                    // Add delay between batches to prevent overwhelming the server
                    if (hasMoreOperations && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken); // 1 second delay
                    }
                }

                // Process retryable operations
                if (!cancellationToken.IsCancellationRequested)
                {
                    var retryResult = await _queueService.RetryFailedOperationsAsync(ProcessSingleOperationAsync);
                    totalProcessed += retryResult.TotalProcessed;
                    totalSuccess += retryResult.SuccessCount;
                    totalFailed += retryResult.FailedCount;
                }

                var duration = DateTime.Now - startTime;
                
                await _databaseService.LogSyncOperationAsync(
                    "BackgroundQueueProcess", 
                    "upload", 
                    totalProcessed, 
                    totalSuccess, 
                    totalFailed, 
                    totalFailed > 0 ? $"{totalFailed} operations failed" : null,
                    startTime, 
                    duration
                );

                ProcessingCompleted?.Invoke(this, new QueueProcessingEventArgs 
                { 
                    StartedAt = startTime,
                    CompletedAt = DateTime.Now,
                    TotalProcessed = totalProcessed,
                    SuccessCount = totalSuccess,
                    FailedCount = totalFailed
                });
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "BackgroundQueueProcessError", 
                    ex.Message, 
                    ex.StackTrace
                );

                ProcessingError?.Invoke(this, new QueueProcessingErrorEventArgs 
                { 
                    Error = ex,
                    StartedAt = startTime,
                    TotalProcessed = totalProcessed
                });
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Process a single queued operation
        /// </summary>
        private async Task<bool> ProcessSingleOperationAsync(QueuedOperation operation)
        {
            try
            {
                switch (operation.OperationType)
                {
                    case "FingerprintRegistration":
                        return await ProcessRegistrationAsync(operation);
                    
                    case "FingerprintVerification":
                        return await ProcessVerificationAsync(operation);
                    
                    case "StudentUpdate":
                        return await ProcessStudentUpdateAsync(operation);
                    
                    default:
                        await _databaseService.LogErrorAsync(
                            "UnknownOperationType", 
                            $"Unknown operation type: {operation.OperationType}", 
                            null,
                            contextData: System.Text.Json.JsonSerializer.Serialize(operation)
                        );
                        return false;
                }
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "ProcessSingleOperationError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: System.Text.Json.JsonSerializer.Serialize(new { operation.Id, operation.OperationType })
                );
                return false;
            }
        }

        /// <summary>
        /// Process fingerprint registration operation
        /// </summary>
        private async Task<bool> ProcessRegistrationAsync(QueuedOperation operation)
        {
            try
            {
                var registration = System.Text.Json.JsonSerializer.Deserialize<Registration>(operation.OperationData);
                
                // Call API to save registration
                var response = await _apiService.PostAsync<object>("/api/v1/operator/fingerprint/save", registration);
                
                return response.Success;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "ProcessRegistrationError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: System.Text.Json.JsonSerializer.Serialize(new { operation.Id })
                );
                return false;
            }
        }

        /// <summary>
        /// Process fingerprint verification operation
        /// </summary>
        private async Task<bool> ProcessVerificationAsync(QueuedOperation operation)
        {
            try
            {
                var verification = System.Text.Json.JsonSerializer.Deserialize<Verification>(operation.OperationData);
                
                // Call API to save verification result
                var response = await _apiService.PostAsync<object>("/api/v1/admin/verification/save", verification);
                
                return response.Success;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "ProcessVerificationError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: System.Text.Json.JsonSerializer.Serialize(new { operation.Id })
                );
                return false;
            }
        }

        /// <summary>
        /// Process student update operation
        /// </summary>
        private async Task<bool> ProcessStudentUpdateAsync(QueuedOperation operation)
        {
            try
            {
                var studentData = System.Text.Json.JsonSerializer.Deserialize<Student>(operation.OperationData);
                
                // Call API to update student data
                var response = await _apiService.PutAsync<object>($"/api/v1/students/{studentData.Id}", studentData);
                
                return response.Success;
            }
            catch (Exception ex)
            {
                await _databaseService.LogErrorAsync(
                    "ProcessStudentUpdateError", 
                    ex.Message, 
                    ex.StackTrace,
                    contextData: System.Text.Json.JsonSerializer.Serialize(new { operation.Id })
                );
                return false;
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
                _processingTimer?.Stop();
                _processingTimer?.Dispose();
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();

                if (_apiService != null)
                {
                    _apiService.NetworkStatusChanged -= OnNetworkStatusChanged;
                }

                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Event arguments for queue processing events
    /// </summary>
    public class QueueProcessingEventArgs : EventArgs
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
    }

    /// <summary>
    /// Event arguments for queue processing errors
    /// </summary>
    public class QueueProcessingErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public DateTime StartedAt { get; set; }
        public int TotalProcessed { get; set; }
    }
}