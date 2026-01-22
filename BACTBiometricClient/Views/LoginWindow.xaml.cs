using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;
        private readonly DatabaseService _database;
        private readonly ApiService _apiService;
        private readonly HttpClient _httpClient;
        private string _apiUrl;

        public string AuthToken { get; private set; }
        public string UserName { get; private set; }
        public string UserEmail { get; private set; }
        public string UserRole { get; private set; }
        public bool LoginSuccessful { get; private set; } = false;

        public LoginWindow()
        {
            InitializeComponent();

            _database = new DatabaseService();
            _httpClient = new HttpClient();

            // Initialize API Service with base URL
            _apiUrl = "http://localhost:8000/api"; // Laravel API URL
            _apiService = new ApiService(_apiUrl);

            _authService = new AuthService(_database);

            LoadSettings();
            CheckConnectionStatus();

            Logger.Info("Login window opened");
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _database.GetAppSettings();

                // Use API URL from settings if available
                //if (!string.IsNullOrEmpty(settings.ApiUrl))
                //{
                //    _apiUrl = settings.ApiUrl;
                //}

                if (settings.RememberCredentials && !string.IsNullOrEmpty(settings.LastLoginEmail))
                {
                    txtEmail.Text = settings.LastLoginEmail;
                    chkRememberMe.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings", ex);
            }
        }

        private async void CheckConnectionStatus()
        {
            try
            {
                bool isConnected = await InternetChecker.IsConnectedAsync();

                if (!isConnected)
                {
                    txtConnectionStatus.Text = "⚠ No internet connection detected. You need internet to login.";
                    borderConnectionStatus.Visibility = Visibility.Visible;
                }
                else
                {
                    string status = InternetChecker.GetDetailedConnectionStatus();
                    txtConnectionStatus.Text = $"✓ {status}";
                    borderConnectionStatus.Background = System.Windows.Media.Brushes.LightGreen;
                    borderConnectionStatus.BorderBrush = System.Windows.Media.Brushes.Green;
                    borderConnectionStatus.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to check connection", ex);
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }
        private async Task LoginAsync()
        {
            string email = txtEmail.Text.Trim();
            string password = GetCurrentPassword();

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Please enter your email");
                txtEmail.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Please enter your password");
                FocusPasswordField();
                return;
            }

            SetLoading(true);
            HideError();

            try
            {
                Logger.Info($"Attempting login for: {email}");

                // Try all possible login endpoints
                var endpoints = new[]
                {
            new { Path = "biometric-operator/login", Role = "operator" },
            new { Path = "college/login", Role = "college" }
        };

                foreach (var endpoint in endpoints)
                {
                    var loginEndpoint = $"{_apiUrl}/{endpoint.Path}";

                    System.Diagnostics.Debug.WriteLine($"=== TRYING {endpoint.Role.ToUpper()} LOGIN ===");
                    System.Diagnostics.Debug.WriteLine($"API URL: {loginEndpoint}");
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

                    try
                    {
                        // Call Laravel API
                        var response = await _httpClient.PostAsync(loginEndpoint, content);
                        var responseJson = await response.Content.ReadAsStringAsync();

                        System.Diagnostics.Debug.WriteLine($"Status: {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"Response: {responseJson.Substring(0, Math.Min(200, responseJson.Length))}...");

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
                            else if (result.TryGetProperty("data", out var dataProp)
                                     && dataProp.TryGetProperty("token", out var dataTokenProp))
                            {
                                token = dataTokenProp.GetString();
                            }

                            // Extract user info - always use Hassan Sarparrah as default
                            string userName = "Hassan Sarparrah"; // Default name
                            string userEmail = email;
                            string userRole = endpoint.Role;

                            // Try to get user info from different possible response structures
                            if (result.TryGetProperty("user", out var userProp))
                            {
                                // Only override if API returns a valid name
                                if (userProp.TryGetProperty("name", out var nameProp) && !string.IsNullOrEmpty(nameProp.GetString()))
                                {
                                    userName = nameProp.GetString();
                                }

                                userEmail = userProp.TryGetProperty("email", out var emailProp) && !string.IsNullOrEmpty(emailProp.GetString())
                                    ? emailProp.GetString()
                                    : userEmail;

                                userRole = userProp.TryGetProperty("role", out var roleProp) && !string.IsNullOrEmpty(roleProp.GetString())
                                    ? roleProp.GetString()
                                    : endpoint.Role;
                            }
                            else if (result.TryGetProperty("data", out var dataProp) && dataProp.TryGetProperty("user", out var dataUserProp))
                            {
                                // Only override if API returns a valid name
                                if (dataUserProp.TryGetProperty("name", out var nameProp) && !string.IsNullOrEmpty(nameProp.GetString()))
                                {
                                    userName = nameProp.GetString();
                                }

                                userEmail = dataUserProp.TryGetProperty("email", out var emailProp) && !string.IsNullOrEmpty(emailProp.GetString())
                                    ? emailProp.GetString()
                                    : userEmail;

                                userRole = dataUserProp.TryGetProperty("role", out var roleProp) && !string.IsNullOrEmpty(roleProp.GetString())
                                    ? roleProp.GetString()
                                    : endpoint.Role;
                            }

                            if (!string.IsNullOrEmpty(token))
                            {
                                AuthToken = token;
                                LoginSuccessful = true;
                                UserName = userName;
                                UserEmail = userEmail;
                                UserRole = userRole;

                                // Save credentials if remember me is checked
                                var settings = _database.GetAppSettings();
                                settings.RememberCredentials = chkRememberMe.IsChecked == true;
                                settings.LastLoginEmail = email;
                                _database.SaveAppSettings(settings);

                                // Save to AuthService
                                await _authService.SaveAuthTokenAsync(token, UserName, UserEmail, UserRole);

                                Logger.Info($"✓ Login successful: {UserName} ({UserRole})");
                                System.Diagnostics.Debug.WriteLine($"✓ Authenticated as {userRole.ToUpper()}: {UserName}");

                                // Open MainWindow
                                var mainWindow = new MainWindow(_authService, _database, _apiService);
                                mainWindow.Show();
                                this.Close();
                                return; // Success - stop trying other endpoints
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            // Wrong endpoint or wrong credentials - try next endpoint
                            System.Diagnostics.Debug.WriteLine($"✗ {endpoint.Role.ToUpper()} endpoint: Unauthorized");
                            continue;
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            // Account exists but is inactive/forbidden
                            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                            string errorMessage = result.TryGetProperty("message", out var msg)
                                ? msg.GetString()
                                : "Account is deactivated";

                            ShowError(errorMessage);
                            Logger.Warning($"Login failed: {errorMessage}");
                            return; // Don't try other endpoints - account is disabled
                        }
                        else
                        {
                            // Other error - try next endpoint
                            System.Diagnostics.Debug.WriteLine($"✗ {endpoint.Role.ToUpper()} endpoint: {response.StatusCode}");
                            continue;
                        }
                    }
                    catch (HttpRequestException)
                    {
                        // Connection error - continue to next endpoint
                        continue;
                    }
                }

                // If we get here, all endpoints failed
                ShowError("Invalid email or password");
                Logger.Warning("Login failed: Invalid credentials on all endpoints");
                System.Diagnostics.Debug.WriteLine("✗ All login endpoints failed");
            }
            catch (HttpRequestException httpEx)
            {
                ShowError("Cannot connect to server.\n\nPlease ensure Laravel server is running:\nphp artisan serve");
                Logger.Error("Login HTTP error", httpEx);
            }
            catch (Exception ex)
            {
                ShowError($"Login error: {ex.Message}");
                Logger.Error("Login exception", ex);
            }
            finally
            {
                SetLoading(false);
            }
        }
        private void ShowError(string message)
        {
            txtStatus.Text = message;
            txtStatus.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            txtStatus.Visibility = Visibility.Collapsed;
        }

        private void SetLoading(bool isLoading)
        {
            btnLogin.IsEnabled = !isLoading;
            txtEmail.IsEnabled = !isLoading;
            txtPassword.IsEnabled = !isLoading;
            txtPasswordVisible.IsEnabled = !isLoading;
            chkRememberMe.IsEnabled = !isLoading;
            btnTogglePassword.IsEnabled = !isLoading;
            panelLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

            if (isLoading)
            {
                btnLogin.Content = "SIGNING IN...";
            }
            else
            {
                btnLogin.Content = "SIGN IN";
            }
        }

        private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            if (txtPassword.Visibility == Visibility.Visible)
            {
                // Switch to visible text
                txtPasswordVisible.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Visibility = Visibility.Visible;
                
                // Update eye icon to "hide" state
                var button = sender as Button;
                var template = button?.Template;
                if (template != null)
                {
                    var eyeIcon = template.FindName("EyeIcon", button) as TextBlock;
                    if (eyeIcon != null)
                    {
                        eyeIcon.Text = "🙈"; // Hide icon
                    }
                }
                
                txtPasswordVisible.Focus();
                txtPasswordVisible.CaretIndex = txtPasswordVisible.Text.Length;
            }
            else
            {
                // Switch to hidden password
                txtPassword.Password = txtPasswordVisible.Text;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                
                // Update eye icon to "show" state
                var button = sender as Button;
                var template = button?.Template;
                if (template != null)
                {
                    var eyeIcon = template.FindName("EyeIcon", button) as TextBlock;
                    if (eyeIcon != null)
                    {
                        eyeIcon.Text = "👁️"; // Show icon
                    }
                }
                
                txtPassword.Focus();
            }
        }

        private string GetCurrentPassword()
        {
            return txtPassword.Visibility == Visibility.Visible 
                ? txtPassword.Password 
                : txtPasswordVisible.Text;
        }

        private void FocusPasswordField()
        {
            if (txtPassword.Visibility == Visibility.Visible)
            {
                txtPassword.Focus();
            }
            else
            {
                txtPasswordVisible.Focus();
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                _ = LoginAsync();
            }
        }
    }
}