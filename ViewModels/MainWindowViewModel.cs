using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadiumLauncher.Models;
using RadiumLauncher.Services;

namespace RadiumLauncher.ViewModels;

public enum LauncherState
{
    Ready,
    NeedsDownload,
    NeedsUpdate,
    NeedsPatching,
    Unavailable,
    Downloading,
    Verifying,
    Extracting,
    Patching,
    Running
}

public partial class MainWindowViewModel : ViewModelBase
{
    public string OperatingSystemName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown";

    [ObservableProperty] private Bitmap? _oSLogo;
    [ObservableProperty] private Bitmap? _mainLogo;
    public string OsVersion => Environment.OSVersion.VersionString;

    [ObservableProperty] private string _selectedLaunchMode = "Screen";
    [ObservableProperty] private bool _screenIconVisible = true;
    [ObservableProperty] private bool _vrIconVisible;
    [ObservableProperty] private LauncherState _currentState = LauncherState.Unavailable;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _progressDetails = string.Empty;
    [ObservableProperty] private string _etaText = string.Empty;
    [ObservableProperty] private string? _infoResponse;
    [ObservableProperty] private string _screenModeBatchFile = "RecRoom_ScreenMode.bat";
    [ObservableProperty] private string _vrModeBatchFile = "RecRoom_VR.bat";

    public string GameFolder { get; set; } = string.Empty;

    public ObservableCollection<string> Announcements { get; } = new();
    public Process? GameProcess { get; set; }

    public ObservableCollection<FeedItem> CommunityMediaItems { get; } = new();

    [ObservableProperty] private FeedItem? _maximizedPhoto;
    [ObservableProperty] private bool _isPhotoMaximized;

    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string? _latestVersion;
    [ObservableProperty] private string? _updateUrl;
    [ObservableProperty] private bool _showUpdateButton;
    [ObservableProperty] private bool _showUpdatePopup;

    public MainWindowViewModel()
    {
        _ = CheckForUpdatesAsync();
    }

    [RelayCommand]
    private void SetLaunchMode(string mode)
    {
        SelectedLaunchMode = mode;
        ScreenIconVisible = mode == "Screen";
        VrIconVisible = mode == "VR";
    }

    [RelayCommand]
    private async Task MaximizePhoto(FeedItem item)
    {
        if (MaximizedPhoto != null) return;
        MaximizedPhoto = item;
        IsPhotoMaximized = true;
        await item.LoadLargeImage();
    }

    [RelayCommand]
    private async Task CloseMaximizedPhoto()
    {
        IsPhotoMaximized = false;
        await Task.Delay(300);
        MaximizedPhoto = null;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            var current = entry?.GetName().Version?.ToString() ?? "0.0.0";
            var svc = new UpdateService();
            var (available, latest, url) = await svc.CheckLatestReleaseAsync(current);

            LatestVersion = latest;
            UpdateUrl = url;
            IsUpdateAvailable = available;

            if (available)
            {
                ShowUpdatePopup = true;
                ShowUpdateButton = false;
            }
            else
            {
                ShowUpdatePopup = false;
                ShowUpdateButton = false;
            }
        }
        catch
        {
            // ignore failures
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (string.IsNullOrWhiteSpace(UpdateUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        ShowUpdatePopup = false;
        ShowUpdateButton = true;
    }
}