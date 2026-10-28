using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Services;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class AuthenticationHttpTests
{
    private const string Email = "alex.turner@northwind-support.example";
    private static async Task<LoginResponse> Login(HttpClient client, string password = SupportDbSeeder.DevelopmentPassword)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("  " + Email.ToUpperInvariant() + "  ", password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static string Mint(string key, string? subject = "1", string issuer = "Northwind.SupportDesk",
        string audience = "Northwind.SupportDesk.Web", bool expired = false, string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var claims = subject is null ? Array.Empty<Claim>() : new[] { new Claim("sub", subject) };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(issuer, audience, claims, now.AddMinutes(-10),
            expired ? now.AddMinutes(-5) : now.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [SqlServerFact]
    public async Task LoginFallbackAuthorizationTokenValidationAndAttributionWorkOverHttp()
    {
        await using var host = new AuthenticatedApiHost();
        await host.StartAsync();
        var client = host.Client;
        var protectedGets = new[] { "/api/tickets", "/api/tickets/1", "/api/customers", "/api/customers/1",
            "/api/agents", "/api/categories", "/api/tickets/1/escalations", "/api/auth/me" };
        foreach (var path in protectedGets)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        }
        foreach (var path in new[] { "/api/tickets", "/api/tickets/1/escalate" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        foreach (var path in new[] { "/api/tickets/1/status", "/api/tickets/1/assignment" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync(path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        var schema = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.Equal(0, schema.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post").GetProperty("security").GetArrayLength());
        foreach (var invalid in new[] { new LoginRequest(Email, "wrong password"),
            new LoginRequest("missing@example.com", SupportDbSeeder.DevelopmentPassword),
            new LoginRequest("yuki.tanaka@northwind-support.example", SupportDbSeeder.DevelopmentPassword) })
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", invalid);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var failure = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Invalid email or password.", failure.GetProperty("detail").GetString());
            Assert.False(failure.TryGetProperty("token", out _));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("invalid", ""))).StatusCode);
        var login = await Login(client);
        Assert.Equal(Email, login.User.Email);
        Assert.Equal("Alex Turner", login.User.FullName);
        Assert.InRange(login.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.Equal(login.User.Id.ToString(), decoded.Subject);
        Assert.Equal(3600, (decoded.ValidTo - decoded.ValidFrom).TotalSeconds);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.Token);
        foreach (var path in protectedGets) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        var me = (await client.GetFromJsonAsync<AuthUserDto>("/api/auth/me"))!;
        Assert.Equal(login.User, me);
        var agentsJson = await client.GetStringAsync("/api/agents");
        Assert.DoesNotContain("passwordHash", agentsJson, StringComparison.OrdinalIgnoreCase);
        var unsigned = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "Northwind.SupportDesk", "Northwind.SupportDesk.Web", new[] { new Claim("sub", "1") },
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5)));
        var noExpiry = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "Northwind.SupportDesk", "Northwind.SupportDesk.Web", new[] { new Claim("sub", "1") },
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(host.SigningKey)), SecurityAlgorithms.HmacSha256)));
        var invalidTokens = new[]
        {
            Mint(host.SigningKey, expired: true), Mint(host.SigningKey, issuer: "untrusted"),
            Mint(host.SigningKey, audience: "someone-else"), Mint(host.SigningKey, subject: null),
            Mint(host.SigningKey, subject: "0"), Mint(host.SigningKey, subject: "999999"), unsigned, noExpiry, Mint(host.SigningKey, subject: "not-an-id"),
            Mint(new string('z', 64)), Mint(host.SigningKey, algorithm: SecurityAlgorithms.HmacSha384),
            login.Token[..(login.Token.Length / 2)] + "x" + login.Token[(login.Token.Length / 2 + 1)..]
        };
        foreach (var token in invalidTokens)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets")).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.Token);
        var createdResponse = await client.PostAsJsonAsync("/api/tickets", new
        {
            title = "Authentication audit case",
            description = "A real request for authenticated audit verification.",
            customerId = 1,
            categoryId = 1,
            requestedPriority = "Low"
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();
        var escalated = await client.PostAsJsonAsync($"/api/tickets/{id}/escalate", new { reason = "Impact increased", escalatedBy = "Spoofed administrator" });
        Assert.Equal(HttpStatusCode.OK, escalated.StatusCode);
        var decision = await escalated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"agent:{login.User.Id}", decision.GetProperty("escalation").GetProperty("escalatedBy").GetString());
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/tickets/{id}/escalations");
        Assert.Equal($"agent:{login.User.Id}", history[0].GetProperty("escalatedBy").GetString());
        var account = await host.Database.Set<Agent>().SingleAsync(a => a.Id == login.User.Id);
        account.Deactivate();
        await host.Database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets")).StatusCode);
    }

    [SqlServerFact]
    public async Task UpgradedDemoAgentGetsHashedCredentialsAndRestartPreservesChangedHash()
    {
        await using var host = new AuthenticatedApiHost();
        await host.Database.GetService<IMigrator>().MigrateAsync("20261028101304_TicketEscalationAndSla");
        await host.Database.Database.ExecuteSqlRawAsync("""
            INSERT INTO Categories(Name, IsActive, RequiresSpecialist, ForcesCriticalPriority) VALUES ('General',1,0,0);
            INSERT INTO Customers(Name, Email, Tier, CreatedAtUtc) VALUES ('Existing customer','customer@example.com',1,'2026-03-02T09:00:00');
            INSERT INTO Agents(FullName, Email, IsActive, MaxOpenTickets, CreatedAtUtc)
            VALUES ('Alex Turner','  ALEX.TURNER@NORTHWIND-SUPPORT.EXAMPLE  ',1,10,'2026-03-02T09:00:00');
            INSERT INTO Tickets(Reference,Title,Description,CustomerId,CategoryId,Priority,Status,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES ('LEGACY-AUTH','Existing ticket','A ticket before credentials.',1,1,2,1,'2026-03-02T09:00:00','2026-03-02T09:00:00',NEWID());
            """);
        await host.StartAsync();
        await Login(host.Client);
        var account = await host.Database.Set<Agent>().SingleAsync();
        Assert.NotNull(account.PasswordHash);
        Assert.NotEqual(SupportDbSeeder.DevelopmentPassword, account.PasswordHash);
        Assert.Equal(Email, account.Email);
        const string changed = "Changed_Local_Test_Password2!";
        account.SetPasswordHash(new IdentityPasswordService().Hash(account, changed));
        var hash = account.PasswordHash;
        await host.Database.SaveChangesAsync();
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Email, SupportDbSeeder.DevelopmentPassword))).StatusCode);
        await Login(host.Client, changed);
        host.Database.ChangeTracker.Clear();
        Assert.Equal(hash, (await host.Database.Set<Agent>().SingleAsync()).PasswordHash);
        Assert.Equal(1, await host.Database.Set<SupportDesk.Domain.Aggregates.Tickets.Ticket>().CountAsync());
    }

    [SqlServerFact]
    public async Task ProductionStartupDoesNotProvisionDevelopmentCredentials()
    {
        await using var host = new AuthenticatedApiHost();
        await host.Database.Database.MigrateAsync();
        host.Database.Add(new Agent("Alex Turner", Email, 10, DateTime.UtcNow));
        await host.Database.SaveChangesAsync();
        await host.StartAsync("Production");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Email, SupportDbSeeder.DevelopmentPassword))).StatusCode);
        host.Database.ChangeTracker.Clear();
        Assert.Null((await host.Database.Set<Agent>().SingleAsync()).PasswordHash);
    }
}
