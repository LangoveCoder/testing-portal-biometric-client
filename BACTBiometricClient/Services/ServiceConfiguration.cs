using System;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Service configuration and dependency injection helper
    /// Provides centralized service management for the application
    /// </summary>
    public static class ServiceConfiguration
    {
        private static DatabaseService _databaseService;
        private static ApiService _apiService;
        private static AuthenticationService _authenticationService;
        private static IQueueManagementService _queueManagementService;
        private static BackgroundQueueProcessor _backgroundQueueProcessor;
        private static FingerprintService _fingerprintService;
        private static ISynchronizationService _synchronizationService;
        private static IOfflineDataManager _offlineDataManager;

        /// <summary>
        /// Initialize all services with proper dependencies
        /// </summary>
        public static void InitializeServices()
        {
            // Initialize database service first
            _databaseService = new DatabaseService();
            
            // Initialize API service
            _apiService = new ApiService("http://localhost:8000/api");
            
            // Initialize authentication service with dependencies
            _authenticationService = new AuthenticationService(_apiService, _databaseService);
            
            // Initialize queue management service
            _queueManagementService = new QueueManagementService(_databaseService);
            
            // Initialize synchronization service
            _synchronizationService = new SynchronizationService(
                _apiService, 
                _databaseService, 
                (QueueManagementService)_queueManagementService
            );
            
            // Initialize offline data manager
            // TODO: Temporarily disabled due to compilation order issue
            // _offlineDataManager = new OfflineDataManager(
            //     _apiService, 
            //     _databaseService, 
            //     _authenticationService
            // );
            
            // Initialize background queue processor
            _backgroundQueueProcessor = new BackgroundQueueProcessor(
                _queueManagementService, 
                _apiService, 
                _databaseService
            );
            
            // Initialize fingerprint service
            _fingerprintService = new FingerprintService();
        }

        /// <summary>
        /// Get database service instance
        /// </summary>
        public static DatabaseService GetDatabaseService()
        {
            return _databaseService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get API service instance
        /// </summary>
        public static ApiService GetApiService()
        {
            return _apiService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get authentication service instance
        /// </summary>
        public static AuthenticationService GetAuthenticationService()
        {
            return _authenticationService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get queue management service instance
        /// </summary>
        public static IQueueManagementService GetQueueManagementService()
        {
            return _queueManagementService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get synchronization service instance
        /// </summary>
        public static ISynchronizationService GetSynchronizationService()
        {
            return _synchronizationService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get background queue processor instance
        /// </summary>
        public static BackgroundQueueProcessor GetBackgroundQueueProcessor()
        {
            return _backgroundQueueProcessor ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get fingerprint service instance
        /// </summary>
        public static FingerprintService GetFingerprintService()
        {
            return _fingerprintService ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Get offline data manager instance
        /// </summary>
        public static IOfflineDataManager GetOfflineDataManager()
        {
            // TODO: Temporarily disabled due to compilation order issue
            throw new NotImplementedException("OfflineDataManager initialization is temporarily disabled due to compilation issues");
            // return _offlineDataManager ?? throw new InvalidOperationException("Services not initialized. Call InitializeServices() first.");
        }

        /// <summary>
        /// Dispose all services
        /// </summary>
        public static void DisposeServices()
        {
            _backgroundQueueProcessor?.Dispose();
            _offlineDataManager?.Dispose();
            _synchronizationService?.Dispose();
            _queueManagementService?.Dispose();
            _authenticationService?.Dispose();
            _apiService?.Dispose();
            _databaseService?.Dispose();
            _fingerprintService?.Dispose();

            _backgroundQueueProcessor = null;
            _offlineDataManager = null;
            _synchronizationService = null;
            _queueManagementService = null;
            _authenticationService = null;
            _apiService = null;
            _databaseService = null;
            _fingerprintService = null;
        }

        /// <summary>
        /// Start background services
        /// </summary>
        public static async System.Threading.Tasks.Task StartBackgroundServicesAsync()
        {
            if (_backgroundQueueProcessor != null)
            {
                // Get sync interval from settings
                var settings = await _databaseService.GetAppSettingsAsync();
                await _backgroundQueueProcessor.StartAsync(settings.SyncIntervalMinutes);
            }
        }

        /// <summary>
        /// Stop background services
        /// </summary>
        public static async System.Threading.Tasks.Task StopBackgroundServicesAsync()
        {
            if (_backgroundQueueProcessor != null)
            {
                await _backgroundQueueProcessor.StopAsync();
            }
        }
    }
}