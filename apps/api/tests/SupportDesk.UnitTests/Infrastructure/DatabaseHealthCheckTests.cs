using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportDesk.Presentation.Health;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task AnUnreachableServerIsUnhealthyWithoutWaitingForApplicationRetriesOrLeakingSecrets()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"127.0.0.1,{port}",
            InitialCatalog = "private-database",
            UserID = "private-user",
            Password = "private-password",
            ConnectTimeout = 60,
            ConnectRetryCount = 10
        }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString
        }).Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new DatabaseHealthCheck(configuration).CheckHealthAsync(new HealthCheckContext(), deadline.Token);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Database is unavailable.", result.Description);
        Assert.Null(result.Exception);
        Assert.False(deadline.IsCancellationRequested);
        Assert.Equal(connectionString, configuration.GetConnectionString("Default"));
    }
}
