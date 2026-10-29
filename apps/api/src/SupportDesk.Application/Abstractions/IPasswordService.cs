using SupportDesk.Domain.Aggregates.Agents;
namespace SupportDesk.Application.Abstractions;

public enum PasswordCheck { Failed, Success, RehashNeeded }

public interface IPasswordService
{
    string Hash(Agent agent, string password);
    PasswordCheck Verify(Agent? agent, string password);
}
