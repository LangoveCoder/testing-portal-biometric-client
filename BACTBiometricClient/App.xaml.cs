using System.Windows;
using System.IO;
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
                // Initialize SQLite
                SQLitePCL.Batteries.Init();
                
                // Test database initialization with better error handling
                try 
                {
                    var database = new DatabaseService();
                    database.InitializeDatabaseAsync().GetAwaiter().GetResult();
                    Logger.Info("Database initialized successfully");
                }
                catch (System.Exception dbEx)
                {
                    Logger.Error("Database initialization failed", dbEx);
                    MessageBox.Show($"Database initialization failed:\n{dbEx.Message}\n\nThe app will continue but some features may not work properly.", 
                        "Database Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Show login window (restored normal flow)
                var loginWindow = new LoginWindow();
                loginWindow.Show();
                Logger.Info("Login window opened");
            }
            catch (System.Exception ex)
            {
                Logger.Error("Application startup failed", ex);
                MessageBox.Show($"Application startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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