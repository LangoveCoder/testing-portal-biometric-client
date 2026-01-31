using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;
using Newtonsoft.Json;

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
            _apiUrl = "http://192.168.100.89:8000/api/v1"; // Updated to working API URL
            _apiService = new ApiService(_apiUrl);

            _authService = new AuthService(_database);

            LoadSettings();
            CheckConnectionStatus();

            // Clear email field for actual credentials
            txtEmail.Text = "";
            
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
            try
            {
                Logger.Debug("Login button clicked");
                await LoginAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Error in BtnLogin_Click: " + ex.Message);
                MessageBox.Show($"Error in login button click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task LoginAsync()
        {
            try
            {
                Logger.Debug("LoginAsync started");
                
                string email = txtEmail.Text.Trim();
                string password = GetCurrentPassword();

                Logger.Debug($"Email: '{email}', Password length: {password?.Length ?? 0}");

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

                Logger.Info($"Attempting login for: {email}");

                // Try Laravel API login using the working implementation from ApiTestWindow
                try
                {
                    var loginData = new
                    {
                        email = email,
                        password = password,
                        device_info = "Windows 10 Pro - BACT Biometric Client v1.0"
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(loginData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Set headers
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                    var loginEndpoint = $"{_apiUrl}/auth/login";
                    Logger.Info($"Calling API: {loginEndpoint}");

                    var response = await _httpClient.PostAsync(loginEndpoint, content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    Logger.Debug($"API Response Status: {response.StatusCode}");
                    Logger.Debug($"API Response: {responseText.Substring(0, Math.Min(200, responseText.Length))}...");

                    if (response.IsSuccessStatusCode)
                    {
                        // Parse response using Newtonsoft.Json like the working ApiTestWindow
                        var formattedJson = Newtonsoft.Json.JsonConvert.DeserializeObject(responseText);
                        
                        // Extract token using the same logic as ApiTestWindow
                        string extractedToken = null;
                        
                        try
                        {
                            var loginResponse = formattedJson as Newtonsoft.Json.Linq.JObject;
                            if (loginResponse != null)
                            {
                                // Try data.token first
                                extractedToken = loginResponse["data"]?["token"]?.ToString();
                                
                                // If not found, try token directly
                                if (string.IsNullOrEmpty(extractedToken))
                                {
                                    extractedToken = loginResponse["token"]?.ToString();
                                }
                                
                                // If still not found, try access_token
                                if (string.IsNullOrEmpty(extractedToken))
                                {
                                    extractedToken = loginResponse["access_token"]?.ToString();
                                }

                                // Extract user info
                                string userName = "Hassan Sarparrah"; // Default
                                string userRole = "operator"; // Default
                                
                                // Try to get user info from response
                                var userInfo = loginResponse["data"]?["user"] ?? loginResponse["user"];
                                if (userInfo != null)
                                {
                                    var nameFromApi = userInfo["name"]?.ToString();
                                    if (!string.IsNullOrEmpty(nameFromApi))
                                    {
                                        userName = nameFromApi;
                                    }
                                    
                                    var roleFromApi = userInfo["role"]?.ToString();
                                    if (!string.IsNullOrEmpty(roleFromApi))
                                    {
                                        userRole = roleFromApi;
                                    }
                                }

                                if (!string.IsNullOrEmpty(extractedToken))
                                {
                                    AuthToken = extractedToken;
                                    LoginSuccessful = true;
                                    UserName = userName;
                                    UserEmail = email;
                                    UserRole = userRole;

                                    // Save credentials if remember me is checked
                                    try
                                    {
                                        var settings = _database.GetAppSettings();
                                        settings.RememberCredentials = chkRememberMe.IsChecked == true;
                                        settings.LastLoginEmail = email;
                                        _database.SaveAppSettings(settings);
                                        Logger.Info($"✓ Settings saved successfully");
                                    }
                                    catch (Exception settingsEx)
                                    {
                                        Logger.Error("Could not save settings", settingsEx);
                                        // Continue anyway - login was successful, just couldn't save settings
                                    }

                                    // Save to AuthService
                                    try
                                    {
                                        await _authService.SaveAuthTokenAsync(extractedToken, UserName, UserEmail, UserRole);
                                        Logger.Info($"✓ Auth token saved successfully");
                                    }
                                    catch (Exception authEx)
                                    {
                                        Logger.Error("Failed to save auth token to database", authEx);
                                        // Continue anyway - login was successful, just couldn't save to local DB
                                    }

                                    // Show success message and open Tests window
                                    Logger.Info($"✓ API login successful: {UserName} ({UserRole})");
                                    
                                    try
                                    {
                                        // Open the actual TestsWindow with authentication token
                                        var testsWindow = new TestsWindow(extractedToken, _apiUrl);
                                        testsWindow.Show();
                                        
                                        // Hide the login window
                                        this.Hide();
                                        
                                        // Set up event handler to close login window when test window closes
                                        testsWindow.Closed += (s, args) => {
                                            Application.Current.Shutdown();
                                        };
                                    }
                                    catch (Exception testsEx)
                                    {
                                        MessageBox.Show($"Login successful but failed to open tests window:\n\n{testsEx.Message}", 
                                            "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    }
                                    
                                    return;
                                }
                                else
                                {
                                    Logger.Warning("Login successful but no token found in response");
                                    ShowError("Login successful but authentication token not received. Please try again.");
                                    return;
                                }
                            }
                        }
                        catch (Exception tokenEx)
                        {
                            Logger.Error("Token extraction error", tokenEx);
                            Logger.Debug($"Response that failed to parse: {responseText}");
                            ShowError($"Login response parsing error. Please try again.\n\nAPI Response: {responseText.Substring(0, Math.Min(100, responseText.Length))}...");
                            return;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        ShowError("Invalid email or password.");
                        Logger.Warning("Login failed: Invalid credentials");
                        return;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        // Try to get error message from response
                        try
                        {
                            var errorResponse = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(responseText);
                            string errorMessage = errorResponse.TryGetProperty("message", out var msg)
                                ? msg.GetString()
                                : "Account is deactivated";
                            ShowError(errorMessage);
                        }
                        catch
                        {
                            ShowError("Account access is restricted. Please contact administrator.");
                        }
                        Logger.Warning("Login failed: Account forbidden");
                        return;
                    }
                    else
                    {
                        ShowError($"Server error ({response.StatusCode}). Please try again later.");
                        Logger.Error($"Login failed with status: {response.StatusCode}");
                        return;
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    ShowError("Cannot connect to server. Please check your internet connection and try again.");
                    Logger.Error("Login HTTP error", httpEx);
                }
                catch (TaskCanceledException)
                {
                    ShowError("Login request timed out. Please try again.");
                    Logger.Error("Login request timed out");
                }
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
            borderStatus.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            borderStatus.Visibility = Visibility.Collapsed;
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
                btnLogin.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "SIGNING IN...", Margin = new Thickness(0,0,8,0) }
                    }
                };
            }
            else
            {
                btnLogin.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "SIGN IN", Margin = new Thickness(0,0,8,0) },
                        new TextBlock { Text = "→", FontSize = 16 }
                    }
                };
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