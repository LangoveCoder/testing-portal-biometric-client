// Views/VerificationTab.xaml.cs
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
    public partial class VerificationTab : UserControl
    {
        private readonly string _apiUrl = "http://localhost:8000/api";
        private readonly HttpClient _httpClient;
        private FingerprintService _fingerprintService;
        private RoleBasedUIService _roleBasedUIService;

        private Student _currentStudent;
        private byte[] _registeredTemplate;
        private byte[] _capturedFingerprintImage;
        private byte[] _capturedTemplate;

        private bool _isDeviceInitialized = false;
        private bool _isVerified = false;
        private int _matchScore = 0;
        private bool _isMatched = false;
        private string _authToken = null;
        private bool _isAuthenticated = false;

        // Enhanced verification workflow state management
        private VerificationWorkflowState _workflowState = VerificationWorkflowState.SearchStudent;
        private int _captureAttempts = 0;
        private const int MaxCaptureAttempts = 3;
        private DateTime _verificationStartTime;
        private string _verificationDecision = "";
        private string _manualOverrideReason = "";

        // Verification workflow states
        public enum VerificationWorkflowState
        {
            SearchStudent,
            StudentLoaded,
            TemplateLoaded,
            ReadyToCapture,
            CapturingFingerprint,
            QualityValidation,
            PerformingMatch,
            MatchCompleted,
            AwaitingDecision,
            DecisionMade,
            SavingResult
        }

        public VerificationTab()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _fingerprintService = new FingerprintService();

            Loaded += VerificationTab_Loaded;
            Unloaded += VerificationTab_Unloaded;
            
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

            System.Diagnostics.Debug.WriteLine($"✓ VerificationTab: Auth token set for {userName} ({userRole})");
            Logger.Info($"VerificationTab initialized for {userName} ({userRole})");
        }

        /// <summary>
        /// Set role-based UI service for college filtering and access control
        /// </summary>
        public void SetRoleBasedUIService(RoleBasedUIService roleBasedUIService)
        {
            _roleBasedUIService = roleBasedUIService;
            
            if (_roleBasedUIService != null)
            {
                SetupCollegeRestrictions();
                Logger.Info("VerificationTab: Role-based UI service configured with college restrictions");
            }
        }

        /// <summary>
        /// Setup college access restrictions UI based on user role
        /// </summary>
        private void SetupCollegeRestrictions()
        {
            if (_roleBasedUIService?.GetAssignedColleges() == null)
                return;

            var colleges = _roleBasedUIService.GetAssignedColleges();
            
            if (colleges.Count == 1)
            {
                // Single college - show restriction notice
                panelCollegeFilter.Visibility = Visibility.Visible;
                txtCollegeRestriction.Text = $"Access restricted to {colleges[0].Name} students only";
                
                Logger.Info($"College access restricted to: {colleges[0].Name}");
            }
            else if (colleges.Count > 1)
            {
                // Multiple colleges - show general restriction notice
                panelCollegeFilter.Visibility = Visibility.Visible;
                txtCollegeRestriction.Text = $"Access restricted to students from your {colleges.Count} assigned colleges";
                
                Logger.Info($"College access restricted to {colleges.Count} colleges");
            }
            else
            {
                // No colleges assigned
                panelCollegeFilter.Visibility = Visibility.Collapsed;
                Logger.Warning("No colleges assigned to current user");
            }
        }

        #endregion

        #region Device Initialization

        private async void VerificationTab_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeFingerprintDevice();
        }

        private async System.Threading.Tasks.Task InitializeFingerprintDevice()
        {
            try
            {
                txtScannerStatus.Text = "Initializing...";
                txtScannerStatus.Foreground = new SolidColorBrush(Colors.Orange);
                scannerIndicator.Fill = new SolidColorBrush(Colors.Orange);

                // Simple direct initialization
                var result = await _fingerprintService.AutoDetectScannerAsync();

                if (result.Success)
                {
                    _isDeviceInitialized = true;

                    var scannerInfo = _fingerprintService.GetCurrentScannerInfo();

                    txtScannerStatus.Text = $"Connected: {scannerInfo?.Name ?? "SecuGen Scanner"}";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                    scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(102, 187, 106));

                    System.Diagnostics.Debug.WriteLine($"✓ Scanner Ready: {result.Message}");

                    MessageBox.Show(
                        $"Scanner Connected Successfully!\n\n{scannerInfo?.Name}\n{scannerInfo?.Model}",
                        "Scanner Ready",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    _isDeviceInitialized = false;
                    txtScannerStatus.Text = "Simulation Mode";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));
                    scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                    System.Diagnostics.Debug.WriteLine($"⚠ No Scanner: {result.Message}");

                    if (!string.IsNullOrEmpty(result.ErrorDetails))
                    {
                        System.Diagnostics.Debug.WriteLine($"Details: {result.ErrorDetails}");
                    }
                }
            }
            catch (Exception ex)
            {
                _isDeviceInitialized = false;
                txtScannerStatus.Text = "Error";
                txtScannerStatus.Foreground = new SolidColorBrush(Colors.Red);
                scannerIndicator.Fill = new SolidColorBrush(Colors.Red);

                System.Diagnostics.Debug.WriteLine($"❌ Exception: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");

                MessageBox.Show(
                    $"Scanner Initialization Failed:\n\n{ex.GetType().Name}\n{ex.Message}\n\n" +
                    $"Working in Simulation Mode.",
                    "Scanner Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
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
                Logger.Info("DatabaseService initialized for enhanced search in VerificationTab");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize DatabaseService for search in VerificationTab", ex);
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
                    // Filter only students with registered fingerprints for verification
                    var verifiableStudents = localResults.Where(s => 
                        s.FingerprintTemplate != null || s.FingerprintRegisteredAt.HasValue).ToList();
                    
                    DisplaySearchResults(verifiableStudents, "Local Cache");
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
                
                Logger.Info($"Local search found {results.Count} results for '{searchTerm}' in VerificationTab");
                return results;
            }
            catch (Exception ex)
            {
                Logger.Error($"Local database search failed in VerificationTab: {ex.Message}", ex);
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
                // Use verification-specific API endpoint
                var response = await _httpClient.GetAsync($"{_apiUrl}/biometric-verification/search?roll_number={searchTerm}");

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                    
                    var student = ParseStudentFromJson(result);
                    DisplaySearchResults(new List<Student> { student }, "Server");
                }
                else
                {
                    DisplaySearchResults(new List<Student>(), "Server");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"API search failed in VerificationTab: {ex.Message}", ex);
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
                txtSearchSummary.Text = $"Found {results.Count} verifiable student{(results.Count == 1 ? "" : "s")} from {source}";
                txtSearchTime.Text = _searchStopwatch != null ? $"({_searchStopwatch.ElapsedMilliseconds}ms)" : "";

                // Populate results list with enhanced data
                var enhancedResults = results.Select(s => new EnhancedStudentResult
                {
                    Student = s,
                    HasFingerprint = s.FingerprintTemplate != null || s.FingerprintRegisteredAt.HasValue,
                    FingerprintStatus = GetFingerprintStatusText(s),
                    StatusColor = GetFingerprintStatusColor(s),
                    VerificationHistory = GetVerificationHistoryText(s)
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
        /// Get verification history text for display
        /// </summary>
        private string GetVerificationHistoryText(Student student)
        {
            // This could be enhanced to show actual verification count from database
            return "Ready for verification";
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
                            "You can only verify students from your assigned college.",
                            "Access Denied",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                // Load the selected student for verification
                if (selectedResult.HasFingerprint)
                {
                    // Simulate loading registered template (in real implementation, this would come from API/DB)
                    _registeredTemplate = student.FingerprintTemplate ?? new byte[400]; // Placeholder
                    LoadStudent(student, student.FingerprintImage, true);
                    txtLoadSuccess.Visibility = Visibility.Visible;
                }
                else
                {
                    LoadStudent(student, null, false);
                    txtLoadSuccess.Visibility = Visibility.Visible;
                }
                
                // Update search box with selected student's roll number
                txtSearch.Text = student.RollNumber;
                
                // Hide search results after selection
                panelSearchResults.Visibility = Visibility.Collapsed;
                
                Logger.Info($"Student selected from search results for verification: {student.RollNumber} - {student.Name}");
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
        /// Clear student information display
        /// </summary>
        private void ClearStudentInfo()
        {
            _currentStudent = null;
            _registeredTemplate = null;
            _capturedFingerprintImage = null;
            _capturedTemplate = null;
            _isVerified = false;
            _isMatched = false;
            _matchScore = 0;

            panelStudentDetails.Visibility = Visibility.Collapsed;
            panelLiveVerification.Visibility = Visibility.Collapsed;
            panelMatchResult.Visibility = Visibility.Collapsed;
            txtLoadSuccess.Visibility = Visibility.Collapsed;

            imgRegistrationPhoto.Source = null;
            imgTestPhoto.Source = null;
            imgSavedFingerprint.Source = null;
            imgLiveCapture.Source = null;

            btnVerify.IsEnabled = false;
            btnSaveLog.IsEnabled = false;
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
            public string VerificationHistory { get; set; }
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
                RollNumber = GetStringProperty(jsonData, "roll_number"),
                Name = GetStringProperty(jsonData, "name"),
                FatherName = GetStringProperty(jsonData, "father_name"),
                CNIC = GetStringProperty(jsonData, "cnic"),
                Gender = GetStringProperty(jsonData, "gender"),
                TestName = GetStringProperty(jsonData, "test_name"),
                CollegeName = GetStringProperty(jsonData, "college_name"),
                CollegeId = GetIntProperty(jsonData, "college_id"),
                Venue = GetStringProperty(jsonData, "venue"),
                Hall = GetStringProperty(jsonData, "hall"),
                Zone = GetStringProperty(jsonData, "zone"),
                Row = GetStringProperty(jsonData, "row"),
                Seat = GetStringProperty(jsonData, "seat"),
            };

            // Parse picture (base64 to byte[])
            if (jsonData.TryGetProperty("picture", out var pic) &&
                pic.ValueKind != JsonValueKind.Null &&
                !string.IsNullOrEmpty(pic.GetString()))
            {
                try
                {
                    student.Picture = Convert.FromBase64String(pic.GetString());
                }
                catch { }
            }

            // Parse test photo (base64 to byte[])
            if (jsonData.TryGetProperty("test_photo", out var tp) &&
                tp.ValueKind != JsonValueKind.Null &&
                !string.IsNullOrEmpty(tp.GetString()))
            {
                try
                {
                    student.TestPhoto = Convert.FromBase64String(tp.GetString());
                }
                catch { }
            }

            // Parse fingerprint registered date
            if (jsonData.TryGetProperty("fingerprint_registered_at", out var regAt) &&
                regAt.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(regAt.GetString(), out DateTime registeredAt))
            {
                student.FingerprintRegisteredAt = registeredAt;
            }

            // Parse quality score
            if (jsonData.TryGetProperty("fingerprint_quality", out var quality) &&
                quality.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    student.FingerprintQuality = quality.GetInt32();
                }
                catch { }
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
                string value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return null;
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
        /// Load student information into UI with enhanced template loading
        /// </summary>
        private async void LoadStudent(Student student, byte[] registeredFingerprintImage, bool hasFingerprintRegistered)
        {
            _currentStudent = student;
            _workflowState = VerificationWorkflowState.StudentLoaded;

            panelStudentDetails.Visibility = Visibility.Visible;
            panelLiveVerification.Visibility = Visibility.Visible;

            // Display student information
            txtStudentName.Text = student.Name ?? "-";
            txtFatherName.Text = student.FatherName ?? "-";
            txtRollNumber.Text = student.RollNumber ?? "-";

            // Force CNIC update
            txtCNIC.Text = string.Empty;
            txtCNIC.UpdateLayout();
            txtCNIC.Text = student.CNIC ?? "-";

            // Display registration photo
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
                    imgRegistrationPhoto.Source = bitmap;
                    txtNoPhoto.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    imgRegistrationPhoto.Source = null;
                    txtNoPhoto.Visibility = Visibility.Visible;
                }
            }
            else
            {
                imgRegistrationPhoto.Source = null;
                txtNoPhoto.Visibility = Visibility.Visible;
            }

            // Display test photo
            if (student.TestPhoto != null && student.TestPhoto.Length > 0)
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    using (MemoryStream ms = new MemoryStream(student.TestPhoto))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    imgTestPhoto.Source = bitmap;
                    txtNoTestPhoto.Visibility = Visibility.Collapsed;
                    txtTestPhotoStatus.Text = "✓ Captured";
                    txtTestPhotoStatus.Foreground = new SolidColorBrush(Color.FromRgb(67, 160, 71));
                }
                catch
                {
                    imgTestPhoto.Source = null;
                    txtNoTestPhoto.Visibility = Visibility.Visible;
                    txtTestPhotoStatus.Text = "✗ Not captured";
                    txtTestPhotoStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }
            }
            else
            {
                imgTestPhoto.Source = null;
                txtNoTestPhoto.Visibility = Visibility.Visible;
                txtTestPhotoStatus.Text = "✗ Not captured";
                txtTestPhotoStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            }

            // Enhanced fingerprint template loading
            if (hasFingerprintRegistered)
            {
                await LoadFingerprintTemplateAsync(student, registeredFingerprintImage);
            }
            else
            {
                imgSavedFingerprint.Source = null;
                txtNoFingerprint.Visibility = Visibility.Visible;
                txtNoFingerprint.Text = "Not registered";
                txtFingerprintStatus.Text = "✗ Not registered";
                txtFingerprintStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                btnVerify.IsEnabled = false;
                _workflowState = VerificationWorkflowState.StudentLoaded;
            }

            // Reset verification state
            ResetVerificationState();
        }

        /// <summary>
        /// Enhanced fingerprint template loading with validation
        /// </summary>
        private async Task LoadFingerprintTemplateAsync(Student student, byte[] registeredFingerprintImage)
        {
            try
            {
                _workflowState = VerificationWorkflowState.TemplateLoaded;

                // Load template from student data or fetch from API/database
                if (student.FingerprintTemplate != null && student.FingerprintTemplate.Length > 0)
                {
                    _registeredTemplate = student.FingerprintTemplate;
                }
                else
                {
                    // Fetch template from API if not available locally
                    _registeredTemplate = await FetchFingerprintTemplateAsync(student.RollNumber);
                }

                if (_registeredTemplate != null && _registeredTemplate.Length > 0)
                {
                    // Display registered fingerprint image
                    if (registeredFingerprintImage != null && registeredFingerprintImage.Length > 0)
                    {
                        DisplayRegisteredFingerprint(registeredFingerprintImage);
                    }
                    else
                    {
                        // Generate placeholder image for template
                        DisplayTemplatePlaceholder();
                    }

                    txtFingerprintStatus.Text = "✓ Template Loaded";
                    txtFingerprintStatus.Foreground = new SolidColorBrush(Color.FromRgb(67, 160, 71));
                    btnVerify.IsEnabled = true;

                    Logger.Info($"Fingerprint template loaded for student: {student.RollNumber}");
                }
                else
                {
                    throw new Exception("Failed to load fingerprint template");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load fingerprint template for student {student.RollNumber}", ex);
                
                imgSavedFingerprint.Source = null;
                txtNoFingerprint.Visibility = Visibility.Visible;
                txtNoFingerprint.Text = "Template load failed";
                txtFingerprintStatus.Text = "✗ Load failed";
                txtFingerprintStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                btnVerify.IsEnabled = false;
                _workflowState = VerificationWorkflowState.StudentLoaded;

                MessageBox.Show(
                    $"Failed to load fingerprint template:\n\n{ex.Message}\n\nVerification cannot proceed.",
                    "Template Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Fetch fingerprint template from API
        /// </summary>
        private async Task<byte[]> FetchFingerprintTemplateAsync(string rollNumber)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/biometric-verification/template/{rollNumber}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                    
                    if (result.TryGetProperty("template", out var templateProp) && 
                        templateProp.ValueKind != JsonValueKind.Null)
                    {
                        string templateBase64 = templateProp.GetString();
                        if (!string.IsNullOrEmpty(templateBase64))
                        {
                            return Convert.FromBase64String(templateBase64);
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"API call failed to fetch template for {rollNumber}", ex);
                return null;
            }
        }

        /// <summary>
        /// Display placeholder for template when no image available
        /// </summary>
        private void DisplayTemplatePlaceholder()
        {
            try
            {
                // Create a simple placeholder image
                int width = 300;
                int height = 400;
                byte[] placeholderData = new byte[width * height];
                
                // Create a simple pattern
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        placeholderData[index] = (byte)(128 + (x + y) % 64);
                    }
                }

                WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Gray8, null);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), placeholderData, width, 0);

                imgSavedFingerprint.Source = bitmap;
                txtNoFingerprint.Visibility = Visibility.Collapsed;
            }
            catch
            {
                imgSavedFingerprint.Source = null;
                txtNoFingerprint.Visibility = Visibility.Visible;
                txtNoFingerprint.Text = "Template available";
            }
        }

        /// <summary>
        /// Reset verification state for new verification
        /// </summary>
        private void ResetVerificationState()
        {
            _isVerified = false;
            _isMatched = false;
            _matchScore = 0;
            _captureAttempts = 0;
            _verificationDecision = "";
            _manualOverrideReason = "";
            
            panelMatchResult.Visibility = Visibility.Collapsed;
            imgLiveCapture.Source = null;
            txtNoLiveCapture.Visibility = Visibility.Visible;
            btnSaveLog.IsEnabled = false;
            
            _capturedTemplate = null;
            _capturedFingerprintImage = null;
        }

        #endregion

        #region Display Methods

        /// <summary>
        /// Display registered fingerprint image
        /// </summary>
        private void DisplayRegisteredFingerprint(byte[] imageData)
        {
            try
            {
                int width = 300;
                int height = 400;

                WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Gray8, null);

                bitmap.WritePixels(new Int32Rect(0, 0, width, height), imageData, width, 0);

                imgSavedFingerprint.Source = bitmap;
                txtNoFingerprint.Visibility = Visibility.Collapsed;
            }
            catch
            {
                imgSavedFingerprint.Source = null;
                txtNoFingerprint.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Enhanced Fingerprint Verification Workflow

        private async void BtnVerify_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStudent == null)
            {
                MessageBox.Show("Please search and load a student first", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_registeredTemplate == null)
            {
                MessageBox.Show("No registered template found for this student", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Role-based validation for fingerprint verification
            if (_roleBasedUIService != null)
            {
                var validation = _roleBasedUIService.ValidateAction(UserAction.VerifyFingerprint);
                if (!validation.IsValid)
                {
                    MessageBox.Show(validation.ErrorMessage, "Access Denied",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Start verification workflow
            _verificationStartTime = DateTime.Now;
            _workflowState = VerificationWorkflowState.ReadyToCapture;
            
            if (_isDeviceInitialized)
            {
                await PerformVerificationWithRealDeviceAsync();
            }
            else
            {
                await PerformVerificationWithSimulationAsync();
            }
        }

        /// <summary>
        /// Enhanced verification workflow with real device
        /// </summary>
        private async Task PerformVerificationWithRealDeviceAsync()
        {
            try
            {
                btnVerify.IsEnabled = false;
                _workflowState = VerificationWorkflowState.CapturingFingerprint;
                _captureAttempts++;

                UpdateVerificationStatus("Place finger on scanner...", Colors.Orange);

                // Update match threshold
                _fingerprintService.MatchThreshold = (int)sliderMatchThreshold.Value;

                // Capture fingerprint with quality validation
                var captureResult = await _fingerprintService.CaptureAsync();

                if (!captureResult.Success)
                {
                    await HandleCaptureFailureAsync(captureResult);
                    return;
                }

                _workflowState = VerificationWorkflowState.QualityValidation;
                
                // Validate capture quality
                if (!ValidateCaptureQuality(captureResult))
                {
                    return;
                }

                // Display captured fingerprint
                DisplayCapturedFingerprint(captureResult.ImageData, captureResult.ImageWidth, captureResult.ImageHeight);
                
                _capturedTemplate = captureResult.Template;
                _capturedFingerprintImage = captureResult.ImageData;

                // Perform matching
                _workflowState = VerificationWorkflowState.PerformingMatch;
                UpdateVerificationStatus("Performing match analysis...", Colors.Blue);

                var matchResult = await _fingerprintService.VerifyAsync(_registeredTemplate, _capturedTemplate);
                
                _matchScore = matchResult.ConfidenceScore;
                _isMatched = matchResult.IsMatch;
                _workflowState = VerificationWorkflowState.MatchCompleted;

                // Display enhanced match results with decision recommendation
                DisplayEnhancedMatchResult(matchResult);
                
                _workflowState = VerificationWorkflowState.AwaitingDecision;
                UpdateVerificationStatus(_isMatched ? "Match confirmed - Review decision" : "No match - Review decision", 
                    _isMatched ? Colors.Green : Colors.Red);

                Logger.Info($"Verification completed for {_currentStudent.RollNumber}: {(_isMatched ? "MATCHED" : "NO MATCH")} (Score: {_matchScore})");
            }
            catch (Exception ex)
            {
                await HandleVerificationErrorAsync(ex);
            }
            finally
            {
                btnVerify.IsEnabled = true;
            }
        }

        /// <summary>
        /// Enhanced verification workflow with simulation
        /// </summary>
        private async Task PerformVerificationWithSimulationAsync()
        {
            try
            {
                btnVerify.IsEnabled = false;
                _workflowState = VerificationWorkflowState.CapturingFingerprint;
                _captureAttempts++;

                UpdateVerificationStatus("Simulating fingerprint capture...", Colors.Orange);
                await Task.Delay(1500); // Simulate capture time

                // Generate simulated fingerprint data
                int width = 300;
                int height = 400;
                _capturedFingerprintImage = GenerateSimulatedFingerprint(width, height);
                _capturedTemplate = new byte[400];
                new Random().NextBytes(_capturedTemplate);

                DisplayCapturedFingerprint(_capturedFingerprintImage, width, height);

                _workflowState = VerificationWorkflowState.PerformingMatch;
                UpdateVerificationStatus("Simulating match analysis...", Colors.Blue);
                await Task.Delay(1000);

                // Generate simulated match result
                var simulatedResult = GenerateSimulatedMatchResult();
                _matchScore = simulatedResult.ConfidenceScore;
                _isMatched = simulatedResult.IsMatch;
                _workflowState = VerificationWorkflowState.MatchCompleted;

                DisplayEnhancedMatchResult(simulatedResult);
                
                _workflowState = VerificationWorkflowState.AwaitingDecision;
                UpdateVerificationStatus("Simulation complete - Review decision", Colors.Orange);

                // Show simulation warning
                MessageBox.Show(
                    $"⚠ SIMULATED VERIFICATION COMPLETE\n\n" +
                    $"Result: {(_isMatched ? "MATCHED" : "NOT MATCHED")}\n" +
                    $"Confidence: {_matchScore}%\n" +
                    $"Quality: {simulatedResult.Quality}\n" +
                    $"Recommendation: {GetVerificationRecommendation(_matchScore, simulatedResult.Quality)}\n\n" +
                    "This is NOT real biometric verification!\nReview the result and make your decision.",
                    "Simulation Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Logger.Info($"Simulated verification for {_currentStudent.RollNumber}: {(_isMatched ? "MATCHED" : "NO MATCH")} (Score: {_matchScore})");
            }
            catch (Exception ex)
            {
                await HandleVerificationErrorAsync(ex);
            }
            finally
            {
                btnVerify.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handle capture failure with retry logic
        /// </summary>
        private async Task HandleCaptureFailureAsync(FingerprintCaptureResult captureResult)
        {
            string errorMessage = GetCaptureFailureGuidance(captureResult.FailureReason ?? CaptureFailureReason.DeviceError);
            
            if (_captureAttempts < MaxCaptureAttempts)
            {
                var retryResult = MessageBox.Show(
                    $"Capture Failed (Attempt {_captureAttempts}/{MaxCaptureAttempts})\n\n" +
                    $"{errorMessage}\n\n" +
                    "Would you like to try again?",
                    "Capture Failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (retryResult == MessageBoxResult.Yes)
                {
                    UpdateVerificationStatus("Ready for retry...", Colors.Orange);
                    await Task.Delay(1000);
                    await PerformVerificationWithRealDeviceAsync();
                    return;
                }
            }
            else
            {
                MessageBox.Show(
                    $"Maximum capture attempts reached ({MaxCaptureAttempts})\n\n" +
                    $"{errorMessage}\n\n" +
                    "Please check the scanner and try again later.",
                    "Capture Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            UpdateVerificationStatus("Capture failed", Colors.Red);
            _workflowState = VerificationWorkflowState.TemplateLoaded;
        }

        /// <summary>
        /// Validate capture quality and provide feedback
        /// </summary>
        private bool ValidateCaptureQuality(FingerprintCaptureResult captureResult)
        {
            if (captureResult.QualityScore < _fingerprintService.MinimumQualityScore)
            {
                string qualityGuidance = GetQualityGuidance(captureResult.QualityScore);
                
                var retryResult = MessageBox.Show(
                    $"Poor Fingerprint Quality\n\n" +
                    $"Quality Score: {captureResult.QualityScore}% (Minimum: {_fingerprintService.MinimumQualityScore}%)\n\n" +
                    $"{qualityGuidance}\n\n" +
                    "Would you like to recapture?",
                    "Quality Check",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (retryResult == MessageBoxResult.Yes && _captureAttempts < MaxCaptureAttempts)
                {
                    Task.Run(async () => await PerformVerificationWithRealDeviceAsync());
                    return false;
                }
                else
                {
                    UpdateVerificationStatus("Quality check failed", Colors.Red);
                    _workflowState = VerificationWorkflowState.TemplateLoaded;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Display enhanced match result with decision recommendation
        /// </summary>
        private void DisplayEnhancedMatchResult(FingerprintMatchResult matchResult)
        {
            panelMatchResult.Visibility = Visibility.Visible;

            // Update match result display
            if (_isMatched)
            {
                panelMatchResult.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                panelMatchResult.BorderBrush = new SolidColorBrush(Color.FromRgb(102, 187, 106));

                txtMatchResult.Text = "✓ MATCH CONFIRMED";
                txtMatchResult.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            }
            else
            {
                panelMatchResult.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                panelMatchResult.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 83, 80));

                txtMatchResult.Text = "✖ NO MATCH";
                txtMatchResult.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
            }

            // Enhanced score display with quality indicator
            string qualityText = GetQualityDescription(matchResult.Quality);
            txtMatchScore.Text = $"Confidence: {_matchScore}% | Quality: {qualityText}";
            txtMatchScore.Foreground = _isMatched 
                ? new SolidColorBrush(Color.FromRgb(56, 142, 60))
                : new SolidColorBrush(Color.FromRgb(211, 47, 47));

            // Set recommended decision
            _verificationDecision = GetVerificationRecommendation(_matchScore, matchResult.Quality);
            
            _isVerified = true;
            btnSaveLog.IsEnabled = true;
        }

        /// <summary>
        /// Get verification recommendation based on score and quality
        /// </summary>
        private string GetVerificationRecommendation(int score, MatchQuality quality)
        {
            if (score >= 95 && quality >= MatchQuality.Good)
                return "ACCEPT - High confidence match";
            else if (score >= 85 && quality >= MatchQuality.Fair)
                return "ACCEPT - Good match";
            else if (score >= 70 && quality >= MatchQuality.Fair)
                return "REVIEW - Moderate match, manual review recommended";
            else if (score >= 60)
                return "REVIEW - Low confidence, careful review required";
            else
                return "REJECT - No match detected";
        }

        /// <summary>
        /// Get quality description for display
        /// </summary>
        private string GetQualityDescription(MatchQuality quality)
        {
            return quality switch
            {
                MatchQuality.Excellent => "Excellent",
                MatchQuality.Good => "Good",
                MatchQuality.Fair => "Fair",
                MatchQuality.Poor => "Poor",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Generate simulated fingerprint image
        /// </summary>
        private byte[] GenerateSimulatedFingerprint(int width, int height)
        {
            byte[] imageData = new byte[width * height];
            Random rand = new Random();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    // Create a more realistic fingerprint pattern
                    double distance = Math.Sqrt(Math.Pow(x - width/2, 2) + Math.Pow(y - height/2, 2));
                    double pattern = Math.Sin(distance * 0.1) * 50 + 128;
                    imageData[index] = (byte)Math.Max(0, Math.Min(255, pattern + rand.Next(-30, 30)));
                }
            }
            
            return imageData;
        }

        /// <summary>
        /// Generate simulated match result
        /// </summary>
        private FingerprintMatchResult GenerateSimulatedMatchResult()
        {
            Random rand = new Random();
            int threshold = (int)sliderMatchThreshold.Value;
            
            // Simulate realistic score distribution
            int score = rand.Next(40, 120);
            bool isMatch = score >= threshold;
            
            MatchQuality quality = score switch
            {
                >= 90 => MatchQuality.Excellent,
                >= 80 => MatchQuality.Good,
                >= 70 => MatchQuality.Fair,
                _ => MatchQuality.Poor
            };

            return new FingerprintMatchResult
            {
                IsMatch = isMatch,
                ConfidenceScore = score,
                Quality = quality,
                Message = $"Simulated match result: {score}% confidence"
            };
        }

        /// <summary>
        /// Get capture failure guidance message
        /// </summary>
        private string GetCaptureFailureGuidance(CaptureFailureReason reason)
        {
            return reason switch
            {
                CaptureFailureReason.Timeout => "No finger detected on scanner.\n\nPlease ensure finger is properly placed on the scanner surface.",
                CaptureFailureReason.PoorQuality => "Fingerprint quality too low.\n\nClean finger and scanner, press firmly but gently.",
                CaptureFailureReason.DeviceNotConnected => "Scanner not connected.\n\nCheck USB connection and restart application.",
                CaptureFailureReason.DeviceError => "Scanner hardware error.\n\nCheck device connection and try again.",
                _ => "Unknown capture error.\n\nPlease try again or contact support."
            };
        }

        /// <summary>
        /// Get quality improvement guidance
        /// </summary>
        private string GetQualityGuidance(int qualityScore)
        {
            if (qualityScore < 30)
                return "Very poor quality. Clean finger and scanner thoroughly, ensure proper finger placement.";
            else if (qualityScore < 50)
                return "Poor quality. Press finger more firmly, avoid sliding, ensure finger is dry.";
            else if (qualityScore < 60)
                return "Fair quality. Adjust finger position slightly, ensure full contact with scanner.";
            else
                return "Quality acceptable but could be improved. Try slight adjustment of finger position.";
        }

        /// <summary>
        /// Update verification status display
        /// </summary>
        private void UpdateVerificationStatus(string message, Color color)
        {
            txtScannerStatus.Text = message;
            txtScannerStatus.Foreground = new SolidColorBrush(color);
            scannerIndicator.Fill = new SolidColorBrush(color);
        }

        /// <summary>
        /// Handle verification errors
        /// </summary>
        private async Task HandleVerificationErrorAsync(Exception ex)
        {
            Logger.Error($"Verification error for student {_currentStudent?.RollNumber}", ex);
            
            UpdateVerificationStatus("Verification failed", Colors.Red);
            _workflowState = VerificationWorkflowState.TemplateLoaded;

            MessageBox.Show(
                $"Verification Failed\n\n{ex.Message}\n\nPlease try again or contact support.",
                "Verification Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// Display captured fingerprint image
        /// </summary>
        private void DisplayCapturedFingerprint(byte[] imageData, int width, int height)
        {
            try
            {
                WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Gray8, null);

                bitmap.WritePixels(new Int32Rect(0, 0, width, height), imageData, width, 0);

                imgLiveCapture.Source = bitmap;
                txtNoLiveCapture.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying fingerprint: {ex.Message}", "Display Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Display match result with basic formatting
        /// </summary>
        private void DisplayMatchResult()
        {
            panelMatchResult.Visibility = Visibility.Visible;

            if (_isMatched)
            {
                panelMatchResult.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                panelMatchResult.BorderBrush = new SolidColorBrush(Color.FromRgb(102, 187, 106));

                txtMatchResult.Text = "✓ MATCH CONFIRMED";
                txtMatchResult.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));

                txtMatchScore.Text = $"Match Score: {_matchScore}";
                txtMatchScore.Foreground = new SolidColorBrush(Color.FromRgb(56, 142, 60));
            }
            else
            {
                panelMatchResult.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                panelMatchResult.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 83, 80));

                txtMatchResult.Text = "✖ NO MATCH";
                txtMatchResult.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));

                txtMatchScore.Text = $"Match Score: {_matchScore}";
                txtMatchScore.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            }
        }

        #endregion

        #region Enhanced Save Verification Log with Manual Override

        private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStudent == null || !_isVerified)
            {
                MessageBox.Show("Please verify fingerprint first", "Error",
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

            // Show decision confirmation dialog with manual override option
            var decisionResult = ShowDecisionConfirmationDialog();
            if (decisionResult == null) return; // User cancelled

            try
            {
                btnSaveLog.IsEnabled = false;
                _workflowState = VerificationWorkflowState.SavingResult;
                UpdateVerificationStatus("Saving verification result...", Colors.Blue);

                var verificationData = CreateVerificationData(decisionResult);
                var json = JsonSerializer.Serialize(verificationData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/biometric-verification/verify", content);

                if (response.IsSuccessStatusCode)
                {
                    await HandleSuccessfulSaveAsync(decisionResult);
                }
                else
                {
                    await HandleSaveErrorAsync(response);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving verification log for {_currentStudent.RollNumber}", ex);
                MessageBox.Show($"Error saving verification log:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateVerificationStatus("Save failed", Colors.Red);
            }
            finally
            {
                btnSaveLog.IsEnabled = true;
            }
        }

        /// <summary>
        /// Show decision confirmation dialog with manual override option
        /// </summary>
        private VerificationDecision ShowDecisionConfirmationDialog()
        {
            var dialog = new VerificationDecisionDialog(_currentStudent, _matchScore, _verificationDecision, _isMatched);
            var result = dialog.ShowDialog();
            
            if (result == true)
            {
                return dialog.Decision;
            }
            
            return null; // User cancelled
        }

        /// <summary>
        /// Create verification data object for API
        /// </summary>
        private object CreateVerificationData(VerificationDecision decision)
        {
            string deviceInfo = _isDeviceInitialized
                ? _fingerprintService.GetCurrentScannerInfo()?.Model ?? "SecuGen Scanner"
                : "Simulation Mode";

            return new
            {
                roll_number = _currentStudent.RollNumber,
                is_matched = decision.FinalDecision == "ACCEPT",
                match_score = _matchScore,
                captured_template = Convert.ToBase64String(_capturedTemplate ?? new byte[0]),
                captured_image = _capturedFingerprintImage != null ? Convert.ToBase64String(_capturedFingerprintImage) : null,
                capture_quality = _capturedTemplate != null ? _fingerprintService.GetQualityScore(_capturedTemplate) : 0,
                verified_by = "College Admin", // This should come from current user
                device_info = deviceInfo,
                verification_decision = decision.FinalDecision,
                recommended_decision = _verificationDecision,
                manual_override = decision.IsManualOverride,
                override_reason = decision.OverrideReason,
                verification_notes = decision.Notes,
                verification_duration = (DateTime.Now - _verificationStartTime).TotalSeconds,
                capture_attempts = _captureAttempts,
                failure_reason = !_isMatched && !decision.IsManualOverride 
                    ? $"Score {_matchScore} below threshold {(int)sliderMatchThreshold.Value}" 
                    : null
            };
        }

        /// <summary>
        /// Handle successful save operation
        /// </summary>
        private async Task HandleSuccessfulSaveAsync(VerificationDecision decision)
        {
            string mode = _isDeviceInitialized ? "Real Device" : "Simulation";
            string overrideText = decision.IsManualOverride ? " (Manual Override)" : "";
            
            MessageBox.Show(
                $"✓ Verification Result Saved Successfully!\n\n" +
                $"Student: {_currentStudent.Name}\n" +
                $"Roll Number: {_currentStudent.RollNumber}\n" +
                $"Decision: {decision.FinalDecision}{overrideText}\n" +
                $"Confidence: {_matchScore}%\n" +
                $"Mode: {mode}\n" +
                $"Duration: {(DateTime.Now - _verificationStartTime).TotalSeconds:F1}s",
                "Verification Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            Logger.Info($"Verification saved for {_currentStudent.RollNumber}: {decision.FinalDecision} (Score: {_matchScore}, Override: {decision.IsManualOverride})");
            
            UpdateVerificationStatus("Result saved successfully", Colors.Green);
            _workflowState = VerificationWorkflowState.DecisionMade;
            
            // Auto-reset for next verification after short delay
            await Task.Delay(2000);
            ResetForNextVerification();
        }

        /// <summary>
        /// Handle save error
        /// </summary>
        private async Task HandleSaveErrorAsync(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                MessageBox.Show(
                    "Authentication expired.\n\nPlease restart the application.",
                    "Unauthorized",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                _isAuthenticated = false;
                UpdateVerificationStatus("Authentication expired", Colors.Red);
            }
            else
            {
                string errorMessage = await response.Content.ReadAsStringAsync();
                Logger.Error($"API error saving verification: {response.StatusCode} - {errorMessage}");
                
                MessageBox.Show(
                    $"Failed to save verification result:\n\n" +
                    $"Status: {response.StatusCode}\n" +
                    $"Error: {errorMessage}",
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                UpdateVerificationStatus("Save failed", Colors.Red);
            }
        }

        /// <summary>
        /// Reset for next verification
        /// </summary>
        private void ResetForNextVerification()
        {
            txtSearch.Text = "";
            txtLoadSuccess.Visibility = Visibility.Collapsed;

            panelStudentDetails.Visibility = Visibility.Collapsed;
            panelLiveVerification.Visibility = Visibility.Collapsed;
            panelMatchResult.Visibility = Visibility.Collapsed;

            _currentStudent = null;
            _registeredTemplate = null;
            _capturedFingerprintImage = null;
            _capturedTemplate = null;
            _isVerified = false;
            _matchScore = 0;
            _isMatched = false;
            _captureAttempts = 0;
            _verificationDecision = "";
            _manualOverrideReason = "";
            _workflowState = VerificationWorkflowState.SearchStudent;

            imgRegistrationPhoto.Source = null;
            imgTestPhoto.Source = null;
            imgSavedFingerprint.Source = null;
            imgLiveCapture.Source = null;

            txtNoPhoto.Visibility = Visibility.Visible;
            txtNoTestPhoto.Visibility = Visibility.Visible;
            txtNoFingerprint.Visibility = Visibility.Visible;
            txtNoLiveCapture.Visibility = Visibility.Visible;

            btnVerify.IsEnabled = false;
            btnSaveLog.IsEnabled = false;

            UpdateVerificationStatus("Ready for next verification", Colors.Gray);
            txtSearch.Focus();
        }

        #endregion

        #region Match Threshold Slider

        private void SliderMatchThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtMatchThresholdValue != null)
            {
                txtMatchThresholdValue.Text = $"{(int)e.NewValue}";
            }

            // Update service threshold in real-time
            if (_fingerprintService != null)
            {
                _fingerprintService.MatchThreshold = (int)e.NewValue;
            }
        }

        #endregion

        #region Image Zoom Functionality

        private void ImgRegistrationPhoto_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (imgRegistrationPhoto.Source != null)
            {
                ShowImageZoom(imgRegistrationPhoto.Source, "Registration Photo");
            }
        }

        private void ImgTestPhoto_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (imgTestPhoto.Source != null)
            {
                ShowImageZoom(imgTestPhoto.Source, "Test Photo");
            }
        }

        private void ImgSavedFingerprint_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (imgSavedFingerprint.Source != null)
            {
                ShowImageZoom(imgSavedFingerprint.Source, "Saved Fingerprint");
            }
        }

        private void ImgLiveCapture_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (imgLiveCapture.Source != null)
            {
                ShowImageZoom(imgLiveCapture.Source, "Live Capture");
            }
        }

        private void ShowImageZoom(ImageSource imageSource, string title)
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imageBorder = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(20, 20, 20, 10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(144, 164, 174),
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.2
                },
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Image
                    {
                        Source = imageSource,
                        Stretch = Stretch.None,
                        Margin = new Thickness(20)
                    }
                }
            };

            Grid.SetRow(imageBorder, 0);
            mainGrid.Children.Add(imageBorder);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var closeButton = new Button
            {
                Content = "✖ Close",
                Width = 150,
                Height = 50,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(211, 47, 47)),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            buttonPanel.Children.Add(closeButton);
            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            Window zoomWindow = new Window
            {
                Title = title,
                Width = 800,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                Content = mainGrid
            };

            closeButton.Click += (s, e) => zoomWindow.Close();

            zoomWindow.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    zoomWindow.Close();
                }
            };

            zoomWindow.ShowDialog();
        }

        #endregion

        #region Cleanup

        private void VerificationTab_Unloaded(object sender, RoutedEventArgs e)
        {
            _fingerprintService?.DisconnectAsync();
            _httpClient?.Dispose();
        }

        #endregion
    }
}