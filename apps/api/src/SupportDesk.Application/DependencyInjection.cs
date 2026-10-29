using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Features.Agents.Queries.GetAgents;
using SupportDesk.Application.Features.Auth;
using SupportDesk.Application.Features.Categories.Queries.GetCategories;
using SupportDesk.Application.Features.Customers.Queries.GetCustomer;
using SupportDesk.Application.Features.Customers.Queries.GetCustomers;
using SupportDesk.Application.Features.Reports.GetSlaSummary;
using SupportDesk.Application.Features.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetEscalations;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Queries.SearchTickets;

namespace SupportDesk.Application;

/// <summary>
/// Registers the command and query handlers and the request validators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginCommandHandler>();
        services.AddScoped<GetSlaSummaryQueryHandler>();
        services.AddScoped<GetCurrentAgentQueryHandler>();
        services.AddScoped<TicketTriageService>();
        services.AddScoped<EscalateTicketCommandHandler>();
        services.AddScoped<GetEscalationsQueryHandler>();
        services.AddScoped<RaiseTicketCommandHandler>();
        services.AddScoped<ChangeTicketStatusCommandHandler>();
        services.AddScoped<AssignTicketCommandHandler>();
        services.AddScoped<SearchTicketsQueryHandler>();
        services.AddScoped<GetTicketQueryHandler>();

        services.AddScoped<GetCustomersQueryHandler>();
        services.AddScoped<GetCustomerQueryHandler>();

        services.AddScoped<GetAgentsQueryHandler>();

        services.AddScoped<GetCategoriesQueryHandler>();

        services.AddValidatorsFromAssemblyContaining<RaiseTicketCommandHandler>(ServiceLifetime.Singleton);

        return services;
    }
}
