using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using DiscordRPC;
using RadiumLauncher.Models;
using RadiumLauncher.ViewModels;
using HtmlAgilityPack;

namespace RadiumLauncher.Services;

public class DiscordRpcService
{
    private readonly DiscordRpcClient _rpcClient = new DiscordRpcClient("1512951037009461248");
    private readonly HttpClient _httpClient = new HttpClient();
    private const string SettingsFileName = "launcher-settings.json";
    private readonly string _configFolder = Path.Combine(AppConstants.AppDataDirectory, "Configuration");
    private readonly string _settingsPath;
    private DateTime startTime;
    private readonly Timer _timer = new Timer();
    private bool _isRpcActive = false;
    
    public DiscordRpcService(MainWindowViewModel vm)
    {
        _settingsPath = Path.Combine(_configFolder, SettingsFileName);
        _timer.Interval = 10000;
        _timer.Elapsed += (_, _) => Tick(vm);
        _rpcClient.Initialize();
        _timer.Start();
    }

    private async void Tick(MainWindowViewModel vm)
    {
        var settings = new LauncherSettings();
        if (File.Exists(_settingsPath))
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? settings;
        }

        if (vm.CurrentState == LauncherState.Running && settings.DiscordRpcEnabled == true)
        {
            if (!_isRpcActive)
            {
                startTime = DateTime.UtcNow;
                _isRpcActive = true;
            }

            if (string.IsNullOrEmpty(settings.RadiumUsername)) return;
            string currentRoom = await GetCurrentRoom(settings.RadiumUsername);
            _rpcClient.UpdateState($"In {currentRoom}");
            _rpcClient.UpdateDetails($"Playing as @{settings.RadiumUsername}");
            _rpcClient.UpdateStartTime(startTime);
            _rpcClient.UpdateLargeAsset("radium-icon", "Radium");
            Button visitProfile = new Button
            {
                Label = "Visit Profile",
                Url = $"https://www.radie.app/user/{settings.RadiumUsername}"
            };
            _rpcClient.UpdateButtons([visitProfile]);
        }
        else
        {
            if (_isRpcActive)
            {
                _rpcClient.ClearPresence();
                _isRpcActive = false;
            }
        }
    }

    private async Task<string> GetCurrentRoom(string username)
    {
        var userPage = await _httpClient.GetStringAsync($"https://www.radie.app/user/{username}");
        
        var doc = new HtmlDocument();
        doc.LoadHtml(userPage);
    
        var node = doc.DocumentNode.SelectSingleNode("//p[contains(text(), '@')]/parent::div/following-sibling::p");

        if (node != null)
        {
            if (node.InnerText.Trim() == "^")
            {
                return "offline dorm room";
            }
            return node.InnerText.Trim();
        }

        return "an Unknown Room";
    }

    public void Dispose()
    {
        if (_rpcClient.CurrentPresence != null) _rpcClient.ClearPresence();
        _rpcClient.Dispose();
        _httpClient.Dispose();
        _timer.Dispose();
    }
}