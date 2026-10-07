using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

// DevHost is the "local launcher for PlatformHost and Worker, used by Visual
// Studio" (brief §11/§12): pressing F5 with DevHost as the startup project
// should Just Work without any separate script step. It publishes the three
// components fresh (dotnet build does not copy wwwroot into bin output —
// only publish does, discovered in Increment 3 — so a stale Debug build
// would silently serve no frontend at all), launches PlatformHost and the
// Provisioning Worker as child processes sharing this console (so Ctrl+C
// here also reaches them and their own tenant children — see
// docs/architecture.md for the full process tree), and waits.

var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("Could not find MPSellerTools.sln above this executable's directory.");

var buildRoot = Path.Combine(solutionRoot, ".local", "build");
var platformHostPublishDir = Path.Combine(buildRoot, "PlatformHost");
var tenantHostPublishDir = Path.Combine(buildRoot, "TenantHost");
var workerPublishDir = Path.Combine(buildRoot, "Worker");

Console.WriteLine("MPSellerTools DevHost");
Console.WriteLine("=====================");
Console.WriteLine($"Solution root: {solutionRoot}");
Console.WriteLine();

Console.WriteLine("Publishing PlatformHost, TenantHost, and Provisioning.Worker (Release)...");
await PublishAsync(solutionRoot, "MPSellerTools.PlatformHost", platformHostPublishDir);
await PublishAsync(solutionRoot, "MPSellerTools.TenantHost", tenantHostPublishDir);
await PublishAsync(solutionRoot, "MPSellerTools.Provisioning.Worker", workerPublishDir);
Console.WriteLine("Publish complete.");
Console.WriteLine();

// Opt-in (scripts/Start-Dev.ps1 -PublicHost): the host name or IP address
// other machines use to reach this one. When set, PlatformHost and every
// tenant instance listen on all interfaces instead of localhost only, and
// accept requests addressed to that host. With -BehindProxy a reverse proxy
// on this machine answers on the public host instead (see README.md), so
// they keep listening on localhost and only the accepted host changes.
var publicHost = Environment.GetEnvironmentVariable("MPST_PUBLIC_HOST")?.Trim();
var isPublic = !string.IsNullOrEmpty(publicHost);
var behindProxy = isPublic && Environment.GetEnvironmentVariable("MPST_BEHIND_PROXY") == "1";

var platformEnvironment = new Dictionary<string, string>
{
    ["ASPNETCORE_URLS"] = isPublic && !behindProxy ? "https://*:7100" : "https://localhost:7100",
    ["ASPNETCORE_ENVIRONMENT"] = "Development",
};
if (isPublic)
{
    platformEnvironment["AllowedHosts"] = $"localhost;{publicHost}";
}

using var platformHost = StartChild(
    Path.Combine(platformHostPublishDir, "MPSellerTools.PlatformHost.dll"),
    platformHostPublishDir,
    platformEnvironment);
Console.WriteLine($"Started PlatformHost (PID {platformHost.Id}).");

Console.WriteLine("Waiting for PlatformHost to become ready...");
await WaitForHealthyAsync("https://localhost:7100/api/health");
Console.WriteLine("PlatformHost is ready.");
Console.WriteLine();

var workerEnvironment = new Dictionary<string, string>
{
    ["DOTNET_ENVIRONMENT"] = "Development",
    ["Provisioning__TenantHostPublishDirectory"] = tenantHostPublishDir,
};
if (isPublic)
{
    workerEnvironment["Provisioning__PublicHost"] = publicHost!;
    workerEnvironment["Provisioning__BehindProxy"] = behindProxy ? "true" : "false";
}

using var worker = StartChild(
    Path.Combine(workerPublishDir, "MPSellerTools.Provisioning.Worker.dll"),
    workerPublishDir,
    workerEnvironment);
Console.WriteLine($"Started Provisioning.Worker (PID {worker.Id}). It will start any Active tenants automatically.");
Console.WriteLine();

await WriteStateFileAsync(solutionRoot, platformHost, worker);

Console.WriteLine("Login URLs:");
Console.WriteLine($"  PlatformAdmin : https://{(isPublic ? publicHost : "localhost")}:7100/login");
Console.WriteLine("  Companies     : shown on each company's detail page in the platform console once Active");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop DevHost, PlatformHost, and the Provisioning Worker.");

var stopRequested = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // handle shutdown ourselves instead of letting the runtime hard-kill us
    stopRequested.TrySetResult();
};

await Task.WhenAny(stopRequested.Task, platformHost.WaitForExitAsync(), worker.WaitForExitAsync());

Console.WriteLine();
Console.WriteLine("Stopping...");
// Both children were started attached to this same console without
// CREATE_NEW_PROCESS_GROUP, so the Ctrl+C above already propagated to them
// (and, transitively, to any TenantHost the worker itself launched) via
// normal Windows console signal propagation. These explicit stops are a
// safety net for the remaining cases: a child hung, or DevHost is exiting
// because a child crashed rather than because of Ctrl+C.
TryStop(platformHost);
TryStop(worker);
await File.WriteAllTextAsync(Path.Combine(solutionRoot, ".local", "devhost-state.json"), "{}");

Console.WriteLine("Stopped.");
return;

static string? FindSolutionRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (directory.GetFiles("MPSellerTools.sln").Length > 0)
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    return null;
}

static async Task PublishAsync(string solutionRoot, string projectName, string outputDir)
{
    var projectPath = Path.Combine(solutionRoot, "src", projectName, $"{projectName}.csproj");
    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = solutionRoot,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("publish");
    startInfo.ArgumentList.Add(projectPath);
    startInfo.ArgumentList.Add("-c");
    startInfo.ArgumentList.Add("Release");
    startInfo.ArgumentList.Add("-o");
    startInfo.ArgumentList.Add(outputDir);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start dotnet publish for {projectName}.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet publish failed for {projectName} (exit code {process.ExitCode}).");
    }
}

static Process StartChild(string dllPath, string workingDirectory, Dictionary<string, string> environment)
{
    if (!File.Exists(dllPath))
    {
        throw new InvalidOperationException($"Expected published output not found: {dllPath}");
    }

    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(dllPath);
    foreach (var (key, value) in environment)
    {
        startInfo.Environment[key] = value;
    }

    return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {dllPath}.");
}

static async Task WaitForHealthyAsync(string healthUrl)
{
    using var handler = new HttpClientHandler
    {
        // Dev-only, localhost-only: this just avoids requiring `dotnet
        // dev-certs https --trust` to have already run before DevHost's own
        // first readiness check can succeed.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await client.GetFromJsonAsync<JsonElement?>(healthUrl);
            if (response?.GetProperty("databaseReachable").GetBoolean() == true)
            {
                return;
            }
        }
        catch
        {
            // Not up yet.
        }
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException($"{healthUrl} did not report healthy within 60 seconds.");
}

static void TryStop(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
    catch
    {
        // Already gone.
    }
}

static async Task WriteStateFileAsync(string solutionRoot, Process platformHost, Process worker)
{
    var localDir = Path.Combine(solutionRoot, ".local");
    Directory.CreateDirectory(localDir);

    var state = new
    {
        DevHostPid = Environment.ProcessId,
        PlatformHostPid = platformHost.Id,
        PlatformHostStartTimeUtc = platformHost.StartTime.ToUniversalTime(),
        WorkerPid = worker.Id,
        WorkerStartTimeUtc = worker.StartTime.ToUniversalTime(),
    };
    await File.WriteAllTextAsync(
        Path.Combine(localDir, "devhost-state.json"),
        JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
}
