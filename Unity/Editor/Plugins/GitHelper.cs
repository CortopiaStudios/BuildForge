using System;
using System.Diagnostics;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    internal static class GitHelper
    {
        public static string Run(string arguments, int timeoutMs = 5000)
        {
            Process process = null;
            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = arguments,
                        WorkingDirectory = Application.dataPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }

                stdout.Wait(1000);
                stderr.Wait(1000);

                if (process.ExitCode != 0)
                    return null;

                var output = stdout.Result?.Trim();
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        public static int? RunInt(string arguments, int timeoutMs = 5000)
        {
            var output = Run(arguments, timeoutMs);
            if (output != null && int.TryParse(output, out var value))
                return value;
            return null;
        }
    }
}
