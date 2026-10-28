namespace SupportDesk.Application.Abstractions;

public interface ICurrentAgent
{
    int AgentId { get; }
    string AuditActor { get; }
}
