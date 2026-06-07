using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RadiumLauncher.Models;

namespace RadiumLauncher.Views;

public partial class ConfigurationWindow : Window
{
    private const string SettingsFileName = "launcher-settings.json";
    private readonly string _configFolder = Path.Combine(AppConstants.AppDataDirectory, "Configuration");
    private readonly string _downloadedRadiumFolder = Path.Combine(AppConstants.AppDataDirectory, "Radium_PC");
    private readonly string _settingsPath;

    public ConfigurationWindow()
    {
        InitializeComponent();
        if (!Directory.Exists(_configFolder))
        {
            Directory.CreateDirectory(_configFolder);
        }

        if (!Directory.Exists(AppConstants.GameFolder))
        {
            Directory.CreateDirectory(AppConstants.GameFolder);
        }

        _settingsPath = Path.Combine(_configFolder, SettingsFileName);

        string protonPathFile = Path.Combine(_configFolder, "protonpath.txt");
        string launchOptionsFile = Path.Combine(_configFolder, "launchoptions.txt");
        string steamAppIdFile = Path.Combine(AppConstants.GameFolder, "steam_appid.txt");

        string currentProtonPath = File.Exists(protonPathFile) ? File.ReadAllText(protonPathFile) : string.Empty;
        string currentLaunchOptions =
            File.Exists(launchOptionsFile) ? File.ReadAllText(launchOptionsFile) : "%command%";

        if (!File.Exists(steamAppIdFile))
        {
            File.WriteAllText(steamAppIdFile, "471710");
        }
        
        Protonpathtb.Text = currentProtonPath;
        Launchoptstb.Text = currentLaunchOptions;
        Steamappidtb.Text = File.ReadAllText(Path.Combine(AppConstants.GameFolder, "steam_appid.txt")).Trim();
        
        LoadSettings();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ProtonOption.IsVisible = true;
            AdvancedOptions.IsVisible = false; // replace when linux gets an advanced feature
        }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = new LauncherSettings();
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? settings;
            }

            ScreenBatchFileTb.Text = string.IsNullOrWhiteSpace(settings.ScreenModeBatchFile)
                ? "RecRoom_ScreenMode.bat"
                : settings.ScreenModeBatchFile;
            VrBatchFileTb.Text = string.IsNullOrWhiteSpace(settings.VrModeBatchFile)
                ? "RecRoom_VR.bat"
                : settings.VrModeBatchFile;
            Threadcountnud.Value = settings.DlThreadCount;
            DiscordRPCOption.IsChecked = settings.DiscordRpcEnabled;
            Usernameopttb.Text = settings.RadiumUsername;
        }
        catch
        {
            ScreenBatchFileTb.Text = "RecRoom_ScreenMode.bat";
            VrBatchFileTb.Text = "RecRoom_VR.bat";
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new LauncherSettings();
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? settings;
            }

            settings.ScreenModeBatchFile = ScreenBatchFileTb.Text ?? "RecRoom_ScreenMode.bat";
            settings.VrModeBatchFile = VrBatchFileTb.Text ?? "RecRoom_VR.bat";
            settings.DlThreadCount = Threadcountnud.Value ?? 8;
            settings.DiscordRpcEnabled = DiscordRPCOption.IsChecked;
            settings.RadiumUsername = Usernameopttb.Text;
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ignore write failures for config settings
        }
    }

    private void ThreadCount_Changed(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void ProtonPath_Changed(object? sender, RoutedEventArgs e)
    {
        File.WriteAllText(Path.Combine(_configFolder, "protonpath.txt"), Protonpathtb.Text ?? string.Empty);
    }

    private void SteamAppId_Changed(object? sender, RoutedEventArgs e)
    {
        File.WriteAllText(Path.Combine(AppConstants.GameFolder, "steam_appid.txt"), Steamappidtb.Text);
        AppConstants.SteamAppId = Steamappidtb.Text;
    }

    private void LaunchOptions_Changed(object? sender, RoutedEventArgs e)
    {
        File.WriteAllText(Path.Combine(_configFolder, "launchoptions.txt"), Launchoptstb.Text ?? "%command%");
    }

    private void ScreenBatchFile_Changed(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void VrBatchFile_Changed(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private async void UninstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_downloadedRadiumFolder))
        {
            await new MessageBoxWindow("Uninstall", "No downloaded Radium installation was found in the launcher folder.", null)
                .ShowDialog(this);
            return;
        }

        var confirm = new ConfirmationWindow(
            "Confirm Uninstall",
            $"This will delete the launcher-downloaded Radium installation at:\n{_downloadedRadiumFolder}\n\nThis does not remove any other Radium installation on your system.",
            "Delete",
            "Cancel");

        var result = await confirm.ShowDialog<bool?>(this);
        if (result != true)
        {
            return;
        }

        try
        {
            Directory.Delete(_downloadedRadiumFolder, true);
            await new MessageBoxWindow("Uninstall Complete", "Downloaded Radium has been removed. Please restart the launcher if you wish to reinstall.", null)
                .ShowDialog(this);
        }
        catch (Exception ex)
        {
            await new MessageBoxWindow("Uninstall Failed", $"Could not remove downloaded Radium: {ex.Message}", null)
                .ShowDialog(this);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DiscordRPCOption_Checked(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void Username_Changed(object? sender, TextChangedEventArgs e)
    {
        SaveSettings();
    }
}