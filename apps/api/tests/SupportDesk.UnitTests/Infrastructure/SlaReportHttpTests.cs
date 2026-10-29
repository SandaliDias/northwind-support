using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class SlaReportHttpTests
{
    [SqlServerFact]
    public async Task ReportRequiresAuthenticationValidatesInputAndSerializesBothGroupings()
    {
        await using var host = new AuthenticatedApiHost();
        await host.StartAsync();
        const string path = "/api/reports/sla-summary";
        const string bounds = "from=2000-01-01T00:00:00Z&to=2100-01-01T00:00:00Z";
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync($"{path}?{bounds}")).StatusCode);
        var login = await host.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            "alex.turner@northwind-support.example", "Local_Only_Demo1!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        foreach (var invalid in new[] { "", bounds + "&groupBy=customer",
                     "from=not-a-date&to=2100-01-01T00:00:00Z", "from=2026-01-01&to=2027-01-01",
                     "from=2100-01-01T00:00:00Z&to=2000-01-01T00:00:00Z" })
        {
            var response = await host.Client.GetAsync($"{path}?{invalid}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.TryGetProperty("errors", out _));
        }
        foreach (var grouping in new[] { "agent", "priority" })
        {
            var response = await host.Client.GetAsync($"{path}?{bounds}&groupBy={grouping}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rows = json.EnumerateArray().ToArray();
            Assert.Equal(40, rows.Sum(row => row.GetProperty("total").GetInt32()));
            Assert.All(rows, row =>
            {
                Assert.True(row.TryGetProperty("breachRate", out _));
                Assert.True(row.TryGetProperty("averageResolutionHours", out _));
                Assert.InRange(row.GetProperty("breachRate").GetDouble(), 0, 100);
                if (grouping == "priority") Assert.Equal(JsonValueKind.String, row.GetProperty("priority").ValueKind);
            });
        }
        var empty = await host.Client.GetFromJsonAsync<SlaSummaryGroupDto[]>(
            $"{path}?from=1900-01-01T00:00:00Z&to=1901-01-01T00:00:00Z");
        Assert.Empty(empty!);
        var offset = await host.Client.GetAsync($"{path}?from=2000-01-01T05:30:00%2B05:30&to=2100-01-01T05:30:00%2B05:30");
        Assert.Equal(HttpStatusCode.OK, offset.StatusCode);
    }
}
