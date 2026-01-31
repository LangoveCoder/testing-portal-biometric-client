using System.Windows;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;
using BACTBiometricClient.Views;

namespace BACTBiometricClient
{
    public partial class MainWindow : Window
    {
        private readonly AuthService _authService;
        private readonly DatabaseService _database;
        private readonly ApiService _apiService;
        private readonly RoleBasedUIService _roleBasedUI;

        public MainWindow(AuthService authService, DatabaseService database, ApiService apiService)
        {
            InitializeComponent();

            _authService = authService;
            _database = database;
            _apiService = apiService;
            _roleBasedUI = new RoleBasedUIService(_database);

            Logger.Info("Main window opened");

            LoadUserInterface();
            UpdateConnectionStatus();
            UpdatePendingCounts();
        }

        private void LoadUserInterface()
        {
            var user = _authService.CurrentUser;

            if (user == null)
            {
                MessageBox.Show("No user session found", "Error");
                Application.Current.Shutdown();
                return;
            }

            // Set user for role-based UI service
            _roleBasedUI.SetCurrentUser(user);

            // Update header with user info
            txtUserName.Text = user.Name;
            txtUserRole.Text = user.Role.ToUpper();

            // Get auth token from AuthService
            string authToken = _authService.GetAuthToken();

            Logger.Info($"Loading UI for: {user.Name} ({user.Role})");

            // Configure tabs based on role using RoleBasedUIService
            _roleBasedUI.ConfigureUIForRole(mainTabControl, tabRegistration, tabVerification);

            // Load appropriate tab content based on role
            if (user.IsOperator)
            {
                // Load Registration Tab for Operators
                var registrationTab = new RegistrationTab();
                registrationTab.SetAuthToken(authToken, user.Name, user.Role);
                
                // Pass role-based UI service for college selection
                registrationTab.SetRoleBasedUIService(_roleBasedUI);
                
                tabRegistration.Content = registrationTab;
                Logger.Info("Loaded Registration Tab for Operator with college selection");
            }
            else if (user.IsCollegeAdmin)
            {
                // Load Verification Tab for College Admins
                var verificationTab = new VerificationTab();
                verificationTab.SetAuthToken(authToken, user.Name, user.Role);
                
                // Pass role-based UI service for college filtering
                verificationTab.SetRoleBasedUIService(_roleBasedUI);
                
                tabVerification.Content = verificationTab;
                Logger.Info("Loaded Verification Tab for College Admin with college restrictions");
            }
            else
            {
                // Unknown role or SuperAdmin: Load both tabs
                var registrationTab = new RegistrationTab();
                registrationTab.SetAuthToken(authToken, user.Name, user.Role);
                registrationTab.SetRoleBasedUIService(_roleBasedUI);
                tabRegistration.Content = registrationTab;

                var verificationTab = new VerificationTab();
                verificationTab.SetAuthToken(authToken, user.Name, user.Role);
                verificationTab.SetRoleBasedUIService(_roleBasedUI);
                tabVerification.Content = verificationTab;

                Logger.Info("Loaded both tabs for user with role: " + user.Role);
            }

            // Update status message with role-specific welcome
            txtStatusMessage.Text = _roleBasedUI.GetWelcomeMessage();
        }

        private async void UpdateConnectionStatus()
        {
            try
            {
                bool isConnected = await InternetChecker.IsConnectedAsync();

                if (isConnected)
                {
                    txtConnectionStatus.Text = "ONLINE";
                    statusIndicator.Fill = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    txtConnectionStatus.Text = "OFFLINE";
                    statusIndicator.Fill = System.Windows.Media.Brushes.Orange;
                }
            }
            catch
            {
                txtConnectionStatus.Text = "OFFLINE";
                statusIndicator.Fill = System.Windows.Media.Brushes.Red;
            }
        }

        private void UpdatePendingCounts()
        {
            try
            {
                var (registrations, verifications, totalOperations) = _database.GetPendingCounts();
                int total = registrations + verifications;

                txtPendingCount.Text = total == 0 ? "✓ SYNCED" : $"{total} PENDING";

                if (total > 0)
                {
                    txtStatusMessage.Text = $"{registrations} registrations, {verifications} verifications pending sync";
                }
                else
                {
                    txtStatusMessage.Text = "System Ready • All Services Running";
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("Failed to get pending counts", ex);
            }
        }

        private void BtnApiTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var apiTestWindow = new ApiTestWindow();
                apiTestWindow.Show();
                Logger.Info("API Test Console opened");
            }
            catch (System.Exception ex)
            {
                Logger.Error("Failed to open API Test Console", ex);
                MessageBox.Show("Failed to open API Test Console: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to logout?",
                "Logout Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                Logger.Info("User logged out");
                _authService.Logout();

                var loginWindow = new LoginWindow();
                loginWindow.Show();
                this.Close();
            }
        }
    }
}