namespace OpenClaw.Agent.Routing;

public sealed class NoopTurnRoutingPolicy : ITurnRoutingPolicy
{
    public static NoopTurnRoutingPolicy Instance { get; } = new();

    private NoopTurnRoutingPolicy()
    {
    }

    public ValueTask<TurnRoutingDecision> ResolveAsync(TurnRoutingRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new TurnRoutingDecision());
}