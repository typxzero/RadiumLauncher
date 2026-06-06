namespace RadiumLauncher.Models;

public class LauncherSettings
{
    public string SelectedLaunchMode { get; set; } = "Screen";
    public string? GameFolder { get; set; }
    public string ScreenModeBatchFile { get; set; } = "RecRoom_ScreenMode.bat";
    public string VrModeBatchFile { get; set; } = "RecRoom_VR.bat";
    public int MusicVolume { get; set; } = 100;
}
