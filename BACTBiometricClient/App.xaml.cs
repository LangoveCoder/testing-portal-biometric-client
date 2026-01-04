using System.Windows;
using BACTBiometricClient.Services;
using BACTBiometricClient.Views;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Initialize database on startup
                var database = new DatabaseService();
                database.InitializeDatabase();

                Logger.Info("Application started");
                Logger.Info("Database initialized successfully");

                // Show login window
                var loginWindow = new LoginWindow();
                loginWindow.Show();
            }
            catch (System.Exception ex)
            {
                Logger.Error("Application startup failed", ex);
                MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("Application closing");
            base.OnExit(e);
        }
    }
}