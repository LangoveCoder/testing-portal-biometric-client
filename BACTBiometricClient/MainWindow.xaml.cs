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

        public MainWindow(AuthService authService, DatabaseService database, ApiService apiService)
        {
            InitializeComponent();

            _authService = authService;
            _database = database;
            _apiService = apiService;

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

            // Update header with user info
            txtUserName.Text = user.Name;
            txtUserRole.Text = user.Role.ToUpper();

            // Get auth token from AuthService
            string authToken = _authService.GetAuthToken();

            Logger.Info($"Loading UI for: {user.Name} ({user.Role})");

            // Show/hide tabs based on role
            if (user.IsOperator)
            {
                // Operator: Show Registration tab only
                tabRegistration.Visibility = Visibility.Visible;
                tabVerification.Visibility = Visibility.Collapsed;
                mainTabControl.SelectedItem = tabRegistration;

                // Load Registration Tab UserControl
                var registrationTab = new RegistrationTab();

                // Pass auth token to registration tab
                registrationTab.SetAuthToken(authToken, user.Name, user.Role);

                tabRegistration.Content = registrationTab;

                Logger.Info("Loaded Registration Tab for Operator");
            }
            else if (user.IsCollegeAdmin)
            {
                // College Admin: Show Verification tab only
                tabRegistration.Visibility = Visibility.Collapsed;
                tabVerification.Visibility = Visibility.Visible;
                mainTabControl.SelectedItem = tabVerification;

                // Load Verification Tab UserControl
                var verificationTab = new VerificationTab();

                // Pass auth token to verification tab
                verificationTab.SetAuthToken(authToken, user.Name, user.Role);

                tabVerification.Content = verificationTab;

                Logger.Info("Loaded Verification Tab for College Admin");
            }
            else
            {
                // Unknown role or SuperAdmin: Show both tabs
                tabRegistration.Visibility = Visibility.Visible;
                tabVerification.Visibility = Visibility.Visible;

                // Load Registration Tab
                var registrationTab = new RegistrationTab();
                registrationTab.SetAuthToken(authToken, user.Name, user.Role);
                tabRegistration.Content = registrationTab;

                // Load Verification Tab
                var verificationTab = new VerificationTab();
                verificationTab.SetAuthToken(authToken, user.Name, user.Role);
                tabVerification.Content = verificationTab;

                Logger.Info("Loaded both tabs for user with role: " + user.Role);
            }
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
                var (registrations, verifications) = _database.GetPendingCounts();
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