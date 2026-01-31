using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Interface for offline-first data management
    /// Handles caching, refresh logic, and offline operation continuity
    /// Implements Requirements 4.1, 4.2, 4.5 for offline data management
    /// </summary>
    public interface IOfflineDataManager : IDisposable
    {
        /// <summary>
        /// Download and cache students for specified colleges
        /// </summary>
        Task<CacheResult> CacheStudentsAsync(List<int> collegeIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Download and cache colleges for current user
        /// </summary>
        Task<CacheResult> CacheCollegesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh cached data if stale
        /// </summary>
        Task<RefreshResult> RefreshCacheIfStaleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get cached students with optional filtering
        /// </summary>
        Task<List<Student>> GetCachedStudentsAsync(int? collegeId = null, int? testId = null, string searchTerm = null);

        /// <summary>
        /// Get cached colleges
        /// </summary>
        Task<List<College>> GetCachedCollegesAsync();

        /// <summary>
        /// Check if cache is stale and needs refresh
        /// </summary>
        Task<bool> IsCacheStaleAsync();

        /// <summary>
        /// Get cache statistics
        /// </summary>
        Task<CacheStatistics> GetCacheStatisticsAsync();

        /// <summary>
        /// Clear all cached data
        /// </summary>
        Task ClearCacheAsync();

        /// <summary>
        /// Ensure offline operation continuity
        /// </summary>
        Task<bool> EnsureOfflineContinuityAsync();

        /// <summary>
        /// Event raised when cache is updated
        /// </summary>
        event EventHandler<CacheUpdatedEventArgs> CacheUpdated;

        /// <summary>
        /// Event raised when cache refresh is needed
        /// </summary>
        event EventHandler<CacheRefreshNeededEventArgs> CacheRefreshNeeded;

        /// <summary>
        /// Event raised when offline continuity status changes
        /// </summary>
        event EventHandler<OfflineContinuityEventArgs> OfflineContinuityChanged;
    }

    /// <summary>
    /// Result of cache operation
    /// </summary>
    public class CacheResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int StudentsCount { get; set; }
        public int CollegesCount { get; set; }
        public DateTime CachedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of cache refresh operation
    /// </summary>
    public class RefreshResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool WasStale { get; set; }
        public int UpdatedStudents { get; set; }
        public int UpdatedColleges { get; set; }
        public DateTime RefreshedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        public int TotalStudents { get; set; }
        public int TotalColleges { get; set; }
        public DateTime LastUpdated { get; set; }
        public TimeSpan CacheAge { get; set; }
        public bool IsStale { get; set; }
        public long CacheSizeBytes { get; set; }
        public Dictionary<int, int> StudentsByCollege { get; set; } = new Dictionary<int, int>();
    }

    /// <summary>
    /// Event arguments for cache updated
    /// </summary>
    public class CacheUpdatedEventArgs : EventArgs
    {
        public string CacheType { get; }
        public int RecordsCount { get; }
        public DateTime UpdatedAt { get; }

        public CacheUpdatedEventArgs(string cacheType, int recordsCount)
        {
            CacheType = cacheType;
            RecordsCount = recordsCount;
            UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for cache refresh needed
    /// </summary>
    public class CacheRefreshNeededEventArgs : EventArgs
    {
        public string Reason { get; }
        public TimeSpan CacheAge { get; }
        public DateTime DetectedAt { get; }

        public CacheRefreshNeededEventArgs(string reason, TimeSpan cacheAge)
        {
            Reason = reason;
            CacheAge = cacheAge;
            DetectedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for offline continuity changes
    /// </summary>
    public class OfflineContinuityEventArgs : EventArgs
    {
        public bool IsOfflineReady { get; }
        public string Status { get; }
        public DateTime StatusChangedAt { get; }

        public OfflineContinuityEventArgs(bool isOfflineReady, string status)
        {
            IsOfflineReady = isOfflineReady;
            Status = status;
            StatusChangedAt = DateTime.Now;
        }
    }
}