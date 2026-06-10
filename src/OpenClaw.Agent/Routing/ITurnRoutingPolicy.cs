namespace OpenClaw.Agent.Routing;

public interface ITurnRoutingPolicy
{
    ValueTask<TurnRoutingDecision> ResolveAsync(TurnRoutingRequest request, CancellationToken cancellationToken);
}