using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Enhanced authentication service with secure token management
    /// Implements Requirements 2.1, 2.2, 2.3, 2.4, 2.5
    /// </summary>
    public class AuthenticationService : IDisposable
    {
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;
        private User _currentUser;
        private readonly object _lockObject = new object();

        // Windows Credential Manager constants
        private const string CredentialTarget = "BACTBiometricClient_AuthToken";
        private const int TokenRefreshThresholdMinutes = 30; // Refresh token if expires within 30 minutes

        public AuthenticationService(ApiService apiService, DatabaseService databaseService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            
            Logger.Info("Enhanced AuthenticationService initialized");
        }

        #region IAuthenticationService Implementation

        public User GetCurrentUser()
        {
            lock (_lockObject)
            {
                return _currentUser;
            }
        }

        public bool IsAuthenticated
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentUser != null && 
                           !string.IsNullOrEmpty(_currentUser.Token) && 
                           !_currentUser.IsTokenExpired;
                }
            }
        }

        public async Task<LoginResult> LoginAsync(string email, string password)
        {
            try
            {
                Logger.Info($"Login attempt for: {email}");

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    return new LoginResult
                    {
                        Success = false,
                        Message = "Email and password are required",
                        ErrorCode = "INVALID_INPUT"
                    };
                }

                // Check network connectivity
                if (!_apiService.IsOnline)
                {
                    return new LoginResult
                    {
                        Success = false,
                        Message = "No internet connection available. Please check your network connection.",
                        ErrorCode = "NO_NETWORK"
                    };
                }

                // Attempt login via API
                var loginRequest = new
                {
                    email = email,
                    password = password
                };

                var response = await _apiService.PostAsync<LoginApiResponse>("api/v1/auth/login", loginRequest);

                if (response.Success && response.Data?.Success == true)
                {
                    var loginData = response.Data;
                    
                    // Create user object
                    var user = new User
                    {
                        Id = loginData.User?.Id ?? 0,
                        Email = loginData.User?.Email ?? email,
                        Name = loginData.User?.Name ?? "Unknown User",
                        Role = loginData.User?.Role ?? "operator",
                        Token = loginData.Token,
                        TokenExpiresAt = DateTime.Now.AddHours(8), // Default 8 hours
                        LoggedInAt = DateTime.Now,
                        LastActivityAt = DateTime.Now,
                        IsActive = true
                    };

                    // Parse token expiration if provided
                    if (!string.IsNullOrEmpty(loginData.ExpiresAt))
                    {
                        if (DateTime.TryParse(loginData.ExpiresAt, out var expiresAt))
                        {
                            user.TokenExpiresAt = expiresAt;
                        }
                    }

                    // Store user and token securely
                    await StoreUserSessionAsync(user);
                    
                    lock (_lockObject)
                    {
                        _currentUser = user;
                    }

                    // Update API service with new token
                    _apiService.SetAuthToken(user.Token);

                    Logger.Info($"Login successful for: {email} as {user.Role}");
                    
                    return new LoginResult
                    {
                        Success = true,
                        Message = "Login successful",
                        User = user,
                        Token = user.Token
                    };
                }
                else
                {
                    var errorMessage = response.Data?.Message ?? response.Message ?? "Login failed";
                    Logger.Warning($"Login failed for: {email} - {errorMessage}");
                    
                    return new LoginResult
                    {
                        Success = false,
                        Message = errorMessage,
                        ErrorCode = response.StatusCode?.ToString() ?? "LOGIN_FAILED"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Login error", ex);
                return new LoginResult
                {
                    Success = false,
                    Message = $"Login error: {ex.Message}",
                    ErrorCode = "EXCEPTION"
                };
            }
        }

        public async Task<bool> LogoutAsync()
        {
            try
            {
                Logger.Info("Logout initiated");

                // Clear current user
                User userToLogout;
                lock (_lockObject)
                {
                    userToLogout = _currentUser;
                    _currentUser = null;
                }

                if (userToLogout != null)
                {
                    // Attempt to notify server of logout (best effort)
                    try
                    {
                        await _apiService.PostAsync<object>("api/v1/auth/logout", null);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("Server logout notification failed");
                        Logger.Error("Server logout notification details", ex);
                        // Continue with local logout even if server notification fails
                    }

                    // Clear stored credentials
                    await ClearStoredCredentialsAsync();
                    
                    // Clear API service token
                    _apiService.SetAuthToken(null);
                    
                    Logger.Info($"Logout completed for: {userToLogout.Email}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Logout error", ex);
                return false;
            }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            try
            {
                User currentUser;
                lock (_lockObject)
                {
                    currentUser = _currentUser;
                }

                if (currentUser == null || string.IsNullOrEmpty(currentUser.Token))
                {
                    Logger.Warning("Cannot refresh token: No current user or token");
                    return false;
                }

                Logger.Info("Attempting token refresh");

                var refreshRequest = new
                {
                    token = currentUser.Token
                };

                var response = await _apiService.PostAsync<RefreshTokenResponse>("api/v1/auth/refresh", refreshRequest);

                if (response.Success && response.Data?.Success == true)
                {
                    // Update token information
                    currentUser.Token = response.Data.Token;
                    
                    if (!string.IsNullOrEmpty(response.Data.ExpiresAt))
                    {
                        if (DateTime.TryParse(response.Data.ExpiresAt, out var expiresAt))
                        {
                            currentUser.TokenExpiresAt = expiresAt;
                        }
                    }
                    else
                    {
                        // Default to 8 hours from now
                        currentUser.TokenExpiresAt = DateTime.Now.AddHours(8);
                    }

                    // Store updated session
                    await StoreUserSessionAsync(currentUser);
                    
                    // Update API service with new token
                    _apiService.SetAuthToken(currentUser.Token);

                    Logger.Info("Token refresh successful");
                    return true;
                }
                else
                {
                    Logger.Warning($"Token refresh failed: {response.Message}");
                    
                    // If refresh fails, logout user
                    await LogoutAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Token refresh error", ex);
                
                // If refresh fails with exception, logout user
                await LogoutAsync();
                return false;
            }
        }

        public async Task<bool> ValidateTokenAsync()
        {
            try
            {
                User currentUser;
                lock (_lockObject)
                {
                    currentUser = _currentUser;
                }

                if (currentUser == null || string.IsNullOrEmpty(currentUser.Token))
                {
                    return false;
                }

                // Check if token is expired
                if (currentUser.IsTokenExpired)
                {
                    Logger.Info("Token expired, attempting refresh");
                    return await RefreshTokenAsync();
                }

                // Check if token expires soon and needs refresh
                if (currentUser.TokenExpiresAt.HasValue)
                {
                    var timeUntilExpiry = currentUser.TokenExpiresAt.Value - DateTime.Now;
                    if (timeUntilExpiry.TotalMinutes <= TokenRefreshThresholdMinutes)
                    {
                        Logger.Info($"Token expires in {timeUntilExpiry.TotalMinutes:F1} minutes, attempting refresh");
                        return await RefreshTokenAsync();
                    }
                }

                // Validate token with server (optional - can be expensive)
                // For now, we'll trust local validation unless there's a specific need
                
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Token validation error", ex);
                return false;
            }
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Restore session from secure storage if available
        /// </summary>
        public async Task<bool> RestoreSessionAsync()
        {
            try
            {
                Logger.Info("Attempting to restore session");

                // Try to load from Windows Credential Manager first
                var storedToken = await GetStoredTokenAsync();
                if (!string.IsNullOrEmpty(storedToken))
                {
                    // Try to load user data from database
                    var userData = _databaseService.GetActiveSession();
                    if (userData != null)
                    {
                        userData.Token = storedToken;
                        
                        // Validate the restored session
                        lock (_lockObject)
                        {
                            _currentUser = userData;
                        }

                        if (await ValidateTokenAsync())
                        {
                            _apiService.SetAuthToken(userData.Token);
                            Logger.Info($"Session restored for: {userData.Email}");
                            return true;
                        }
                        else
                        {
                            Logger.Info("Restored session validation failed");
                            await ClearStoredCredentialsAsync();
                        }
                    }
                }

                Logger.Info("No valid session to restore");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Session restoration error", ex);
                return false;
            }
        }

        /// <summary>
        /// Update user activity timestamp
        /// </summary>
        public void UpdateActivity()
        {
            lock (_lockObject)
            {
                if (_currentUser != null)
                {
                    _currentUser.UpdateActivity();
                    
                    // Update in database (async fire-and-forget)
                    Task.Run(async () =>
                    {
                        try
                        {
                            await StoreUserSessionAsync(_currentUser);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Failed to update activity in storage");
                        Logger.Error("Activity update error details", ex);
                        }
                    });
                }
            }
        }

        #endregion

        #region Secure Storage (Windows Credential Manager)

        /// <summary>
        /// Store user session securely using Windows Credential Manager
        /// </summary>
        private async Task StoreUserSessionAsync(User user)
        {
            try
            {
                // Store token in Windows Credential Manager
                await StoreTokenAsync(user.Token);
                
                // Store user data (without token) in database
                var userCopy = new User
                {
                    Id = user.Id,
                    Email = user.Email,
                    Name = user.Name,
                    Role = user.Role,
                    Token = null, // Don't store token in database
                    TokenExpiresAt = user.TokenExpiresAt,
                    LoggedInAt = user.LoggedInAt,
                    LastActivityAt = user.LastActivityAt,
                    IsActive = user.IsActive,
                    AssignedTestIds = user.AssignedTestIds,
                    AssignedTestNames = user.AssignedTestNames
                };

                _databaseService.SaveUserSession(userCopy);
                
                Logger.Info("User session stored securely");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to store user session", ex);
                throw;
            }
        }

        /// <summary>
        /// Store authentication token securely in Windows Credential Manager
        /// </summary>
        private async Task StoreTokenAsync(string token)
        {
            await Task.Run(() =>
            {
                try
                {
                    var credential = new CREDENTIAL
                    {
                        Type = CRED_TYPE.GENERIC,
                        TargetName = CredentialTarget,
                        CredentialBlob = Marshal.StringToHGlobalAnsi(token),
                        CredentialBlobSize = Encoding.UTF8.GetByteCount(token),
                        Persist = CRED_PERSIST.LOCAL_MACHINE,
                        AttributeCount = 0,
                        Attributes = IntPtr.Zero,
                        Comment = "BACT Biometric Client Authentication Token",
                        UserName = Environment.UserName
                    };

                    try
                    {
                        if (!CredWrite(ref credential, 0))
                        {
                            var error = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException($"Failed to store credential. Error: {error}");
                        }

                        Logger.Info("Authentication token stored securely");
                    }
                    finally
                    {
                        // Free the allocated memory
                        if (credential.CredentialBlob != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(credential.CredentialBlob);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to store token in Credential Manager", ex);
                    throw;
                }
            });
        }

        /// <summary>
        /// Retrieve authentication token from Windows Credential Manager
        /// </summary>
        private async Task<string> GetStoredTokenAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (CredRead(CredentialTarget, CRED_TYPE.GENERIC, 0, out IntPtr credPtr))
                    {
                        var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                        
                        // Read the credential blob as a string
                        var token = Marshal.PtrToStringAnsi(credential.CredentialBlob, credential.CredentialBlobSize);
                        
                        CredFree(credPtr);
                        
                        Logger.Info("Authentication token retrieved from secure storage");
                        return token;
                    }
                    else
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != 1168) // ERROR_NOT_FOUND
                        {
                            Logger.Warning($"Failed to retrieve credential. Error: {error}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to retrieve token from Credential Manager", ex);
                    return null;
                }
            });
        }

        /// <summary>
        /// Clear stored credentials from Windows Credential Manager and database
        /// </summary>
        private async Task ClearStoredCredentialsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Clear from Windows Credential Manager
                    if (!CredDelete(CredentialTarget, CRED_TYPE.GENERIC, 0))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != 1168) // ERROR_NOT_FOUND is acceptable
                        {
                            Logger.Warning($"Failed to delete credential. Error: {error}");
                        }
                    }

                    // Clear from database
                    _databaseService.ClearAllSessions();
                    
                    Logger.Info("Stored credentials cleared");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to clear stored credentials", ex);
                }
            });
        }

        #endregion

        #region Windows Credential Manager P/Invoke

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, CRED_TYPE type, int reservedFlag);

        [DllImport("advapi32.dll")]
        private static extern void CredFree([In] IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public CRED_TYPE Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public CRED_PERSIST Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        private enum CRED_TYPE : uint
        {
            GENERIC = 1,
            DOMAIN_PASSWORD = 2,
            DOMAIN_CERTIFICATE = 3,
            DOMAIN_VISIBLE_PASSWORD = 4,
            GENERIC_CERTIFICATE = 5,
            DOMAIN_EXTENDED = 6,
            MAXIMUM = 7,
            MAXIMUM_EX = (MAXIMUM + 1000)
        }

        private enum CRED_PERSIST : uint
        {
            SESSION = 1,
            LOCAL_MACHINE = 2,
            ENTERPRISE = 3
        }

        #endregion

        #region Response Models

        public class LoginApiResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string Token { get; set; }
            public string ExpiresAt { get; set; }
            public UserData User { get; set; }
        }

        public class UserData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Role { get; set; }
        }

        public class RefreshTokenResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string Token { get; set; }
            public string ExpiresAt { get; set; }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            // Clear sensitive data from memory
            lock (_lockObject)
            {
                _currentUser = null;
            }
            
            Logger.Info("AuthenticationService disposed");
        }

        #endregion
    }

    #region Interfaces and Models

    #endregion
}

/// <summary>
/// Result of login operation
/// </summary>
public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string ErrorCode { get; set; }
    public User User { get; set; }
    public string Token { get; set; }
}