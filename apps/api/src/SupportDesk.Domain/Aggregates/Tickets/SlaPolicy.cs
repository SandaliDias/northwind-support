using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>Plain policy data. Configuration binding belongs to the composition root.</summary>
public sealed class SlaPolicy
{
    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = new()
    {
        [TicketPriority.Low] = 72,
        [TicketPriority.Medium] = 24,
        [TicketPriority.High] = 8,
        [TicketPriority.Critical] = 4
    };

    public double PremiumCustomerMultiplier { get; set; } = 0.5;
    public double MinimumWindowHours { get; set; } = 1;
    public decimal AtRiskThresholdPercent { get; set; } = 25;

    public void Validate()
    {
        if (Enum.GetValues<TicketPriority>().Any(p => !BaseWindowHours.TryGetValue(p, out var hours)
                || !double.IsFinite(hours) || hours <= 0 || hours > 8760)
            || !double.IsFinite(PremiumCustomerMultiplier) || PremiumCustomerMultiplier <= 0 || PremiumCustomerMultiplier > 1
            || !double.IsFinite(MinimumWindowHours) || MinimumWindowHours <= 0 || MinimumWindowHours > 8760
            || AtRiskThresholdPercent is <= 0 or > 100
            || decimal.Round(AtRiskThresholdPercent, 8) != AtRiskThresholdPercent
            || Enum.GetValues<TicketPriority>().Any(p => Enum.GetValues<CustomerTier>()
                .Any(t => Window(p, t) < TimeSpan.FromMilliseconds(1))))
        {
            throw new InvalidOperationException("Invalid Sla configuration: positive windows/floor up to one year, premium multiplier (0,1], and threshold (0,100] are required.");
        }
    }

    public TimeSpan Window(TicketPriority priority, CustomerTier tier)
    {
        var hours = BaseWindowHours[priority];
        if (tier == CustomerTier.Premium)
        {
            hours = Math.Max(MinimumWindowHours, hours * PremiumCustomerMultiplier);
        }

        // The persistence contract is datetime2(3), so decisions use whole milliseconds.
        return TimeSpan.FromMilliseconds(Math.Round(TimeSpan.FromHours(hours).TotalMilliseconds));
    }

    public static TicketPriority Priority(TicketPriority? requested, bool forcesCritical) =>
        forcesCritical ? TicketPriority.Critical : requested ?? TicketPriority.Medium;
}
