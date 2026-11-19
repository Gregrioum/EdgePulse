using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace EdgePulse;
public static class SensorSetup
{
    // Official, signed PawnIO 2.1.0 distributed by LibreHardwareMonitor v0.9.6.
    // The publisher signature was verified during packaging; pin the exact bytes.
    public const string DownloadUrl = "https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe";
    public const string Sha256 = "A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0";
    public static async Task Install()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EdgePulse-Sensors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "PawnIO_setup.exe");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            byte[] bytes = await client.GetByteArrayAsync(DownloadUrl);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("The sensor installer did not match the verified official download.");
            await File.WriteAllBytesAsync(file, bytes);
            using var process = Process.Start(new ProcessStartInfo(file, "-install") { UseShellExecute = true, Verb = "runas" });
            if (process == null) throw new IOException("Windows could not start the sensor installer.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Sensor setup did not complete (exit code " + process.ExitCode + ").");
        }
        finally { try { if (File.Exists(file)) File.Delete(file); Directory.Delete(directory); } catch (IOException) { } }
    }
}
