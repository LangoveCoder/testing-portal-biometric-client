using System;
using System.Collections.Generic;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents a logged-in user with authentication and role information
    /// </summary>
    public class User
    {
        // Primary identifiers
        public int Id { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }

        // Authentication
        public string Token { get; set; }
        public DateTime? TokenExpiresAt { get; set; }

        // Assignment information
        public List<int> AssignedTestIds { get; set; }
        public List<string> AssignedTestNames { get; set; }
        public int? AssignedCollegeId { get; set; }

        // Session tracking
        public DateTime LoggedInAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; }

        // Computed properties
        public bool IsOperator => Role?.ToLower() == "operator";
        public bool IsCollegeAdmin => Role?.ToLower() == "college" || Role?.ToLower() == "college_admin";
        public bool IsTokenExpired => TokenExpiresAt.HasValue && TokenExpiresAt.Value <= DateTime.Now;
        public bool IsTokenValid => !string.IsNullOrEmpty(Token) && !IsTokenExpired;
        public string RoleDisplay => Role?.ToUpper() ?? "UNKNOWN";

        public User()
        {
            AssignedTestIds = new List<int>();
            AssignedTestNames = new List<string>();
            LoggedInAt = DateTime.Now;
            LastActivityAt = DateTime.Now;
            IsActive = true;
        }

        /// <summary>
        /// Update the last activity timestamp
        /// </summary>
        public void UpdateActivity()
        {
            LastActivityAt = DateTime.Now;
        }

        /// <summary>
        /// Check if session has been inactive for specified minutes
        /// </summary>
        public bool IsInactive(int minutes)
        {
            return (DateTime.Now - LastActivityAt).TotalMinutes > minutes;
        }

        /// <summary>
        /// Check if user has access to a specific test
        /// </summary>
        public bool HasAccessToTest(int testId)
        {
            return AssignedTestIds.Contains(testId);
        }

        /// <summary>
        /// Logout the user by marking as inactive
        /// </summary>
        public void Logout()
        {
            IsActive = false;
            Token = null;
        }
    }
}