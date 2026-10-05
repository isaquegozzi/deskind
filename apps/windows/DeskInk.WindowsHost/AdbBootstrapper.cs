using System.Diagnostics;

namespace DeskInk.WindowsHost;

internal static class AdbBootstrapper
{
    public static string ConfigureUsbReverse()
    {
        try
        {
            var adb = ResolveAdb();
            if (adb is null) return "ADB não encontrado; LAN continua disponível.";

            var devices = Run(adb, ["devices"]);
            if (devices.ExitCode != 0)
                return $"ADB indisponível: {FirstMessage(devices.StandardError)}";

            var authorized = ParseAuthorizedDevices(devices.StandardOutput);
            if (authorized.Count == 0)
                return "Nenhum tablet ADB autorizado; LAN continua disponível.";
            if (authorized.Count > 1)
                return "Mais de um Android conectado; use LAN ou deixe somente um no USB.";

            var serial = authorized[0];
            ConfigureReverse(adb, serial, 27183);
            ConfigureReverse(adb, serial, 27184);
            return $"USB/ADB pronto: {serial}";
        }
        catch (Exception exception)
        {
            return $"ADB não configurado: {exception.Message}";
        }
    }

    internal static IReadOnlyList<string> ParseAuthorizedDevices(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[1].Equals("device", StringComparison.Ordinal))
        .Select(parts => parts[0])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static void ConfigureReverse(string adb, string serial, int port)
    {
        var result = Run(adb, ["-s", serial, "reverse", $"tcp:{port}", $"tcp:{port}"]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"reverse da porta {port} falhou: {FirstMessage(result.StandardError)}");
    }

    private static string? ResolveAdb()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android", "Sdk", "platform-tools", "adb.exe"),
            @"C:\ADB\adb.exe",
        };
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directory => Path.Combine(directory.Trim('"'), "adb.exe")));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static ProcessResult Run(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o ADB.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ADB não respondeu em 5 segundos.");
        }
        Task.WaitAll(stdout, stderr);
        return new ProcessResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string FirstMessage(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "erro desconhecido";

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
