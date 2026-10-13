// Adapted from RavenDB (https://github.com/IlluminedProject/ravendb), commit 46371e89d5ae3a6202e0018d1a5671b0fc6f2c79
// Copyright RavenDB Ltd.; see LICENSE.txt in this directory

using System.Diagnostics;

namespace ShadowVPN2.Infrastructure.RavenEmbedded;

internal static class RavenServerRunner {
    public static Process Run(ServerOptions options) {
        if (string.IsNullOrWhiteSpace(options.ServerDirectory))
            throw new ArgumentNullException(nameof(options.ServerDirectory));

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
            throw new ArgumentNullException(nameof(options.DataDirectory));

        if (string.IsNullOrWhiteSpace(options.LogsPath))
            throw new ArgumentNullException(nameof(options.LogsPath));

        var exec = GetExecutable(options);

        var commandLineArgs = new List<string>(options.CommandLineArgs);

        using (var currentProcess = Process.GetCurrentProcess()) {
            commandLineArgs.Add($"--Embedded.ParentProcessId={currentProcess.Id}");
        }

        if (!string.IsNullOrWhiteSpace(options.Licensing.License) &&
            !string.IsNullOrWhiteSpace(options.Licensing.LicensePath)) {
            throw new ArgumentException(
                $"Only one of Licence options '{nameof(options.Licensing.License)}' or '{nameof(options.Licensing.LicensePath)}' should be specified");
        }

        if (!string.IsNullOrWhiteSpace(options.Licensing.License))
            commandLineArgs.Add($"--License={options.Licensing.License}");
        else if (!string.IsNullOrWhiteSpace(options.Licensing.LicensePath))
            commandLineArgs.Add($"--License.Path={options.Licensing.LicensePath}");

        commandLineArgs.Add($"--License.Eula.Accepted={options.Licensing.EulaAccepted}");
        commandLineArgs.Add($"--License.DisableAutoUpdate={options.Licensing.DisableAutoUpdate}");
        commandLineArgs.Add($"--License.DisableAutoUpdateFromApi={options.Licensing.DisableAutoUpdateFromApi}");
        commandLineArgs.Add($"--License.DisableLicenseSupportCheck={options.Licensing.DisableLicenseSupportCheck}");
        commandLineArgs.Add(
            $"--License.ThrowOnInvalidOrMissingLicense={options.Licensing.ThrowOnInvalidOrMissingLicense}");

        commandLineArgs.Add("--Setup.Mode=None");
        commandLineArgs.Add($"--DataDir={options.DataDirectory}");
        commandLineArgs.Add($"--Logs.Path={options.LogsPath}");

        if (options.Security != null) {
            if (string.IsNullOrWhiteSpace(options.ServerUrl))
                options.ServerUrl = "https://127.0.0.1:0";

            if (options.Security.CertificatePath != null) {
                commandLineArgs.Add($"--Security.Certificate.Path={options.Security.CertificatePath}");
                if (options.Security.CertificatePassword != null)
                    commandLineArgs.Add($"--Security.Certificate.Password={options.Security.CertificatePassword}");
            }
            else {
                commandLineArgs.Add($"--Security.Certificate.Load.Exec={options.Security.CertificateLoadExec}");
                commandLineArgs.Add(
                    $"--Security.Certificate.Load.Exec.Arguments={options.Security.CertificateLoadExecArguments}");
            }

            var adminCertificates = options.BootstrapAdminCertificateThumbprints
                .Append(options.Security.ServerCertificateThumbprint);
            commandLineArgs.Add($"--Security.WellKnownCertificates.Admin={string.Join(';', adminCertificates)}");
        }
        else {
            if (string.IsNullOrWhiteSpace(options.ServerUrl))
                options.ServerUrl = "http://127.0.0.1:0";
        }

        commandLineArgs.Add($"--ServerUrl={options.ServerUrl}");


        var processStartInfo = new ProcessStartInfo {
            FileName = exec,

            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        foreach (var argument in commandLineArgs)
            processStartInfo.ArgumentList.Add(argument);
        RemoveEnvironmentVariables(processStartInfo);

        Process? process = null;
        try {
            process = Process.Start(processStartInfo) ??
                      throw new InvalidOperationException("Server process did not start");
            process.EnableRaisingEvents = true;
        }
        catch (Exception e) {
            process?.Kill();
            throw new InvalidOperationException("Unable to execute server." + Environment.NewLine +
                                                "Command was: " + Environment.NewLine +
                                                (processStartInfo.WorkingDirectory ??
                                                 Directory.GetCurrentDirectory()) + "> "
                                                + processStartInfo.FileName + " " + processStartInfo.Arguments, e);
        }

        return process;
    }

    private static string GetExecutable(ServerOptions options) {
        var executable = Path.Combine(options.ServerDirectory!,
            OperatingSystem.IsWindows() ? "Raven.Server.exe" : "Raven.Server");
        if (!File.Exists(executable))
            throw new FileNotFoundException("RavenDB server executable was not found", executable);
        return executable;
    }

    private static void RemoveEnvironmentVariables(ProcessStartInfo processStartInfo) {
        if (processStartInfo.Environment == null || processStartInfo.Environment.Count == 0)
            return;

        var variablesToRemove = new List<string>();
        foreach (var key in processStartInfo.Environment.Keys) {
            if (key == null)
                continue;

            if (key.StartsWith("APP_POOL_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("IIS_", StringComparison.OrdinalIgnoreCase))
                variablesToRemove.Add(key);
        }

        foreach (var key in variablesToRemove)
            processStartInfo.Environment.Remove(key);
    }
}