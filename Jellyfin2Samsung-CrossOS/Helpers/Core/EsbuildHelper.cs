using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Core
{
    public static class EsbuildHelper
    {
        public static string? GetEsbuildPath()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string relPath;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "win-x64", "esbuild.exe");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "linux-x64", "esbuild");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "osx-universal", "esbuild");
                }
                else
                {
                    return null;
                }

                string fullPath = Path.Combine(baseDir, relPath);
                return File.Exists(fullPath) ? fullPath : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Transpiles ES2015+ JavaScript to ES5 using esbuild.
        /// If esbuild is missing or fails, returns the original JS.
        /// </summary>
        public static async Task<string> TranspileAsync(string js, string? relPathForLog = null)
        {
            try
            {
                string? esbuildPath = GetEsbuildPath();
                if (string.IsNullOrEmpty(esbuildPath))
                {
                    Trace.WriteLine($"⚠ esbuild binary not found, skipping transpile for {relPathForLog ?? "unknown"}");
                    return js;
                }

                string tempRoot = Path.Combine(Path.GetTempPath(), "J2S_Esbuild");
                Directory.CreateDirectory(tempRoot);

                string inputPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".js");
                string outputPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".js");

                await File.WriteAllTextAsync(inputPath, js, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = esbuildPath,
                    Arguments = $"\"{inputPath}\" --outfile=\"{outputPath}\" --target=es2015",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();

                proc.WaitForExit();

                if (proc.ExitCode != 0 || !File.Exists(outputPath))
                {
                    Trace.WriteLine($"⚠ esbuild failed for {relPathForLog ?? "unknown"} (exit {proc.ExitCode}): {stderr}");
                    return js;
                }

                string transpiled = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);

                try
                {
                    File.Delete(inputPath);
                    File.Delete(outputPath);
                }
                catch
                {
                    // ignore cleanup errors
                }

                Trace.WriteLine($"      ✓ Transpiled {relPathForLog ?? "unknown"} via esbuild");
                return transpiled;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"⚠ esbuild transpile error for {relPathForLog ?? "unknown"}: {ex}");
                return js;
            }
        }
    }
}