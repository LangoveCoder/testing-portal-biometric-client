using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Service for managing role-based user interface adaptations
    /// Implements Requirements 3.1, 3.2, 3.3, 3.4, 3.5 for role-based functionality
    /// </summary>
    public class RoleBasedUIService
    {
        private readonly DatabaseService _database;
        private User _currentUser;

        public RoleBasedUIService(DatabaseService database)
        {
            _database = database;
        }

        /// <summary>
        /// Set the current user for role-based operations
        /// </summary>
        public void SetCurrentUser(User user)
        {
            _currentUser = user;
            Logger.Info($"RoleBasedUIService: User set to {user?.Name} ({user?.Role})");
        }

        /// <summary>
        /// Get colleges assigned to the current user based on their role
        /// Requirement 3.1: Operators access multiple colleges, College Admins access single college
        /// </summary>
        public List<College> GetAssignedColleges()
        {
            if (_currentUser == null)
            {
                Logger.Warning("No current user set for college access");
                return new List<College>();
            }

            try
            {
                if (_currentUser.IsOperator)
                {
                    // Operators can access multiple colleges
                    var allColleges = _database.GetAllColleges();
                    Logger.Info($"Operator {_currentUser.Name} has access to {allColleges.Count} colleges");
                    return allColleges;
                }
                else if (_currentUser.IsCollegeAdmin)
                {
                    // College admins access only their assigned college
                    if (_currentUser.AssignedCollegeId.HasValue)
                    {
                        var college = _database.GetCollegeById(_currentUser.AssignedCollegeId.Value);
                        if (college != null)
                        {
                            Logger.Info($"College Admin {_currentUser.Name} has access to college: {college.Name}");
                            return new List<College> { college };
                        }
                    }
                    
                    Logger.Warning($"College Admin {_currentUser.Name} has no assigned college");
                    return new List<College>();
                }
                else
                {
                    // Unknown role - no college access
                    Logger.Warning($"User {_currentUser.Name} has unknown role: {_currentUser.Role}");
                    return new List<College>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get assigned colleges", ex);
                return new List<College>();
            }
        }

        /// <summary>
        /// Check if user has access to a specific college
        /// Requirement 3.4: College data filtering based on user assignments
        /// </summary>
        public bool HasAccessToCollege(int collegeId)
        {
            if (_currentUser == null)
                return false;

            var assignedColleges = GetAssignedColleges();
            return assignedColleges.Any(c => c.Id == collegeId);
        }

        /// <summary>
        /// Get students filtered by user's college access
        /// Requirement 3.4: Show only students from assigned colleges
        /// </summary>
        public List<Student> GetFilteredStudents(string searchTerm = null, int? collegeId = null)
        {
            if (_currentUser == null)
            {
                Logger.Warning("No current user set for student filtering");
                return new List<Student>();
            }

            try
            {
                var assignedColleges = GetAssignedColleges();
                var assignedCollegeIds = assignedColleges.Select(c => c.Id).ToList();

                if (!assignedCollegeIds.Any())
                {
                    Logger.Warning($"User {_currentUser.Name} has no assigned colleges");
                    return new List<Student>();
                }

                // If specific college requested, validate access
                if (collegeId.HasValue)
                {
                    if (!HasAccessToCollege(collegeId.Value))
                    {
                        Logger.Warning($"User {_currentUser.Name} denied access to college {collegeId}");
                        return new List<Student>();
                    }
                    assignedCollegeIds = new List<int> { collegeId.Value };
                }

                var students = _database.SearchStudents(searchTerm, assignedCollegeIds);
                Logger.Info($"Filtered {students.Count} students for user {_currentUser.Name}");
                return students;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get filtered students", ex);
                return new List<Student>();
            }
        }

        /// <summary>
        /// Configure UI elements based on user role
        /// Requirement 3.2: Role-based UI adaptation
        /// </summary>
        public void ConfigureUIForRole(TabControl mainTabControl, TabItem registrationTab, TabItem verificationTab)
        {
            if (_currentUser == null)
            {
                Logger.Warning("No current user for UI configuration");
                HideAllTabs(registrationTab, verificationTab);
                return;
            }

            try
            {
                if (_currentUser.IsOperator)
                {
                    // Operators: Registration interface with college selection
                    registrationTab.Visibility = Visibility.Visible;
                    verificationTab.Visibility = Visibility.Collapsed;
                    mainTabControl.SelectedItem = registrationTab;

                    Logger.Info($"UI configured for Operator: {_currentUser.Name}");
                }
                else if (_currentUser.IsCollegeAdmin)
                {
                    // College Admins: Verification interface restricted to assigned college
                    registrationTab.Visibility = Visibility.Collapsed;
                    verificationTab.Visibility = Visibility.Visible;
                    mainTabControl.SelectedItem = verificationTab;

                    Logger.Info($"UI configured for College Admin: {_currentUser.Name}");
                }
                else
                {
                    // Unknown role: Show both tabs (fallback for development/admin)
                    registrationTab.Visibility = Visibility.Visible;
                    verificationTab.Visibility = Visibility.Visible;
                    mainTabControl.SelectedItem = registrationTab;

                    Logger.Warning($"UI configured for unknown role: {_currentUser.Role}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to configure UI for role", ex);
                HideAllTabs(registrationTab, verificationTab);
            }
        }

        /// <summary>
        /// Validate if user can perform a specific action
        /// Requirement 3.5: Prevent unauthorized actions
        /// </summary>
        public ValidationResult ValidateAction(UserAction action, int? collegeId = null)
        {
            if (_currentUser == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "No user session found. Please login again."
                };
            }

            try
            {
                switch (action)
                {
                    case UserAction.RegisterFingerprint:
                        if (!_currentUser.IsOperator)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Only Biometric Operators can register fingerprints."
                            };
                        }
                        break;

                    case UserAction.VerifyFingerprint:
                        if (!_currentUser.IsCollegeAdmin)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Only College Admins can verify fingerprints."
                            };
                        }
                        break;

                    case UserAction.AccessCollegeData:
                        if (collegeId.HasValue && !HasAccessToCollege(collegeId.Value))
                        {
                            var collegeName = _database.GetCollegeById(collegeId.Value)?.Name ?? "Unknown";
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"You don't have access to {collegeName} college data."
                            };
                        }
                        break;

                    case UserAction.ViewStudentData:
                        // All authenticated users can view student data (filtered by college access)
                        break;

                    default:
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "Unknown action requested."
                        };
                }

                return new ValidationResult { IsValid = true };
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to validate action {action}", ex);
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Validation error occurred. Please try again."
                };
            }
        }

        /// <summary>
        /// Get role-specific welcome message
        /// </summary>
        public string GetWelcomeMessage()
        {
            if (_currentUser == null)
                return "Welcome to BACT Biometric System";

            return _currentUser.Role?.ToLower() switch
            {
                "operator" => $"Welcome, {_currentUser.Name}! Ready to register student fingerprints.",
                "college" or "college_admin" => $"Welcome, {_currentUser.Name}! Ready to verify student identities.",
                _ => $"Welcome, {_currentUser.Name}! System access granted."
            };
        }

        /// <summary>
        /// Get role-specific instructions
        /// </summary>
        public string GetRoleInstructions()
        {
            if (_currentUser == null)
                return "Please login to access the system.";

            return _currentUser.Role?.ToLower() switch
            {
                "operator" => "Search for students and register their fingerprints during test day operations.",
                "college" or "college_admin" => "Verify student identities using fingerprint matching during admission interviews.",
                _ => "Access system features based on your assigned role."
            };
        }

        /// <summary>
        /// Create college selection UI for operators
        /// Requirement 3.1: Operator college selection interface
        /// </summary>
        public ComboBox CreateCollegeSelectionComboBox()
        {
            var comboBox = new ComboBox
            {
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id",
                Height = 40,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };

            if (_currentUser?.IsOperator == true)
            {
                var colleges = GetAssignedColleges();
                comboBox.ItemsSource = colleges;

                if (colleges.Count == 1)
                {
                    comboBox.SelectedIndex = 0;
                    comboBox.IsEnabled = false; // Single college - no selection needed
                }
            }

            return comboBox;
        }

        private void HideAllTabs(TabItem registrationTab, TabItem verificationTab)
        {
            registrationTab.Visibility = Visibility.Collapsed;
            verificationTab.Visibility = Visibility.Collapsed;
        }
    }

    #region Supporting Classes

    /// <summary>
    /// User actions that require role-based validation
    /// </summary>
    public enum UserAction
    {
        RegisterFingerprint,
        VerifyFingerprint,
        AccessCollegeData,
        ViewStudentData
    }

    /// <summary>
    /// Result of role-based validation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    #endregion
}