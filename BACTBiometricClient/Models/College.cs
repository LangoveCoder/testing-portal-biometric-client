using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents a college/institution in the system
    /// Used for caching and offline operations
    /// </summary>
    public class College
    {
        // Primary key
        public int Id { get; set; }

        // College information
        public string Name { get; set; }
        public string District { get; set; }
        public string Address { get; set; }
        public string ContactNumber { get; set; }
        public string Email { get; set; }

        // Status and metadata
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string SyncStatus { get; set; }

        // Computed properties
        public bool IsCached => SyncStatus == "cached";
        public string DisplayName => $"{Name} - {District}";

        public College()
        {
            CreatedAt = DateTime.Now;
            IsActive = true;
            SyncStatus = "pending";
        }

        /// <summary>
        /// Mark this college as cached
        /// </summary>
        public void MarkAsCached()
        {
            SyncStatus = "cached";
            UpdatedAt = DateTime.Now;
        }

        /// <summary>
        /// Update college information
        /// </summary>
        public void UpdateInfo(string name, string district, string address = null, string contactNumber = null, string email = null)
        {
            Name = name;
            District = district;
            Address = address;
            ContactNumber = contactNumber;
            Email = email;
            UpdatedAt = DateTime.Now;
        }
    }
}