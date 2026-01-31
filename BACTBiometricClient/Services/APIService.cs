using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Enhanced API service with robust error handling, retry logic, and network status detection
    /// Handles all API communication with BACT Laravel backend
    /// </summary>
    public class ApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _authToken;
        private bool _isOnline;
        private readonly Timer _networkCheckTimer;
        private readonly SemaphoreSlim _networkCheckSemaphore;

        // Retry configuration
        private const int MaxRetryAttempts = 3;
        private const int BaseDelayMs = 1000; // 1 second base delay
        private const int MaxDelayMs = 30000; // 30 seconds max delay
        private const int NetworkCheckIntervalMs = 5000; // Check network every 5 seconds

        // Events
        public event EventHandler<NetworkStatusChangedEventArgs> NetworkStatusChanged;

        public bool IsOnline => _isOnline;

        public ApiService(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _networkCheckSemaphore = new SemaphoreSlim(1, 1);
            
            // Configure HttpClient with proper timeouts and headers
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Set default headers
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BACTBiometricClient", "1.0"));

            // Initialize network status
            _ = CheckNetworkStatusAsync();

            // Start network monitoring timer
            _networkCheckTimer = new Timer(async _ => await CheckNetworkStatusAsync(), 
                null, NetworkCheckIntervalMs, NetworkCheckIntervalMs);

            Logger.Info("Enhanced ApiService initialized");
        }

        /// <summary>
        /// Set authentication token for API requests
        /// </summary>
        public void SetAuthToken(string token)
        {
            _authToken = token;
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                Logger.Info("Authentication token updated");
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
                Logger.Info("Authentication token cleared");
            }
        }

        /// <summary>
        /// Check network connectivity status
        /// </summary>
        private async Task CheckNetworkStatusAsync()
        {
            if (!await _networkCheckSemaphore.WaitAsync(100))
                return; // Skip if already checking

            try
            {
                bool wasOnline = _isOnline;
                _isOnline = await InternetChecker.IsConnectedAsync();

                if (wasOnline != _isOnline)
                {
                    Logger.Info($"Network status changed: {(wasOnline ? "Online" : "Offline")} -> {(_isOnline ? "Online" : "Offline")}");
                    NetworkStatusChanged?.Invoke(this, new NetworkStatusChangedEventArgs(_isOnline, wasOnline));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Network status check failed", ex);
                _isOnline = false;
            }
            finally
            {
                _networkCheckSemaphore.Release();
            }
        }

        /// <summary>
        /// Execute HTTP request with retry logic and exponential backoff
        /// </summary>
        private async Task<ApiResponse<T>> ExecuteWithRetryAsync<T>(
            Func<Task<HttpResponseMessage>> httpCall,
            string operationName,
            CancellationToken cancellationToken = default)
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    Logger.Info($"{operationName} - Attempt {attempt}/{MaxRetryAttempts}");

                    // Check if we're online before making the request
                    if (!_isOnline)
                    {
                        await CheckNetworkStatusAsync();
                        if (!_isOnline)
                        {
                            return new ApiResponse<T>
                            {
                                Success = false,
                                Message = "No internet connection available",
                                IsNetworkError = true
                            };
                        }
                    }

                    var response = await httpCall();
                    var responseContent = await response.Content.ReadAsStringAsync();

                    Logger.Info($"{operationName} - Response: {response.StatusCode}");

                    // Handle successful responses
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var result = JsonConvert.DeserializeObject<T>(responseContent);
                            return new ApiResponse<T>
                            {
                                Success = true,
                                Data = result,
                                StatusCode = response.StatusCode,
                                RawResponse = responseContent
                            };
                        }
                        catch (JsonException jsonEx)
                        {
                            Logger.Error($"{operationName} - JSON parsing failed", jsonEx);
                            return new ApiResponse<T>
                            {
                                Success = false,
                                Message = "Invalid response format from server",
                                StatusCode = response.StatusCode,
                                RawResponse = responseContent
                            };
                        }
                    }

                    // Handle HTTP error responses
                    var errorResponse = await HandleHttpErrorAsync<T>(response, responseContent, operationName);
                    
                    // Don't retry on authentication errors or client errors (4xx)
                    if (response.StatusCode == HttpStatusCode.Unauthorized || 
                        response.StatusCode == HttpStatusCode.Forbidden ||
                        ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500))
                    {
                        return errorResponse;
                    }

                    // For server errors (5xx) or network errors, continue with retry logic
                    if (attempt == MaxRetryAttempts)
                    {
                        return errorResponse;
                    }

                    // Calculate exponential backoff delay
                    var delay = CalculateRetryDelay(attempt);
                    Logger.Warning($"{operationName} - Retrying in {delay}ms (attempt {attempt + 1}/{MaxRetryAttempts})");
                    await Task.Delay(delay, cancellationToken);
                }
                catch (HttpRequestException httpEx)
                {
                    lastException = httpEx;
                    Logger.Warning($"{operationName} - HTTP request failed (attempt {attempt}): {httpEx.Message}");
                    
                    // Mark as offline and continue retry logic
                    _isOnline = false;
                    
                    if (attempt == MaxRetryAttempts)
                        break;

                    var delay = CalculateRetryDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
                {
                    lastException = tcEx;
                    Logger.Warning($"{operationName} - Request timeout (attempt {attempt})");
                    
                    if (attempt == MaxRetryAttempts)
                        break;

                    var delay = CalculateRetryDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Logger.Error($"{operationName} - Unexpected error (attempt {attempt})", ex);
                    
                    if (attempt == MaxRetryAttempts)
                        break;

                    var delay = CalculateRetryDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            // All retries failed
            Logger.Error($"{operationName} - All retry attempts failed");
            return new ApiResponse<T>
            {
                Success = false,
                Message = $"Request failed after {MaxRetryAttempts} attempts: {lastException?.Message ?? "Unknown error"}",
                IsNetworkError = true,
                Exception = lastException
            };
        }

        /// <summary>
        /// Handle HTTP error responses
        /// </summary>
        private async Task<ApiResponse<T>> HandleHttpErrorAsync<T>(
            HttpResponseMessage response, 
            string responseContent, 
            string operationName)
        {
            Logger.Warning($"{operationName} - HTTP Error {response.StatusCode}: {responseContent}");

            string errorMessage = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Authentication failed. Please login again.",
                HttpStatusCode.Forbidden => "Access denied. Insufficient permissions.",
                HttpStatusCode.NotFound => "Requested resource not found.",
                HttpStatusCode.BadRequest => "Invalid request format.",
                HttpStatusCode.InternalServerError => "Server error. Please try again later.",
                HttpStatusCode.ServiceUnavailable => "Service temporarily unavailable.",
                _ => $"Request failed: {response.StatusCode}"
            };

            // Try to extract error message from response
            try
            {
                var errorObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
                if (errorObj?.message != null)
                {
                    errorMessage = errorObj.message.ToString();
                }
                else if (errorObj?.error != null)
                {
                    errorMessage = errorObj.error.ToString();
                }
            }
            catch
            {
                // Use default error message if JSON parsing fails
            }

            return new ApiResponse<T>
            {
                Success = false,
                Message = errorMessage,
                StatusCode = response.StatusCode,
                RawResponse = responseContent,
                IsNetworkError = IsNetworkRelatedError(response.StatusCode)
            };
        }

        /// <summary>
        /// Calculate exponential backoff delay
        /// </summary>
        private int CalculateRetryDelay(int attempt)
        {
            var delay = BaseDelayMs * Math.Pow(2, attempt - 1);
            return Math.Min((int)delay, MaxDelayMs);
        }

        /// <summary>
        /// Check if status code indicates a network-related error
        /// </summary>
        private bool IsNetworkRelatedError(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout ||
                   (int)statusCode >= 500;
        }

        #region Public API Methods

        /// <summary>
        /// Generic GET request
        /// </summary>
        public async Task<ApiResponse<T>> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync<T>(
                () => _httpClient.GetAsync($"{_baseUrl}/{endpoint.TrimStart('/')}", cancellationToken),
                $"GET {endpoint}",
                cancellationToken
            );
        }

        /// <summary>
        /// Generic POST request
        /// </summary>
        public async Task<ApiResponse<T>> PostAsync<T>(string endpoint, object data = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync<T>(
                () =>
                {
                    var json = data != null ? JsonConvert.SerializeObject(data) : "{}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    return _httpClient.PostAsync($"{_baseUrl}/{endpoint.TrimStart('/')}", content, cancellationToken);
                },
                $"POST {endpoint}",
                cancellationToken
            );
        }

        /// <summary>
        /// Generic PUT request
        /// </summary>
        public async Task<ApiResponse<T>> PutAsync<T>(string endpoint, object data, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync<T>(
                () =>
                {
                    var json = JsonConvert.SerializeObject(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    return _httpClient.PutAsync($"{_baseUrl}/{endpoint.TrimStart('/')}", content, cancellationToken);
                },
                $"PUT {endpoint}",
                cancellationToken
            );
        }

        /// <summary>
        /// Generic DELETE request
        /// </summary>
        public async Task<ApiResponse<T>> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync<T>(
                () => _httpClient.DeleteAsync($"{_baseUrl}/{endpoint.TrimStart('/')}", cancellationToken),
                $"DELETE {endpoint}",
                cancellationToken
            );
        }

        #endregion

        #region Operator - Registration (Enhanced)

        /// <summary>
        /// Search for student by roll number with enhanced error handling
        /// </summary>
        public async Task<(bool success, string message, StudentDto student)> SearchStudentAsync(
            string rollNumber, 
            int? collegeId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Searching for student: {rollNumber}");

                var requestData = new
                {
                    roll_number = rollNumber,
                    college_id = collegeId
                };

                var response = await PostAsync<SearchStudentResponse>(
                    "api/v1/operator/student/search", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true && response.Data.Student != null)
                {
                    Logger.Info($"Student found: {response.Data.Student.Name}");
                    return (true, "Student found", response.Data.Student);
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Student not found";
                    Logger.Warning($"Student not found: {rollNumber} - {message}");
                    return (false, message, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Search student error", ex);
                return (false, $"Search error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Save fingerprint registration to server with enhanced error handling
        /// </summary>
        public async Task<(bool success, string message)> SaveFingerprintAsync(
            string rollNumber, 
            string fingerprintTemplate, 
            byte[] fingerprintImage, 
            int qualityScore,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Saving fingerprint for: {rollNumber}");

                var requestData = new
                {
                    roll_number = rollNumber,
                    fingerprint_template = fingerprintTemplate,
                    fingerprint_image = ImageHelper.ByteArrayToBase64(fingerprintImage),
                    quality_score = qualityScore,
                    captured_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var response = await PostAsync<SaveFingerprintResponse>(
                    "api/v1/operator/fingerprint/save", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Fingerprint saved successfully: {rollNumber}");
                    return (true, response.Data.Message ?? "Fingerprint saved successfully");
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Save failed";
                    Logger.Warning($"Save failed: {message}");
                    return (false, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Save fingerprint error", ex);
                return (false, $"Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get colleges assigned to operator
        /// </summary>
        public async Task<(bool success, string message, List<CollegeDto> colleges)> GetAssignedCollegesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info("Getting assigned colleges");

                var response = await GetAsync<GetCollegesResponse>("api/v1/operator/colleges", cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Retrieved {response.Data.Colleges?.Count ?? 0} colleges");
                    return (true, "Colleges retrieved", response.Data.Colleges ?? new List<CollegeDto>());
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Failed to get colleges";
                    Logger.Warning($"Get colleges failed: {message}");
                    return (false, message, new List<CollegeDto>());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Get colleges error", ex);
                return (false, $"Error: {ex.Message}", new List<CollegeDto>());
            }
        }

        /// <summary>
        /// Download students for offline cache
        /// </summary>
        public async Task<(bool success, string message, List<StudentDto> students)> DownloadStudentsAsync(
            int collegeId, 
            int? testId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Downloading students for college {collegeId}");

                var queryParams = $"college_id={collegeId}";
                if (testId.HasValue)
                    queryParams += $"&test_id={testId}";

                var response = await GetAsync<DownloadStudentsResponse>(
                    $"api/v1/operator/students?{queryParams}", 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Downloaded {response.Data.Students?.Count ?? 0} students");
                    return (true, "Students downloaded", response.Data.Students ?? new List<StudentDto>());
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Failed to download students";
                    Logger.Warning($"Download students failed: {message}");
                    return (false, message, new List<StudentDto>());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Download students error", ex);
                return (false, $"Error: {ex.Message}", new List<StudentDto>());
            }
        }

        #endregion

        #region College - Verification (Enhanced)

        /// <summary>
        /// Load student data for verification with enhanced error handling
        /// </summary>
        public async Task<(bool success, string message, StudentDto student)> LoadStudentForVerificationAsync(
            string rollNumber,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Loading student for verification: {rollNumber}");

                var requestData = new { roll_number = rollNumber };

                var response = await PostAsync<LoadStudentResponse>(
                    "api/v1/admin/student/load", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true && response.Data.Student != null)
                {
                    Logger.Info($"Student loaded: {response.Data.Student.Name}");
                    return (true, "Student loaded", response.Data.Student);
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Student not found or fingerprint not registered";
                    Logger.Warning($"Student load failed: {rollNumber} - {message}");
                    return (false, message, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Load student error", ex);
                return (false, $"Connection error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Log verification result to server with enhanced error handling
        /// </summary>
        public async Task<(bool success, string message)> LogVerificationAsync(
            string rollNumber,
            bool matchResult,
            double confidenceScore,
            bool entryAllowed,
            string remarks = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Logging verification for: {rollNumber}");

                var requestData = new
                {
                    roll_number = rollNumber,
                    match_result = matchResult ? "match" : "no_match",
                    confidence_score = confidenceScore,
                    entry_allowed = entryAllowed,
                    verified_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    remarks = remarks
                };

                var response = await PostAsync<LogVerificationResponse>(
                    "api/v1/admin/verification/save", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Verification logged successfully: {rollNumber}");
                    return (true, response.Data.Message ?? "Verification logged successfully");
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Log failed";
                    Logger.Warning($"Log failed: {message}");
                    return (false, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Log verification error", ex);
                return (false, $"Log error: {ex.Message}");
            }
        }

        #endregion

        #region Sync Operations (New)

        /// <summary>
        /// Bulk upload queued registrations
        /// </summary>
        public async Task<(bool success, string message, SyncResultDto result)> SyncRegistrationsAsync(
            List<RegistrationSyncDto> registrations,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Syncing {registrations.Count} registrations");

                var requestData = new { registrations = registrations };

                var response = await PostAsync<SyncRegistrationsResponse>(
                    "api/v1/sync/registrations", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Sync completed: {response.Data.Results?.Successful ?? 0} successful");
                    return (true, response.Data.Message ?? "Sync completed", response.Data.Results);
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Sync failed";
                    Logger.Warning($"Sync failed: {message}");
                    return (false, message, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Sync registrations error", ex);
                return (false, $"Sync error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Bulk upload queued verifications
        /// </summary>
        public async Task<(bool success, string message, SyncResultDto result)> SyncVerificationsAsync(
            List<VerificationSyncDto> verifications,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info($"Syncing {verifications.Count} verifications");

                var requestData = new { verifications = verifications };

                var response = await PostAsync<SyncVerificationsResponse>(
                    "api/v1/sync/verifications", 
                    requestData, 
                    cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info($"Sync completed: {response.Data.Results?.Successful ?? 0} successful");
                    return (true, response.Data.Message ?? "Sync completed", response.Data.Results);
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Sync failed";
                    Logger.Warning($"Sync failed: {message}");
                    return (false, message, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Sync verifications error", ex);
                return (false, $"Sync error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Get sync statistics
        /// </summary>
        public async Task<(bool success, string message, SyncStatusDto status)> GetSyncStatusAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Info("Getting sync status");

                var response = await GetAsync<SyncStatusResponse>("api/v1/sync/status", cancellationToken);

                if (response.Success && response.Data?.Success == true)
                {
                    Logger.Info("Sync status retrieved");
                    return (true, "Status retrieved", response.Data.Stats);
                }
                else
                {
                    var message = response.Data?.Message ?? response.Message ?? "Failed to get status";
                    Logger.Warning($"Get sync status failed: {message}");
                    return (false, message, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Get sync status error", ex);
                return (false, $"Error: {ex.Message}", null);
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            _networkCheckTimer?.Dispose();
            _networkCheckSemaphore?.Dispose();
            _httpClient?.Dispose();
            Logger.Info("ApiService disposed");
        }

        #endregion

        #region Enhanced Response Models

        /// <summary>
        /// Generic API response wrapper
        /// </summary>
        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public T Data { get; set; }
            public HttpStatusCode? StatusCode { get; set; }
            public string RawResponse { get; set; }
            public bool IsNetworkError { get; set; }
            public Exception Exception { get; set; }
        }

        /// <summary>
        /// Network status change event arguments
        /// </summary>
        public class NetworkStatusChangedEventArgs : EventArgs
        {
            public bool IsOnline { get; }
            public bool WasOnline { get; }
            public DateTime Timestamp { get; }

            public NetworkStatusChangedEventArgs(bool isOnline, bool wasOnline)
            {
                IsOnline = isOnline;
                WasOnline = wasOnline;
                Timestamp = DateTime.Now;
            }
        }

        // Existing response models (enhanced)
        public class SearchStudentResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("student")]
            public StudentDto Student { get; set; }
        }

        public class LoadStudentResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("student")]
            public StudentDto Student { get; set; }
        }

        public class SaveFingerprintResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("registration_id")]
            public int? RegistrationId { get; set; }
        }

        public class LogVerificationResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("verification_id")]
            public int? VerificationId { get; set; }
        }

        // New response models
        public class GetCollegesResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("colleges")]
            public List<CollegeDto> Colleges { get; set; }
        }

        public class DownloadStudentsResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("count")]
            public int Count { get; set; }

            [JsonProperty("students")]
            public List<StudentDto> Students { get; set; }
        }

        public class SyncRegistrationsResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("results")]
            public SyncResultDto Results { get; set; }
        }

        public class SyncVerificationsResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("results")]
            public SyncResultDto Results { get; set; }
        }

        public class SyncStatusResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("server_time")]
            public string ServerTime { get; set; }

            [JsonProperty("stats")]
            public SyncStatusDto Stats { get; set; }
        }

        // New DTOs
        public class CollegeDto
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("district")]
            public string District { get; set; }

            [JsonProperty("active_tests")]
            public List<TestDto> ActiveTests { get; set; }
        }

        public class TestDto
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("test_date")]
            public string TestDate { get; set; }

            [JsonProperty("student_count")]
            public int StudentCount { get; set; }
        }

        public class RegistrationSyncDto
        {
            [JsonProperty("roll_number")]
            public string RollNumber { get; set; }

            [JsonProperty("fingerprint_template")]
            public string FingerprintTemplate { get; set; }

            [JsonProperty("fingerprint_image")]
            public string FingerprintImage { get; set; }

            [JsonProperty("quality_score")]
            public int QualityScore { get; set; }

            [JsonProperty("captured_at")]
            public string CapturedAt { get; set; }
        }

        public class VerificationSyncDto
        {
            [JsonProperty("roll_number")]
            public string RollNumber { get; set; }

            [JsonProperty("match_result")]
            public string MatchResult { get; set; }

            [JsonProperty("confidence_score")]
            public double ConfidenceScore { get; set; }

            [JsonProperty("entry_allowed")]
            public bool EntryAllowed { get; set; }

            [JsonProperty("verified_at")]
            public string VerifiedAt { get; set; }

            [JsonProperty("remarks")]
            public string Remarks { get; set; }
        }

        public class SyncResultDto
        {
            [JsonProperty("total")]
            public int Total { get; set; }

            [JsonProperty("successful")]
            public int Successful { get; set; }

            [JsonProperty("failed")]
            public int Failed { get; set; }

            [JsonProperty("details")]
            public List<SyncDetailDto> Details { get; set; }
        }

        public class SyncDetailDto
        {
            [JsonProperty("roll_number")]
            public string RollNumber { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; }
        }

        public class SyncStatusDto
        {
            [JsonProperty("total_students")]
            public int TotalStudents { get; set; }

            [JsonProperty("registered_fingerprints")]
            public int RegisteredFingerprints { get; set; }

            [JsonProperty("pending_verifications")]
            public int PendingVerifications { get; set; }

            [JsonProperty("completed_verifications")]
            public int CompletedVerifications { get; set; }
        }

        public class StudentDto
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("roll_number")]
            public string RollNumber { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("father_name")]
            public string FatherName { get; set; }

            [JsonProperty("cnic")]
            public string CNIC { get; set; }

            [JsonProperty("gender")]
            public string Gender { get; set; }

            [JsonProperty("picture")]
            public string Picture { get; set; } // Base64

            [JsonProperty("test_photo")]
            public string TestPhoto { get; set; } // Base64

            [JsonProperty("test_id")]
            public int? TestId { get; set; }

            [JsonProperty("test_name")]
            public string TestName { get; set; }

            [JsonProperty("college_id")]
            public int? CollegeId { get; set; }

            [JsonProperty("college_name")]
            public string CollegeName { get; set; }

            [JsonProperty("venue")]
            public string Venue { get; set; }

            [JsonProperty("hall")]
            public string Hall { get; set; }

            [JsonProperty("zone")]
            public string Zone { get; set; }

            [JsonProperty("row")]
            public string Row { get; set; }

            [JsonProperty("seat")]
            public string Seat { get; set; }

            [JsonProperty("fingerprint_template")]
            public string FingerprintTemplate { get; set; }

            [JsonProperty("fingerprint_image")]
            public string FingerprintImage { get; set; } // Base64

            [JsonProperty("quality_score")]
            public int? QualityScore { get; set; }

            [JsonProperty("fingerprint_quality")]
            public int? FingerprintQuality { get; set; }

            [JsonProperty("registration_date")]
            public string RegistrationDate { get; set; }
        }

        #endregion
    }
}