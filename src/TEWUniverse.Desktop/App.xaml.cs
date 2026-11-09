using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TEWUniverse.Desktop;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnRecoverableUiException;
    }

    private static void OnRecoverableUiException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A malformed DataGrid edit/sort must never silently close Studio.
        // Retain an opt-in local diagnostic trail; never upload MDB contents,
        // entered passwords or game records to a remote service.
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TEW Universe Studio", "logs");
        var file = Path.Combine(folder, "ui-errors.log");
        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(file,
                DateTimeOffset.Now.ToString("u") + "  " +
                e.Exception.GetType().FullName + ": " +
                e.Exception.Message + Environment.NewLine +
                e.Exception.StackTrace + Environment.NewLine +
                Environment.NewLine);
        }
        catch (IOException) { file = "Local logging unavailable"; }
        catch (UnauthorizedAccessException) { file = "Local logging unavailable"; }

        // Fatal runtime failures cannot safely be recovered by WPF.
        if (e.Exception is OutOfMemoryException or AccessViolationException)
            return;

        e.Handled = true;
        MessageBox.Show(
            "A UI operation could not be completed. No save change was committed " +
            "automatically.\n\n" + e.Exception.Message +
            "\n\nDiagnostic log: " + file,
            "TEW Universe Studio | UI error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
