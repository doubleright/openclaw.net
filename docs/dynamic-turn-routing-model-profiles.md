# Dynamic Turn Routing and Model Profiles Collaboration Guide

Chinese version: [zh-CN/dynamic-turn-routing-model-profiles.md](zh-CN/dynamic-turn-routing-model-profiles.md)

This guide explains how Dynamic Turn Routing and Model Profiles collaborate in OpenClaw. It focuses on three practical questions:

1. Which session fields are changed by turn routing
2. When profile selection happens and what the precedence is
3. How to map tier strategy to an operable profile system

## One-line mental model

Dynamic Turn Routing does not replace model selection. It writes turn-scoped routing preferences for the current turn (profile, tags, fallback, tool scope, response behavior), then the existing Model Profiles selection flow resolves the final model call.

## Collaboration data surface

Turn routing produces a TurnRoutingDecision and projects it onto session routing fields. Common high-impact fields:

- `ModelProfileId`: primary profile for this turn
- `DirectModelFallbackProfileId`: preferred fallback profile for this turn
- `PreferredTags`: turn-scoped profile tag preferences
- `AllowedTools` / `DisableTools`: turn-scoped tool-surface narrowing
- `ReasoningLevel`: turn-scoped reasoning intensity
- `ResponsePolicy`: turn-scoped response style
- `Tier` / `Reason`: machine-readable tier and reason

These overrides are turn-scoped and restored after the turn. `RouteModelTier` keeps sticky semantics for cross-turn policies such as `EnableStickyTier`.

## Execution order

Typical request flow:

1. Read existing session route state (manual route settings, prior tier, tags, fallback)
2. Run turn routing and produce a decision
3. Apply turn-scoped decision fields to session state
4. Run Model Profiles selection against that routed session view
5. Execute model call (tool declarations, system prompt, reasoning/response settings)
6. Restore session state to pre-turn snapshot (except sticky fields)

This means:

- Dynamic Turn Routing is the pre-routing strategy layer
- Model Profiles is the final model-resolution layer

## Precedence and override rules

Within a single turn, practical precedence is:

1. explicit fields from TurnRoutingDecision (highest)
2. existing session route fields (manual settings or retained state)
3. global defaults (`OpenClaw:Models:DefaultProfile` and `OpenClaw:Llm:*`)

Important details:

- In the MAF path, `AllowedTools=[]` preserves manual allowlists (empty means "do not override", non-empty applies)
- `DirectModelFallbackProfileId` is promoted to the front of the turn's fallback order
- `ReasoningLevel` / `ResponsePolicy` shape the current request and are restored after the turn

## Recommended configuration patterns

### 1) Bind tiers to profiles (primary pattern)

Define a clear `ModelProfileId` per tier in `DynamicTurnRouting.Policy.Tiers`:

```json
{
  "OpenClaw": {
    "DynamicTurnRouting": {
      "Enabled": true,
      "BundlePath": "models/routing/opensquilla-v4-compat",
      "Policy": {
        "Tiers": {
          "T0": { "ModelProfileId": "local-freeform", "DisableTools": true, "PromptMode": "minimal" },
          "T1": { "ModelProfileId": "mini-readonly", "AllowedTools": ["read_file", "grep_search"], "PromptMode": "compact" },
          "T2": { "ModelProfileId": "frontier-tools" },
          "T3": { "ModelProfileId": "frontier-deep" }
        }
      }
    }
  }
}
```

### 2) Use tags for intra-tier elasticity

When one tier can use multiple equivalent profiles, keep `ModelProfileId` as the primary anchor and use `PreferredTags` for same-capability preference steering (for example `local`, `cheap`, `tool-reliable`).

### 3) Use direct fallback to control downgrade path

For higher-capability tiers (for example T2/T3), set `DirectModelFallbackProfileId` explicitly so fallback does not drift into a profile that fails required capabilities.

## Design guidance

- Keep tier semantics stable: T0/T1 for cost control, T2/T3 for capability retention
- Every profile referenced by `Policy.Tiers` must exist in `OpenClaw:Models:Profiles`
- Keep fallback chains in the same capability domain to avoid losing tools/structured-output support on downgrade
- Validate `ReasoningLevel` and `ResponsePolicy` against actual upstream capability support
- Keep native and MAF changes test-synced to avoid one-path drift

## Debug checklist

1. Check tier decision first: did it hit expected `T0..T3`
2. Check profile projection: did `ModelProfileId`, `PreferredTags`, `FallbackModelProfileIds` apply as expected
3. Check tool surface: are `DisableTools` / `AllowedTools` effective
4. Check post-turn restore: are non-sticky fields rolled back correctly
5. Check native/MAF parity: same input + same config should converge to same decision

## Related docs

- Dynamic routing architecture and ONNX boundary: [opensquilla-dynamic-turn-routing.md](opensquilla-dynamic-turn-routing.md)
- Model profile capabilities and selection constraints: [MODEL_PROFILES.md](MODEL_PROFILES.md)
- MAF runtime path details: [integrations/microsoft-agent-framework.md](integrations/microsoft-agent-framework.md)

For production rollout, treat this guide as the collaboration playbook and [opensquilla-dynamic-turn-routing.md](opensquilla-dynamic-turn-routing.md) as the boundary/risk reference.
