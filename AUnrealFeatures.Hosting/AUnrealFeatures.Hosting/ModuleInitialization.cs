using AUnrealFeatures.Hosting.Configuration;
﻿using Serilog;
using Serilog.Enrichers.CallerInfo;
using Serilog.Events;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AUnrealFeatures.Hosting
{
    public static class ModuleInitialization
    {
        const string CONSOLE_OUTPUT_TEMPLATE =
            "[{Timestamp:HH:mm:ss}] [{Bucket} - {ProcessId}/{ThreadId}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        const string FILE_OUTPUT_TEMPLATE =
            "[{Timestamp:MM/dd/yy HH:mm:ss}] [{Bucket} - {ProcessId}/{ThreadId}] [{SourceFile}({Method}:{LineNumber})] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        public static void Initialize()
        {
            if (EnvironmentSettings.GetBoolean("AUNREAL_AUTO_ELEVATE", false))
                TryRestartWithElevatedPrivileges();

            Log.Logger = InitializeLogger("Main");
        }

        internal static ILogger InitializeLogger(string name)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithCallerInfo(
                    includeFileInfo: true,
                    assemblyPrefix: "AUnrealFeatures.",
                    filePathDepth: 1)
                .Enrich.WithProperty("Bucket", name, false)
                .WriteTo.Console(outputTemplate: CONSOLE_OUTPUT_TEMPLATE)
                .WriteTo.File("logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: FILE_OUTPUT_TEMPLATE)
                .CreateLogger();
        }

        public static bool IsElevated()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);

                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetEffectiveUserId() == 0;

            return false;
        }

        public static void TryRestartWithElevatedPrivileges()
        {
            if (IsElevated())
                return;

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException();

                ProcessStartInfo startInfo = new ProcessStartInfo();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo.FileName = exePath;
                    startInfo.Verb = "runas";
                    startInfo.UseShellExecute = true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    startInfo.FileName = "sudo";
                    startInfo.Arguments = exePath;
                    startInfo.UseShellExecute = false;
                }
                else throw new PlatformNotSupportedException();

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to restart with elevated privileges: {ex.Message}");
            }
        }

        [DllImport("libc", EntryPoint = "geteuid")]
        private static extern uint GetEffectiveUserId();
    }
}