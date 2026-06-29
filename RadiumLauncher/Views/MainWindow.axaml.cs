using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RadiumLauncher.Models;
using RadiumLauncher.ViewModels;
using Downloader;
using RadiumLauncher.Services;

namespace RadiumLauncher.Views;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new HttpClient();
    private int _imagesLoaded;
    private bool _isLoadingFeed;
    private const double HomeLogoMotionTimerIntervalMs = 30;
    private const double HomeLogoMotionSpeed = 2.0; // Increase for faster motion, decrease for slower
    private const double HomeLogoMotionLoopDuration = 5.0; // seconds per full loop
    private const double HomeLogoMotionAmplitudeX = 2.0;
    private const double HomeLogoMotionAmplitudeY = 0.8;
    private const double DefaultWindowSizeRatio = 0.8;
    private const double CommunityMediaHorizontalGap = 10;
    private const double CommunityMediaVerticalGap = 12;
    private const string SettingsFileName = "launcher-settings.json";
    private const string AnnouncementsUrl = "https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/LatestNews.txt";

    private readonly DispatcherTimer? _inactivityTimer;
    private readonly DispatcherTimer? _titleLogoAnimationTimer;
    private bool _isInitialized;
    private bool _isApplyingWindowBounds;
    private bool _hasUserDefinedWindowSize;
    private double _titleLogoAnimationTime;
    private MainWindowViewModel? _currentViewModel;
    private readonly string _configFolder = Path.Combine(AppConstants.AppDataDirectory, "Configuration");
    private string? _patchOnline;
    private string? _patchLocal;

    public MainWindow()
    {
        InitializeComponent();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "typxzero/RadiumLauncher");
        Opened += MainWindow_Opened;

        Closed += (_, _) =>
        {
            _titleLogoAnimationTimer?.Stop();
        };

        _titleLogoAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HomeLogoMotionTimerIntervalMs) };
        _titleLogoAnimationTimer.Tick += TitleLogoAnimationTimer_Tick;
        _titleLogoAnimationTimer.Start();

        DataContextChanged += MainWindow_DataContextChanged;
        SizeChanged += MainWindow_SizeChanged;

        var scrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
        if (scrollViewer != null) scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _inactivityTimer.Tick += InactivityTimer_Tick;
        _inactivityTimer.Start();

        PointerMoved += MainWindow_PointerMoved;
        PointerPressed += (_, _) => ResetInactivity();
        KeyDown += (_, _) => ResetInactivity();
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        var screen = Screens.ScreenFromVisual(this) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var settings = LoadLauncherSettings();
        var workingArea = screen.WorkingArea;
        var defaultWidth = Math.Max(MinWidth, Width);
        var defaultHeight = Math.Max(MinHeight, Height);
        _hasUserDefinedWindowSize = settings.WindowSizeSetByUserResize;
        var width = settings.WindowSizeSetByUserResize && settings.WindowWidth is > 0
            ? Math.Max(MinWidth, settings.WindowWidth.Value)
            : defaultWidth;
        var height = settings.WindowSizeSetByUserResize && settings.WindowHeight is > 0
            ? Math.Max(MinHeight, settings.WindowHeight.Value)
            : defaultHeight;

        ApplyWindowBounds(width, height, screen);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyWindowBounds(width, height, Screens.ScreenFromVisual(this) ?? screen);
            _isApplyingWindowBounds = false;
            UpdateCommunityMediaLayout();
            UpdateBackgroundLightingLayout();
        }, DispatcherPriority.Background);
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCommunityMediaLayout();
            UpdateBackgroundLightingLayout();
        }, DispatcherPriority.Background);

        if (_isApplyingWindowBounds || !_hasUserDefinedWindowSize || DataContext is not MainWindowViewModel vm || WindowState != WindowState.Normal)
        {
            return;
        }

        SaveSettings(vm);
    }

    private void MainWindow_PointerMoved(object? sender, PointerEventArgs e)
    {
        ResetInactivity();
        var position = e.GetPosition(this);
        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;
        if (centerX <= 0 || centerY <= 0)
        {
            return;
        }

        if (HomeBackgroundLayer?.RenderTransform is TranslateTransform backgroundTransform)
        {
            backgroundTransform.X = (position.X - centerX) / centerX * 18;
            backgroundTransform.Y = (position.Y - centerY) / centerY * 12;
        }

        if (TopAccentGlow?.RenderTransform is TranslateTransform topGlowTransform)
        {
            topGlowTransform.X = (position.X - centerX) / centerX * 8;
            topGlowTransform.Y = (position.Y - centerY) / centerY * 6;
        }
    }

    private void ResetInactivity()
    {
        _inactivityTimer?.Stop();
        _inactivityTimer?.Start();
    }

    private void TitleLogoAnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentViewModel?.CurrentState == LauncherState.Running) return;
        _titleLogoAnimationTime += HomeLogoMotionTimerIntervalMs / 1000.0 * HomeLogoMotionSpeed;
        if (_titleLogoAnimationTime > HomeLogoMotionLoopDuration)
        {
            _titleLogoAnimationTime -= HomeLogoMotionLoopDuration;
        }

        var progress = _titleLogoAnimationTime / HomeLogoMotionLoopDuration;
        var angle = progress * Math.PI * 2;
        var motionX = Math.Sin(angle) * HomeLogoMotionAmplitudeX;
        var motionY = Math.Cos(angle) * HomeLogoMotionAmplitudeY;

        if (HomeLogoGrid?.RenderTransform is TranslateTransform homeMotion)
        {
            homeMotion.X = motionX;
            homeMotion.Y = motionY;
        }
    }

    private async void InactivityTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
            if (scrollViewer is { Offset.Y: <= 10 })
            {
                await RefreshFeed();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in InactivityTimer_Tick: {ex}");
        }
    }

    private async void MainWindow_DataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel vm && !_isInitialized)
            {
                _isInitialized = true;
                _currentViewModel?.PropertyChanged -= Vm_PropertyChanged;
                _currentViewModel = vm;
                _currentViewModel.PropertyChanged += Vm_PropertyChanged;

                vm.GameFolder = Path.Combine(AppConstants.AppDataDirectory, "Radium_PC");

                LoadLaunchMode(vm);
                await CheckForExistingInstall(vm);
                LoadAssets(vm);

                await InitializeLauncher(vm);
                await LoadFeed();
                await LoadAnnouncements(vm);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in MainWindow_DataContextChanged: {ex}");
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm) return;
        if (e.PropertyName == nameof(vm.SelectedLaunchMode))
        {
            SaveSettings(vm);
        }
    }

    private void LoadLaunchMode(MainWindowViewModel vm)
    {
        try
        {
            Directory.CreateDirectory(_configFolder);
            string settingsPath = Path.Combine(_configFolder, SettingsFileName);
            if (!File.Exists(settingsPath)) return;

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
            if (settings == null) return;

            if (!string.IsNullOrWhiteSpace(settings.SelectedLaunchMode))
            {
                vm.SelectedLaunchMode = settings.SelectedLaunchMode;
            }

            if (!string.IsNullOrWhiteSpace(settings.ScreenModeBatchFile))
            {
                vm.ScreenModeBatchFile = settings.ScreenModeBatchFile;
            }

            if (!string.IsNullOrWhiteSpace(settings.VrModeBatchFile))
            {
                vm.VrModeBatchFile = settings.VrModeBatchFile;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load launcher settings: {ex}");
        }
    }

    private void SaveSettings(MainWindowViewModel vm)
    {
        try
        {
            Directory.CreateDirectory(_configFolder);
            string settingsPath = Path.Combine(_configFolder, SettingsFileName);
            var settings = LoadLauncherSettings();

            settings.SelectedLaunchMode = vm.SelectedLaunchMode;
            settings.GameFolder = vm.GameFolder;
            settings.ScreenModeBatchFile = vm.ScreenModeBatchFile;
            settings.VrModeBatchFile = vm.VrModeBatchFile;
            if (WindowState == WindowState.Normal)
            {
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
                settings.UseSavedWindowSize = true;
                settings.HasUserDefinedWindowSize = true;
                settings.WindowSizeSetByUserResize = _hasUserDefinedWindowSize;
            }
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save launcher settings: {ex}");
        }
    }

    private LauncherSettings LoadLauncherSettings()
    {
        Directory.CreateDirectory(_configFolder);
        string settingsPath = Path.Combine(_configFolder, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return new LauncherSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load launcher settings: {ex}");
            return new LauncherSettings();
        }
    }

    private void UpdateCommunityMediaLayout()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var itemsControl = this.FindControl<ItemsControl>("CommunityMediaItemsControl");
        var availableWidth = itemsControl?.Bounds.Width ?? 0;
        if (availableWidth <= 0)
        {
            return;
        }

        const double minimumCardWidth = 220;
        var columns = Math.Max(1, (int)Math.Floor(availableWidth / minimumCardWidth));
        var itemWidth = Math.Floor((availableWidth - (CommunityMediaHorizontalGap * columns)) / columns);
        var itemHeight = Math.Round(itemWidth * (140d / 243d));

        vm.CommunityMediaItemWidth = itemWidth;
        vm.CommunityMediaItemHeight = itemHeight;
    }

    private void UpdateBackgroundLightingLayout()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var dim = Math.Max(width, height);
        var minDim = Math.Min(width, height);

        var leftGlowDiameter = Math.Max(700, dim * 1.125);
        var rightGlowDiameter = Math.Max(520, dim * 0.875);
        var centerGlowWidth = Math.Max(900, width * 1.5);
        var centerGlowHeight = Math.Max(520, height * 1.556);
        var bottomGlowWidth = Math.Max(780, width * 1.25);
        var bottomGlowHeight = Math.Max(420, height * 1.333);
        var topAccentDiameter = Math.Max(320, minDim * 0.889);

        if (TopLeftShadowGlow != null)
        {
            TopLeftShadowGlow.Width = leftGlowDiameter;
            TopLeftShadowGlow.Height = leftGlowDiameter;
            TopLeftShadowGlow.CornerRadius = new CornerRadius(leftGlowDiameter / 2);
            TopLeftShadowGlow.Margin = new Thickness(-leftGlowDiameter * 0.278, -leftGlowDiameter * 0.389, 0, 0);
        }

        if (TopRightShadowGlow != null)
        {
            TopRightShadowGlow.Width = rightGlowDiameter;
            TopRightShadowGlow.Height = rightGlowDiameter;
            TopRightShadowGlow.CornerRadius = new CornerRadius(rightGlowDiameter / 2);
            TopRightShadowGlow.Margin = new Thickness(0, -rightGlowDiameter * 0.429, -rightGlowDiameter * 0.286, 0);
        }

        if (HomeBackgroundLayer != null)
        {
            HomeBackgroundLayer.Width = bottomGlowWidth;
            HomeBackgroundLayer.Height = bottomGlowHeight;
            HomeBackgroundLayer.CornerRadius = new CornerRadius(bottomGlowHeight / 2);
            HomeBackgroundLayer.Margin = new Thickness(0, 0, 0, -bottomGlowHeight * 0.333);
        }

        if (CenterAtmosphereGlow != null)
        {
            CenterAtmosphereGlow.Width = centerGlowWidth;
            CenterAtmosphereGlow.Height = centerGlowHeight;
        }

        if (TopAccentGlow != null)
        {
            TopAccentGlow.Width = topAccentDiameter;
            TopAccentGlow.Height = topAccentDiameter;
            TopAccentGlow.CornerRadius = new CornerRadius(topAccentDiameter / 2);
            TopAccentGlow.Margin = new Thickness(0, -topAccentDiameter * 0.625, 0, 0);
        }
    }

    private void ApplyWindowBounds(double width, double height, Screen screen)
    {
        _isApplyingWindowBounds = true;
        Width = width;
        Height = height;

        var workingArea = screen.WorkingArea;
        var widthPixels = (int)Math.Round(width * screen.Scaling);
        var heightPixels = (int)Math.Round(height * screen.Scaling);
        Position = new PixelPoint(
            workingArea.X + Math.Max(0, (workingArea.Width - widthPixels) / 2),
            workingArea.Y + Math.Max(0, (workingArea.Height - heightPixels) / 2));
    }

    private void MarkWindowSizeAsUserDefined()
    {
        _hasUserDefinedWindowSize = true;
    }

    private async Task LoadAnnouncements(MainWindowViewModel vm)
    {
        try
        {
            var res = await _httpClient.GetStringAsync(AnnouncementsUrl);
            PopulateAnnouncements(vm, res);
        }
        catch
        {
            if (!string.IsNullOrEmpty(vm.InfoResponse))
            {
                var extraText = string.Join("\n", vm.InfoResponse.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Skip(4));
                PopulateAnnouncements(vm, extraText);
            }
            else
            {
                PopulateAnnouncements(vm, string.Empty);
            }
        }
    }

    private async Task CheckForExistingInstall(MainWindowViewModel vm)
    {
        var defaultFolder = Path.Combine(AppConstants.AppDataDirectory, "Radium_PC");
        vm.GameFolder = defaultFolder;
        await Task.CompletedTask;
    }

    private void PopulateAnnouncements(MainWindowViewModel vm, string content)
    {
        vm.Announcements.Clear();
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line));

        foreach (var line in lines)
        {
            vm.Announcements.Add(line);
        }

        if (vm.Announcements.Count != 0)
        {
            AnnouncementsPanel.IsVisible = true;
        }
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";

        return $"{span.Seconds}s";
    }

    private async Task InitializeLauncher(MainWindowViewModel vm)
    {
        await FetchLatestInfo(vm);
        await UpdateLauncherState(vm);
        string[] info = (vm.InfoResponse ?? string.Empty).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        string? gameExePath = GetGameExecutablePath(vm, info);
        if (string.IsNullOrEmpty(gameExePath))
        {
            AppConstants.SteamAppId = "471710";
        }
        else
        {
            AppConstants.GameFolder = Path.Combine(gameExePath, "..");
            string appIdPath = Path.Combine(AppConstants.GameFolder, "steam_appid.txt");
            if (File.Exists(appIdPath))
            {
                AppConstants.SteamAppId = (await File.ReadAllTextAsync(appIdPath)).Trim();
            }
            else
            {
                AppConstants.SteamAppId = "471710";
                await File.WriteAllTextAsync(appIdPath, AppConstants.SteamAppId);
            }
        }
    }

    private async Task UpdateLauncherState(MainWindowViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.InfoResponse))
        {
            vm.CurrentState = LauncherState.Unavailable;
            return;
        }

        if (Directory.Exists(vm.GameFolder))
        {
            try
            {
                string hashPath = Path.Combine(vm.GameFolder, "hash.txt");
                if (File.Exists(hashPath))
                {
                    string localHash = (await File.ReadAllTextAsync(hashPath)).Trim();
                    string[] info = vm.InfoResponse.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

                    int hashIndex = _isTwyEnabled && vm.OperatingSystemName == "Linux" ? 6 : 1;
                    if (info.Length > hashIndex && localHash.Equals(info[hashIndex].Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (_isTwyEnabled)
                        {
                            vm.CurrentState = LauncherState.Ready;
                            return;
                        }
                        try
                        {
                            string patchFilename = vm.OperatingSystemName == "Windows" ? "Patch.bat" : "Patch.sh";
                            _patchOnline = await _httpClient.GetStringAsync(
                                $"https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/{patchFilename}");
                            _patchLocal = await File.ReadAllTextAsync(Path.Combine(vm.GameFolder, patchFilename));
                        }
                        catch
                        {
                            Console.WriteLine("Failed to fetch online or local patch.");
                        }

                        if (_patchOnline != null && _patchLocal != null)
                        {
                            vm.CurrentState = _patchLocal == _patchOnline
                                ? LauncherState.Ready
                                : LauncherState.NeedsPatching;
                        }
                        else
                        {
                            vm.CurrentState = LauncherState.Ready;
                        }
                    }
                    else vm.CurrentState = LauncherState.NeedsUpdate;
                }
                else
                {
                    vm.CurrentState = LauncherState.NeedsDownload;
                }
            }
            catch
            {
                vm.CurrentState = LauncherState.NeedsDownload;
            }
        }
        else vm.CurrentState = LauncherState.NeedsDownload;
    }

    private async Task FetchLatestInfo(MainWindowViewModel vm)
    {
        try
        {
            var res = await _httpClient.GetStringAsync(
                "https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/LatestInfo.txt");
            vm.InfoResponse = res;
        }
        catch (HttpRequestException ex)
        {
            vm.InfoResponse = string.Empty;
            _ = new MessageBoxWindow("Network Error",
                $"Could not fetch latest info. Please check your internet connection. Details: {ex.Message}", null).ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.InfoResponse = string.Empty;
            _ = new MessageBoxWindow("Error",
                $"An unexpected error occurred while fetching latest info. Details: {ex.Message}", null).ShowDialog(this);
        }
    }

    private async Task InstallRadium(MainWindowViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.InfoResponse)) return;
        try
        {
            string[] info = vm.InfoResponse.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            string downloadUrl;
            if (_isTwyEnabled)
            {
                if (vm.OperatingSystemName == "Windows")
                {
                    downloadUrl = info[0].Trim();
                }
                else
                {
                    downloadUrl = info[5].Trim();
                }
            }
            else
            {
                downloadUrl = info[0].Trim();
            }

            string expectedHash;
            if (vm.OperatingSystemName == "Windows")
            {
                expectedHash = info[1].Trim();
            }
            else
            {
                expectedHash = info[6].Trim();
            }

            vm.CurrentState = LauncherState.Downloading;
            vm.DownloadProgress = 0;

            var zipPath = Path.Combine(AppConstants.AppDataDirectory, info[4]);
            
            string settingsPath = Path.Combine(_configFolder, SettingsFileName);
            LauncherSettings? settings = null;
            if (File.Exists(settingsPath))
            {
                var json = await File.ReadAllTextAsync(settingsPath);
                settings = JsonSerializer.Deserialize<LauncherSettings>(json);
            }

            var downloadConfiguration = new DownloadConfiguration()
            {
                BufferBlockSize = 10240,
                ChunkCount = (int)(settings?.DlThreadCount ?? 4),
                MaximumMemoryBufferBytes = 1024 * 1024 * 10,
                BlockTimeout = 10000
            };

            var downloader = new DownloadService(downloadConfiguration);
            downloader.DownloadProgressChanged += (_, args) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.DownloadProgress = args.ProgressPercentage;
                    vm.ProgressDetails =
                        $"{args.ReceivedBytesSize / 1024.0 / 1024.0:F1}MB / {args.TotalBytesToReceive / 1024.0 / 1024.0:F1}MB | {args.BytesPerSecondSpeed / 1024.0 / 1024.0:F1} MB/s";
                    
                    double etaSeconds = 0;
                    if (args.BytesPerSecondSpeed > 0)
                    {
                        etaSeconds = (args.TotalBytesToReceive - args.ReceivedBytesSize) / args.BytesPerSecondSpeed;
                    }

                    if (double.IsFinite(etaSeconds) && etaSeconds >= 0 && etaSeconds <= TimeSpan.MaxValue.TotalSeconds)
                    {
                        var eta = TimeSpan.FromSeconds(etaSeconds);
                        vm.EtaText = $"ETA: {FormatTimeSpan(eta)}";
                    }
                    else
                    {
                        vm.EtaText = "ETA: calculating...";
                    }
                });
            };

            await downloader.DownloadFileTaskAsync(downloadUrl, zipPath);

            vm.CurrentState = LauncherState.Verifying;
            var hashed = await SHA256.HashDataAsync(File.OpenRead(zipPath));
            string hashedStr = BitConverter.ToString(hashed).Replace("-", "").ToLowerInvariant();

            if (!string.IsNullOrEmpty(expectedHash) &&
                !hashedStr.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _ = new MessageBoxWindow("Hash Mismatch",
                    "File hashes do not match. Current installation may be corrupt.", null).ShowDialog(this);
                vm.CurrentState = LauncherState.NeedsDownload;
                return;
            }

            vm.CurrentState = LauncherState.Extracting;
            vm.EtaText = string.Empty;
            vm.ProgressDetails = string.Empty;

            if (Directory.Exists(vm.GameFolder))
            {
                Directory.Delete(vm.GameFolder, true);
            }

            Directory.CreateDirectory(vm.GameFolder);

            await File.WriteAllTextAsync(Path.Combine(vm.GameFolder, "hash.txt"), hashedStr);
            await Task.Run(() => ExtractArchive(zipPath, vm.GameFolder));

            // File.Delete(zipPath); // LISTEN ITS LIKE 1 AM, IM TOO LAZY FOR THIS

            if (!_isTwyEnabled) await PatchRadium(vm);
            await UpdateLauncherState(vm);
        }
        catch (Exception ex)
        {
            _ = new MessageBoxWindow("Installation Failed", ex.Message, null).ShowDialog(this);
            vm.CurrentState = LauncherState.NeedsDownload;
        }
    }

    private async Task PatchRadium(MainWindowViewModel vm)
    {
        vm.CurrentState = LauncherState.Patching;
        try
        {
            string scriptUrl = vm.OperatingSystemName == "Windows"
                ? "https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/Patch.bat"
                : "https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/Patch.sh";

            string scriptName = vm.OperatingSystemName == "Windows" ? "Patch.bat" : "Patch.sh";
            string scriptPath = Path.Combine(vm.GameFolder, scriptName);

            var scriptData = await _httpClient.GetByteArrayAsync(scriptUrl);
            await File.WriteAllBytesAsync(scriptPath, scriptData);

            string binary = vm.OperatingSystemName == "Windows" ? "7za.exe" :
                vm.OperatingSystemName == "macOS" ? "7zz-mac" : "7zz";
            string binaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Binaries", binary);

            var pInfo = new ProcessStartInfo
            {
                WorkingDirectory = vm.GameFolder,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (vm.OperatingSystemName == "Windows")
            {
                pInfo.FileName = "cmd.exe";
                pInfo.Arguments = $"/c \"{scriptPath}\" \"{binaryPath}\"";
            }
            else
            {
                Process? chmodProcess = Process.Start(new ProcessStartInfo("chmod", $"+x \"{scriptPath}\""));

                chmodProcess?.Exited += (_, _) =>
                {
                    pInfo.FileName = "/bin/bash";
                    pInfo.Arguments = $"\"{scriptPath}\" \"{binaryPath}\"";
                };
            }

            var process = Process.Start(pInfo);
            if (process != null) await process.WaitForExitAsync();
            vm.CurrentState = LauncherState.Ready;
        }
        catch (Exception ex)
        {
            _ = new MessageBoxWindow("Patching Failed", ex.Message, null).ShowDialog(this);
        }
    }

    private void ExtractArchive(string archivePath, string destinationPath)
    {
        string binary = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "7za.exe" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "7zz-mac" : "7zz";
        string binaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Binaries", binary);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo("chmod", $"+x \"{binaryPath}\""))?.WaitForExit();

        var p = Process.Start(new ProcessStartInfo(binaryPath, $"x \"{archivePath}\" -o\"{destinationPath}\" -y")
            { CreateNoWindow = true });
        p?.WaitForExit();
    }

    private async Task LaunchRadium(MainWindowViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.InfoResponse)) return;
        string[] info = vm.InfoResponse.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (info.Length < 4) return;

        string? batchPath = GetCustomBatchFilePath(vm);
        if (!string.IsNullOrEmpty(batchPath) && File.Exists(batchPath) && vm.OperatingSystemName == "Windows")
        {
            await StartProcess(batchPath, vm, isBatch: true);
            return;
        }

        string? gameExePath = GetGameExecutablePath(vm, info);
        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
        {
            _ = new MessageBoxWindow("Missing Executable", "Could not find the Radium executable.", null).ShowDialog(this);
            return;
        }
        
        string appId = AppConstants.SteamAppId;
        var pInfo = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(gameExePath),
            UseShellExecute = false,
            EnvironmentVariables =
            {
                ["SteamAppId"] = appId,
                ["STEAM_COMPAT_APP_ID"] = appId,
                ["SteamGameId"] = appId,
                ["SteamClientLaunch"] = "1"
            }
        };

        string mode = vm.SelectedLaunchMode == "VR" ? "+mode:vr" : "+mode:screen";

        if (_isTwyEnabled && vm.OperatingSystemName == "Linux")
        {
            Process.Start(new ProcessStartInfo("chmod", $"+x \"{gameExePath}\"") { CreateNoWindow = true })?.WaitForExit();
            pInfo.FileName = gameExePath;
            pInfo.Arguments = mode;
        }
        else if (vm.OperatingSystemName == "Linux")
        {
            string protonPathFile = Path.Combine(_configFolder, "protonpath.txt");
            string protonPath = File.Exists(protonPathFile) ? (await File.ReadAllTextAsync(protonPathFile)).Trim() : "";

            if (string.IsNullOrEmpty(protonPath))
            {
                _ = new MessageBoxWindow("Missing Configuration", "Please set the Proton folder in the configuration window to continue.", null).ShowDialog(this);
                return;
            }
            
            string finalSteamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam/steam");
            if (!Directory.Exists(finalSteamPath))
            {
                finalSteamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Steam");
                if (!Directory.Exists(finalSteamPath))
                {
                    finalSteamPath = Path.Combine(protonPath, "..", "..", "..");
                    if (!Directory.Exists(finalSteamPath))
                    {
                        _ = new MessageBoxWindow("Steam Missing", "Could not find the Steam folder. Please check if Steam is installed.", null).ShowDialog(this);
                        return;
                    }
                }
            }

            string compatDataPath = Path.Combine(vm.GameFolder, "compatdata_radium");

            pInfo.FileName = Path.Combine(protonPath, "proton");
            pInfo.EnvironmentVariables["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = finalSteamPath;
            if (!Directory.Exists(compatDataPath)) Directory.CreateDirectory(compatDataPath);
            pInfo.EnvironmentVariables["STEAM_COMPAT_DATA_PATH"] = compatDataPath;
            pInfo.EnvironmentVariables["WINEDLLOVERRIDES"] = "winhttp=n,b";
            pInfo.Arguments = $"run \"{gameExePath}\" {mode}";
        }
        else if (vm.OperatingSystemName == "macOS")
        {
            // please someone on macos fix this if it is incorrect
            string winePathFile = Path.Combine(_configFolder, "winepath.txt");
            string winePath = File.Exists(winePathFile) ? (await File.ReadAllTextAsync(winePathFile)).Trim() : "";

            if (string.IsNullOrEmpty(winePath))
            {
                _ = new MessageBoxWindow("Missing Configuration", "Please set the Wine executable path in the configuration window to continue.", null).ShowDialog(this);
                return;
            }
            
            string finalSteamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam");
            if (!Directory.Exists(finalSteamPath))
            {
                _ = new MessageBoxWindow("Steam Missing", "Could not find the Steam folder. Please check if Steam is installed.", null).ShowDialog(this);
                return;
            }

            string compatDataPath = Path.Combine(vm.GameFolder, "compatdata_radium");

            pInfo.FileName = winePath;
            pInfo.EnvironmentVariables["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = finalSteamPath;
            if (!Directory.Exists(compatDataPath)) Directory.CreateDirectory(compatDataPath);
            pInfo.EnvironmentVariables["WINEPREFIX"] = compatDataPath;
            pInfo.EnvironmentVariables["WINEDLLOVERRIDES"] = "winhttp=n,b";
            pInfo.Arguments = $"\"{gameExePath}\" {mode}";
        }
        else
        {
            pInfo.FileName = gameExePath;
            pInfo.Arguments = mode;
        }

        try
        {
            var p = Process.Start(pInfo);
            if (p != null)
            {
                var discordRpcService = _isTwyEnabled ? null : new DiscordRpcService(vm);
                vm.GameProcess = p;
                vm.CurrentState = LauncherState.Running;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    vm.CurrentState = LauncherState.Ready;
                    vm.GameProcess = null;
                    discordRpcService?.Dispose();
                });
            }
        }
        catch (Exception ex)
        {
            _ = new MessageBoxWindow("Launch Failed", ex.Message, null).ShowDialog(this);
        }
    }

    private bool _isTwyEnabled;
    private string _radiumGameFolder;
    private async void ToggleTenWholeYears(MainWindowViewModel vm)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _ = new MessageBoxWindow("Incompatible Operating System", "TenWholeYears is not compatible with macOS.", null).ShowDialog(this);
        }
        if (_isTwyEnabled)
        {
            vm.GameFolder = _radiumGameFolder;
            _isTwyEnabled = false;
            await FetchLatestInfo(vm);
            await UpdateLauncherState(vm);
            IconImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/radium-icon.png")));
            LogoImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/radium-logo.png")));
            MainLogoImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/radium-logo.png")));
        }
        else
        {
            try
            {
                var res = await _httpClient.GetStringAsync(
                    "https://raw.githubusercontent.com/typxzero/RadiumLauncherFiles/refs/heads/main/LatestInfo_TWY.txt");
                vm.InfoResponse = res;
            }
            catch (HttpRequestException ex)
            {
                vm.InfoResponse = string.Empty;
                _ = new MessageBoxWindow("Network Error",
                        $"Could not fetch latest info. Please check your internet connection. Details: {ex.Message}",
                        null)
                    .ShowDialog(this);
            }
            catch (Exception ex)
            {
                vm.InfoResponse = string.Empty;
                _ = new MessageBoxWindow("Error",
                        $"An unexpected error occurred while fetching latest info. Details: {ex.Message}", null)
                    .ShowDialog(this);
            }

            _radiumGameFolder = vm.GameFolder;
            vm.GameFolder = Path.Combine(AppConstants.AppDataDirectory, "TenWholeYears_PC");
            _isTwyEnabled = true;
            await UpdateLauncherState(vm);
            IconImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/tenwholeyears-icon.png")));
            LogoImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/tenwholeyears-logo.png")));
            MainLogoImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/tenwholeyears-logo.png")));
        }
    }

    private string? GetCustomBatchFilePath(MainWindowViewModel vm)
    {
        string batchFile = vm.SelectedLaunchMode == "VR" ? vm.VrModeBatchFile : vm.ScreenModeBatchFile;
        if (string.IsNullOrWhiteSpace(batchFile))
        {
            return null;
        }

        if (Path.IsPathRooted(batchFile))
        {
            return batchFile;
        }

        string relativePath = Path.Combine(vm.GameFolder, batchFile);
        if (File.Exists(relativePath))
        {
            return relativePath;
        }

        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, batchFile);
        return File.Exists(localPath) ? localPath : null;
    }

    private Task StartProcess(string path, MainWindowViewModel vm, bool isBatch = false)
    {
        try
        {
            var pInfo = new ProcessStartInfo
            {
                FileName = isBatch ? "cmd.exe" : path,
                Arguments = isBatch ? $"/c \"{path}\"" : string.Empty,
                WorkingDirectory = Path.GetDirectoryName(path) ?? vm.GameFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var p = Process.Start(pInfo);
            if (p != null)
            {
                vm.GameProcess = p;
                vm.CurrentState = LauncherState.Running;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    vm.CurrentState = LauncherState.Ready;
                    vm.GameProcess = null;
                });
            }
        }
        catch (Exception ex)
        {
            _ = new MessageBoxWindow("Launch Failed", ex.Message, null).ShowDialog(this);
        }

        return Task.CompletedTask;
    }

    private string? GetGameExecutablePath(MainWindowViewModel vm, string[] info)
    {
        string expectedExeName;
        if (_isTwyEnabled)
        {
            if (vm.OperatingSystemName == "Windows")
            {
                expectedExeName = info[3];
            }
            else
            {
                expectedExeName = info[7];
            }
        }
        else
        {
            expectedExeName = info[3];
        }
        string candidate = Path.Combine(vm.GameFolder, expectedExeName);
        if (File.Exists(candidate))
        {
            return candidate;
        }
        
        try
        {
            var found = Directory.EnumerateFiles(vm.GameFolder, expectedExeName, SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }

            string searchPattern = expectedExeName.Contains('.') ? "*.exe" : "*";
            return Directory.EnumerateFiles(vm.GameFolder, searchPattern, SearchOption.AllDirectories)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(expectedExeName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private async Task StopRadium(MainWindowViewModel vm)
    {
        try
        {
            var gameProcess = vm.GameProcess;
            if (gameProcess == null) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _isTwyEnabled)
            {
                gameProcess.Kill(true);
            }
            else
            {
                string compatDataPath = Path.Combine(vm.GameFolder, "compatdata_radium");
                var pInfo = new ProcessStartInfo("bash", $"-c \"WINEPREFIX='{compatDataPath}/pfx' wineserver -k\"")
                {
                    CreateNoWindow = true,
                };

                var p = Process.Start(pInfo);
                if (p != null) await p.WaitForExitAsync();

                await Task.Delay(2000);

                if (!gameProcess.HasExited)
                {
                    gameProcess.Kill(true);
                }
            }
        }
        catch (Exception ex)
        {
            _ = new MessageBoxWindow("Stop Failed", ex.Message, null).ShowDialog(this);
        }
    }

    private void LoadAssets(MainWindowViewModel vm)
    {
        try
        {
            vm.MainLogo = new Bitmap(AssetLoader.Open(new Uri("avares://RadiumLauncher/Assets/radium-logo.png")));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading main logo: {ex.Message}");
        }

        string osAssetPath = "";
        try
        {
            osAssetPath = vm.OperatingSystemName switch
            {
                "Windows" => "avares://RadiumLauncher/Assets/icons8-windows-11-50.png",
                "Linux" => "avares://RadiumLauncher/Assets/icons8-linux-50.png",
                "macOS" => "avares://RadiumLauncher/Assets/icons8-mac-logo-30.png",
                _ => "avares://RadiumLauncher/Assets/avalonia-logo.ico"
            };
            vm.OSLogo = new Bitmap(AssetLoader.Open(new Uri(osAssetPath)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading OS logo from {osAssetPath}: {ex}");
        }
    }

    private async Task LoadFeed()
    {
        if (_isLoadingFeed) return;
        _isLoadingFeed = true;
        try
        {
            var res = await _httpClient.GetAsync(
                "https://launcher.radie.app/api/photos/v1/feed?skip=" + _imagesLoaded + "&take=15");
            if (res.IsSuccessStatusCode && DataContext is MainWindowViewModel vm)
            {
                var feed = JsonSerializer.Deserialize<FeedResponse>(await res.Content.ReadAsStringAsync());
                if (feed?.Results != null)
                {
                    var newItems = feed.Results;

                    var playerIds = newItems.Select(i => i.PlayerId).Distinct().ToList();
                    var roomIds = newItems.Where(i => i.RoomId.HasValue).Select(i => i.RoomId!.Value).Distinct()
                        .ToList();

                    var accountsTask = GetAccounts(playerIds);
                    var roomsTask = roomIds.Any() ? GetRooms(roomIds) : Task.FromResult(new List<RoomResponse>());

                    await Task.WhenAll(accountsTask, roomsTask);

                    var accounts = accountsTask.Result.ToDictionary(a => a.AccountId);
                    var rooms = roomsTask.Result.ToDictionary(r => r.RoomId);

                    foreach (var item in newItems)
                    {
                        if (accounts.TryGetValue(item.PlayerId, out var acc))
                        {
                            item.Username = acc.Username;
                            _ = item.LoadProfileImage(acc.ProfileImageUrl);
                        }

                        if (item.RoomId.HasValue && rooms.TryGetValue(item.RoomId.Value, out var room))
                        {
                            item.RoomName = $"^{room.Name}";
                        }

                        vm.CommunityMediaItems.Add(item);
                        _ = item.LoadImage();
                    }

                    _imagesLoaded += newItems.Count;
                }
            }
        }
        catch
        {
            Console.WriteLine("Failed to fetch feed.");
        }
        finally
        {
            _isLoadingFeed = false;
        }
    }

    private async Task<List<AccountResponse>> GetAccounts(List<int> ids)
    {
        if (!ids.Any()) return [];
        try
        {
            var query = string.Join("&", ids.Select(id => $"id={id}"));
            var response = await _httpClient.GetAsync($"https://accounts.radie.app/account/bulk?{query}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AccountResponse>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
        }
        catch
        {
            Console.WriteLine("Failed to fetch accounts.");
        }

        return [];
    }

    private async Task<List<RoomResponse>> GetRooms(List<int> ids)
    {
        if (!ids.Any()) return [];
        try
        {
            var query = string.Join("&", ids.Select(id => $"id={id}"));
            var response = await _httpClient.GetAsync($"https://api.radie.app/roomserver/rooms/bulk?{query}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<RoomResponse>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
        }
        catch
        {
            Console.WriteLine("Failed to fetch rooms.");
        }

        return [];
    }

    private async Task RefreshFeed()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            foreach (var item in vm.CommunityMediaItems) item.FullCleanup();
            vm.CommunityMediaItems.Clear();
            _imagesLoaded = 0;
            await LoadFeed();
        }
    }

    private void UpdateVisibleItems(ScrollViewer scrollViewer)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var itemsControl = this.FindControl<ItemsControl>("CommunityMediaItemsControl");
            if (itemsControl is null) return;

            var transform = itemsControl.TranslatePoint(new Point(0, 0), scrollViewer);
            if (!transform.HasValue) return;

            double itemsTopInViewport = transform.Value.Y;
            double viewportHeight = scrollViewer.Viewport.Height;

            double relativeVisibleTop = Math.Max(0, -itemsTopInViewport);
            double relativeVisibleBottom = relativeVisibleTop + viewportHeight;

            int itemsPerRow = Math.Max(1,
                (int)Math.Floor(itemsControl.Bounds.Width / Math.Max(1, vm.CommunityMediaItemWidth + CommunityMediaHorizontalGap)));
            double itemHeight = vm.CommunityMediaItemHeight + CommunityMediaVerticalGap;
            int startRow = (int)Math.Max(0, Math.Floor(relativeVisibleTop / itemHeight) - 1);
            int endRow = (int)Math.Ceiling(relativeVisibleBottom / itemHeight) + 1;

            int startIndex = startRow * itemsPerRow;
            int endIndex = (endRow + 1) * itemsPerRow;

            for (int i = 0; i < vm.CommunityMediaItems.Count; i++)
            {
                var item = vm.CommunityMediaItems[i];
                if (i >= startIndex && i <= endIndex)
                {
                    _ = item.LoadImage();
                }
                else if (item != vm.MaximizedPhoto)
                {
                    item.UnloadImage();
                }
            }
        }
    }

    private async void MainButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (vm.CurrentState == LauncherState.Ready)
            {
                bool apiAvailable;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    try
                    {
                        var checkApiRes = await _httpClient.GetAsync("https://radie.app/", cts.Token);
                        apiAvailable = checkApiRes.IsSuccessStatusCode;
                    }
                    catch (TaskCanceledException ex) when (ex.CancellationToken == cts.Token)
                    {
                        Debug.WriteLine("Server check timed out.");
                        apiAvailable = false;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking server status: {ex}");
                        apiAvailable = false;
                    }
                    if (_isTwyEnabled)
                    {
                        apiAvailable = true;
                    }
                }

                if (apiAvailable)
                {
                    await LaunchRadium(vm);
                }
                else
                {
                    var confirm = new ConfirmationWindow(
                        "Server Unavailable",
                        $"Radium's server is down or unavailable. Would you like to continue anyway?",
                        "Yes",
                        "No")
                    {
                        Height = 175
                    };

                    var result = await confirm.ShowDialog<bool?>(this);
                    if (result == true)
                    {
                        await LaunchRadium(vm);
                    }
                }
            }
            else if (vm.CurrentState is LauncherState.NeedsDownload or LauncherState.NeedsUpdate)
            {
                await InstallRadium(vm);
            }
            else if (vm.CurrentState == LauncherState.NeedsPatching)
            {
                await PatchRadium(vm);
            }
            else if (vm.CurrentState == LauncherState.Running)
            {
                await StopRadium(vm);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in MainButton_Click: {ex}");
        }
    }

    private void ConfigButton_Click(object? sender, RoutedEventArgs e)
    {
        ConfigurationWindow configurationWindow = new ConfigurationWindow();
        configurationWindow.ShowDialog(this);
    }

    public void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public void FullscreenButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private async void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        try
        {
            if (sender is ScrollViewer scrollViewer)
            {
                UpdateVisibleItems(scrollViewer);

                if (scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 500 &&
                    !_isLoadingFeed)
                {
                    await LoadFeed();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in ScrollViewer_ScrollChanged: {ex}");
        }
    }

    private void ResizeTop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.North, e);
    }

    private void ResizeBottom_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.South, e);
    }

    private void ResizeLeft_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.West, e);
    }

    private void ResizeRight_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.East, e);
    }

    private void ResizeTopLeft_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.NorthWest, e);
    }

    private void ResizeTopRight_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.NorthEast, e);
    }

    private void ResizeBottomLeft_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.SouthWest, e);
    }

    private void ResizeBottomRight_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MarkWindowSizeAsUserDefined();
        BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void MainLogoImage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            ToggleTenWholeYears(vm);
        }
    }
}
