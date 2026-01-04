using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Handles user authentication and session management
    /// </summary>
    public class AuthService
    {
        private readonly DatabaseService _database;
        private readonly HttpClient _httpClient;
        private User _currentUser;

        public AuthService(DatabaseService database)
        {
            _database = database;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Get the currently logged-in user
        /// </summary>
        public User CurrentUser => _currentUser;

        /// <summary>
        /// Check if user is logged in
        /// </summary>
        public bool IsLoggedIn => _currentUser != null && _currentUser.IsActive;

        /// <summary>
        /// Get auth token for API calls
        /// </summary>
        public string GetAuthToken()
        {
            return _currentUser?.Token ?? string.Empty;
        }

        /// <summary>
        /// Save auth token from successful login
        /// </summary>
        public async Task SaveAuthTokenAsync(string token, string name, string email, string role)
        {
            try
            {
                var user = new User
                {
                    Token = token,
                    Name = name,
                    Email = email,
                    Role = role,
                    TokenExpiresAt = DateTime.Now.AddHours(8), // 8 hour token expiry
                    LoggedInAt = DateTime.Now,
                    IsActive = true
                };

                _currentUser = user;

                // Save to database for session persistence
                _database.SaveUserSession(user);

                Logger.Info($"Auth token saved for: {name} ({role})");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save auth token", ex);
                throw;
            }
        }

        /// <summary>
        /// Login with email and password - REAL API VERSION
        /// </summary>
        public async Task<(bool success, string message, User user)> LoginAsync(string email, string password, string apiUrl)
        {
            try
            {
                Logger.Info($"Login attempt for: {email}");

                // Check internet connection
                if (!await InternetChecker.IsConnectedAsync())
                {
                    Logger.Warning("Login failed: No internet connection");
                    return (false, "No internet connection. Please connect to the internet to login.", null);
                }

                System.Diagnostics.Debug.WriteLine("=== AUTH SERVICE LOGIN ===");
                System.Diagnostics.Debug.WriteLine($"API URL: {apiUrl}/biometric-operator/login");
                System.Diagnostics.Debug.WriteLine($"Email: {email}");

                // Prepare login request
                var loginData = new
                {
                    email = email,
                    password = password
                };

                var json = JsonSerializer.Serialize(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Set headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // Make API call
                var response = await _httpClient.PostAsync(
                    $"{apiUrl}/biometric-operator/login",
                    content
                );

                var responseJson = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response: {responseJson}");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

                    // Extract token
                    string token = null;
                    if (result.TryGetProperty("token", out var tokenProp))
                    {
                        token = tokenProp.GetString();
                    }
                    else if (result.TryGetProperty("access_token", out var accessTokenProp))
                    {
                        token = accessTokenProp.GetString();
                    }

                    // Extract user info
                    string userName = email.Split('@')[0];
                    string userEmail = email;
                    string userRole = "operator";

                    if (result.TryGetProperty("user", out var userProp))
                    {
                        userName = userProp.TryGetProperty("name", out var nameProp)
                            ? nameProp.GetString()
                            : userName;

                        userEmail = userProp.TryGetProperty("email", out var emailProp)
                            ? emailProp.GetString()
                            : userEmail;

                        userRole = userProp.TryGetProperty("role", out var roleProp)
                            ? roleProp.GetString()
                            : userRole;
                    }

                    if (!string.IsNullOrEmpty(token))
                    {
                        // Create user object
                        var user = new User
                        {
                            Token = token,
                            Name = userName,
                            Email = userEmail,
                            Role = userRole,
                            TokenExpiresAt = DateTime.Now.AddHours(8),
                            LoggedInAt = DateTime.Now,
                            IsActive = true
                        };

                        // Save session
                        _database.SaveUserSession(user);
                        _currentUser = user;

                        // Save last login email if remember is enabled
                        var settings = _database.GetAppSettings();
                        if (settings.RememberCredentials)
                        {
                            _database.SetSetting("last_login_email", email);
                        }

                        Logger.Info($"Login successful for: {email} as {userRole}");
                        return (true, "Login successful", user);
                    }
                    else
                    {
                        Logger.Warning("Login failed: No token in response");
                        return (false, "Login response did not contain authentication token", null);
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Logger.Warning($"Login failed for: {email} - Invalid credentials");
                    return (false, "Invalid email or password", null);
                }
                else
                {
                    // Try to parse error message
                    try
                    {
                        var errorResult = JsonSerializer.Deserialize<JsonElement>(responseJson);
                        string errorMessage = errorResult.TryGetProperty("message", out var msg)
                            ? msg.GetString()
                            : $"Login failed: {response.StatusCode}";

                        Logger.Warning($"Login failed: {errorMessage}");
                        return (false, errorMessage, null);
                    }
                    catch
                    {
                        Logger.Warning($"Login failed: {response.StatusCode}");
                        return (false, $"Login failed: {response.StatusCode}", null);
                    }
                }
            }
            catch (HttpRequestException httpEx)
            {
                Logger.Error("Login HTTP error", httpEx);
                return (false, "Cannot connect to server. Please ensure Laravel server is running.", null);
            }
            catch (Exception ex)
            {
                Logger.Error("Login error", ex);
                return (false, $"Login error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Restore session from database if available
        /// </summary>
        public bool RestoreSession()
        {
            try
            {
                var session = _database.GetActiveSession();

                if (session == null)
                {
                    Logger.Info("No active session found");
                    return false;
                }

                // Check if token is expired
                if (session.IsTokenExpired)
                {
                    Logger.Info("Session token expired");
                    Logout();
                    return false;
                }

                // Check inactivity timeout
                var settings = _database.GetAppSettings();
                if (session.IsInactive(settings.InactivityTimeoutMinutes))
                {
                    Logger.Info("Session inactive timeout");
                    Logout();
                    return false;
                }

                _currentUser = session;
                Logger.Info($"Session restored for: {session.Email}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to restore session", ex);
                return false;
            }
        }

        /// <summary>
        /// Logout current user
        /// </summary>
        public void Logout()
        {
            try
            {
                if (_currentUser != null)
                {
                    Logger.Info($"Logout: {_currentUser.Email}");
                    _currentUser = null;
                }

                _database.ClearAllSessions();
            }
            catch (Exception ex)
            {
                Logger.Error("Logout error", ex);
            }
        }

        /// <summary>
        /// Update user activity timestamp
        /// </summary>
        public void UpdateActivity()
        {
            if (_currentUser != null)
            {
                _currentUser.UpdateActivity();
            }
        }

        /// <summary>
        /// Check if current session is still valid
        /// </summary>
        public bool IsSessionValid()
        {
            if (_currentUser == null || !_currentUser.IsActive)
                return false;

            // Check token expiration
            if (_currentUser.IsTokenExpired)
            {
                Logger.Info("Token expired");
                Logout();
                return false;
            }

            // Check inactivity
            var settings = _database.GetAppSettings();
            if (_currentUser.IsInactive(settings.InactivityTimeoutMinutes))
            {
                Logger.Info("Session inactive");
                Logout();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get user role display name
        /// </summary>
        public string GetUserRoleDisplay()
        {
            if (_currentUser == null)
                return "NOT LOGGED IN";

            return _currentUser.RoleDisplay;
        }
    }
}