using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ShellX.Desktop;

// Native AOT embeds managed code itself. These resources carry the remaining native dependencies.
internal static class NativeDependencies
{
    private const string Prefix = "ShellX.Native/";

    public static void Initialize()
    {
        var assembly = typeof(NativeDependencies).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Order().ToArray();
        if (resources.Length == 0) return;
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("此单文件 AOT 包仅支持 Windows x64。");

        var files = resources.Select(name => (Name: name, Hash: HashResource(assembly, name))).ToArray();
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files.Select(file => file.Name + file.Hash)))))[..24];
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellX", "native", "win-x64", version);
        Directory.CreateDirectory(directory);
        using var mutex = new Mutex(false, @"Local\ShellX.Native." + version);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("初始化原生组件超时，请稍后重试。");
            foreach (var file in files)
            {
                var relative = file.Name[Prefix.Length..];
                if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) throw new IOException("原生组件路径无效。");
                var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path) && HashFile(path) == file.Hash) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var source = assembly.GetManifestResourceStream(file.Name)!)
                    using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) source.CopyTo(target);
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }

        // Preloading by absolute path also lets Windows resolve later imports by the DLL's basename.
        foreach (var file in files.Where(file => file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            NativeLibrary.Load(Path.Combine(directory, file.Name[Prefix.Length..]));
    }

    private static string HashResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
