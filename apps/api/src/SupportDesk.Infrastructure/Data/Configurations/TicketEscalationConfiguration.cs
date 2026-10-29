using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data.Configurations;

public sealed class TicketEscalationConfiguration : IEntityTypeConfiguration<TicketEscalation>
{
    public void Configure(EntityTypeBuilder<TicketEscalation> builder)
    {
        builder.ToTable("TicketEscalations", t =>
        {
            t.HasCheckConstraint("CK_TicketEscalations_Priorities", "[FromPriority] BETWEEN 1 AND 3 AND [ToPriority] = [FromPriority] + 1");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();
        builder.Property(e => e.EscalatedBy).HasMaxLength(100).IsRequired();
        builder.Property(e => e.EscalatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(e => e.FromDueAtUtc).HasColumnType("datetime2(3)");
        builder.Property(e => e.ToDueAtUtc).HasColumnType("datetime2(3)");
        builder.HasOne<Ticket>().WithMany().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Agent>().WithMany().HasForeignKey(e => e.FromAgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Agent>().WithMany().HasForeignKey(e => e.ToAgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.TicketId, e.EscalatedAtUtc, e.Id })
            .IsDescending(false, true, true).HasDatabaseName("IX_TicketEscalations_Ticket_History");
    }
}
