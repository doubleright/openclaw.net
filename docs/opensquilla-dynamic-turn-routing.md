# OpenSquilla Dynamic Turn Routing

中文版本： [zh-CN/opensquilla-dynamic-turn-routing.md](zh-CN/opensquilla-dynamic-turn-routing.md)

OpenClaw.NET now includes an optional OpenSquilla-style dynamic turn-routing surface that can classify each user turn into `T0` through `T3`, then project that decision onto the existing OpenClaw model-profile, tool-filter, and system-prompt seams.

This is not a second routing stack. The implementation reuses the existing session-scoped route fields and model selection pipeline:

- `Session.ModelProfileId`
- `Session.PreferredModelTags`
- `Session.RouteAllowedTools`
- `Session.SystemPromptOverride`
- `Session.RouteModelTier`
- `Session.RouteReason`

The result is a turn-scoped routing layer that stays compatible with the repository's NativeAOT-first architecture and optional-dependency discipline.

## What It Does

At the start of each turn, OpenClaw can resolve a `TurnRoutingDecision` that contains:

- a tier name such as `T0`, `T1`, `T2`, or `T3`
- an optional model profile override
- an optional tool allowlist for the turn
- optional preferred profile tags
- an optional prompt suffix used as route instructions
- a machine-readable reason string

That decision is applied only for the current turn. After the turn completes, the previous session route state is restored.

One field is intentionally sticky: `Session.RouteModelTier` is retained across turns so `EnableStickyTier` policy behavior can be applied consistently.

This gives the runtime a cheap place to narrow prompt scope and tool scope before the normal model call happens.

## Supported Runtime Paths

Dynamic turn routing is currently wired into both supported orchestrators:

- the native `AgentRuntime`
- the Microsoft Agent Framework adapter `MafAgentRuntime`

That matters because `MafAgentRuntime` is a separate orchestration path and does not flow through `NativeAgentRuntimeFactory`. The routing policy had to be wired explicitly into the MAF adapter so both runtimes now honor the same turn-scoped routing contract.

## Architecture Shape

The implementation is intentionally split across three layers.

### Core contracts

`OpenClaw.Core` contains only configuration and validation contracts:

- `DynamicTurnRoutingConfig`
- `DynamicTurnRoutingClassifierConfig`
- `DynamicTurnRoutingEmbeddingsConfig`
- `DynamicTurnRoutingTierMap`
- `DynamicTurnRoutingTierTarget`

This keeps the core runtime free of ONNX and tokenizer dependencies.

### Runtime abstraction

`OpenClaw.Agent` contains the runtime-facing abstraction:

- `ITurnRoutingPolicy`
- `TurnRoutingRequest`
- `TurnRoutingDecision`
- `NoopTurnRoutingPolicy`

The native runtime and the MAF runtime both consume this abstraction and apply route-scoped session overrides before they build tool declarations, system prompt text, and chat options for the current turn.

### Optional ONNX implementation

`OpenClaw.Routing.Onnx` contains the optional ONNX-backed implementation boundary.

The gateway composes that implementation only when `OpenClaw:DynamicTurnRouting:Enabled=true`.

This preserves the repository's boundary rules:

- `OpenClaw.Core` stays small and AOT-friendly
- `OpenClaw.Agent` depends only on a routing interface
- `OpenClaw.Gateway` decides whether to compose the ONNX implementation

## Configuration

The feature is configured under `OpenClaw:DynamicTurnRouting`.

OpenClaw now supports two operator-facing routing inputs:

- direct OpenClaw configuration through `Assets` and `Policy`
- imported OpenSquilla-style bundle directories through `BundlePath`

At startup, both shapes normalize into one internal resolved routing model before the ONNX policy is constructed.

Preferred modern shape:

```json
{
  "OpenClaw": {
    "DynamicTurnRouting": {
      "Enabled": true,
      "BundlePath": "models/routing/opensquilla-v4",
      "Assets": {
        "ClassifierModelPath": "models/routing/override/classifier.onnx"
      },
      "Policy": {
        "EnableStickyTier": true,
        "EnableMarginUpgrade": true,
        "EnableUnderRoutingSafety": true
      }
    }
  }
}
```

Legacy compatibility shape:

```json
{
  "OpenClaw": {
    "DynamicTurnRouting": {
      "Enabled": true,
      "Classifier": {
        "ModelPath": "models/routing/squilla_classifier.onnx"
      },
      "Embeddings": {
        "ModelPath": "models/routing/minilm/model.onnx",
        "TokenizerPath": "models/routing/minilm/tokenizer.json",
        "Dimensions": 384
      },
      "Tiers": {
        "T0": {
          "ModelProfileId": "local-freeform",
          "DisableTools": true,
          "PromptMode": "minimal"
        },
        "T1": {
          "ModelProfileId": "mini-readonly",
          "AllowedTools": ["read_file"],
          "PromptMode": "compact"
        },
        "T2": {
          "ModelProfileId": "frontier-tools",
          "PromptMode": "full"
        },
        "T3": {
          "ModelProfileId": "frontier-deep",
          "PromptMode": "full"
        }
      }
    }
  }
}
```

Legacy `Classifier` / `Embeddings` / `Tiers` settings remain supported as compatibility mode.

## Tier Mapping Model

Each tier target can currently shape the turn in four ways:

- `ModelProfileId`: chooses which existing OpenClaw profile should serve the turn
- `AllowedTools`: narrows the declared tool list for that turn
- `PreferredTags`: biases profile selection toward matching profile tags
- `PromptMode` and `DisableTools`: appends a compact route instruction suffix to the system prompt

The built-in prompt suffix behavior is intentionally small:

- `minimal`: `Respond directly with minimal reasoning.`
- `compact`: `Keep the reply short and skip planning.`
- `DisableTools=true`: `Respond directly and do not call tools.`

## Validation Rules

Startup validation now checks both the legacy and modern routing shapes.

The validator currently fails fast when:

- a configured tier points at a profile id that is not present in `OpenClaw:Models:Profiles`
- classifier assets imply embeddings without a tokenizer path
- bundle-based configuration resolves a tier-to-profile mapping that is invalid

For bundle asset discovery failures, startup remains available and runtime falls back to `T2` with a machine-readable reason instead of hard-failing process boot.

## Current Runtime Behavior

The current implementation has two distinct parts:

1. the turn-scoped routing contract and runtime wiring are implemented and tested
2. the optional ONNX policy now executes a real local inference path when its assets are present

What is implemented today:

- native runtime applies route-scoped model, prompt, and tool overrides per turn
- MAF runtime applies the same route-scoped overrides per turn
- route state is restored after the turn, except `Session.RouteModelTier` which is intentionally sticky across turns
- gateway composition enables a noop policy by default and an ONNX-backed policy when configured
- config validation rejects tier-to-profile mappings that reference unknown profiles
- `LocalOnnxEmbeddingGenerator` loads a Hugging Face-style BPE `tokenizer.json`, tokenizes the user turn, runs the embedding ONNX model, and converts the result into a fixed-size embedding vector
- `PromptFeatureExtractor` combines lightweight prompt heuristics with that embedding vector
- `OnnxTurnRoutingPolicy` feeds the resulting feature vector into the classifier ONNX model and maps the predicted class back onto the configured tier target

Current fallback and compatibility behavior:

- when classifier assets are missing, the policy falls back to `T2`
- when local inference throws at runtime, the policy also falls back to `T2` with a machine-readable reason
- the classifier path accepts common integer label outputs as well as float logits and applies `argmax` for the latter

The feature support today is best understood as:

- architecture and runtime integration: implemented
- operator config surface: implemented
- local embedding plus classifier inference path: implemented
- fallback semantics: implemented

This is still an experimental routing capability, not a tuned production classifier. The runtime wiring is stable, but the prompt features and threshold defaults are intentionally conservative and are not claimed to match OpenSquilla-equivalent accuracy.

The remaining limitations are narrower now:

- the tokenizer loader currently supports Hugging Face-style BPE `tokenizer.json` files, not every tokenizer family
- the embedding model path assumes common transformer input names and either direct embedding outputs or sequence outputs that can be mean-pooled
- prompt features and classifier thresholds are still intentionally simple and have not been tuned to claim OpenSquilla-equivalent quality

## Native And MAF Semantics

Both runtimes now share the same high-level semantics:

- resolve a turn-routing decision before the model call
- apply turn-scoped session overrides
- build tools and system prompt from the routed session view
- execute the turn
- restore the prior session route state

For the MAF adapter there is one extra nuance: if a routing decision returns an empty `AllowedTools` array, the runtime does not overwrite an already-populated manual `Session.RouteAllowedTools` allowlist. This preserves existing MAF route-filter behavior when no explicit per-turn allowlist is supplied by the router.

## Operator Guidance

Use this feature when you want:

- a future-proof seam for cheap turn classification before model selection
- route-scoped tool narrowing for read-only or lightweight turns
- compact prompt instructions for simpler requests
- a clean architecture boundary for local classifier experiments

Do not treat it yet as a finished OpenSquilla-equivalent local router if you need:

- real learned tier classification in production
- full local embedding plus classifier inference behavior
- tuned prompt feature extraction and probability thresholds

If you need stronger claims, run the offline routing evaluation baseline first and compare the report against a known sample set before widening the operating envelope.

For now, think of it as a stable routing contract plus optional ONNX composition boundary that is ready for a stronger classifier implementation.

## Related Docs

- [LOCAL_MODELS.md](LOCAL_MODELS.md): local asset and operator guidance
- [ARCHITECTURE_BOUNDARIES.md](ARCHITECTURE_BOUNDARIES.md): why ONNX routing stays outside `OpenClaw.Core`
- [MODEL_PROFILES.md](MODEL_PROFILES.md): how routed profile ids flow through normal model selection
- [integrations/microsoft-agent-framework.md](integrations/microsoft-agent-framework.md): MAF runtime path and adapter context
- [OpenSquilla 动态路由实现研究.md](OpenSquilla%20动态路由实现研究.md): original research note that motivated the implementation
