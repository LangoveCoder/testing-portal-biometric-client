using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BACTBiometricClient.Services;
using BACTBiometricClient.Helpers;
using Newtonsoft.Json;

namespace BACTBiometricClient.Views
{
    public partial class TestsWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly string _authToken;
        private readonly string _apiBaseUrl;
        private List<TestInfo> _allTests = new List<TestInfo>();
        private List<CollegeInfo> _colleges = new List<CollegeInfo>();

        public TestInfo? SelectedTest { get; private set; }

        public TestsWindow(string authToken, string apiBaseUrl = "http://192.168.100.89:8000/api/v1")
        {
            // Initialize UI first
            InitializeComponent();
            
            // Set basic properties
            _authToken = authToken ?? "";
            _apiBaseUrl = apiBaseUrl ?? "http://192.168.100.89:8000/api/v1";
            
            // Initialize HTTP client
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Set authorization header if token is provided
            if (!string.IsNullOrEmpty(_authToken))
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");
            }

            // Set up event handler
            Loaded += TestsWindow_Loaded;
        }

        private async void TestsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadTestsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading tests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Error loading tests");
            }
        }

        private async Task LoadTestsAsync()
        {
            try
            {
                SetStatus("Loading available tests...");
                loadingPanel.Visibility = Visibility.Visible;
                noTestsPanel.Visibility = Visibility.Collapsed;
                ClearTestsPanel();

                // Load colleges first for filter
                await LoadCollegesAsync();

                // Load tests
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/operator/tests/available");
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseText);
                    if (apiResponse?.Success == true && apiResponse.Data?.Tests != null)
                    {
                        _allTests = apiResponse.Data.Tests;
                        DisplayTests(_allTests);
                        UpdateTestCount(_allTests.Count);
                        SetStatus($"Loaded {_allTests.Count} available tests");
                        txtLastUpdated.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
                    }
                    else
                    {
                        ShowNoTests("No tests found in response");
                        SetStatus("No tests available");
                    }
                }
                else
                {
                    ShowNoTests($"Failed to load tests: {response.StatusCode}");
                    SetStatus($"Error loading tests: {response.StatusCode}");
                    Logger.Error($"Failed to load tests: {response.StatusCode} - {responseText}");
                }
            }
            catch (Exception ex)
            {
                ShowNoTests($"Error loading tests: {ex.Message}");
                SetStatus("Error loading tests");
                Logger.Error("Error loading tests", ex);
            }
            finally
            {
                loadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadCollegesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/operator/colleges");
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<CollegesResponse>(responseText);
                    if (apiResponse?.Success == true && apiResponse.Data?.Colleges != null)
                    {
                        _colleges = apiResponse.Data.Colleges;
                        PopulateCollegeFilter();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading colleges for filter", ex);
            }
        }

        private void PopulateCollegeFilter()
        {
            cmbCollegeFilter.Items.Clear();
            cmbCollegeFilter.Items.Add(new ComboBoxItem { Content = "All Colleges", Tag = null });

            foreach (var college in _colleges)
            {
                cmbCollegeFilter.Items.Add(new ComboBoxItem 
                { 
                    Content = college.Name, 
                    Tag = college.Id 
                });
            }

            cmbCollegeFilter.SelectedIndex = 0;
        }

        private void DisplayTests(List<TestInfo> tests)
        {
            ClearTestsPanel();

            if (tests == null || !tests.Any())
            {
                ShowNoTests("No tests match the current filter");
                return;
            }

            foreach (var test in tests)
            {
                var testCard = CreateTestCard(test);
                testsPanel.Children.Add(testCard);
            }
        }

        private Border CreateTestCard(TestInfo test)
        {
            var card = new Border();
            card.Style = (Style)FindResource("TestCard");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Main content
            var mainContent = new StackPanel();
            Grid.SetColumn(mainContent, 0);

            // Test name and category
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            var testName = new TextBlock
            {
                Text = test.Name,
                Style = (Style)FindResource("HeaderText")
            };
            headerPanel.Children.Add(testName);

            var categoryBadge = new Border
            {
                Style = (Style)FindResource("StatusBadge"),
                Background = GetCategoryColor(test.TestCategory),
                Child = new TextBlock
                {
                    Text = test.TestCategory.ToUpper(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            };
            headerPanel.Children.Add(categoryBadge);

            mainContent.Children.Add(headerPanel);

            // Test details
            var detailsGrid = new Grid();
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new StackPanel();
            Grid.SetColumn(leftPanel, 0);

            leftPanel.Children.Add(new TextBlock { Text = $"📅 Date: {DateTime.Parse(test.TestDate):MMM dd, yyyy}", Style = (Style)FindResource("InfoText") });
            leftPanel.Children.Add(new TextBlock { Text = $"🕒 Time: {test.TestTime}", Style = (Style)FindResource("InfoText") });
            leftPanel.Children.Add(new TextBlock { Text = $"🏛️ Owner: {test.OwnerName}", Style = (Style)FindResource("InfoText") });
            
            if (!string.IsNullOrEmpty(test.CollegeName))
            {
                leftPanel.Children.Add(new TextBlock { Text = $"🎓 College: {test.CollegeName}", Style = (Style)FindResource("InfoText") });
            }
            
            if (!string.IsNullOrEmpty(test.DepartmentName))
            {
                leftPanel.Children.Add(new TextBlock { Text = $"🏢 Department: {test.DepartmentName}", Style = (Style)FindResource("InfoText") });
            }

            var rightPanel = new StackPanel();
            Grid.SetColumn(rightPanel, 1);

            rightPanel.Children.Add(new TextBlock { Text = $"👥 Total Students: {test.TotalStudents}", Style = (Style)FindResource("InfoText") });
            rightPanel.Children.Add(new TextBlock { Text = $"✅ Attendance Marked: {test.AttendanceMarked}", Style = (Style)FindResource("InfoText") });
            rightPanel.Children.Add(new TextBlock { Text = $"⏳ Pending: {test.AttendancePending}", Style = (Style)FindResource("InfoText") });

            // Progress bar
            var progressPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
            var progressBar = new ProgressBar
            {
                Height = 6,
                Maximum = test.TotalStudents,
                Value = test.AttendanceMarked,
                Background = Brushes.LightGray,
                Foreground = Brushes.Green
            };
            progressPanel.Children.Add(progressBar);
            
            var progressText = new TextBlock
            {
                Text = $"{(test.TotalStudents > 0 ? (test.AttendanceMarked * 100.0 / test.TotalStudents):0):F1}% Complete",
                FontSize = 10,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0)
            };
            progressPanel.Children.Add(progressText);
            rightPanel.Children.Add(progressPanel);

            detailsGrid.Children.Add(leftPanel);
            detailsGrid.Children.Add(rightPanel);
            mainContent.Children.Add(detailsGrid);

            // Action buttons
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(actionPanel, 1);

            var selectButton = new Button
            {
                Content = "📋 Select Test",
                Style = (Style)FindResource("ActionButton"),
                Tag = test
            };
            selectButton.Click += SelectTest_Click;
            actionPanel.Children.Add(selectButton);

            var viewButton = new Button
            {
                Content = "👁️ View Details",
                Style = (Style)FindResource("ActionButton"),
                Background = Brushes.Gray,
                Tag = test
            };
            viewButton.Click += ViewTestDetails_Click;
            actionPanel.Children.Add(viewButton);

            grid.Children.Add(mainContent);
            grid.Children.Add(actionPanel);
            card.Child = grid;

            return card;
        }

        private Brush GetCategoryColor(string category)
        {
            return category?.ToLower() switch
            {
                "college" => new SolidColorBrush(Color.FromRgb(33, 150, 243)), // Blue
                "departmental" => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                "recruitment" => new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange
                _ => new SolidColorBrush(Color.FromRgb(158, 158, 158)) // Gray
            };
        }

        private void SelectTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TestInfo test)
            {
                SelectedTest = test;
                SetStatus($"Selected test: {test.Name}");
                
                var result = MessageBox.Show(
                    $"You have selected:\n\n" +
                    $"Test: {test.Name}\n" +
                    $"Date: {DateTime.Parse(test.TestDate):MMM dd, yyyy}\n" +
                    $"Time: {test.TestTime}\n" +
                    $"Students: {test.TotalStudents}\n\n" +
                    $"Do you want to proceed with this test?",
                    "Confirm Test Selection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void ViewTestDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TestInfo test)
            {
                var details = $"Test Details:\n\n" +
                             $"ID: {test.Id}\n" +
                             $"Name: {test.Name}\n" +
                             $"Category: {test.TestCategory}\n" +
                             $"Owner: {test.OwnerName}\n" +
                             $"Date: {DateTime.Parse(test.TestDate):MMM dd, yyyy}\n" +
                             $"Time: {test.TestTime}\n" +
                             $"Total Students: {test.TotalStudents}\n" +
                             $"Attendance Marked: {test.AttendanceMarked}\n" +
                             $"Attendance Pending: {test.AttendancePending}\n";

                if (!string.IsNullOrEmpty(test.CollegeName))
                    details += $"College: {test.CollegeName}\n";
                
                if (!string.IsNullOrEmpty(test.DepartmentName))
                    details += $"Department: {test.DepartmentName}\n";

                MessageBox.Show(details, "Test Details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CmbCollegeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbCollegeFilter.SelectedItem is ComboBoxItem selectedItem)
            {
                var collegeId = selectedItem.Tag as int?;
                
                List<TestInfo> filteredTests;
                if (collegeId.HasValue)
                {
                    filteredTests = _allTests.Where(t => t.CollegeId == collegeId.Value).ToList();
                    SetStatus($"Filtered by college: {selectedItem.Content}");
                }
                else
                {
                    filteredTests = _allTests;
                    SetStatus("Showing all tests");
                }

                DisplayTests(filteredTests);
                UpdateTestCount(filteredTests.Count);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTestsAsync();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Close the application when tests window is closed
            Application.Current.Shutdown();
        }

        private void ClearTestsPanel()
        {
            // Remove all test cards but keep loading and no tests panels
            var itemsToRemove = testsPanel.Children.OfType<Border>()
                .Where(b => b != loadingPanel && b != noTestsPanel)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                testsPanel.Children.Remove(item);
            }
        }

        private void ShowNoTests(string message)
        {
            ClearTestsPanel();
            noTestsPanel.Visibility = Visibility.Visible;
            
            // Update the no tests message
            if (noTestsPanel.Child is StackPanel panel)
            {
                var textBlocks = panel.Children.OfType<TextBlock>().ToList();
                if (textBlocks.Count > 2)
                {
                    textBlocks[2].Text = message;
                }
            }
        }

        private void UpdateTestCount(int count)
        {
            try
            {
                if (txtTestCount != null)
                {
                    txtTestCount.Text = count == 1 ? "1 test available" : $"{count} tests available";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating test count: {ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            try
            {
                if (txtStatus != null)
                {
                    txtStatus.Text = message;
                }
            }
            catch (Exception ex)
            {
                // Fail silently for UI updates
                System.Diagnostics.Debug.WriteLine($"Error setting status: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _httpClient?.Dispose();
            base.OnClosed(e);
        }

        // Data models
        public class ApiResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public TestsData Data { get; set; }
        }

        public class TestsData
        {
            public List<TestInfo> Tests { get; set; }
        }

        public class TestInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            [JsonProperty("test_category")]
            public string TestCategory { get; set; }
            [JsonProperty("owner_name")]
            public string OwnerName { get; set; }
            [JsonProperty("test_date")]
            public string TestDate { get; set; }
            [JsonProperty("test_time")]
            public string TestTime { get; set; }
            [JsonProperty("college_id")]
            public int? CollegeId { get; set; }
            [JsonProperty("college_name")]
            public string CollegeName { get; set; }
            [JsonProperty("department_id")]
            public int? DepartmentId { get; set; }
            [JsonProperty("department_name")]
            public string DepartmentName { get; set; }
            [JsonProperty("total_students")]
            public int TotalStudents { get; set; }
            [JsonProperty("attendance_marked")]
            public int AttendanceMarked { get; set; }
            [JsonProperty("attendance_pending")]
            public int AttendancePending { get; set; }
        }

        public class CollegesResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public CollegesData Data { get; set; }
        }

        public class CollegesData
        {
            public List<CollegeInfo> Colleges { get; set; }
        }

        public class CollegeInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string District { get; set; }
        }
    }
}