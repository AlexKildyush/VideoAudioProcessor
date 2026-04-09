using System.IO;
using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;
using VideoAudioProcessor.Infrastructure.Theming;

namespace VideoAudioProcessor.View;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppTheme currentTheme, string rootPath)
    {
        InitializeComponent();

        LightThemeCheckBox.IsChecked = currentTheme == AppTheme.Light;
        DarkThemeCheckBox.IsChecked = currentTheme == AppTheme.Dark;
        RootPathTextBox.Text = rootPath;
    }

    public AppTheme SelectedTheme => DarkThemeCheckBox.IsChecked == true ? AppTheme.Dark : AppTheme.Light;
    public string SelectedRootPath => RootPathTextBox.Text.Trim();

    private void BrowseRootPathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog
        {
            IsFolderPicker = true,
            Title = "Выберите корневую папку"
        };

        if (!string.IsNullOrWhiteSpace(RootPathTextBox.Text) && Directory.Exists(RootPathTextBox.Text))
        {
            dialog.InitialDirectory = RootPathTextBox.Text;
            dialog.DefaultDirectory = RootPathTextBox.Text;
        }

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            RootPathTextBox.Text = dialog.FileName;
        }
    }

    private void LightThemeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (DarkThemeCheckBox != null)
        {
            DarkThemeCheckBox.IsChecked = false;
        }
    }

    private void LightThemeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (DarkThemeCheckBox != null && DarkThemeCheckBox.IsChecked != true)
        {
            LightThemeCheckBox.IsChecked = true;
        }
    }

    private void DarkThemeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (LightThemeCheckBox != null)
        {
            LightThemeCheckBox.IsChecked = false;
        }
    }

    private void DarkThemeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (LightThemeCheckBox != null && LightThemeCheckBox.IsChecked != true)
        {
            DarkThemeCheckBox.IsChecked = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            MessageBox.Show("Укажите корневую папку для хранения данных.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
