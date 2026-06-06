using System;
using System.IO;

namespace RadiumLauncher.Models;

public static class AppConstants
{
    // The GitHub repository that hosts releases in the format "owner/repo".
    // Defaulting to the original repo; change to your fork if you prefer updates from there.
    public const string GitHubRepo = "typxzero/RadiumLauncher";

    public static string GameFolder;
    public static string SteamAppId = "471710";
    
    public static string AppDataDirectory
    {
        get
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RadiumLauncher");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }
    }
}