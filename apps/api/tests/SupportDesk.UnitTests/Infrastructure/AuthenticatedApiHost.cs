using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.UnitTests.Infrastructure;

internal sealed class AuthenticatedApiHost : IAsyncDisposable
{
    public SupportDbContext Database { get; } = SqlServerTask2Tests.NewDatabase();
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Process? _process;
    private Task<string>? _output;
    private Task<string>? _errors;
    private readonly int _port;
    public HttpClient Client { get; }

    public AuthenticatedApiHost()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        _port = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        Client = new() { BaseAddress = new Uri($"http://127.0.0.1:{_port}"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task StartAsync(string environment = "Development")
    {
        if (_process is not null)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            _process.Dispose();
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SupportDesk.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = typeof(AuthenticatedApiHost).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(root.FullName, "apps", "api", "src", "SupportDesk.Presentation");
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(Path.Combine(project, "bin", configuration, "net10.0", "SupportDesk.Presentation.dll"));
        // Restricted Windows test users cannot write the system Event Log; keep test diagnostics on the console.
        info.Environment["Logging__EventLog__LogLevel__Default"] = "None";
        info.Environment["Jwt__Issuer"] = "Northwind.SupportDesk";
        info.Environment["Jwt__Audience"] = "Northwind.SupportDesk.Web";
        info.Environment["Jwt__TokenLifetimeMinutes"] = "60";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        info.Environment["ASPNETCORE_URLS"] = Client.BaseAddress!.ToString();
        info.Environment["Jwt__SigningKey"] = SigningKey;
        info.Environment["ConnectionStrings__Default"] = Database.Database.GetConnectionString()!;
        _process = Process.Start(info)!;
        _output = _process.StandardOutput.ReadToEndAsync();
        _errors = _process.StandardError.ReadToEndAsync();
        for (var i = 0; i < 150; i++)
        {
            if (_process.HasExited) break;
            try
            {
                using var response = await Client.GetAsync("/api/auth/me");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200);
        }
        Assert.Fail(_process.HasExited ? await _errors + await _output : "Authentication test API did not become ready.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            _process.Dispose();
        }
        Client.Dispose();
        await SqlServerTask2Tests.RemoveTestDatabase(Database);
        await Database.DisposeAsync();
    }
}
