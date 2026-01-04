using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Handles all API communication with BACT Laravel backend
    /// </summary>
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _authToken;

        public ApiService(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Set authentication token for API requests
        /// </summary>
        public void SetAuthToken(string token)
        {
            _authToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        #region Operator - Registration

        /// <summary>
        /// Search for student by roll number
        /// </summary>
        public async Task<(bool success, string message, StudentDto student)> SearchStudentAsync(string rollNumber)
        {
            try
            {
                Logger.Info($"Searching for student: {rollNumber}");

                var requestData = new
                {
                    roll_number = rollNumber
                };

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/biometric-operator/registration/search-student", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<SearchStudentResponse>(responseContent);

                    if (result.Success && result.Student != null)
                    {
                        Logger.Info($"Student found: {result.Student.Name}");
                        return (true, "Student found", result.Student);
                    }
                    else
                    {
                        Logger.Warning($"Student not found: {rollNumber}");
                        return (false, result.Message ?? "Student not found", null);
                    }
                }
                else
                {
                    Logger.Warning($"Search failed: {response.StatusCode}");
                    return (false, $"Search failed: {response.StatusCode}", null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Search student error", ex);
                return (false, $"Connection error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Save fingerprint registration to server
        /// </summary>
        public async Task<(bool success, string message)> SaveFingerprintAsync(string rollNumber, string fingerprintTemplate, byte[] fingerprintImage, int qualityScore)
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

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/biometric-operator/registration/save-fingerprint", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<SaveFingerprintResponse>(responseContent);

                    if (result.Success)
                    {
                        Logger.Info($"Fingerprint saved successfully: {rollNumber}");
                        return (true, result.Message ?? "Fingerprint saved successfully");
                    }
                    else
                    {
                        Logger.Warning($"Save failed: {result.Message}");
                        return (false, result.Message ?? "Save failed");
                    }
                }
                else
                {
                    Logger.Warning($"Save failed: {response.StatusCode}");
                    return (false, $"Save failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Save fingerprint error", ex);
                return (false, $"Save error: {ex.Message}");
            }
        }

        #endregion

        #region College - Verification

        /// <summary>
        /// Load student data for verification
        /// </summary>
        public async Task<(bool success, string message, StudentDto student)> LoadStudentForVerificationAsync(string rollNumber)
        {
            try
            {
                Logger.Info($"Loading student for verification: {rollNumber}");

                var requestData = new
                {
                    roll_number = rollNumber
                };

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/college/fingerprint-verification/load-student", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<LoadStudentResponse>(responseContent);

                    if (result.Success && result.Student != null)
                    {
                        Logger.Info($"Student loaded: {result.Student.Name}");
                        return (true, "Student loaded", result.Student);
                    }
                    else
                    {
                        Logger.Warning($"Student not found or no fingerprint: {rollNumber}");
                        return (false, result.Message ?? "Student not found or fingerprint not registered", null);
                    }
                }
                else
                {
                    Logger.Warning($"Load failed: {response.StatusCode}");
                    return (false, $"Load failed: {response.StatusCode}", null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Load student error", ex);
                return (false, $"Connection error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Log verification result to server
        /// </summary>
        public async Task<(bool success, string message)> LogVerificationAsync(
            string rollNumber,
            bool matchResult,
            double confidenceScore,
            bool entryAllowed,
            string remarks = null)
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

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/college/fingerprint-verification/log", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<LogVerificationResponse>(responseContent);

                    if (result.Success)
                    {
                        Logger.Info($"Verification logged successfully: {rollNumber}");
                        return (true, result.Message ?? "Verification logged successfully");
                    }
                    else
                    {
                        Logger.Warning($"Log failed: {result.Message}");
                        return (false, result.Message ?? "Log failed");
                    }
                }
                else
                {
                    Logger.Warning($"Log failed: {response.StatusCode}");
                    return (false, $"Log failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Log verification error", ex);
                return (false, $"Log error: {ex.Message}");
            }
        }

        #endregion

        #region Response Models

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
        }

        public class LogVerificationResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
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

            [JsonProperty("registration_date")]
            public string RegistrationDate { get; set; }
        }

        #endregion
    }
}