using System;

namespace BACTBiometricClient.Models
{
    public class Student
    {
        // Primary Key
        public int Id { get; set; }
        public int StudentId { get; set; }  // Legacy/alternate ID

        // Student Information
        public string RollNumber { get; set; }
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string CNIC { get; set; }
        public string Gender { get; set; }

        // Test & College Information
        public int? TestId { get; set; }
        public string TestName { get; set; }
        public int? CollegeId { get; set; }
        public string CollegeName { get; set; }

        // Venue & Seating Information
        public string Venue { get; set; }
        public string Hall { get; set; }
        public string Zone { get; set; }
        public string Row { get; set; }
        public string Seat { get; set; }

        // Photos
        public byte[] Picture { get; set; }
        public byte[] TestPhoto { get; set; }

        // Fingerprint Data
        public byte[] FingerprintTemplate { get; set; }
        public byte[] FingerprintImage { get; set; }
        public DateTime? FingerprintRegisteredAt { get; set; }
        public int? FingerprintQuality { get; set; }

        // Sync Status
        public string SyncStatus { get; set; }  // "pending", "synced", "failed"
        public DateTime? LastSyncedAt { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}