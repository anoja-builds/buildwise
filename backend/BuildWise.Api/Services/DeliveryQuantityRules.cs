namespace BuildWise.Api.Services;

/// <summary>Receiving and discrepancy analysis share the same fulfilment arithmetic.
/// Undamaged means eligible for inspection, not quality-approved inventory.</summary>
public static class DeliveryQuantityRules
{
    public static decimal Fulfilled(decimal received, decimal damaged) => received - damaged;
    public static decimal Outstanding(decimal ordered, decimal fulfilled) => Math.Max(0, ordered - fulfilled);

    public static DeliveryQuantityResult Calculate(decimal ordered, decimal priorReceived,
        decimal priorDamaged, decimal received, decimal damaged)
    {
        var prior = Fulfilled(priorReceived, priorDamaged);
        var current = Fulfilled(received, damaged);
        var before = Outstanding(ordered, prior);
        return new(prior, current, prior + current, before, Outstanding(ordered, prior + current),
            Math.Max(0, before - received), received > before);
    }
}

public record DeliveryQuantityResult(decimal PriorFulfilled, decimal CurrentUndamaged,
    decimal TotalFulfilled, decimal OutstandingBefore, decimal OutstandingAfter,
    decimal PhysicalShortage, bool OverDelivery);
