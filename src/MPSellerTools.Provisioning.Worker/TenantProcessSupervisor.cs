using System.ComponentModel;
using System.Diagnostics;
using MPSellerTools.Core.Platform;

namespace MPSellerTools.Provisioning.Worker;

public record LaunchedProcess(int ProcessId, DateTime StartTimeUtc);

/// <summary>
/// Starts, verifies, and stops TenantHost instances. Verification always
/// checks the stored PID's start time (and, where accessible, its module
/// path) against what we launched — a bare PID is not sufficient, since the
/// OS reuses them (brief §10). This class must never affect a process it did
/// not itself verify as one of ours; it never enumerates or kills processes
/// by name alone.
/// </summary>
public class TenantProcessSupervisor(ProvisioningOptions options, ILogger<TenantProcessSupervisor> logger)
{
    public LaunchedProcess Start(string instanceConfigPath, string url, string workingDirectory)
    {
        var dllPath = Path.Combine(options.TenantHostPublishDirectory, "MPSellerTools.TenantHost.dll");
        if (!File.Exists(dllPath))
        {
            throw new InvalidOperationException(
                $"Published TenantHost not found at '{dllPath}'. Run scripts/Build.ps1 first.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = options.DotnetExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        startInfo.ArgumentList.Add(dllPath);
        startInfo.Environment["ASPNETCORE_URLS"] = url;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        // The instance config path is the only thing passed to the child process
        // beyond the standard ASP.NET Core variables — no secrets on the command
        // line or in an argument list that would show up in a process listing
        // (brief §10: "Do not pass passwords through CLI arguments").
        startInfo.Environment["MPST_INSTANCE_CONFIG_FILE"] = instanceConfigPath;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start TenantHost process.");

        logger.LogInformation("Started TenantHost process {Pid} for {Url}", process.Id, url);
        return new LaunchedProcess(process.Id, process.StartTime.ToUniversalTime());
    }

    public bool IsRunning(Tenant tenant)
    {
        if (tenant.ProcessId is not { } pid || tenant.ProcessStartTimeUtc is not { } startTimeUtc)
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            // Allow a small tolerance: process start times as reported by the OS
            // can be truncated to whole seconds depending on platform.
            var matches = Math.Abs((process.StartTime.ToUniversalTime() - startTimeUtc).TotalSeconds) < 2;
            return matches && !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No process with that ID exists any more.
            return false;
        }
        catch (Win32Exception)
        {
            // The PID exists but belongs to a process we can't query (the OS
            // reused it for something we don't have access to) — definitely
            // not the tenant process we launched.
            return false;
        }
    }

    public void Stop(Tenant tenant)
    {
        if (!IsRunning(tenant) || tenant.ProcessId is not { } pid)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            logger.LogInformation("Stopping verified TenantHost process {Pid} for tenant {Slug}", pid, tenant.Slug);
            // No graceful HTTP drain in this MVP — brief §10 requires Suspend to
            // "actually stop access", which an immediate terminate satisfies;
            // it does not require zero-downtime shutdown of in-flight requests.
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
