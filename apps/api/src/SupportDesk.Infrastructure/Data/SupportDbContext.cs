using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Infrastructure.Data;

/// <summary>
/// EF Core context for the support desk, and the unit of work that commits each request's
/// changes. Mapping lives in the per-aggregate configuration classes in
/// <c>Data/Configurations</c>.
/// </summary>
public class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options), IUnitOfWork
{
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureHistoryIsImmutable();

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This ticket changed while the request was being saved. Refresh and try again.");
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    private void EnsureHistoryIsImmutable()
    {
        if (ChangeTracker.Entries<TicketEscalation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Escalation history is immutable.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureHistoryIsImmutable();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This ticket changed while the request was being saved. Refresh and try again.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
        modelBuilder.HasDbFunction(typeof(SupportDbContext).GetMethod(nameof(TicketSlaStatus))!)
            .HasName(nameof(TicketSlaStatus)).HasSchema(Database.IsSqlServer() ? "dbo" : null)
            .HasParameter("threshold").HasStoreType("decimal(18,8)");
    }

    /// <summary>Mapped to the migration's SQL function; never executed by the production CLR.</summary>
    public static int TicketSlaStatus(DateTime? due, DateTime? resolved, DateTime? started,
        DateTime now, decimal threshold) => throw new InvalidOperationException("SQL expression only.");

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp in this database is UTC; say so, so that it survives the round trip.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
