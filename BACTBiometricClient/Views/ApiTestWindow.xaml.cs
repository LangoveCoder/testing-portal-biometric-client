using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Microsoft.Win32;
using System.IO;

namespace BACTBiometricClient.Views
{
    public partial class ApiTestWindow : Window
    {
        private readonly HttpClient _httpClient;
        private string _currentToken = "";
        private string _apiBaseUrl = "http://192.168.100.89:8000/api/v1";

        public ApiTestWindow(string authToken = null)
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            // Set token if provided from login
            if (!string.IsNullOrEmpty(authToken))
            {
                SetToken(authToken);
                AddResponse("✅ Authenticated user session active - ready to test API endpoints!");
            }
            else
            {
                AddResponse("⚠️ No authentication token provided. Some API calls may fail.");
            }
        }

        private void UpdateUrl_Click(object sender, RoutedEventArgs e)
        {
            _apiBaseUrl = ApiBaseUrlInput.Text.Trim();
            AddResponse($"✅ API Base URL updated to: {_apiBaseUrl}");
        }

        private void SetToken(string token)
        {
            _currentToken = token;
            TokenStatus.Text = string.IsNullOrEmpty(token) ? "❌ No Token" : "✅ Token Active";
            TokenStatus.Foreground = string.IsNullOrEmpty(token) ? 
                System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Green;
        }

        private void AddResponse(string response)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var newContent = $"[{timestamp}] {response}\n" + new string('─', 50) + "\n";
            ResponseTextBox.Text = newContent + ResponseTextBox.Text;
        }

        private void ClearResponse_Click(object sender, RoutedEventArgs e)
        {
            ResponseTextBox.Text = "🚀 API Test Console Ready! Configure your API base URL and start testing...";
        }

        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"api_test_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveDialog.FileName, ResponseTextBox.Text);
                AddResponse($"✅ Log saved to: {saveDialog.FileName}");
            }
        }



        // Operator APIs
        private async void GetTests_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                var url = "/operator/tests/available";
                if (!string.IsNullOrWhiteSpace(CollegeIdInput.Text))
                {
                    url += $"?college_id={CollegeIdInput.Text.Trim()}";
                }
                return await GetAsync(url);
            }, "📋 Get Available Tests");
        }

        private async void GetColleges_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                return await GetAsync("/operator/colleges");
            }, "🏛️ Get Colleges");
        }

        private async void SearchStudent_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                var searchData = new
                {
                    roll_number = RollNumberInput.Text.Trim(),
                    test_id = string.IsNullOrWhiteSpace(TestIdInput.Text) ? (int?)null : int.Parse(TestIdInput.Text.Trim())
                };

                return await PostJsonAsync("/operator/student/search", searchData);
            }, "🔍 Search Student");
        }
        private async void SaveFingerprint_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                // Generate realistic fingerprint template data (ISO 19794-2 format simulation)
                var templateData = new byte[1024]; // Larger template size for better compatibility
                
                // Fill with realistic-looking data
                var random = new Random();
                random.NextBytes(templateData);
                
                // Add ISO 19794-2 FMR header structure
                templateData[0] = 0x46; // 'F' - Format identifier
                templateData[1] = 0x4D; // 'M' - Format identifier  
                templateData[2] = 0x52; // 'R' - Format identifier
                templateData[3] = 0x20; // ' ' - Format identifier
                templateData[4] = 0x32; // '2' - Version 2.0
                templateData[5] = 0x30; // '0' - Version 2.0
                templateData[6] = 0x00; // Reserved
                templateData[7] = 0x00; // Reserved
                
                // Record length (4 bytes, big endian)
                var recordLength = templateData.Length;
                templateData[8] = (byte)((recordLength >> 24) & 0xFF);
                templateData[9] = (byte)((recordLength >> 16) & 0xFF);
                templateData[10] = (byte)((recordLength >> 8) & 0xFF);
                templateData[11] = (byte)(recordLength & 0xFF);
                
                // Add some realistic minutiae data structure
                templateData[12] = 0x01; // Number of finger views
                templateData[13] = 0x00; // Reserved
                templateData[14] = 0x01; // Finger position (right thumb)
                templateData[15] = 0x00; // View number
                
                // Image quality (1 byte)
                templateData[16] = 0x55; // Quality score (85 in hex)
                
                // Number of minutiae (1 byte) - simulate 20-40 minutiae points
                var minutiaeCount = 25 + (random.Next() % 15);
                templateData[17] = (byte)minutiaeCount;
                
                // Fill remaining with structured minutiae data (each minutiae = 6 bytes)
                for (int i = 0; i < minutiaeCount && (18 + i * 6 + 5) < templateData.Length; i++)
                {
                    var baseIndex = 18 + i * 6;
                    // X coordinate (2 bytes)
                    templateData[baseIndex] = (byte)(random.Next(256));
                    templateData[baseIndex + 1] = (byte)(random.Next(256));
                    // Y coordinate (2 bytes)
                    templateData[baseIndex + 2] = (byte)(random.Next(256));
                    templateData[baseIndex + 3] = (byte)(random.Next(256));
                    // Angle (1 byte)
                    templateData[baseIndex + 4] = (byte)(random.Next(256));
                    // Type (1 byte)
                    templateData[baseIndex + 5] = (byte)(random.Next(3)); // 0=ending, 1=bifurcation, 2=other
                }
                
                // Create a simple 100x100 pixel blank BMP image (grayscale)
                var imageSize = 100 * 100; // 100x100 pixels
                var paletteSize = 256 * 4; // 256 colors * 4 bytes per color (BGRA)
                var headerSize = 54; // BMP header size
                var totalSize = headerSize + paletteSize + imageSize;
                var imageData = new byte[totalSize];
                
                // BMP File Header (14 bytes)
                imageData[0] = 0x42; // 'B'
                imageData[1] = 0x4D; // 'M'
                imageData[2] = (byte)(totalSize & 0xFF); // File size (low byte)
                imageData[3] = (byte)((totalSize >> 8) & 0xFF);
                imageData[4] = (byte)((totalSize >> 16) & 0xFF);
                imageData[5] = (byte)((totalSize >> 24) & 0xFF);
                imageData[6] = 0x00; // Reserved
                imageData[7] = 0x00;
                imageData[8] = 0x00; // Reserved
                imageData[9] = 0x00;
                imageData[10] = (byte)((headerSize + paletteSize) & 0xFF); // Offset to pixel data
                imageData[11] = (byte)(((headerSize + paletteSize) >> 8) & 0xFF);
                imageData[12] = (byte)(((headerSize + paletteSize) >> 16) & 0xFF);
                imageData[13] = (byte)(((headerSize + paletteSize) >> 24) & 0xFF);
                
                // BMP Info Header (40 bytes)
                imageData[14] = 0x28; // Header size (40 bytes)
                imageData[15] = 0x00;
                imageData[16] = 0x00;
                imageData[17] = 0x00;
                imageData[18] = 0x64; // Width (100 pixels)
                imageData[19] = 0x00;
                imageData[20] = 0x00;
                imageData[21] = 0x00;
                imageData[22] = 0x64; // Height (100 pixels)
                imageData[23] = 0x00;
                imageData[24] = 0x00;
                imageData[25] = 0x00;
                imageData[26] = 0x01; // Planes (1)
                imageData[27] = 0x00;
                imageData[28] = 0x08; // Bits per pixel (8-bit grayscale)
                imageData[29] = 0x00;
                // Rest of header is zeros (compression, image size, etc.)
                
                // Create grayscale palette (256 colors)
                for (int i = 0; i < 256; i++)
                {
                    var paletteIndex = headerSize + i * 4;
                    imageData[paletteIndex] = (byte)i;     // Blue
                    imageData[paletteIndex + 1] = (byte)i; // Green
                    imageData[paletteIndex + 2] = (byte)i; // Red
                    imageData[paletteIndex + 3] = 0x00;    // Reserved
                }
                
                // Fill pixel data with random grayscale fingerprint-like pattern
                var pixelDataStart = headerSize + paletteSize;
                for (int i = 0; i < imageSize; i++)
                {
                    imageData[pixelDataStart + i] = (byte)(random.Next(256));
                }
                
                var testTemplate = Convert.ToBase64String(templateData);
                var testImage = Convert.ToBase64String(imageData);
                
                var fingerprintData = new
                {
                    roll_number = RollNumberInput.Text.Trim(),
                    fingerprint_template = testTemplate,
                    fingerprint_image = testImage,
                    quality_score = 85,
                    captured_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                return await PostJsonAsync("/operator/fingerprint/save", fingerprintData);
            }, "💾 Save Fingerprint");
        }

        // Admin APIs
        private async void LoadStudent_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                var loadData = new
                {
                    roll_number = LoadRollNumberInput.Text.Trim()
                };

                return await PostJsonAsync("/admin/student/load", loadData);
            }, "📋 Load Student for Verification");
        }

        private async void SaveVerification_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                var verificationData = new
                {
                    roll_number = VerifyRollNumberInput.Text.Trim(),
                    match_result = ((ComboBoxItem)MatchResultComboBox.SelectedItem).Content.ToString(),
                    confidence_score = 92.5,
                    entry_allowed = ((ComboBoxItem)MatchResultComboBox.SelectedItem).Content.ToString() == "match",
                    verified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    remarks = "Test verification from API console"
                };

                return await PostJsonAsync("/admin/verification/save", verificationData);
            }, "✅ Save Verification");
        }

        private async void GetStudents_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                return await GetAsync("/admin/students?test_id=1&status=all&page=1");
            }, "👥 Get Students");
        }

        private async void GetStats_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                return await GetAsync("/admin/stats");
            }, "📊 Get Statistics");
        }
        // Sync APIs
        private async void BulkSyncRegistrations_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                // Generate realistic fingerprint template data
                var generateTemplate = () => {
                    var templateData = new byte[1024]; // Larger template size
                    var random = new Random();
                    random.NextBytes(templateData);
                    
                    // Add ISO 19794-2 FMR header structure
                    templateData[0] = 0x46; // 'F'
                    templateData[1] = 0x4D; // 'M'
                    templateData[2] = 0x52; // 'R'
                    templateData[3] = 0x20; // ' '
                    templateData[4] = 0x32; // '2'
                    templateData[5] = 0x30; // '0'
                    templateData[6] = 0x00; // Reserved
                    templateData[7] = 0x00; // Reserved
                    
                    // Record length (4 bytes, big endian)
                    var recordLength = templateData.Length;
                    templateData[8] = (byte)((recordLength >> 24) & 0xFF);
                    templateData[9] = (byte)((recordLength >> 16) & 0xFF);
                    templateData[10] = (byte)((recordLength >> 8) & 0xFF);
                    templateData[11] = (byte)(recordLength & 0xFF);
                    
                    // Add minutiae structure
                    templateData[12] = 0x01; // Number of finger views
                    templateData[13] = 0x00; // Reserved
                    templateData[14] = 0x01; // Finger position
                    templateData[15] = 0x00; // View number
                    templateData[16] = 0x55; // Quality score
                    templateData[17] = 0x1A; // Number of minutiae (26)
                    
                    return Convert.ToBase64String(templateData);
                };

                var syncData = new
                {
                    registrations = new[]
                    {
                        new
                        {
                            roll_number = "00001",
                            fingerprint_template = generateTemplate(),
                            quality_score = 85,
                            captured_at = DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        },
                        new
                        {
                            roll_number = "00002",
                            fingerprint_template = generateTemplate(),
                            quality_score = 78,
                            captured_at = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        }
                    }
                };

                return await PostJsonAsync("/sync/registrations", syncData);
            }, "📤 Bulk Sync Registrations");
        }

        private async void BulkSyncVerifications_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                var syncData = new
                {
                    verifications = new[]
                    {
                        new
                        {
                            roll_number = "00001",
                            match_result = "match",
                            confidence_score = 92.5,
                            entry_allowed = true,
                            verified_at = DateTime.UtcNow.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        },
                        new
                        {
                            roll_number = "00002",
                            match_result = "no_match",
                            confidence_score = 25.0,
                            entry_allowed = false,
                            verified_at = DateTime.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        }
                    }
                };

                return await PostJsonAsync("/sync/verifications", syncData);
            }, "📤 Bulk Sync Verifications");
        }

        private async void GetSyncStatus_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteApiCall(async () =>
            {
                return await GetAsync("/sync/status");
            }, "📊 Get Sync Status");
        }
        private async void GenerateTestData_Click(object sender, RoutedEventArgs e)
        {
            AddResponse("🎲 Generating test data...");
            
            // Generate realistic fingerprint template data (ISO 19794-2 format simulation)
            var templateData = new byte[1024]; // Larger template size
            var imageData = new byte[2048];    // Larger image size
            var random = new Random();
            random.NextBytes(templateData);
            random.NextBytes(imageData);
            
            // Add ISO 19794-2 FMR header structure
            templateData[0] = 0x46; // 'F'
            templateData[1] = 0x4D; // 'M'
            templateData[2] = 0x52; // 'R'
            templateData[3] = 0x20; // ' '
            templateData[4] = 0x32; // '2'
            templateData[5] = 0x30; // '0'
            templateData[6] = 0x00; // Reserved
            templateData[7] = 0x00; // Reserved
            
            // Record length (4 bytes, big endian)
            var recordLength = templateData.Length;
            templateData[8] = (byte)((recordLength >> 24) & 0xFF);
            templateData[9] = (byte)((recordLength >> 16) & 0xFF);
            templateData[10] = (byte)((recordLength >> 8) & 0xFF);
            templateData[11] = (byte)(recordLength & 0xFF);
            
            // Add minutiae structure
            templateData[12] = 0x01; // Number of finger views
            templateData[13] = 0x00; // Reserved
            templateData[14] = 0x01; // Finger position (right thumb)
            templateData[15] = 0x00; // View number
            templateData[16] = 0x55; // Quality score (85 in hex)
            templateData[17] = 0x1A; // Number of minutiae (26)
            
            // Generate JPEG-like header for image data
            imageData[0] = 0xFF; // JPEG SOI marker
            imageData[1] = 0xD8; // JPEG SOI marker
            imageData[2] = 0xFF; // JPEG marker
            imageData[3] = 0xE0; // JFIF marker
            
            var sampleTemplate = Convert.ToBase64String(templateData);
            var sampleImage = Convert.ToBase64String(imageData);
            
            AddResponse("Sample Registration Data:");
            AddResponse(JsonConvert.SerializeObject(new
            {
                roll_number = "00001",
                fingerprint_template = sampleTemplate.Substring(0, 50) + "...(1024 bytes total)",
                fingerprint_image = sampleImage.Substring(0, 50) + "...(2048 bytes total)",
                quality_score = 85,
                captured_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, Formatting.Indented));
            
            AddResponse("Sample Verification Data:");
            AddResponse(JsonConvert.SerializeObject(new
            {
                roll_number = "00001",
                match_result = "match",
                confidence_score = 92.5,
                entry_allowed = true,
                verified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                remarks = "Verified successfully - Entry granted"
            }, Formatting.Indented));
        }

        // Helper Methods
        private async Task ExecuteApiCall(Func<Task<HttpResponseMessage>> apiCall, string operation)
        {
            try
            {
                AddResponse($"🔄 {operation}");
                
                if (string.IsNullOrEmpty(_currentToken) && !operation.Contains("Login"))
                {
                    AddResponse("❌ No token available. Please login first.");
                    return;
                }

                var response = await apiCall();
                var responseText = await response.Content.ReadAsStringAsync();
                
                var statusIcon = response.IsSuccessStatusCode ? "✅" : "❌";
                AddResponse($"{statusIcon} Response ({(int)response.StatusCode} {response.StatusCode}):");
                
                try
                {
                    var formattedJson = JsonConvert.DeserializeObject(responseText);
                    AddResponse(JsonConvert.SerializeObject(formattedJson, Formatting.Indented));
                }
                catch
                {
                    AddResponse(responseText);
                }
            }
            catch (Exception ex)
            {
                AddResponse($"❌ Error in {operation}: {ex.Message}");
            }
        }

        private async Task<HttpResponseMessage> GetAsync(string endpoint)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(_currentToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_currentToken}");
                AddResponse($"🔑 Using token: {_currentToken.Substring(0, Math.Min(20, _currentToken.Length))}...");
            }
            else
            {
                AddResponse("⚠️ No token available for this request");
            }

            var fullUrl = $"{_apiBaseUrl}{endpoint}";
            AddResponse($"📡 GET {fullUrl}");
            return await _httpClient.GetAsync(fullUrl);
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string endpoint, object data)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            
            // Only add Authorization header if we have a token AND it's not the login endpoint
            if (!string.IsNullOrEmpty(_currentToken) && endpoint != "/auth/login")
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_currentToken}");
                AddResponse($"🔑 Using token: {_currentToken.Substring(0, Math.Min(20, _currentToken.Length))}...");
            }
            else if (endpoint != "/auth/login")
            {
                AddResponse("⚠️ No token available for this request");
            }

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var fullUrl = $"{_apiBaseUrl}{endpoint}";
            AddResponse($"📡 POST {fullUrl}");
            AddResponse($"📤 Request Body:\n{json}");
            
            return await _httpClient.PostAsync(fullUrl, content);
        }

        protected override void OnClosed(EventArgs e)
        {
            _httpClient?.Dispose();
            base.OnClosed(e);
        }
    }
}