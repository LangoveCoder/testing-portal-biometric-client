// Views/VerificationTab.xaml.cs
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
    public partial class VerificationTab : UserControl
    {
        private readonly string _apiUrl = "http://localhost:8000/api";
        private readonly HttpClient _httpClient;
        private FingerprintService _fingerprintService;

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

        public VerificationTab()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _fingerprintService = new FingerprintService();

            Loaded += VerificationTab_Loaded;
            Unloaded += VerificationTab_Unloaded;
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
                btnLoadStudent.IsEnabled = false;
                txtLoadSuccess.Visibility = Visibility.Collapsed;

                var response = await _httpClient.GetAsync($"{_apiUrl}/biometric-verification/search?roll_number={rollNumber}");

                if (response.IsSuccessStatusCode)
                {
                    // SUCCESS: Fingerprint IS registered
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(json);

                    var student = ParseStudentFromJson(result);

                    _registeredTemplate = Convert.FromBase64String(
                        result.GetProperty("fingerprint_template").GetString()
                    );

                    byte[] registeredImage = null;
                    if (result.TryGetProperty("fingerprint_image", out var fpImg) &&
                        fpImg.ValueKind != JsonValueKind.Null &&
                        !string.IsNullOrEmpty(fpImg.GetString()))
                    {
                        registeredImage = Convert.FromBase64String(fpImg.GetString());
                    }

                    LoadStudent(student, registeredImage, true);
                    txtLoadSuccess.Visibility = Visibility.Visible;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    MessageBox.Show("Student not found with this roll number", "Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    // 422: Fingerprint NOT registered, but student exists
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(json);

                    var studentData = result.GetProperty("student");
                    var student = ParseStudentFromJson(studentData);

                    LoadStudent(student, null, false);
                    txtLoadSuccess.Visibility = Visibility.Visible;
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
                    MessageBox.Show($"Error searching student: {response.StatusCode}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnLoadStudent.IsEnabled = true;
            }
        }

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

        private void LoadStudent(Student student, byte[] registeredFingerprintImage, bool hasFingerprintRegistered)
        {
            _currentStudent = student;

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

            // Display registered fingerprint status
            if (hasFingerprintRegistered && registeredFingerprintImage != null && registeredFingerprintImage.Length > 0)
            {
                DisplayRegisteredFingerprint(registeredFingerprintImage);
                txtFingerprintStatus.Text = "✓ Registered";
                txtFingerprintStatus.Foreground = new SolidColorBrush(Color.FromRgb(67, 160, 71));
                btnVerify.IsEnabled = true;
            }
            else
            {
                imgSavedFingerprint.Source = null;
                txtNoFingerprint.Visibility = Visibility.Visible;
                txtNoFingerprint.Text = "Not registered";
                txtFingerprintStatus.Text = "✗ Not registered";
                txtFingerprintStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                btnVerify.IsEnabled = false;
            }

            // Reset verification state
            _isVerified = false;
            panelMatchResult.Visibility = Visibility.Collapsed;
            imgLiveCapture.Source = null;
            txtNoLiveCapture.Visibility = Visibility.Visible;
            btnSaveLog.IsEnabled = false;
        }

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

        #region Fingerprint Verification

        private void BtnVerify_Click(object sender, RoutedEventArgs e)
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

            if (_isDeviceInitialized)
            {
                VerifyWithRealDevice();
            }
            else
            {
                VerifyWithSimulation();
            }
        }

        private async void VerifyWithRealDevice()
        {
            try
            {
                btnVerify.IsEnabled = false;
                txtScannerStatus.Text = "Place finger now...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));

                // Update match threshold
                _fingerprintService.MatchThreshold = (int)sliderMatchThreshold.Value;

                // Capture fingerprint
                var captureResult = await _fingerprintService.CaptureAsync();

                if (!captureResult.Success)
                {
                    txtScannerStatus.Text = "Capture Failed";
                    txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));

                    string errorMsg = captureResult.FailureReason == CaptureFailureReason.Timeout
                        ? "Timeout - No finger detected.\n\nPlease place your finger on the scanner."
                        : $"Capture failed: {captureResult.Message}";

                    MessageBox.Show(errorMsg, "Capture Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Display captured fingerprint image
                if (captureResult.ImageData != null && captureResult.ImageData.Length > 0)
                {
                    DisplayCapturedFingerprint(
                        captureResult.ImageData,
                        captureResult.ImageWidth,
                        captureResult.ImageHeight
                    );
                }

                // Store captured template
                _capturedTemplate = captureResult.Template;
                _capturedFingerprintImage = captureResult.ImageData;

                // Perform matching
                var matchResult = await _fingerprintService.VerifyAsync(_registeredTemplate, _capturedTemplate);

                _isMatched = matchResult.IsMatch;
                _matchScore = matchResult.ConfidenceScore;

                DisplayMatchResult();

                txtScannerStatus.Text = _isMatched ? "Match Confirmed" : "No Match";
                txtScannerStatus.Foreground = _isMatched
                    ? new SolidColorBrush(Color.FromRgb(46, 125, 50))
                    : new SolidColorBrush(Color.FromRgb(211, 47, 47));

                _isVerified = true;
                btnSaveLog.IsEnabled = true;

                System.Diagnostics.Debug.WriteLine(
                    $"✓ Verification complete: {(_isMatched ? "MATCHED" : "NO MATCH")} " +
                    $"(Score: {_matchScore}, Threshold: {_fingerprintService.MatchThreshold})"
                );
            }
            catch (Exception ex)
            {
                txtScannerStatus.Text = "Verification Failed";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));

                MessageBox.Show($"Failed to verify fingerprint:\n\n{ex.Message}", "Verification Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"❌ Verification exception: {ex.Message}");
            }
            finally
            {
                btnVerify.IsEnabled = true;
            }
        }

        private void VerifyWithSimulation()
        {
            try
            {
                btnVerify.IsEnabled = false;
                txtScannerStatus.Text = "Simulating...";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));

                System.Threading.Thread.Sleep(1500);

                int width = 300;
                int height = 400;
                _capturedFingerprintImage = new byte[width * height];

                Random rand = new Random();
                for (int i = 0; i < _capturedFingerprintImage.Length; i++)
                {
                    _capturedFingerprintImage[i] = (byte)rand.Next(50, 200);
                }

                DisplayCapturedFingerprint(_capturedFingerprintImage, width, height);

                _capturedTemplate = new byte[400];
                rand.NextBytes(_capturedTemplate);

                int matchThreshold = (int)sliderMatchThreshold.Value;
                _matchScore = rand.Next(50, 150);
                _isMatched = _matchScore >= matchThreshold;

                DisplayMatchResult();

                txtScannerStatus.Text = "Simulation Complete";
                txtScannerStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));

                _isVerified = true;
                btnSaveLog.IsEnabled = true;

                MessageBox.Show(
                    $"⚠ SIMULATED VERIFICATION\n\n" +
                    $"Result: {(_isMatched ? "MATCHED" : "NOT MATCHED")}\n" +
                    $"Score: {_matchScore}\n" +
                    $"Threshold: {matchThreshold}\n\n" +
                    "This is NOT real biometric verification!",
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
                btnVerify.IsEnabled = true;
            }
        }

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

        #region Save Verification Log

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

            string mode = _isDeviceInitialized ? "REAL" : "SIMULATED";
            string result = _isMatched ? "MATCHED" : "REJECTED";

            var confirmResult = MessageBox.Show(
                $"Save verification log?\n\n" +
                $"Student: {_currentStudent.Name}\n" +
                $"Roll Number: {_currentStudent.RollNumber}\n" +
                $"Result: {result}\n" +
                $"Score: {_matchScore}\n" +
                $"Mode: {mode}",
                "Confirm Save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                btnSaveLog.IsEnabled = false;

                var data = new
                {
                    roll_number = _currentStudent.RollNumber,
                    is_matched = _isMatched,
                    match_score = _matchScore,
                    captured_template = Convert.ToBase64String(_capturedTemplate),
                    captured_image = _capturedFingerprintImage != null ? Convert.ToBase64String(_capturedFingerprintImage) : null,
                    capture_quality = 50,
                    verified_by = "College Admin",
                    device_info = _isDeviceInitialized
                        ? _fingerprintService.GetCurrentScannerInfo()?.Model ?? "SecuGen Scanner"
                        : "Simulation",
                    failure_reason = !_isMatched ? $"Score {_matchScore} below threshold {(int)sliderMatchThreshold.Value}" : null,
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/biometric-verification/verify", content);

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"✓ Verification log saved successfully!\n\n" +
                        $"Student: {_currentStudent.Name}\n" +
                        $"Result: {result}\n" +
                        $"Score: {_matchScore}\n" +
                        $"Mode: {mode}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    ResetVerification();
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
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"Failed to save verification log:\n\n{errorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving verification log:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSaveLog.IsEnabled = true;
            }
        }

        private void ResetVerification()
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