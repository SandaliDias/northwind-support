using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.AssignTicket;

/// <summary>
/// Hands a ticket to an agent, or takes it back off them when no agent is given.
/// </summary>
public sealed class AssignTicketCommandHandler(
    ITicketRepository tickets,
    IAgentRepository agents,
    ICategoryRepository categories,
    IAssignmentQueries assignments,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<AssignTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The ticket or the agent does not exist.</exception>
    /// <exception cref="ConflictException">The agent is inactive.</exception>
    public async Task<TicketDetailDto> HandleAsync(int id, AssignTicketRequest request, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);

        if (request.AgentId is { } agentId)
        {
            var agent = await agents.GetByIdAsync(agentId, ct) ?? throw new NotFoundException("Agent", agentId);

            if (!agent.IsActive)
            {
                throw new ConflictException(
                    $"Agent {agent.FullName} is not active and cannot take new tickets.");
            }
        }

        if (request.AgentId is { } targetId && targetId != ticket.AssignedAgentId)
        {
            var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
                ?? throw new NotFoundException("Category", ticket.CategoryId);
            var candidate = (await assignments.GetCandidatesAsync(ticket.CategoryId, ct)).SingleOrDefault(a => a.Id == targetId);
            if (candidate is null || !AgentAssignmentPolicy.IsEligible(candidate, category.RequiresSpecialist))
            {
                throw new ConflictException("The selected agent does not meet specialization or capacity requirements.");
            }
        }

        ticket.AssignTo(request.AgentId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} assigned to agent {AgentId}.", ticket.Reference, request.AgentId);

        return await getTicket.HandleAsync(id, ct);
    }
}
