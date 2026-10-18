using System.Diagnostics;
using System.Globalization;

namespace TEWUniverse.Updater;

/// <summary>
/// Standalone updater that runs from %LOCALAPPDATA% outside the installed application.
/// Never touches game files, settings or user database projects.
/// </summary>
internal static class Program
{
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "Updates", "update.log");

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Updater] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static int Main(string[] args)
    {
        try
        {
            string Get(string flag)
            {
                int index = Array.IndexOf(args, flag);
                if (index < 0 || index + 1 >= args.Length)
                    throw new ArgumentException("Missing " + flag);
                return args[index + 1];
            }

            int processId = int.Parse(Get("--waitpid"), CultureInfo.InvariantCulture);
            string installer = Path.GetFullPath(Get("--installer"));
            string restart = Path.GetFullPath(Get("--restart"));
            if (!File.Exists(installer))
                throw new FileNotFoundException("Downloaded installer not found.", installer);

            Log($"Update agent started. Waiting for editor process {processId}.");
            // The UI closes immediately after launching us. If it exits before this
            // GetProcessById call, that is a success, not an updater error.
            try
            {
                using var editor = Process.GetProcessById(processId);
                if (!editor.WaitForExit(120000))
                    throw new TimeoutException(
                        "The editor did not close within two minutes. Update cancelled.");
            }
            catch (ArgumentException)
            {
                Log("Editor had already closed.");
            }

            var setupLog = Path.Combine(
                Path.GetDirectoryName(LogPath)!, "installer.log");
            var start = new ProcessStartInfo
            {
                FileName = installer,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(installer)!
            };
            start.ArgumentList.Add("/VERYSILENT");
            start.ArgumentList.Add("/SUPPRESSMSGBOXES");
            start.ArgumentList.Add("/NORESTART");
            start.ArgumentList.Add("/CLOSEAPPLICATIONS");
            start.ArgumentList.Add("/LOG=" + setupLog);
            Log("Starting verified Inno Setup installer.");
            using var setup = Process.Start(start)
                ?? throw new InvalidOperationException("Windows did not launch the installer.");
            if (!setup.WaitForExit(600000))
            {
                try { setup.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Installer did not exit within ten minutes.");
            }

            Log("Installer exited with code " + setup.ExitCode + ".");
            if (setup.ExitCode != 0)
                throw new InvalidOperationException(
                    "Installer exit code " + setup.ExitCode +
                    ". Review installer.log in the Updates directory.");

            if (!File.Exists(restart))
                throw new FileNotFoundException(
                    "Updated application executable not found after installation.", restart);
            Log("Installation successful. Relaunching the editor.");
            Process.Start(new ProcessStartInfo { FileName = restart, UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            Log("UPDATE FAILED: " + ex);
            try
            {
                var destination = Path.Combine(Path.GetTempPath(), "TEWUniverse-UpdateError.txt");
                File.WriteAllText(destination, ex.ToString());
            }
            catch { }
            return 1;
        }
    }
}
