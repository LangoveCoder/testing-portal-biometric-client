using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Offline-first data management service
    /// Implements Requirements 4.1, 4.2, 4.5 for data caching, refresh logic, and offline operation continuity
    /// </summary>
    public class OfflineDataManager : IOfflineDataManager, IDisposable
    {
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;
        private readonly AuthenticationService _authenticationService;
        private readonly Timer _stalenessCheckTimer;
        private readonly SemaphoreSlim _cacheSemaphore;
        
        private bool _disposed = false;
        private readonly TimeSpan _cacheExpiryTime = TimeSpan.FromHours(24); // Cache expires after 24 hours
        private readonly TimeSpan _stalenessCheckInterval = TimeSpan.FromMinutes(30); // Check staleness every 30 minutes
        private readonly int _maxRetryAttempts = 3;

        // Events
        public event EventHandler<CacheUpdatedEventArgs> CacheUpdated;
        public event EventHandler<CacheRefreshNeededEventArgs> CacheRefreshNeeded;
        public event EventHandler<OfflineContinuityEventArgs> OfflineContinuityChanged;

        public OfflineDataManager(
            ApiService apiService,
            DatabaseService databaseService,
            AuthenticationService authenticationService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            
            _cacheSemaphore = new SemaphoreSlim(1, 1);
            
            // Subscribe to network status changes
            _apiService.NetworkStatusChanged += OnNetworkStatusChanged;
            
            // Initialize staleness check timer
            _stalenessCheckTimer = new Timer(OnStalenessCheckTimerElapsed, null, _stalenessCheckInterval, _stalenessCheckInterval);
            
            Logger.Info("OfflineDataManager initialized");
        }

        #region Public Interface

        /// <summary>
        /// Download and cache students for specified colleges
        /// </summary>
        public async Task<CacheResult> CacheStudentsAsync(List<int> collegeIds, CancellationToken cancellationToken = default)
        {
            if (!await _cacheSemaphore.WaitAsync(5000, cancellationToken))
            {
                return new CacheResult
                {
                    Success = false,
                    Message = "Cache operation already in progress",
                    CachedAt = DateTime.Now
                };
            }

            try
            {
                var result = new CacheResult
                {
                    CachedAt = DateTime.Now
                };
                var startTime = DateTime.Now;

                Logger.Info($"Starting cache operation for {collegeIds.Count} colleges");

                if (!_apiService.IsOnline)
                {
                    result.Success = false;
                    result.Message = "No network connectivity available for caching";
                    return result;
                }

                var totalStudents = 0;
                var errors = new List<string>();

                foreach (var collegeId in collegeIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var (success, message, students) = await _apiService.DownloadStudentsAsync(collegeId, cancellationToken: cancellationToken);
                        
                        if (success && students?.Any() == true)
                        {
                            // Convert DTOs to models and save to cache
                            var studentModels = students.Select(dto => new Student
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
                                FingerprintQuality = dto.FingerprintQuality ?? dto.QualityScore,
                                FingerprintRegisteredAt = !string.IsNullOrEmpty(dto.RegistrationDate) ? DateTime.Parse(dto.RegistrationDate) : null,
                                SyncStatus = "cached"
                            });

                            await _databaseService.SaveStudentsBatchAsync(studentModels);
                            totalStudents += students.Count;
                            
                            Logger.Info($"Cached {students.Count} students for college {collegeId}");
                        }
                        else
                        {
                            var error = $"Failed to download students for college {collegeId}: {message}";
                            errors.Add(error);
                            Logger.Warning(error);
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = $"Error caching students for college {collegeId}: {ex.Message}";
                        errors.Add(error);
                        Logger.Error(error, ex);
                    }
                }

                // Update cache metadata
                await UpdateCacheMetadataAsync("students", totalStudents);

                result.Success = errors.Count == 0;
                result.StudentsCount = totalStudents;
                result.Duration = DateTime.Now - startTime;
                result.Errors = errors;
                result.Message = result.Success 
                    ? $"Successfully cached {totalStudents} students from {collegeIds.Count} colleges"
                    : $"Cached {totalStudents} students with {errors.Count} errors";

                // Raise cache updated event
                OnCacheUpdated(new CacheUpdatedEventArgs("students", totalStudents));

                // Check offline continuity after caching
                await EnsureOfflineContinuityAsync();

                Logger.Info($"Cache operation completed: {result.Message}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("Cache students operation failed", ex);
                return new CacheResult
                {
                    Success = false,
                    Message = $"Cache operation failed: {ex.Message}",
                    CachedAt = DateTime.Now,
                    Errors = new List<string> { ex.Message }
                };
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Download and cache colleges for current user
        /// </summary>
        public async Task<CacheResult> CacheCollegesAsync(CancellationToken cancellationToken = default)
        {
            if (!await _cacheSemaphore.WaitAsync(5000, cancellationToken))
            {
                return new CacheResult
                {
                    Success = false,
                    Message = "Cache operation already in progress",
                    CachedAt = DateTime.Now
                };
            }

            try
            {
                var result = new CacheResult
                {
                    CachedAt = DateTime.Now
                };
                var startTime = DateTime.Now;

                Logger.Info("Starting college cache operation");

                if (!_apiService.IsOnline)
                {
                    result.Success = false;
                    result.Message = "No network connectivity available for caching";
                    return result;
                }

                var (success, message, colleges) = await _apiService.GetAssignedCollegesAsync(cancellationToken);
                
                if (success && colleges?.Any() == true)
                {
                    // Convert DTOs to models and save to cache
                    var collegeModels = colleges.Select(dto => new College
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        District = dto.District
                    });

                    await _databaseService.SaveCollegesBatchAsync(collegeModels);
                    
                    // Update cache metadata
                    await UpdateCacheMetadataAsync("colleges", colleges.Count);

                    result.Success = true;
                    result.CollegesCount = colleges.Count;
                    result.Duration = DateTime.Now - startTime;
                    result.Message = $"Successfully cached {colleges.Count} colleges";

                    // Raise cache updated event
                    OnCacheUpdated(new CacheUpdatedEventArgs("colleges", colleges.Count));

                    Logger.Info($"Cached {colleges.Count} colleges successfully");
                }
                else
                {
                    result.Success = false;
                    result.Message = $"Failed to download colleges: {message}";
                    result.Errors.Add(message);
                    Logger.Warning($"Failed to cache colleges: {message}");
                }

                result.Duration = DateTime.Now - startTime;
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("Cache colleges operation failed", ex);
                return new CacheResult
                {
                    Success = false,
                    Message = $"Cache operation failed: {ex.Message}",
                    CachedAt = DateTime.Now,
                    Errors = new List<string> { ex.Message }
                };
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Refresh cached data if stale
        /// </summary>
        public async Task<RefreshResult> RefreshCacheIfStaleAsync(CancellationToken cancellationToken = default)
        {
            var result = new RefreshResult
            {
                RefreshedAt = DateTime.Now
            };
            var startTime = DateTime.Now;

            try
            {
                Logger.Info("Checking cache staleness for refresh");

                var isStale = await IsCacheStaleAsync();
                result.WasStale = isStale;

                if (!isStale)
                {
                    result.Success = true;
                    result.Message = "Cache is fresh, no refresh needed";
                    Logger.Info("Cache is fresh, skipping refresh");
                    return result;
                }

                if (!_apiService.IsOnline)
                {
                    result.Success = false;
                    result.Message = "Cache is stale but no network connectivity available";
                    Logger.Warning("Cache is stale but offline - cannot refresh");
                    return result;
                }

                Logger.Info("Cache is stale, starting refresh operation");

                // Refresh colleges first
                var collegeResult = await CacheCollegesAsync(cancellationToken);
                result.UpdatedColleges = collegeResult.CollegesCount;

                // Get college IDs for student refresh
                var colleges = await GetCachedCollegesAsync();
                var collegeIds = colleges.Select(c => c.Id).ToList();

                if (collegeIds.Any())
                {
                    var studentResult = await CacheStudentsAsync(collegeIds, cancellationToken);
                    result.UpdatedStudents = studentResult.StudentsCount;
                    
                    result.Success = collegeResult.Success && studentResult.Success;
                    result.Message = result.Success
                        ? $"Cache refreshed: {result.UpdatedColleges} colleges, {result.UpdatedStudents} students"
                        : "Cache refresh completed with some errors";
                }
                else
                {
                    result.Success = collegeResult.Success;
                    result.Message = collegeResult.Success
                        ? "Cache refreshed: colleges only (no assigned colleges found)"
                        : "Cache refresh failed for colleges";
                }

                result.Duration = DateTime.Now - startTime;
                Logger.Info($"Cache refresh completed: {result.Message}");
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("Cache refresh operation failed", ex);
                result.Success = false;
                result.Message = $"Cache refresh failed: {ex.Message}";
                result.Duration = DateTime.Now - startTime;
                return result;
            }
        }

        /// <summary>
        /// Get cached students with optional filtering
        /// </summary>
        public async Task<List<Student>> GetCachedStudentsAsync(int? collegeId = null, int? testId = null, string searchTerm = null)
        {
            try
            {
                return await _databaseService.SearchStudentsAsync(searchTerm ?? "", collegeId, testId ?? 0);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get cached students", ex);
                return new List<Student>();
            }
        }

        /// <summary>
        /// Get cached colleges
        /// </summary>
        public async Task<List<College>> GetCachedCollegesAsync()
        {
            try
            {
                return await _databaseService.GetCollegesAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get cached colleges", ex);
                return new List<College>();
            }
        }

        /// <summary>
        /// Check if cache is stale and needs refresh
        /// </summary>
        public async Task<bool> IsCacheStaleAsync()
        {
            try
            {
                var metadata = await _databaseService.GetCacheMetadataAsync("students");
                if (metadata == null)
                {
                    Logger.Info("No cache metadata found - cache is stale");
                    return true;
                }

                var cacheAge = DateTime.Now - metadata.LastUpdated;
                var isStale = cacheAge > _cacheExpiryTime;
                
                Logger.Info($"Cache age: {cacheAge}, Expiry time: {_cacheExpiryTime}, Is stale: {isStale}");
                return isStale;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to check cache staleness", ex);
                return true; // Assume stale on error
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public async Task<CacheStatistics> GetCacheStatisticsAsync()
        {
            try
            {
                var stats = new CacheStatistics();
                
                // Get student count
                var students = await _databaseService.SearchStudentsAsync("", null, 1000);
                stats.TotalStudents = students.Count;
                
                // Get college count
                var colleges = await _databaseService.GetCollegesAsync();
                stats.TotalColleges = colleges.Count;
                
                // Get students by college
                stats.StudentsByCollege = students
                    .Where(s => s.CollegeId.HasValue)
                    .GroupBy(s => s.CollegeId.Value)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                // Get cache metadata
                var metadata = await _databaseService.GetCacheMetadataAsync("students");
                if (metadata != null)
                {
                    stats.LastUpdated = metadata.LastUpdated;
                    stats.CacheAge = DateTime.Now - metadata.LastUpdated;
                    stats.IsStale = stats.CacheAge > _cacheExpiryTime;
                }
                else
                {
                    stats.LastUpdated = DateTime.MinValue;
                    stats.CacheAge = TimeSpan.MaxValue;
                    stats.IsStale = true;
                }
                
                // Estimate cache size (rough calculation)
                stats.CacheSizeBytes = (stats.TotalStudents * 1024) + (stats.TotalColleges * 256); // Rough estimate
                
                return stats;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get cache statistics", ex);
                return new CacheStatistics
                {
                    LastUpdated = DateTime.MinValue,
                    CacheAge = TimeSpan.MaxValue,
                    IsStale = true
                };
            }
        }

        /// <summary>
        /// Clear all cached data
        /// </summary>
        public async Task ClearCacheAsync()
        {
            try
            {
                Logger.Info("Clearing all cached data");
                
                await _databaseService.ClearStudentCacheAsync();
                await _databaseService.ClearCollegeCacheAsync();
                await _databaseService.ClearCacheMetadataAsync();
                
                Logger.Info("Cache cleared successfully");
                
                // Raise cache updated event
                OnCacheUpdated(new CacheUpdatedEventArgs("all", 0));
                
                // Check offline continuity after clearing cache
                await EnsureOfflineContinuityAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear cache", ex);
                throw;
            }
        }

        /// <summary>
        /// Ensure offline operation continuity
        /// </summary>
        public async Task<bool> EnsureOfflineContinuityAsync()
        {
            try
            {
                var stats = await GetCacheStatisticsAsync();
                var hasStudents = stats.TotalStudents > 0;
                var hasColleges = stats.TotalColleges > 0;
                var cacheNotTooStale = stats.CacheAge < TimeSpan.FromDays(7); // Allow up to 7 days for offline operation
                
                var isOfflineReady = hasStudents && hasColleges && cacheNotTooStale;
                
                var status = isOfflineReady 
                    ? "Offline operations ready"
                    : $"Offline readiness issues: Students={hasStudents}, Colleges={hasColleges}, Fresh={cacheNotTooStale}";
                
                Logger.Info($"Offline continuity check: {status}");
                
                // Raise offline continuity event
                OnOfflineContinuityChanged(new OfflineContinuityEventArgs(isOfflineReady, status));
                
                return isOfflineReady;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to check offline continuity", ex);
                OnOfflineContinuityChanged(new OfflineContinuityEventArgs(false, $"Continuity check failed: {ex.Message}"));
                return false;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Update cache metadata
        /// </summary>
        private async Task UpdateCacheMetadataAsync(string cacheType, int recordCount)
        {
            try
            {
                await _databaseService.UpdateCacheMetadataAsync(cacheType, recordCount);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update cache metadata for {cacheType}", ex);
            }
        }

        /// <summary>
        /// Handle network status changes
        /// </summary>
        private async void OnNetworkStatusChanged(object sender, ApiService.NetworkStatusChangedEventArgs e)
        {
            if (e.IsOnline)
            {
                Logger.Info("Network came online - checking cache staleness");
                
                // Check if cache needs refresh when network comes back
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000); // Wait 5 seconds for network to stabilize
                        
                        if (await IsCacheStaleAsync())
                        {
                            OnCacheRefreshNeeded(new CacheRefreshNeededEventArgs("Network reconnected and cache is stale", 
                                (await GetCacheStatisticsAsync()).CacheAge));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error checking cache staleness after network reconnection", ex);
                    }
                });
            }
        }

        /// <summary>
        /// Handle staleness check timer
        /// </summary>
        private async void OnStalenessCheckTimerElapsed(object state)
        {
            try
            {
                if (await IsCacheStaleAsync())
                {
                    var stats = await GetCacheStatisticsAsync();
                    OnCacheRefreshNeeded(new CacheRefreshNeededEventArgs("Periodic staleness check", stats.CacheAge));
                    
                    // Auto-refresh if online
                    if (_apiService.IsOnline)
                    {
                        Logger.Info("Auto-refreshing stale cache");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RefreshCacheIfStaleAsync();
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Auto-refresh failed", ex);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Staleness check timer error", ex);
            }
        }

        /// <summary>
        /// Raise cache updated event
        /// </summary>
        private void OnCacheUpdated(CacheUpdatedEventArgs e)
        {
            CacheUpdated?.Invoke(this, e);
        }

        /// <summary>
        /// Raise cache refresh needed event
        /// </summary>
        private void OnCacheRefreshNeeded(CacheRefreshNeededEventArgs e)
        {
            CacheRefreshNeeded?.Invoke(this, e);
        }

        /// <summary>
        /// Raise offline continuity changed event
        /// </summary>
        private void OnOfflineContinuityChanged(OfflineContinuityEventArgs e)
        {
            OfflineContinuityChanged?.Invoke(this, e);
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
                _stalenessCheckTimer?.Dispose();
                _cacheSemaphore?.Dispose();
                
                if (_apiService != null)
                {
                    _apiService.NetworkStatusChanged -= OnNetworkStatusChanged;
                }
                
                _disposed = true;
            }
        }

        #endregion
    }
}