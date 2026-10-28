using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public DateTime? SlaStartedAtUtc { get; private set; }

    /// <summary>Application-managed token, portable to SQL Server and SQLite tests.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    public void ApplyTriage(DateTime dueAtUtc, int? agentId, DateTime nowUtc)
    {
        if (dueAtUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(dueAtUtc));
        }

        DueAtUtc = dueAtUtc;
        SlaStartedAtUtc = nowUtc;
        AssignTo(agentId, nowUtc);
    }

    public void EnsureCanEscalate()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException($"Ticket {Reference} is {Status} and cannot be escalated.");
        }

        if (Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException($"Ticket {Reference} is already Critical and cannot be escalated.");
        }
    }

    public TicketEscalation Escalate(DateTime dueAtUtc, int? agentId, string reason,
        string escalatedBy, DateTime nowUtc)
    {
        EnsureCanEscalate();
        if (dueAtUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(dueAtUtc));
        }

        var history = new TicketEscalation(Id, Priority, Priority + 1, AssignedAgentId,
            agentId, DueAtUtc, dueAtUtc, reason, escalatedBy, nowUtc);
        Priority++;
        ApplyTriage(dueAtUtc, agentId, nowUtc);
        return history;
    }

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>
    /// Raises a new ticket for a customer. It starts as <see cref="TicketStatus.New"/>, with no
    /// owner and no due date.
    /// </summary>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
        Version = Guid.NewGuid();
    }
}
