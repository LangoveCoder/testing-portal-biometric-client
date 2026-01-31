// Views/RegistrationTab.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BACTBiometricClient.Models;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Views
{
    /// <summary>
    /// Registration workflow states for step-by-step guidance
    /// </summary>
    public enum RegistrationWorkflowState
    {
        SearchStudent,
        StudentLoaded,
        ReadyToCapture,
        CapturingFingerprint,
        QualityValidation,
        ReadyToSave,
        Saving,
        Completed,
        Error
    }

    /// <summary>
    /// Fingerprint capture attempt tracking
    /// </summary>
    public class FingerprintCaptureAttempt
    {
        public DateTime Timestamp { get; set; }
        public int QualityScore { get; set; }
        public bool Success { get; set; }
        public string FailureReason { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public partial class RegistrationTab : UserControl
    {
        private readonly string _apiUrl = "http://localhost:8000/api";
        private readonly HttpClient _httpClient;
        private FingerprintService _fingerprintService;
        private RoleBasedUIService _roleBasedUIService;

        private Student _currentStudent;
        private byte[] _capturedFingerprintImage;
        private byte[] _fingerprintTemplate;
        private int _capturedImageWidth = 300;  // ✅ NEW: Store image dimensions
        private int _capturedImageHeight = 400; // ✅ NEW: Store image dimensions

        private bool _isDeviceInitialized = false;
        private bool _hasExistingFingerprint = false;
        private string _authToken = null;
        private bool _isAuthenticated = false;
        private int? _selectedCollegeId = null;

        // ✅ NEW: Registration Workflow State Management
        private RegistrationWorkflowState _workflowState = RegistrationWorkflowState.SearchStudent;
        private int _captureAttempts = 0;
        private const int MAX_CAPTURE_ATTEMPTS = 3;
        private DateTime _lastCaptureAttempt = DateTime.MinValue;
        private List<FingerprintCaptureAttempt> _captureHistory = new List<FingerprintCaptureAttempt>();

        public RegistrationTab()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _fingerprintService = new FingerprintService();

            Loaded += RegistrationTab_Loaded;
            Unloaded += RegistrationTab_Unloaded;
            
            // Initialize database service for enhanced search
            InitializeDatabaseService();
        }

        #region Authentication

        /// <summary>
        /// Set authentication token from MainWindow
        /// </summary>
        public void SetAuthToken(string token, string userName, string userRole)
        {
            _authToken = token;
            _isAuthenticated = !string.IsNullOrEmpty(token);

            // Set auth header for HTTP client
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            if (_isAuthenticated)
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            System.Diagnostics.Debug.WriteLine($"✓ RegistrationTab: Auth token set for {userName} ({userRole})");
            Logger.Info($"RegistrationTab initialized for {userName} ({userRole})");
        }

        /// <summary>
        /// Set role-based UI service for college selection and filtering
        /// </summary>
        public void SetRoleBasedUIService(RoleBasedUIService roleBasedUIService)
        {
            _roleBasedUIService = roleBasedUIService;
            
            if (_roleBasedUIService != null)
            {
                SetupCollegeSelection();
                Logger.Info("RegistrationTab: Role-based UI service configured with college selection");
            }
        }

        /// <summary>
        /// Setup college selection UI based on user role
        /// </summary>
        private void SetupCollegeSelection()
        {
            if (_roleBasedUIService?.GetAssignedColleges() == null)
                return;

            var colleges = _roleBasedUIService.GetAssignedColleges();
            
            if (colleges.Count > 1)
            {
                // Multiple colleges - show selection dropdown
                panelCollegeSelection.Visibility = Visibility.Visible;
                cmbCollege.ItemsSource = colleges;
                txtCollegeInfo.Text = $"Select from {colleges.Count} assigned colleges";
                
                Logger.Info($"College selection enabled for {colleges.Count} colleges");
            }
            else if (colleges.Count == 1)
            {
                // Single college - auto-select and hide dropdown
                panelCollegeSelection.Visibility = Visibility.Collapsed;
                _selectedCollegeId = colleges[0].Id;
                
                Logger.Info($"Auto-selected single college: {colleges[0].Name}");
            }
            else
            {
                // No colleges assigned
                panelCollegeSelection.Visibility = Visibility.Collapsed;
                Logger.Warning("No colleges assigned to current user");
            }
        }

        #endregion

        #region Device Initialization

        private async void RegistrationTab_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize fingerprint scanner
            await InitializeFingerprintDevice();
        }

        private async System.Threading.Tasks.Task InitializeFingerprintDevice()
        {
            try
            {
                txtScannerStatus.Text = "Initializing...";
                txtScannerStatus.Foreground = new SolidColorBrush(Colors.Orange);
                scannerIndicator.Fill = new SolidColorBrush(Colors.Orange);

                // Try to auto-detect scanner
                var result = await _fingerprintService.AutoDetectScannerAsync();

                if (result.Success)
                {
                    _isDeviceInitialized = true;

                    var scannerInfo = _fingerprintService.GetCurrentScannerInfo();
                    txtScannerStatus.Text = scannerInfo != null
                        ? $"Ready ({scannerInfo.Model})"
                        : "Ready";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210));
                    scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(66, 165, 245));

                    // Set default quality threshold
                    _fingerprintService.MinimumQualityScore = (int)sliderQuality.Value;

                    System.Diagnostics.Debug.WriteLine($"✓ Scanner initialized: {result.Message}");
                }
                else
                {
                    // Scanner not found - fall back to simulation mode
                    _isDeviceInitialized = false;
                    txtScannerStatus.Text = "Simulation Mode";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));
                    scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                    System.Diagnostics.Debug.WriteLine($"⚠ Scanner not found: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                _isDeviceInitialized = false;
                txtScannerStatus.Text = "Simulation Mode";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                System.Diagnostics.Debug.WriteLine($"❌ Scanner init exception: {ex.Message}");
            }
        }

        #endregion

        #region College Selection

        private void CmbCollege_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbCollege.SelectedValue != null)
            {
                _selectedCollegeId = (int)cmbCollege.SelectedValue;
                var selectedCollege = cmbCollege.SelectedItem as College;
                
                if (selectedCollege != null)
                {
                    txtCollegeInfo.Text = $"Filtering students from {selectedCollege.Name}";
                    Logger.Info($"College selected: {selectedCollege.Name} (ID: {_selectedCollegeId})");
                }
            }
            else
            {
                _selectedCollegeId = null;
                txtCollegeInfo.Text = "Select a college to filter students";
            }
        }

        #endregion

        #region Registration Workflow Management

        /// <summary>
        /// Update the registration workflow state and UI guidance
        /// </summary>
        private void UpdateWorkflowState(RegistrationWorkflowState newState)
        {
            var previousState = _workflowState;
            _workflowState = newState;

            Logger.Info($"Registration workflow: {previousState} → {newState}");
            
            // Update UI based on workflow state
            UpdateWorkflowUI();
            
            // Update button states
            UpdateButtonStates();
            
            // Show appropriate guidance
            ShowWorkflowGuidance();
        }

        /// <summary>
        /// Update UI elements based on current workflow state
        /// </summary>
        private void UpdateWorkflowUI()
        {
            switch (_workflowState)
            {
                case RegistrationWorkflowState.SearchStudent:
                    panelStudentInfo.Visibility = Visibility.Collapsed;
                    panelFingerprintCapture.Visibility = Visibility.Collapsed;
                    panelFingerprintWarning.Visibility = Visibility.Collapsed;
                    panelFingerprintSuccess.Visibility = Visibility.Collapsed;
                    panelSearchSuccess.Visibility = Visibility.Collapsed;
                    break;

                case RegistrationWorkflowState.StudentLoaded:
                    panelStudentInfo.Visibility = Visibility.Visible;
                    panelSearchSuccess.Visibility = Visibility.Visible;
                    
                    // Check if student already has fingerprint
                    if (_hasExistingFingerprint)
                    {
                        panelFingerprintSuccess.Visibility = Visibility.Visible;
                        panelFingerprintWarning.Visibility = Visibility.Collapsed;
                        panelFingerprintCapture.Visibility = Visibility.Collapsed;
                        UpdateWorkflowState(RegistrationWorkflowState.Completed);
                    }
                    else
                    {
                        panelFingerprintWarning.Visibility = Visibility.Visible;
                        panelFingerprintSuccess.Visibility = Visibility.Collapsed;
                        UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
                    }
                    break;

                case RegistrationWorkflowState.ReadyToCapture:
                    panelFingerprintCapture.Visibility = Visibility.Visible;
                    ResetCaptureUI();
                    break;

                case RegistrationWorkflowState.CapturingFingerprint:
                    // UI updates handled in capture methods
                    break;

                case RegistrationWorkflowState.QualityValidation:
                    // Show quality feedback and options
                    break;

                case RegistrationWorkflowState.ReadyToSave:
                    panelFingerprintWarning.Visibility = Visibility.Collapsed;
                    break;

                case RegistrationWorkflowState.Saving:
                    // UI updates handled in save method
                    break;

                case RegistrationWorkflowState.Completed:
                    ShowCompletionFeedback();
                    break;

                case RegistrationWorkflowState.Error:
                    // Error handling
                    break;
            }
        }

        /// <summary>
        /// Update button enabled states based on workflow
        /// </summary>
        private void UpdateButtonStates()
        {
            switch (_workflowState)
            {
                case RegistrationWorkflowState.SearchStudent:
                    btnCapture.IsEnabled = false;
                    btnSave.IsEnabled = false;
                    break;

                case RegistrationWorkflowState.StudentLoaded:
                case RegistrationWorkflowState.ReadyToCapture:
                    btnCapture.IsEnabled = true;
                    btnSave.IsEnabled = false;
                    break;

                case RegistrationWorkflowState.CapturingFingerprint:
                case RegistrationWorkflowState.Saving:
                    btnCapture.IsEnabled = false;
                    btnSave.IsEnabled = false;
                    break;

                case RegistrationWorkflowState.QualityValidation:
                    btnCapture.IsEnabled = true; // Allow recapture
                    btnSave.IsEnabled = false;
                    break;

                case RegistrationWorkflowState.ReadyToSave:
                    btnCapture.IsEnabled = true; // Allow recapture
                    btnSave.IsEnabled = true;
                    break;

                case RegistrationWorkflowState.Completed:
                    btnCapture.IsEnabled = false;
                    btnSave.IsEnabled = false;
                    break;
            }
        }

        /// <summary>
        /// Show contextual guidance based on workflow state
        /// </summary>
        private void ShowWorkflowGuidance()
        {
            string guidance = GetWorkflowGuidanceText();
            
            if (!string.IsNullOrEmpty(guidance))
            {
                // Update scanner status with guidance
                txtScannerStatus.Text = guidance;
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210));
            }
        }

        /// <summary>
        /// Get guidance text for current workflow state
        /// </summary>
        private string GetWorkflowGuidanceText()
        {
            switch (_workflowState)
            {
                case RegistrationWorkflowState.SearchStudent:
                    return "Search for a student to begin registration";

                case RegistrationWorkflowState.StudentLoaded:
                    return "Student loaded - checking fingerprint status";

                case RegistrationWorkflowState.ReadyToCapture:
                    return _captureAttempts == 0 
                        ? "Ready to capture fingerprint" 
                        : $"Ready for attempt {_captureAttempts + 1} of {MAX_CAPTURE_ATTEMPTS}";

                case RegistrationWorkflowState.CapturingFingerprint:
                    return "Place finger firmly on scanner...";

                case RegistrationWorkflowState.QualityValidation:
                    return "Validating fingerprint quality...";

                case RegistrationWorkflowState.ReadyToSave:
                    return "Fingerprint captured successfully - ready to save";

                case RegistrationWorkflowState.Saving:
                    return "Saving registration data...";

                case RegistrationWorkflowState.Completed:
                    return "Registration completed successfully";

                case RegistrationWorkflowState.Error:
                    return "Error occurred - please try again";

                default:
                    return "";
            }
        }

        /// <summary>
        /// Reset capture UI elements
        /// </summary>
        private void ResetCaptureUI()
        {
            imgFingerprint.Source = null;
            txtNoFingerprint.Visibility = Visibility.Visible;
            txtQualityScore.Visibility = Visibility.Collapsed;
            
            // Reset scanner status
            if (_isDeviceInitialized)
            {
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(66, 165, 245));
            }
        }

        /// <summary>
        /// Show completion feedback with success message
        /// </summary>
        private void ShowCompletionFeedback()
        {
            panelFingerprintWarning.Visibility = Visibility.Collapsed;
            panelFingerprintSuccess.Visibility = Visibility.Visible;
            
            if (_currentStudent?.FingerprintRegisteredAt != null)
            {
                txtFingerprintDate.Text = $"Registered on: {_currentStudent.FingerprintRegisteredAt:MMM dd, yyyy 'at' HH:mm}";
            }
        }

        /// <summary>
        /// Reset workflow to initial state
        /// </summary>
        private void ResetWorkflow()
        {
            _captureAttempts = 0;
            _captureHistory.Clear();
            _capturedFingerprintImage = null;
            _fingerprintTemplate = null;
            _hasExistingFingerprint = false;
            
            UpdateWorkflowState(RegistrationWorkflowState.SearchStudent);
        }

        #endregion

        #region Enhanced Search and Management

        private List<Student> _searchResults = new List<Student>();
        private DatabaseService _databaseService;
        private System.Diagnostics.Stopwatch _searchStopwatch;

        /// <summary>
        /// Initialize database service for local search
        /// </summary>
        private async void InitializeDatabaseService()
        {
            try
            {
                _databaseService = new DatabaseService();
                await _databaseService.InitializeDatabaseAsync();
                Logger.Info("DatabaseService initialized for enhanced search");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize DatabaseService for search", ex);
                _databaseService = null;
            }
        }

        /// <summary>
        /// Handle text changes for real-time search
        /// </summary>
        private async void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchTerm = txtSearch.Text.Trim();
            
            // Only perform search if we have at least 2 characters or it's a roll number pattern
            if (searchTerm.Length >= 2 || (searchTerm.Length > 0 && searchTerm.All(char.IsDigit)))
            {
                await PerformEnhancedSearchAsync(searchTerm);
            }
            else if (searchTerm.Length == 0)
            {
                ClearSearchResults();
            }
        }

        /// <summary>
        /// Clear search button click handler
        /// </summary>
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Text = "";
            ClearSearchResults();
            ClearStudentInfo();
        }

        /// <summary>
        /// Perform enhanced search with local database and API fallback
        /// </summary>
        private async Task PerformEnhancedSearchAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                ClearSearchResults();
                return;
            }

            try
            {
                _searchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // First try local database search for better performance
                var localResults = await SearchLocalDatabaseAsync(searchTerm);
                
                if (localResults.Any())
                {
                    DisplaySearchResults(localResults, "Local Cache");
                }
                else
                {
                    // Fallback to API search if no local results
                    await SearchApiAsync(searchTerm);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Enhanced search failed for term: {searchTerm}", ex);
                // Fallback to original search method
                await SearchApiAsync(searchTerm);
            }
        }

        /// <summary>
        /// Search local database using full-text search
        /// </summary>
        private async Task<List<Student>> SearchLocalDatabaseAsync(string searchTerm)
        {
            if (_databaseService == null)
                return new List<Student>();

            try
            {
                // Get college IDs for role-based filtering
                List<int> allowedCollegeIds = null;
                if (_roleBasedUIService != null)
                {
                    var colleges = _roleBasedUIService.GetAssignedColleges();
                    allowedCollegeIds = colleges?.Select(c => c.Id).ToList();
                }

                // Use enhanced search with college filtering
                var results = _databaseService.SearchStudents(searchTerm, allowedCollegeIds);
                
                Logger.Info($"Local search found {results.Count} results for '{searchTerm}'");
                return results;
            }
            catch (Exception ex)
            {
                Logger.Error($"Local database search failed: {ex.Message}", ex);
                return new List<Student>();
            }
        }

        /// <summary>
        /// Search API as fallback
        /// </summary>
        private async Task SearchApiAsync(string searchTerm)
        {
            try
            {
                // Use existing API search logic but enhanced
                var searchData = new { 
                    search_term = searchTerm,
                    college_id = _selectedCollegeId,
                    limit = 20 // Get multiple results
                };
                var json = JsonSerializer.Serialize(searchData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_apiUrl}/biometric-operator/registration/search-student",
                    content
                );

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                    
                    var students = new List<Student>();
                    
                    // Handle both single student and array responses
                    if (result.TryGetProperty("data", out var dataProperty))
                    {
                        if (dataProperty.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var studentElement in dataProperty.EnumerateArray())
                            {
                                students.Add(ParseStudentFromJson(studentElement));
                            }
                        }
                        else
                        {
                            students.Add(ParseStudentFromJson(dataProperty));
                        }
                    }
                    else if (result.TryGetProperty("roll_number", out _))
                    {
                        students.Add(ParseStudentFromJson(result));
                    }

                    DisplaySearchResults(students, "Server");
                }
                else
                {
                    DisplaySearchResults(new List<Student>(), "Server");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"API search failed: {ex.Message}", ex);
                DisplaySearchResults(new List<Student>(), "Server (Error)");
            }
        }

        /// <summary>
        /// Display search results in enhanced UI
        /// </summary>
        private void DisplaySearchResults(List<Student> results, string source)
        {
            _searchStopwatch?.Stop();
            _searchResults = results;

            if (results.Any())
            {
                // Show results summary
                panelSearchSummary.Visibility = Visibility.Visible;
                txtSearchSummary.Text = $"Found {results.Count} student{(results.Count == 1 ? "" : "s")} from {source}";
                txtSearchTime.Text = _searchStopwatch != null ? $"({_searchStopwatch.ElapsedMilliseconds}ms)" : "";

                // Populate results list with enhanced data
                var enhancedResults = results.Select(s => new EnhancedStudentResult
                {
                    Student = s,
                    HasFingerprint = s.FingerprintTemplate != null || s.FingerprintRegisteredAt.HasValue,
                    FingerprintStatus = GetFingerprintStatusText(s),
                    StatusColor = GetFingerprintStatusColor(s)
                }).ToList();

                lstSearchResults.ItemsSource = enhancedResults;
                panelSearchResults.Visibility = Visibility.Visible;
            }
            else
            {
                ClearSearchResults();
            }
        }

        /// <summary>
        /// Get fingerprint status text for display
        /// </summary>
        private string GetFingerprintStatusText(Student student)
        {
            if (student.FingerprintTemplate != null || student.FingerprintRegisteredAt.HasValue)
            {
                return "Registered";
            }
            return "Not Registered";
        }

        /// <summary>
        /// Get fingerprint status color for display
        /// </summary>
        private SolidColorBrush GetFingerprintStatusColor(Student student)
        {
            if (student.FingerprintTemplate != null || student.FingerprintRegisteredAt.HasValue)
            {
                return new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red
        }

        /// <summary>
        /// Handle search result selection
        /// </summary>
        private void LstSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSearchResults.SelectedItem is EnhancedStudentResult selectedResult)
            {
                var student = selectedResult.Student;
                
                // Role-based validation for the selected student
                if (_roleBasedUIService != null && student.CollegeId.HasValue)
                {
                    if (!_roleBasedUIService.HasAccessToCollege(student.CollegeId.Value))
                    {
                        MessageBox.Show(
                            $"Access denied to student from {student.CollegeName}.\n\n" +
                            "You can only access students from your assigned colleges.",
                            "Access Denied",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                // Load the selected student
                LoadStudent(student, selectedResult.HasFingerprint, student.FingerprintRegisteredAt);
                panelSearchSuccess.Visibility = Visibility.Visible;
                
                // Update search box with selected student's roll number
                txtSearch.Text = student.RollNumber;
                
                // Hide search results after selection
                panelSearchResults.Visibility = Visibility.Collapsed;
                
                Logger.Info($"Student selected from search results: {student.RollNumber} - {student.Name}");
            }
        }

        /// <summary>
        /// Clear search results display
        /// </summary>
        private void ClearSearchResults()
        {
            panelSearchSummary.Visibility = Visibility.Collapsed;
            panelSearchResults.Visibility = Visibility.Collapsed;
            lstSearchResults.ItemsSource = null;
            _searchResults.Clear();
        }

        /// <summary>
        /// Enhanced student result for display
        /// </summary>
        public class EnhancedStudentResult
        {
            public Student Student { get; set; }
            public bool HasFingerprint { get; set; }
            public string FingerprintStatus { get; set; }
            public SolidColorBrush StatusColor { get; set; }
        }

        /// <summary>
        /// Handle keyboard input for search
        /// </summary>
        private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                BtnSearch_Click(sender, e);
            }
        }

        /// <summary>
        /// Enhanced search button click - delegates to enhanced search or fallback to API
        /// </summary>
        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = txtSearch.Text.Trim();

            if (string.IsNullOrEmpty(searchTerm))
            {
                MessageBox.Show("Please enter a search term", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // For exact roll number searches, use the enhanced search
            if (searchTerm.Length == 5 && searchTerm.All(char.IsDigit))
            {
                await PerformEnhancedSearchAsync(searchTerm);
            }
            else
            {
                // For other searches, use enhanced search as well
                await PerformEnhancedSearchAsync(searchTerm);
            }
        }

        /// <summary>
        /// Parse student from JSON (moved from old search section)
        /// </summary>
        private Student ParseStudentFromJson(JsonElement jsonData)
        {
            var student = new Student
            {
                Id = GetIntProperty(jsonData, "id"),
                RollNumber = GetStringProperty(jsonData, "roll_number"),
                Name = GetStringProperty(jsonData, "name"),
                FatherName = GetStringProperty(jsonData, "father_name")
                             ?? GetStringProperty(jsonData, "father"),
                CNIC = GetStringProperty(jsonData, "cnic"),
                Gender = GetStringProperty(jsonData, "gender"),
                TestName = GetStringProperty(jsonData, "test_name")
                           ?? GetStringProperty(jsonData, "test"),
                CollegeName = GetStringProperty(jsonData, "college_name")
                              ?? GetStringProperty(jsonData, "college"),
                CollegeId = GetIntProperty(jsonData, "college_id"),
                Venue = GetStringProperty(jsonData, "venue"),
                Hall = GetStringProperty(jsonData, "hall"),
                Zone = GetStringProperty(jsonData, "zone"),
                Row = GetStringProperty(jsonData, "row"),
                Seat = GetStringProperty(jsonData, "seat"),
            };

            // Parse picture (base64 to byte[])
            var pictureStr = GetStringProperty(jsonData, "picture")
                             ?? GetStringProperty(jsonData, "photo")
                             ?? GetStringProperty(jsonData, "image");

            if (!string.IsNullOrEmpty(pictureStr))
            {
                try
                {
                    student.Picture = Convert.FromBase64String(pictureStr);
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse picture for {student.RollNumber}");
                }
            }

            return student;
        }

        /// <summary>
        /// Get string property from JSON
        /// </summary>
        private string GetStringProperty(JsonElement json, string propertyName)
        {
            if (json.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    switch (prop.ValueKind)
                    {
                        case JsonValueKind.String:
                            string value = prop.GetString();
                            return string.IsNullOrWhiteSpace(value) ? null : value;

                        case JsonValueKind.Number:
                            if (propertyName == "roll_number")
                            {
                                int num = prop.GetInt32();
                                return num.ToString("D5");
                            }
                            return prop.GetInt32().ToString();

                        case JsonValueKind.True:
                            return "true";

                        case JsonValueKind.False:
                            return "false";

                        default:
                            return prop.ToString();
                    }
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Get boolean property from JSON
        /// </summary>
        private bool GetBoolProperty(JsonElement json, string propertyName)
        {
            if (json.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    if (prop.ValueKind == JsonValueKind.True)
                        return true;
                    if (prop.ValueKind == JsonValueKind.False)
                        return false;
                    if (prop.ValueKind == JsonValueKind.Number)
                        return prop.GetInt32() != 0;
                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        string str = prop.GetString()?.ToLower();
                        return str == "true" || str == "1";
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Get integer property from JSON
        /// </summary>
        private int GetIntProperty(JsonElement json, string propertyName)
        {
            if (json.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                    {
                        return prop.GetInt32();
                    }
                    else if (prop.ValueKind == JsonValueKind.String)
                    {
                        string strValue = prop.GetString();
                        if (int.TryParse(strValue, out int intValue))
                        {
                            return intValue;
                        }
                    }
                }
                catch { }
            }
            return 0;
        }

        /// <summary>
        /// Load student information into UI with workflow integration
        /// </summary>
        private void LoadStudent(Student student, bool hasFingerprint, DateTime? registeredDate)
        {
            _currentStudent = student;
            _hasExistingFingerprint = hasFingerprint;

            // Update UI with student information
            panelStudentInfo.Visibility = Visibility.Visible;

            txtRollNumber.Text = student.RollNumber ?? "-";
            txtName.Text = student.Name ?? "-";
            txtFatherName.Text = student.FatherName ?? "-";
            txtCNIC.Text = student.CNIC ?? "-";
            txtGender.Text = student.Gender ?? "-";
            txtTest.Text = student.TestName ?? "N/A";
            txtVenue.Text = student.Venue ?? "N/A";

            if (!string.IsNullOrEmpty(student.Hall))
            {
                txtSeating.Text = $"{student.Hall}, {student.Zone}, {student.Row}, {student.Seat}";
            }
            else
            {
                txtSeating.Text = "N/A";
            }

            // Load student photo
            if (student.Picture != null && student.Picture.Length > 0)
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    using (MemoryStream ms = new MemoryStream(student.Picture))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    imgStudent.Source = bitmap;
                    panelNoPhoto.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    imgStudent.Source = null;
                    panelNoPhoto.Visibility = Visibility.Visible;
                }
            }
            else
            {
                imgStudent.Source = null;
                panelNoPhoto.Visibility = Visibility.Visible;
            }

            // Update fingerprint registration date if available
            if (hasFingerprint && registeredDate.HasValue)
            {
                _currentStudent.FingerprintRegisteredAt = registeredDate;
            }

            // Update workflow state based on fingerprint status
            UpdateWorkflowState(RegistrationWorkflowState.StudentLoaded);

            Logger.Info($"Student loaded: {student.RollNumber} - {student.Name}, HasFingerprint: {hasFingerprint}");
        }

        /// <summary>
        /// Clear student information from UI with workflow reset
        /// </summary>
        private void ClearStudentInfo()
        {
            _currentStudent = null;
            _capturedFingerprintImage = null;
            _fingerprintTemplate = null;
            _hasExistingFingerprint = false;

            panelSearchSuccess.Visibility = Visibility.Collapsed;
            panelStudentInfo.Visibility = Visibility.Collapsed;
            panelFingerprintWarning.Visibility = Visibility.Collapsed;
            panelFingerprintSuccess.Visibility = Visibility.Collapsed;
            panelFingerprintCapture.Visibility = Visibility.Collapsed;

            imgFingerprint.Source = null;
            txtNoFingerprint.Visibility = Visibility.Visible;
            txtQualityScore.Visibility = Visibility.Collapsed;

            // Reset workflow
            ResetWorkflow();

            Logger.Info("Student information cleared and workflow reset");
        }

        #endregion

        #region Fingerprint Capture

        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStudent == null)
            {
                MessageBox.Show("Please search and load a student first", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Role-based validation for fingerprint registration
            if (_roleBasedUIService != null)
            {
                var validation = _roleBasedUIService.ValidateAction(UserAction.RegisterFingerprint);
                if (!validation.IsValid)
                {
                    MessageBox.Show(validation.ErrorMessage, "Access Denied",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Check capture attempt limits
            if (_captureAttempts >= MAX_CAPTURE_ATTEMPTS)
            {
                var result = MessageBox.Show(
                    $"You have reached the maximum number of capture attempts ({MAX_CAPTURE_ATTEMPTS}).\n\n" +
                    "Would you like to reset and try again with different settings?",
                    "Maximum Attempts Reached",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ResetCaptureAttempts();
                }
                return;
            }

            UpdateWorkflowState(RegistrationWorkflowState.CapturingFingerprint);

            if (_isDeviceInitialized)
            {
                CaptureRealFingerprint();
            }
            else
            {
                CaptureSimulatedFingerprint();
            }
        }

        /// <summary>
        /// Reset capture attempts and allow user to try again
        /// </summary>
        private void ResetCaptureAttempts()
        {
            _captureAttempts = 0;
            _captureHistory.Clear();
            ResetCaptureUI();
            UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
            
            Logger.Info("Capture attempts reset - user can try again");
        }

        private async void CaptureRealFingerprint()
        {
            var startTime = DateTime.Now;
            _captureAttempts++;

            try
            {
                btnCapture.IsEnabled = false;
                txtScannerStatus.Text = "Place finger firmly on scanner...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                _fingerprintService.MinimumQualityScore = (int)sliderQuality.Value;

                var captureResult = await _fingerprintService.CaptureAsync();
                var duration = DateTime.Now - startTime;

                // Record capture attempt
                var attempt = new FingerprintCaptureAttempt
                {
                    Timestamp = startTime,
                    QualityScore = captureResult.QualityScore,
                    Success = captureResult.Success,
                    FailureReason = captureResult.Success ? null : captureResult.FailureReason.ToString(),
                    Duration = duration
                };
                _captureHistory.Add(attempt);

                if (!captureResult.Success)
                {
                    await HandleCaptureFailure(captureResult);
                    return;
                }

                // Successful capture - validate quality
                await HandleSuccessfulCapture(captureResult);
            }
            catch (Exception ex)
            {
                await HandleCaptureException(ex);
            }
            finally
            {
                btnCapture.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handle capture failure with detailed guidance
        /// </summary>
        private async Task HandleCaptureFailure(FingerprintCaptureResult captureResult)
        {
            UpdateWorkflowState(RegistrationWorkflowState.QualityValidation);

            txtScannerStatus.Text = "Capture Failed";
            txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));

            string errorMsg = GetDetailedFailureMessage(captureResult);
            string guidance = GetCaptureGuidance(captureResult.FailureReason);

            var result = MessageBox.Show(
                $"{errorMsg}\n\n{guidance}\n\nAttempt {_captureAttempts} of {MAX_CAPTURE_ATTEMPTS}\n\nWould you like to try again?",
                "Capture Failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes && _captureAttempts < MAX_CAPTURE_ATTEMPTS)
            {
                UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
            }
            else if (_captureAttempts >= MAX_CAPTURE_ATTEMPTS)
            {
                ShowMaxAttemptsReached();
            }
        }

        /// <summary>
        /// Handle successful capture with quality validation
        /// </summary>
        private async Task HandleSuccessfulCapture(FingerprintCaptureResult captureResult)
        {
            UpdateWorkflowState(RegistrationWorkflowState.QualityValidation);

            _fingerprintTemplate = captureResult.Template;
            _capturedFingerprintImage = captureResult.ImageData;
            _capturedImageWidth = captureResult.ImageWidth;
            _capturedImageHeight = captureResult.ImageHeight;

            if (captureResult.ImageData != null && captureResult.ImageData.Length > 0)
            {
                DisplayFingerprintImage(
                    captureResult.ImageData,
                    captureResult.ImageWidth,
                    captureResult.ImageHeight
                );
            }

            // Validate quality and provide feedback
            var qualityAssessment = AssessQualityLevel(captureResult.QualityScore);
            
            txtScannerStatus.Text = "Capture Successful";
            txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(102, 187, 106));

            txtQualityScore.Text = $"✓ Quality: {captureResult.QualityScore}% ({qualityAssessment.Level})";
            txtQualityScore.Foreground = qualityAssessment.Color;
            txtQualityScore.Visibility = Visibility.Visible;

            // Show quality feedback and options
            await ShowQualityFeedback(captureResult.QualityScore, qualityAssessment);

            Logger.Info($"Fingerprint captured successfully: Quality={captureResult.QualityScore}%, Attempt={_captureAttempts}");
        }

        /// <summary>
        /// Handle capture exceptions
        /// </summary>
        private async Task HandleCaptureException(Exception ex)
        {
            UpdateWorkflowState(RegistrationWorkflowState.Error);

            txtScannerStatus.Text = "Capture Error";
            txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));

            MessageBox.Show(
                $"An unexpected error occurred during capture:\n\n{ex.Message}\n\nPlease try again or contact support if the problem persists.",
                "Capture Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Logger.Error($"Fingerprint capture exception on attempt {_captureAttempts}", ex);

            // Allow retry
            UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
        }

        /// <summary>
        /// Get detailed failure message based on failure reason
        /// </summary>
        private string GetDetailedFailureMessage(FingerprintCaptureResult result)
        {
            switch (result.FailureReason)
            {
                case CaptureFailureReason.Timeout:
                    return "Timeout - No finger detected on the scanner.";

                case CaptureFailureReason.PoorQuality:
                    return $"Poor quality fingerprint captured (Score: {result.QualityScore}%).";

                case CaptureFailureReason.DeviceError:
                    return "Scanner device error occurred.";

                case CaptureFailureReason.UserCancelled:
                    return "Capture was cancelled by user.";

                default:
                    return $"Capture failed: {result.Message}";
            }
        }

        /// <summary>
        /// Get specific guidance based on failure reason
        /// </summary>
        private string GetCaptureGuidance(CaptureFailureReason? failureReason)
        {
            if (!failureReason.HasValue)
                return "GUIDANCE:\n• Ensure scanner is properly connected\n• Try again with a clean, dry finger\n• Contact support if problems persist";

            switch (failureReason.Value)
            {
                case CaptureFailureReason.Timeout:
                    return "GUIDANCE:\n• Place your finger firmly on the scanner\n• Ensure finger covers the entire sensor area\n• Hold still until capture completes";

                case CaptureFailureReason.PoorQuality:
                    return "GUIDANCE:\n• Clean your finger with a dry cloth\n• Press firmly but don't slide your finger\n• Try a different finger if this one is damaged\n• Consider lowering the quality threshold";

                case CaptureFailureReason.DeviceError:
                    return "GUIDANCE:\n• Check scanner connection\n• Try unplugging and reconnecting the device\n• Restart the application if needed";

                default:
                    return "GUIDANCE:\n• Ensure scanner is properly connected\n• Try again with a clean, dry finger\n• Contact support if problems persist";
            }
        }

        /// <summary>
        /// Show maximum attempts reached dialog
        /// </summary>
        private void ShowMaxAttemptsReached()
        {
            var result = MessageBox.Show(
                $"Maximum capture attempts ({MAX_CAPTURE_ATTEMPTS}) reached.\n\n" +
                "Suggestions:\n" +
                "• Lower the quality threshold and try again\n" +
                "• Try a different finger\n" +
                "• Clean the scanner and finger thoroughly\n" +
                "• Check scanner connection\n\n" +
                "Would you like to reset and try again?",
                "Maximum Attempts Reached",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ResetCaptureAttempts();
            }
            else
            {
                UpdateWorkflowState(RegistrationWorkflowState.Error);
            }
        }

        /// <summary>
        /// Quality assessment result
        /// </summary>
        public class QualityAssessment
        {
            public string Level { get; set; }
            public SolidColorBrush Color { get; set; }
            public string Description { get; set; }
            public bool IsAcceptable { get; set; }
        }

        /// <summary>
        /// Assess fingerprint quality level
        /// </summary>
        private QualityAssessment AssessQualityLevel(int qualityScore)
        {
            if (qualityScore >= 75)
            {
                return new QualityAssessment
                {
                    Level = "Excellent",
                    Color = new SolidColorBrush(Color.FromRgb(46, 125, 50)),
                    Description = "High quality fingerprint with clear ridge patterns",
                    IsAcceptable = true
                };
            }
            else if (qualityScore >= 65)
            {
                return new QualityAssessment
                {
                    Level = "Good",
                    Color = new SolidColorBrush(Color.FromRgb(102, 187, 106)),
                    Description = "Good quality fingerprint suitable for registration",
                    IsAcceptable = true
                };
            }
            else if (qualityScore >= 55)
            {
                return new QualityAssessment
                {
                    Level = "Fair",
                    Color = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                    Description = "Acceptable quality but could be improved",
                    IsAcceptable = true
                };
            }
            else if (qualityScore >= 45)
            {
                return new QualityAssessment
                {
                    Level = "Poor",
                    Color = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    Description = "Low quality - consider recapturing for better results",
                    IsAcceptable = false
                };
            }
            else
            {
                return new QualityAssessment
                {
                    Level = "Very Poor",
                    Color = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    Description = "Very low quality - recapture recommended",
                    IsAcceptable = false
                };
            }
        }

        /// <summary>
        /// Show quality feedback dialog with options
        /// </summary>
        private async Task ShowQualityFeedback(int qualityScore, QualityAssessment assessment)
        {
            string message = $"Fingerprint Quality Assessment:\n\n" +
                           $"Score: {qualityScore}%\n" +
                           $"Level: {assessment.Level}\n" +
                           $"Assessment: {assessment.Description}\n\n";

            if (assessment.IsAcceptable)
            {
                message += "This fingerprint quality is acceptable for registration.\n\n" +
                          "Would you like to save this registration or try to capture a better quality fingerprint?";

                var result = MessageBox.Show(
                    message,
                    "Quality Assessment - Acceptable",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information,
                    MessageBoxResult.Yes);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        UpdateWorkflowState(RegistrationWorkflowState.ReadyToSave);
                        btnSave.IsEnabled = true;
                        break;

                    case MessageBoxResult.No:
                        if (_captureAttempts < MAX_CAPTURE_ATTEMPTS)
                        {
                            UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
                        }
                        else
                        {
                            ShowMaxAttemptsReached();
                        }
                        break;

                    case MessageBoxResult.Cancel:
                        UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
                        break;
                }
            }
            else
            {
                message += "This fingerprint quality is below recommended standards.\n\n" +
                          "Recommendations:\n" +
                          "• Clean your finger and the scanner\n" +
                          "• Press firmly without sliding\n" +
                          "• Try a different finger if needed\n" +
                          "• Lower quality threshold if necessary\n\n" +
                          "Would you like to try capturing again?";

                var result = MessageBox.Show(
                    message,
                    "Quality Assessment - Poor Quality",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes && _captureAttempts < MAX_CAPTURE_ATTEMPTS)
                {
                    UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
                }
                else if (_captureAttempts >= MAX_CAPTURE_ATTEMPTS)
                {
                    ShowMaxAttemptsReached();
                }
                else
                {
                    // User chose not to retry - allow them to save anyway
                    var forceResult = MessageBox.Show(
                        "Would you like to save this fingerprint anyway?\n\n" +
                        "Note: Poor quality fingerprints may cause verification issues.",
                        "Save Poor Quality Fingerprint?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (forceResult == MessageBoxResult.Yes)
                    {
                        UpdateWorkflowState(RegistrationWorkflowState.ReadyToSave);
                        btnSave.IsEnabled = true;
                    }
                    else
                    {
                        UpdateWorkflowState(RegistrationWorkflowState.ReadyToCapture);
                    }
                }
            }
        }

        private void CaptureSimulatedFingerprint()
        {
            try
            {
                btnCapture.IsEnabled = false;
                txtScannerStatus.Text = "Simulating...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));

                System.Threading.Thread.Sleep(1500);

                int width = 300;
                int height = 400;
                _capturedFingerprintImage = new byte[width * height];
                _capturedImageWidth = width;   // ✅ NEW: Store dimensions
                _capturedImageHeight = height; // ✅ NEW: Store dimensions

                Random rand = new Random();
                for (int i = 0; i < _capturedFingerprintImage.Length; i++)
                {
                    _capturedFingerprintImage[i] = (byte)rand.Next(50, 200);
                }

                DisplayFingerprintImage(_capturedFingerprintImage, width, height);

                _fingerprintTemplate = new byte[400];
                rand.NextBytes(_fingerprintTemplate);

                int simulatedQuality = (int)sliderQuality.Value;

                txtScannerStatus.Text = "Simulation Mode";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));

                txtQualityScore.Text = $"⚠ Quality: {simulatedQuality}% (SIMULATED)";
                txtQualityScore.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));
                txtQualityScore.Visibility = Visibility.Visible;

                btnSave.IsEnabled = true;

                MessageBox.Show(
                    "⚠ SIMULATED FINGERPRINT\n\n" +
                    "This is NOT real biometric data!\n" +
                    "Connect a SecuGen scanner for actual captures.\n\n" +
                    $"Simulated Quality: {simulatedQuality}%",
                    "Simulation Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Simulation error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnCapture.IsEnabled = true;
            }
        }

        private void DisplayFingerprintImage(byte[] imageData, int width, int height)
        {
            try
            {
                WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Gray8, null);

                bitmap.WritePixels(new Int32Rect(0, 0, width, height), imageData, width, 0);

                imgFingerprint.Source = bitmap;
                txtNoFingerprint.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying fingerprint: {ex.Message}", "Display Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Save to Database

        // ✅ NEW METHOD: Convert raw grayscale bytes to PNG format
        /// <summary>
        /// Convert raw fingerprint bytes to PNG format
        /// </summary>
        private byte[] ConvertToPng(byte[] rawImageData, int width, int height)
        {
            try
            {
                // ✅ Step 1: Invert colors (white background, dark ridges)
                byte[] invertedData = new byte[rawImageData.Length];
                for (int i = 0; i < invertedData.Length; i++)
                {
                    invertedData[i] = (byte)(255 - rawImageData[i]);
                }

                // ✅ Step 2: Find min and max for histogram stretching
                byte minVal = 255;
                byte maxVal = 0;

                for (int i = 0; i < invertedData.Length; i++)
                {
                    if (invertedData[i] < minVal) minVal = invertedData[i];
                    if (invertedData[i] > maxVal) maxVal = invertedData[i];
                }

                // ✅ Step 3: Apply STRONG contrast enhancement
                byte[] enhancedData = new byte[invertedData.Length];
                double range = maxVal - minVal;

                if (range > 0)
                {
                    for (int i = 0; i < invertedData.Length; i++)
                    {
                        // Normalize to 0-1 range
                        double normalized = (invertedData[i] - minVal) / range;

                        // Apply STRONG gamma correction (0.5 = very high contrast)
                        double enhanced = Math.Pow(normalized, 0.5);

                        // Additional brightness adjustment - make dark pixels even darker
                        if (enhanced < 0.5)
                        {
                            enhanced = enhanced * 0.8; // Make dark areas darker
                        }
                        else
                        {
                            enhanced = 0.5 + (enhanced - 0.5) * 1.2; // Keep bright areas bright
                        }

                        // Clamp to 0-1 range
                        enhanced = Math.Max(0, Math.Min(1, enhanced));

                        enhancedData[i] = (byte)(enhanced * 255);
                    }
                }
                else
                {
                    Array.Copy(invertedData, enhancedData, invertedData.Length);
                }

                // ✅ Step 4: Create bitmap
                WriteableBitmap bitmap = new WriteableBitmap(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Gray8,
                    null
                );

                bitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    enhancedData,
                    width,
                    0
                );

                // ✅ Step 5: Encode to PNG
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (MemoryStream ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    byte[] pngData = ms.ToArray();

                    System.Diagnostics.Debug.WriteLine($"✓ PNG Conversion (Strong Contrast):");
                    System.Diagnostics.Debug.WriteLine($"  Original range: {minVal}-{maxVal}");
                    System.Diagnostics.Debug.WriteLine($"  Output: {pngData.Length} bytes");

                    return pngData;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PNG conversion failed: {ex.Message}");
                return rawImageData;
            }
        }
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStudent == null || _fingerprintTemplate == null)
            {
                MessageBox.Show("Please capture a fingerprint first", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_isAuthenticated)
            {
                MessageBox.Show(
                    "Not authenticated with server.\n\nPlease restart the application.",
                    "Authentication Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            // Show detailed confirmation with capture history
            if (!await ShowSaveConfirmation())
            {
                return;
            }

            // Update workflow state to saving
            UpdateWorkflowState(RegistrationWorkflowState.Saving);

            try
            {
                btnSave.IsEnabled = false;
                btnCapture.IsEnabled = false;

                // Update UI to show saving progress
                txtScannerStatus.Text = "Saving registration...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(66, 165, 245));

                await SaveFingerprintRegistration();
            }
            catch (Exception ex)
            {
                await HandleSaveException(ex);
            }
            finally
            {
                btnSave.IsEnabled = true;
                btnCapture.IsEnabled = true;
            }
        }

        /// <summary>
        /// Show detailed save confirmation dialog
        /// </summary>
        private async Task<bool> ShowSaveConfirmation()
        {
            string mode = _isDeviceInitialized ? "REAL" : "SIMULATED";
            int quality = _fingerprintService.MinimumQualityScore;

            // Build capture history summary
            string captureHistory = "";
            if (_captureHistory.Count > 0)
            {
                captureHistory = "\n\nCapture History:\n";
                for (int i = 0; i < _captureHistory.Count; i++)
                {
                    var attempt = _captureHistory[i];
                    string status = attempt.Success ? "✓" : "✗";
                    captureHistory += $"  {status} Attempt {i + 1}: {attempt.QualityScore}% ({attempt.Duration.TotalSeconds:F1}s)\n";
                }
            }

            var confirmResult = MessageBox.Show(
                $"Save {mode} fingerprint registration?\n\n" +
                $"Student: {_currentStudent.Name}\n" +
                $"Roll Number: {_currentStudent.RollNumber}\n" +
                $"College: {_currentStudent.CollegeName}\n" +
                $"Final Quality: {quality}%\n" +
                $"Mode: {mode}\n" +
                $"Attempts: {_captureAttempts}" +
                captureHistory +
                "\n\nThis will permanently register the fingerprint.",
                "Confirm Registration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            return confirmResult == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Save fingerprint registration to server
        /// </summary>
        private async Task SaveFingerprintRegistration()
        {
            string mode = _isDeviceInitialized ? "REAL" : "SIMULATED";
            int quality = _fingerprintService.MinimumQualityScore;

            // Convert raw bytes to PNG format before sending
            byte[] pngImageData = null;
            if (_capturedFingerprintImage != null && _capturedFingerprintImage.Length > 0)
            {
                pngImageData = ConvertToPng(_capturedFingerprintImage, _capturedImageWidth, _capturedImageHeight);
                Logger.Info($"Image converted: {_capturedFingerprintImage.Length} bytes → {pngImageData.Length} bytes PNG");
            }

            var data = new
            {
                student_id = _currentStudent.Id,
                fingerprint_template = Convert.ToBase64String(_fingerprintTemplate),
                fingerprint_image = pngImageData != null
                    ? Convert.ToBase64String(pngImageData)
                    : null,
                quality = quality,
                capture_attempts = _captureAttempts,
                mode = mode
            };

            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Logger.Info($"Saving fingerprint registration: Student={_currentStudent.RollNumber}, Quality={quality}%, Attempts={_captureAttempts}");

            var response = await _httpClient.PostAsync(
                $"{_apiUrl}/biometric-operator/registration/save-fingerprint",
                content
            );

            var responseJson = await response.Content.ReadAsStringAsync();

            // Check if response is HTML (authentication/routing issue)
            if (responseJson.TrimStart().StartsWith("<!DOCTYPE") || responseJson.TrimStart().StartsWith("<html"))
            {
                throw new InvalidOperationException("API returned HTML instead of JSON - authentication may have expired");
            }

            if (response.IsSuccessStatusCode)
            {
                await HandleSuccessfulSave(mode, quality, pngImageData?.Length ?? 0);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Authentication expired");
            }
            else
            {
                throw new HttpRequestException($"Server error: {response.StatusCode} - {responseJson}");
            }
        }

        /// <summary>
        /// Handle successful save with completion workflow
        /// </summary>
        private async Task HandleSuccessfulSave(string mode, int quality, int imageSize)
        {
            // Update workflow to completed
            UpdateWorkflowState(RegistrationWorkflowState.Completed);

            // Update current student record
            _currentStudent.FingerprintRegisteredAt = DateTime.Now;
            _hasExistingFingerprint = true;

            // Show success message with details
            MessageBox.Show(
                $"✓ Fingerprint Registration Completed!\n\n" +
                $"Student: {_currentStudent.Name}\n" +
                $"Roll Number: {_currentStudent.RollNumber}\n" +
                $"College: {_currentStudent.CollegeName}\n" +
                $"Quality Score: {quality}%\n" +
                $"Capture Mode: {mode}\n" +
                $"Total Attempts: {_captureAttempts}\n" +
                $"Image Size: {imageSize:N0} bytes (PNG)\n" +
                $"Registered: {DateTime.Now:MMM dd, yyyy 'at' HH:mm}\n\n" +
                "The student's fingerprint has been successfully registered and can now be used for verification.",
                "Registration Successful",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            Logger.Info($"Fingerprint registration completed successfully: {_currentStudent.RollNumber}");

            // Auto-clear and prepare for next registration
            await Task.Delay(1000); // Brief pause to let user see completion state
            ClearStudentInfo();
            txtSearch.Text = "";
            txtSearch.Focus();
        }

        /// <summary>
        /// Handle save exceptions with appropriate user feedback
        /// </summary>
        private async Task HandleSaveException(Exception ex)
        {
            UpdateWorkflowState(RegistrationWorkflowState.Error);

            txtScannerStatus.Text = "Save Failed";
            txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));

            string errorMessage;
            string title;

            switch (ex)
            {
                case UnauthorizedAccessException:
                    errorMessage = "Authentication expired.\n\nPlease restart the application and log in again.";
                    title = "Authentication Required";
                    _isAuthenticated = false;
                    break;

                case HttpRequestException httpEx:
                    errorMessage = $"Server communication error:\n\n{httpEx.Message}\n\nPlease check your network connection and try again.";
                    title = "Network Error";
                    break;

                case InvalidOperationException invalidEx when invalidEx.Message.Contains("HTML"):
                    errorMessage = "API endpoint error - authentication may have expired.\n\nPlease restart the application.";
                    title = "API Error";
                    _isAuthenticated = false;
                    break;

                default:
                    errorMessage = $"An unexpected error occurred while saving:\n\n{ex.Message}\n\nPlease try again or contact support if the problem persists.";
                    title = "Save Error";
                    break;
            }

            MessageBox.Show(errorMessage, title, MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Error($"Fingerprint save failed for {_currentStudent?.RollNumber}", ex);

            // Return to ready-to-save state if not an auth error
            if (ex is not UnauthorizedAccessException && ex is not InvalidOperationException)
            {
                UpdateWorkflowState(RegistrationWorkflowState.ReadyToSave);
            }
        }

        #endregion

        #region Quality Slider

        private void SliderQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtQualityValue != null)
            {
                txtQualityValue.Text = $"{(int)e.NewValue}";
            }

            if (_fingerprintService != null)
            {
                _fingerprintService.MinimumQualityScore = (int)e.NewValue;
            }
        }

        #endregion

        #region Cleanup

        private void RegistrationTab_Unloaded(object sender, RoutedEventArgs e)
        {
            _fingerprintService?.DisconnectAsync();
            _httpClient?.Dispose();
        }

        #endregion
    }
}