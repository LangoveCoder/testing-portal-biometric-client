using System;
using System.Windows;
using System.Windows.Media;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Views
{
    public partial class VerificationDecisionDialog : Window
    {
        public VerificationDecision Decision { get; private set; }
        
        private readonly Student _student;
        private readonly int _matchScore;
        private readonly string _systemRecommendation;
        private readonly bool _systemMatch;
        private string _selectedDecision = "";

        public VerificationDecisionDialog(Student student, int matchScore, string systemRecommendation, bool systemMatch)
        {
            InitializeComponent();
            
            _student = student;
            _matchScore = matchScore;
            _systemRecommendation = systemRecommendation;
            _systemMatch = systemMatch;
            
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            // Set student information
            txtStudentInfo.Text = $"Student: {_student.Name} | Roll: {_student.RollNumber}";
            
            // Set verification results
            txtMatchScore.Text = $"{_matchScore}%";
            txtRecommendation.Text = _systemRecommendation;
            
            // Color code the recommendation
            if (_systemRecommendation.StartsWith("ACCEPT"))
            {
                txtRecommendation.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            }
            else if (_systemRecommendation.StartsWith("REJECT"))
            {
                txtRecommendation.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            }
            else
            {
                txtRecommendation.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));
            }
            
            // Set initial button states
            UpdateButtonStates();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            _selectedDecision = "ACCEPT";
            CheckForManualOverride();
            UpdateButtonStates();
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            _selectedDecision = "REJECT";
            CheckForManualOverride();
            UpdateButtonStates();
        }

        private void CheckForManualOverride()
        {
            bool isOverride = false;
            string overrideMessage = "";

            // Check if user decision differs from system recommendation
            if (_selectedDecision == "ACCEPT" && _systemRecommendation.StartsWith("REJECT"))
            {
                isOverride = true;
                overrideMessage = "You are ACCEPTING a student that the system recommends to REJECT. Please provide justification:";
            }
            else if (_selectedDecision == "REJECT" && _systemRecommendation.StartsWith("ACCEPT"))
            {
                isOverride = true;
                overrideMessage = "You are REJECTING a student that the system recommends to ACCEPT. Please provide justification:";
            }
            else if (_selectedDecision == "ACCEPT" && _systemRecommendation.StartsWith("REVIEW"))
            {
                // Accepting a review case is not an override, but we might want notes
                isOverride = false;
            }
            else if (_selectedDecision == "REJECT" && _systemRecommendation.StartsWith("REVIEW"))
            {
                // Rejecting a review case is not an override, but we might want notes
                isOverride = false;
            }

            if (isOverride)
            {
                panelManualOverride.Visibility = Visibility.Visible;
                txtOverrideMessage.Text = overrideMessage;
                txtOverrideReason.Focus();
            }
            else
            {
                panelManualOverride.Visibility = Visibility.Collapsed;
                txtOverrideReason.Text = "";
                txtNotes.Text = "";
            }
        }

        private void UpdateButtonStates()
        {
            // Enable confirm button if decision is made
            bool canConfirm = !string.IsNullOrEmpty(_selectedDecision);
            
            // If manual override is visible, require override reason
            if (panelManualOverride.Visibility == Visibility.Visible)
            {
                canConfirm = canConfirm && !string.IsNullOrWhiteSpace(txtOverrideReason.Text);
            }
            
            btnConfirm.IsEnabled = canConfirm;
            
            // Update button appearance based on selection
            if (_selectedDecision == "ACCEPT")
            {
                btnAccept.Background = new SolidColorBrush(Color.FromRgb(27, 94, 32)); // Darker green
                btnReject.Background = new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Normal red
            }
            else if (_selectedDecision == "REJECT")
            {
                btnAccept.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Normal green
                btnReject.Background = new SolidColorBrush(Color.FromRgb(183, 28, 28)); // Darker red
            }
            else
            {
                btnAccept.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Normal green
                btnReject.Background = new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Normal red
            }
            
            // Clear validation message when decision changes
            txtValidationMessage.Text = "";
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedDecision))
            {
                txtValidationMessage.Text = "Please select ACCEPT or REJECT";
                return;
            }

            // Check if manual override requires reason
            if (panelManualOverride.Visibility == Visibility.Visible && 
                string.IsNullOrWhiteSpace(txtOverrideReason.Text))
            {
                txtValidationMessage.Text = "Override reason is required";
                txtOverrideReason.Focus();
                return;
            }

            // Create decision object
            Decision = new VerificationDecision
            {
                FinalDecision = _selectedDecision,
                IsManualOverride = panelManualOverride.Visibility == Visibility.Visible,
                OverrideReason = txtOverrideReason.Text.Trim(),
                Notes = txtNotes.Text.Trim(),
                DecisionTime = DateTime.Now,
                DecisionMaker = "College Admin" // This should come from current user context
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtOverrideReason_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                BtnCancel_Click(sender, e);
            }
        }
    }
}