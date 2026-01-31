using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Cache metadata model for tracking cache state
    /// </summary>
    public class CacheMetadata
    {
        public string CacheType { get; set; }
        public int RecordCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}