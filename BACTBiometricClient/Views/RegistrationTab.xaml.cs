// Views/RegistrationTab.xaml.cs
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BACTBiometricClient.Models;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Views
{
    public partial class RegistrationTab : UserControl
    {
        private readonly string _apiUrl = "http://localhost:8000/api";
        private readonly HttpClient _httpClient;
        private FingerprintService _fingerprintService;

        private Student _currentStudent;
        private byte[] _capturedFingerprintImage;
        private byte[] _fingerprintTemplate;

        private bool _isDeviceInitialized = false;
        private bool _hasExistingFingerprint = false;
        private string _authToken = null;
        private bool _isAuthenticated = false;

        public RegistrationTab()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _fingerprintService = new FingerprintService();

            Loaded += RegistrationTab_Loaded;
            Unloaded += RegistrationTab_Unloaded;
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

        #region Search Student

        private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                BtnSearch_Click(sender, e);
            }
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string rollNumber = txtSearch.Text.Trim();

            if (string.IsNullOrEmpty(rollNumber))
            {
                MessageBox.Show("Please enter a roll number", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (rollNumber.Length != 5)
            {
                MessageBox.Show("Roll number must be exactly 5 digits", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btnSearch.IsEnabled = false;
                panelSearchSuccess.Visibility = Visibility.Collapsed;

                // Create request with auth token
                var searchData = new { search_term = rollNumber };
                var json = JsonSerializer.Serialize(searchData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"=== SEARCH REQUEST ===");
                System.Diagnostics.Debug.WriteLine($"URL: {_apiUrl}/biometric-operator/registration/search-student");
                System.Diagnostics.Debug.WriteLine($"Body: {json}");
                System.Diagnostics.Debug.WriteLine($"Auth: {(_isAuthenticated ? "Yes" : "No")}");

                var response = await _httpClient.PostAsync(
                    $"{_apiUrl}/biometric-operator/registration/search-student",
                    content
                );

                var responseJson = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"=== SEARCH RESPONSE ===");
                System.Diagnostics.Debug.WriteLine($"Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Body: {responseJson}");

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

                        // Try to extract student data
                        JsonElement studentData = default;
                        bool foundStudent = false;

                        if (result.TryGetProperty("data", out var dataProperty))
                        {
                            studentData = dataProperty;
                            foundStudent = true;
                            System.Diagnostics.Debug.WriteLine("Found student in 'data' property");
                        }
                        else if (result.TryGetProperty("student", out var studentProperty))
                        {
                            studentData = studentProperty;
                            foundStudent = true;
                            System.Diagnostics.Debug.WriteLine("Found student in 'student' property");
                        }
                        else if (result.TryGetProperty("roll_number", out _))
                        {
                            studentData = result;
                            foundStudent = true;
                            System.Diagnostics.Debug.WriteLine("Found student at root level");
                        }

                        if (!foundStudent)
                        {
                            MessageBox.Show(
                                $"Unexpected API response structure.\n\nResponse:\n{responseJson}",
                                "Parse Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            return;
                        }

                        var student = ParseStudentFromJson(studentData);

                        // Check if fingerprint already registered
                        bool hasFingerprint = GetBoolProperty(studentData, "fingerprint_registered");

                        DateTime? registeredDate = null;
                        if (studentData.TryGetProperty("fingerprint_registered_at", out var fpDate)
                            && fpDate.ValueKind != JsonValueKind.Null)
                        {
                            string dateStr = fpDate.GetString();
                            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out DateTime parsedDate))
                            {
                                registeredDate = parsedDate;
                            }
                        }

                        LoadStudent(student, hasFingerprint, registeredDate);
                        panelSearchSuccess.Visibility = Visibility.Visible;
                    }
                    catch (JsonException jsonEx)
                    {
                        MessageBox.Show(
                            $"Failed to parse JSON response:\n\n{jsonEx.Message}\n\nResponse:\n{responseJson}",
                            "Parse Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    MessageBox.Show("Student not found with this roll number", "Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    ClearStudentInfo();
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    MessageBox.Show(
                        "Authentication required.\n\nPlease restart the application.",
                        "Unauthorized",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    _isAuthenticated = false;
                }
                else
                {
                    try
                    {
                        var errorResult = JsonSerializer.Deserialize<JsonElement>(responseJson);
                        string errorMessage = errorResult.TryGetProperty("message", out var msg)
                            ? msg.GetString()
                            : responseJson;

                        MessageBox.Show(
                            $"Error searching student:\n\n{errorMessage}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch
                    {
                        MessageBox.Show(
                            $"Error searching student:\n\nStatus: {response.StatusCode}\n\n{responseJson}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    ClearStudentInfo();
                }
            }
            catch (HttpRequestException httpEx)
            {
                MessageBox.Show(
                    $"Cannot connect to server:\n\n{httpEx.Message}\n\n" +
                    "Please ensure Laravel server is running:\n" +
                    "php artisan serve",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unexpected Error:\n\n{ex.GetType().Name}\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnSearch.IsEnabled = true;
            }
        }

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

        private void LoadStudent(Student student, bool hasFingerprint, DateTime? registeredDate)
        {
            _currentStudent = student;

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

            if (hasFingerprint)
            {
                panelFingerprintSuccess.Visibility = Visibility.Visible;
                panelFingerprintWarning.Visibility = Visibility.Collapsed;
                panelFingerprintCapture.Visibility = Visibility.Collapsed;

                if (registeredDate.HasValue)
                {
                    txtFingerprintDate.Text = $"Registered on: {registeredDate.Value:MMMM dd, yyyy 'at' hh:mm tt}";
                }
                else
                {
                    txtFingerprintDate.Text = "Already registered";
                }

                btnCapture.IsEnabled = false;
                btnSave.IsEnabled = false;
            }
            else
            {
                panelFingerprintSuccess.Visibility = Visibility.Collapsed;
                panelFingerprintWarning.Visibility = Visibility.Visible;
                panelFingerprintCapture.Visibility = Visibility.Visible;

                btnCapture.IsEnabled = true;
                btnSave.IsEnabled = false;
            }
        }

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

            btnCapture.IsEnabled = false;
            btnSave.IsEnabled = false;
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

            if (_isDeviceInitialized)
            {
                CaptureRealFingerprint();
            }
            else
            {
                CaptureSimulatedFingerprint();
            }
        }

        private async void CaptureRealFingerprint()
        {
            try
            {
                btnCapture.IsEnabled = false;
                txtScannerStatus.Text = "Place finger now...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 140, 0));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                _fingerprintService.MinimumQualityScore = (int)sliderQuality.Value;

                var captureResult = await _fingerprintService.CaptureAsync();

                if (!captureResult.Success)
                {
                    txtScannerStatus.Text = "Capture Failed";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                    scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));

                    string errorMsg = captureResult.FailureReason == CaptureFailureReason.Timeout
                        ? "Timeout - No finger detected.\n\nPlease place your finger firmly on the scanner."
                        : captureResult.FailureReason == CaptureFailureReason.PoorQuality
                        ? $"Poor quality fingerprint (Score: {captureResult.QualityScore}).\n\nPlease try again with a cleaner finger."
                        : $"Capture failed: {captureResult.Message}";

                    MessageBox.Show(errorMsg, "Capture Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _fingerprintTemplate = captureResult.Template;
                _capturedFingerprintImage = captureResult.ImageData;

                if (captureResult.ImageData != null && captureResult.ImageData.Length > 0)
                {
                    DisplayFingerprintImage(
                        captureResult.ImageData,
                        captureResult.ImageWidth,
                        captureResult.ImageHeight
                    );
                }

                txtScannerStatus.Text = "Capture Successful";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(102, 187, 106));

                txtQualityScore.Text = $"✓ Quality: {captureResult.QualityScore}%";
                txtQualityScore.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                txtQualityScore.Visibility = Visibility.Visible;

                btnSave.IsEnabled = true;

                System.Diagnostics.Debug.WriteLine(
                    $"✓ Fingerprint captured: Quality={captureResult.QualityScore}%, " +
                    $"Size={captureResult.ImageWidth}x{captureResult.ImageHeight}"
                );
            }
            catch (Exception ex)
            {
                txtScannerStatus.Text = "Capture Failed";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                scannerIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));

                MessageBox.Show($"Failed to capture fingerprint:\n\n{ex.Message}", "Capture Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"❌ Capture exception: {ex.Message}");
            }
            finally
            {
                btnCapture.IsEnabled = true;
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

            string mode = _isDeviceInitialized ? "REAL" : "SIMULATED";

            var confirmResult = MessageBox.Show(
                $"Save {mode} fingerprint to database?\n\n" +
                $"Student: {_currentStudent.Name}\n" +
                $"Roll Number: {_currentStudent.RollNumber}\n" +
                $"Mode: {mode}",
                "Confirm Save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                btnSave.IsEnabled = false;

                int quality = _fingerprintService.MinimumQualityScore;

                var data = new
                {
                    student_id = _currentStudent.Id,
                    fingerprint_template = Convert.ToBase64String(_fingerprintTemplate),
                    fingerprint_image = _capturedFingerprintImage != null
                        ? Convert.ToBase64String(_capturedFingerprintImage)
                        : null,
                    quality = quality
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine("=== SAVE FINGERPRINT REQUEST ===");
                System.Diagnostics.Debug.WriteLine($"URL: {_apiUrl}/biometric-operator/registration/save-fingerprint");
                System.Diagnostics.Debug.WriteLine($"Student ID: {_currentStudent.Id}");
                System.Diagnostics.Debug.WriteLine($"Roll Number: {_currentStudent.RollNumber}");
                System.Diagnostics.Debug.WriteLine($"Template Length: {_fingerprintTemplate.Length} bytes");
                System.Diagnostics.Debug.WriteLine($"Image Length: {_capturedFingerprintImage?.Length ?? 0} bytes");
                System.Diagnostics.Debug.WriteLine($"Quality: {quality}");
                System.Diagnostics.Debug.WriteLine($"Auth Token: {(_authToken != null ? "Present" : "Missing")}");

                var response = await _httpClient.PostAsync(
                    $"{_apiUrl}/biometric-operator/registration/save-fingerprint",
                    content
                );

                var responseJson = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine("=== SAVE FINGERPRINT RESPONSE ===");
                System.Diagnostics.Debug.WriteLine($"Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
                System.Diagnostics.Debug.WriteLine($"Response (first 500 chars): {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");

                // Check if response is HTML (means auth failed or route issue)
                if (responseJson.TrimStart().StartsWith("<!DOCTYPE") || responseJson.TrimStart().StartsWith("<html"))
                {
                    MessageBox.Show(
                        "API Error: Endpoint returned HTML instead of JSON.\n\n" +
                        "Possible causes:\n" +
                        "• Authentication token expired\n" +
                        "• Route requires different permissions\n" +
                        "• Server redirecting to login page\n\n" +
                        "Please restart the application.",
                        "Endpoint Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    _isAuthenticated = false;
                    return;
                }

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"✓ Fingerprint registered successfully!\n\n" +
                        $"Roll Number: {_currentStudent.RollNumber}\n" +
                        $"Name: {_currentStudent.Name}\n" +
                        $"Quality: {quality}%\n" +
                        $"Mode: {mode}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    ClearStudentInfo();
                    txtSearch.Text = "";
                    txtSearch.Focus();
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    MessageBox.Show(
                        "Authentication expired.\n\nPlease restart the application.",
                        "Unauthorized",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    _isAuthenticated = false;
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to save fingerprint:\n\n" +
                        $"Status: {response.StatusCode}\n" +
                        $"Message: {responseJson}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving fingerprint:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSave.IsEnabled = true;
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