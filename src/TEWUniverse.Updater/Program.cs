using System.Diagnostics;
using System.Globalization;

namespace TEWUniverse.Updater;

// A separate native .NET process lets the main editor close before its files are replaced.
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string Get(string flag)
            {
                int index = Array.IndexOf(args, flag);
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing " + flag);
                return args[index + 1];
            }

            int processId = int.Parse(Get("--waitpid"), CultureInfo.InvariantCulture);
            string installer = Path.GetFullPath(Get("--installer"));
            string restart = Get("--restart");
            if (!File.Exists(installer)) throw new FileNotFoundException("Installer was not downloaded.", installer);
            using (var current = Process.GetProcessById(processId))
                if (!current.WaitForExit(120000))
                    throw new TimeoutException("The editor did not close within two minutes.");

            using var setup = Process.Start(new ProcessStartInfo {
                FileName = installer,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Could not start the installer.");
            setup.WaitForExit();
            if (setup.ExitCode != 0) throw new InvalidOperationException("Installer exit code: " + setup.ExitCode);
            if (File.Exists(restart))
                Process.Start(new ProcessStartInfo { FileName = restart, UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            // No game database data or passwords are passed to the updater.
            try {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "TEWUniverse-UpdateError.txt"), ex.ToString());
            } catch { }
            return 1;
        }
    }
}
