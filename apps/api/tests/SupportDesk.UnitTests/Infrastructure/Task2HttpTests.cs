using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class Task2HttpTests
{
    [SqlServerFact]
    public async Task RealApiValidatesTriagesEscalatesAndPersistsHistory()
    {
        await using var db = SqlServerTask2Tests.NewDatabase();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SupportDesk.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = typeof(Task2HttpTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(root.FullName, "apps", "api", "src", "SupportDesk.Presentation");
        var dll = Path.Combine(project, "bin", configuration, "net10.0", "SupportDesk.Presentation.dll");
        Assert.True(File.Exists(dll), "Build the API in the test configuration first.");
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(dll);
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["ConnectionStrings__Default"] = db.Database.GetConnectionString()!;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var ready = false;
            for (var i = 0; i < 100; i++)
            {
                if (process.HasExited) break;
                try { using var probe = await client.GetAsync("/api/categories"); ready = probe.IsSuccessStatusCode; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                if (ready) break;
                await Task.Delay(200);
            }
            Assert.True(ready, process.HasExited ? await stderr + await stdout : "API did not become ready.");
            var customer = 1;
            // Seed category 5 is flagged Critical and specialist-only: Priya, not full Marcus.
            var createdResponse = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
                "HTTP triage verification", "A realistic description for the HTTP assessment test.", customer, 5, TicketPriority.Low), json);
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = (await createdResponse.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
            Assert.Equal(TicketPriority.Critical, created.Priority);
            Assert.Equal("Priya Nair", created.AssignedAgent!.FullName);
            Assert.Equal(created.CreatedAtUtc.AddHours(2), created.DueAtUtc);
            Assert.NotNull(created.Triage);
            Assert.NotNull(createdResponse.Headers.Location);
            var conflict = await client.PostAsJsonAsync($"/api/tickets/{created.Id}/escalate", new EscalateTicketRequest("More impact", "Lead"));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Contains("Critical", await conflict.Content.ReadAsStringAsync());
            var normalResponse = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
                "HTTP normal verification", "A realistic description for another test ticket.", customer, 1, TicketPriority.Low), json);
            var normal = (await normalResponse.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
            Assert.Equal("Sara Lindqvist", normal.AssignedAgent!.FullName);
            var path = $"/api/tickets/{normal.Id}/escalate";
            foreach (var invalid in new[] { new EscalateTicketRequest("four", "Lead"), new EscalateTicketRequest("Valid reason", " ") })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, invalid)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/tickets/999999/escalate", new EscalateTicketRequest("More impact", "Lead"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tickets/999999/escalations")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<TicketEscalationDto[]>($"/api/tickets/{normal.Id}/escalations", json))!);
            var escalationResponse = await client.PostAsJsonAsync(path, new EscalateTicketRequest("  Increased impact  ", "  Lead  "));
            Assert.Equal(HttpStatusCode.OK, escalationResponse.StatusCode);
            var result = (await escalationResponse.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
            Assert.Equal(TicketPriority.Medium, result.Ticket.Priority);
            Assert.Equal(normal.AssignedAgent.Id, result.Ticket.AssignedAgent!.Id);
            Assert.Equal(result.Escalation.EscalatedAtUtc.AddHours(12), result.Ticket.DueAtUtc);
            var history = (await client.GetFromJsonAsync<TicketEscalationDto[]>($"/api/tickets/{normal.Id}/escalations", json))!;
            Assert.Single(history);
            Assert.Equal("Increased impact", history[0].Reason);
            Assert.Equal("Lead", history[0].EscalatedBy);
            Assert.Equal(normal.DueAtUtc, history[0].FromDueAtUtc);
            Assert.Equal(result.Ticket.DueAtUtc, history[0].ToDueAtUtc);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tickets?slaStatus=999")).StatusCode);
            var resolved = await client.PatchAsJsonAsync($"/api/tickets/{normal.Id}/status", new UpdateTicketStatusRequest(TicketStatus.Resolved), json);
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
            var resolvedConflict = await client.PostAsJsonAsync(path, new EscalateTicketRequest("More impact", "Lead"));
            Assert.Equal(HttpStatusCode.Conflict, resolvedConflict.StatusCode);
            Assert.Contains("Resolved", await resolvedConflict.Content.ReadAsStringAsync());
            Assert.Single((await client.GetFromJsonAsync<TicketEscalationDto[]>($"/api/tickets/{normal.Id}/escalations", json))!);
            await db.Set<SupportDesk.Domain.Aggregates.Agents.Agent>().ExecuteUpdateAsync(setters => setters.SetProperty(a => a.IsActive, false));
            var unassignedResponse = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
                "Nobody eligible verification", "All agents are inactive in this isolated test database.", customer, 1, null), json);
            Assert.Equal(HttpStatusCode.Created, unassignedResponse.StatusCode);
            var unassigned = (await unassignedResponse.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
            Assert.Null(unassigned.AssignedAgent);
            Assert.Contains("Unassigned because", unassigned.Triage!.AssignmentReason);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            using var restarted = Process.Start(info)!;
            var restartedOutput = restarted.StandardOutput.ReadToEndAsync();
            var restartedError = restarted.StandardError.ReadToEndAsync();
            try
            {
                var restartedReady = false;
                for (var i = 0; i < 100; i++)
                {
                    if (restarted.HasExited) break;
                    try { using var probe = await client.GetAsync("/api/categories"); restartedReady = probe.IsSuccessStatusCode; }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    if (restartedReady) break;
                    await Task.Delay(200);
                }
                Assert.True(restartedReady, restarted.HasExited ? await restartedError + await restartedOutput : "Restart did not become ready.");
                var persisted = (await client.GetFromJsonAsync<TicketEscalationDto[]>($"/api/tickets/{normal.Id}/escalations", json))!;
                Assert.Single(persisted);
                Assert.Equal(history[0], persisted[0]);
                var tickets = await client.GetFromJsonAsync<SupportDesk.Application.Contracts.Common.PagedResult<TicketListItemDto>>("/api/tickets", json);
                Assert.Equal(43, tickets!.TotalCount);
            }
            finally
            {
                if (!restarted.HasExited) restarted.Kill(entireProcessTree: true);
                await restarted.WaitForExitAsync();
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await SqlServerTask2Tests.RemoveTestDatabase(db);
        }
    }
}
