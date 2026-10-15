using System.Security.Cryptography;
using System.Text;
using ApexAudio.Core;

namespace ApexAudio.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string root = args.Contains("--data") ? args[Array.IndexOf(args, "--data") + 1] : Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(root);
        using var mutex = new Mutex(true, "Local\\ApexAudio-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root)))), out bool first);
        if (!first) { MessageBox.Show("声音工具已经打开，请从任务栏切回窗口。", "Apex Audio"); return; }
        string? smoke = args.Contains("--ui-smoke") ? args[Array.IndexOf(args, "--ui-smoke") + 1] : null;
        string initialized = Path.Combine(root, "initialized.json"), packages = Path.Combine(AppContext.BaseDirectory, "packages");
        if (!File.Exists(initialized) && Directory.Exists(packages))
        {
            var store = new PackageStore(root);
            foreach (string package in Directory.EnumerateFiles(packages, "*.gpack")) store.Install(package);
            File.WriteAllText(initialized, "{\"schemaVersion\":1}");
        }
        System.Windows.Forms.Application.Run(new MainWindow(root, smoke));
    }
}
