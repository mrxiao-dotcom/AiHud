using System;
using System.Diagnostics;
using AiDaBenYing.Models;

namespace AiDaBenYing.Services;

public class AppLauncherService
{
    public event EventHandler<AppItem>? AppLaunched;

    public bool LaunchApp(AppItem appItem)
    {
        try
        {
            if (string.IsNullOrEmpty(appItem.ExecutablePath))
            {
                return false;
            }

            if (!System.IO.File.Exists(appItem.ExecutablePath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = appItem.ExecutablePath,
                Arguments = appItem.Arguments,
                UseShellExecute = true
            };

            Process.Start(startInfo);

            appItem.LaunchCount++;
            appItem.LastLaunchTime = DateTime.Now;

            AppLaunched?.Invoke(this, appItem);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching app: {ex.Message}");
            return false;
        }
    }

    public bool ValidatePath(string path)
    {
        return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
    }
}
