using SupportDesk.Domain.Common;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>Append-only audit snapshot; existing rows have no mutation operations.</summary>
public sealed class TicketEscalation : Entity
{
    private TicketEscalation() { }

    public TicketEscalation(int ticketId, TicketPriority fromPriority, TicketPriority toPriority,
        int? fromAgentId, int? toAgentId, DateTime? fromDueAtUtc, DateTime? toDueAtUtc,
        string reason, string escalatedBy, DateTime escalatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);
        Reason = reason.Trim();
        EscalatedBy = escalatedBy.Trim();
        if (Reason.Length is < 5 or > 500 || EscalatedBy.Length > 100)
        {
            throw new ArgumentException("Reason must be 5-500 characters and actor at most 100 characters.");
        }

        TicketId = ticketId;
        FromPriority = fromPriority;
        ToPriority = toPriority;
        FromAgentId = fromAgentId;
        ToAgentId = toAgentId;
        FromDueAtUtc = fromDueAtUtc;
        ToDueAtUtc = toDueAtUtc;
        EscalatedAtUtc = escalatedAtUtc;
    }

    public int TicketId { get; private set; }
    public TicketPriority FromPriority { get; private set; }
    public TicketPriority ToPriority { get; private set; }
    public int? FromAgentId { get; private set; }
    public int? ToAgentId { get; private set; }
    public DateTime? FromDueAtUtc { get; private set; }
    public DateTime? ToDueAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string EscalatedBy { get; private set; } = string.Empty;
    public DateTime EscalatedAtUtc { get; private set; }
}
