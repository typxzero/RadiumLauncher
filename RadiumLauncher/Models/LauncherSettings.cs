namespace RadiumLauncher.Models;

public class LauncherSettings
{
    public string SelectedLaunchMode { get; set; } = "Screen";
    public string? GameFolder { get; set; }
    public string ScreenModeBatchFile { get; set; }
    public string VrModeBatchFile { get; set; }
    public string macOSWinePath { get; set; }
    public decimal DlThreadCount { get; set; } = 8;
    public string? RadiumUsername { get; set; }
    public bool? DiscordRpcEnabled { get; set; } = false;
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool UseSavedWindowSize { get; set; }
    public bool HasUserDefinedWindowSize { get; set; }
}
