using System.Windows;

namespace LoL_Announcer_Editor
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Backup current settings before showing dialog
            SettingsManager.BackupSettings();
            
            // Load current settings into UI
            chkPlayOnClick.IsChecked = SettingsManager.PlayOnClick;
            chkExtractOgg.IsChecked = SettingsManager.ExtractOgg;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            // Validate settings
            if (!ValidateSettings())
            {
                return;
            }

            // Save settings to memory
            SettingsManager.PlayOnClick = chkPlayOnClick.IsChecked == true;
            SettingsManager.ExtractOgg = chkExtractOgg.IsChecked == true;
            
            // Save settings to file
            SettingsManager.SaveSettings();
            
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Restore settings from backup (undo any changes)
            SettingsManager.RestoreSettings();
            
            this.DialogResult = false;
            this.Close();
        }

        private bool ValidateSettings()
        {
            // No validation needed since we always extract WEM files
            // and OGG extraction is optional
            return true;
        }

        private void btnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            // Reset settings to defaults
            SettingsManager.ResetToDefaults();
            
            // Update UI to reflect defaults
            chkPlayOnClick.IsChecked = SettingsManager.PlayOnClick;
            chkExtractOgg.IsChecked = SettingsManager.ExtractOgg;
        }
    }
} 
