# Proposal: OpenClaw.NET Industrial Pack Preview

This document is a proposal. It is not a committed roadmap, release promise, or ownership claim over an ecosystem direction.

## Purpose

Explore reusable industrial and edge extensions for OpenClaw.NET while preserving the existing self-hosted, NativeAOT-friendly, vendor-neutral project direction.

The preview should help .NET developers evaluate a lightweight, secure, self-hostable industrial AI gateway for manufacturing and edge monitoring scenarios.

## Positioning

OpenClaw.NET can provide a lightweight, secure, self-hostable industrial AI gateway for .NET developers.

The Industrial Pack should extend OpenClaw.NET through reusable adapters, examples, and docs. It should not move product-specific industrial workflows into core.

## Initial Target

The initial target is manufacturing and edge monitoring.

The first reference implementation idea is a Machine Monitoring Agent that can demonstrate telemetry ingestion, alert classification, maintenance-ticket creation, and shift-summary generation against simulated or clearly documented inputs.

## Candidate Adapters

Candidate adapter order:

1. MQTT first
2. Modbus later
3. OPC UA later
4. MES/ERP connectors later

The first adapter should optimize for a useful 10-minute evaluation path without requiring proprietary systems or vendor-specific defaults.

## Proposed Layers

- OpenClaw.NET Core
- OpenClaw Industrial Pack
- Downstream commercial/product solutions

OpenClaw.NET Core should remain focused on stable runtime contracts, safety, diagnostics, and NativeAOT-friendly behavior.

OpenClaw Industrial Pack should contain reusable industrial abstractions, adapters, examples, templates, and documentation.

Downstream commercial or product solutions should own customer-specific workflows, proprietary UX, factory-specific rules, and vendor-specific deployment choices.

## What Belongs In The Industrial Pack

- protocol abstractions
- telemetry ingestion contracts
- simulated machine sample
- alert classification sample
- maintenance-ticket sample
- shift-summary sample
- diagnostics and observability examples
- deployment guidance
- English and Chinese documentation

## What Does Not Belong

- customer-specific factory logic
- proprietary digital employee workflows
- vendor-exclusive defaults
- closed commercial UX
- product-specific business rules

## Governance

The Industrial Pack must remain vendor-neutral.

Commercial contributors should disclose when a contribution directly supports a company or customer use case. This helps reviewers evaluate whether the change is reusable infrastructure, extension work, documentation, or downstream product logic.

Core runtime changes require core maintainer review. Changes that affect gateway security posture, release authority, package boundaries, default provider behavior, or public roadmap direction also require core maintainer review.

Area maintainers may guide Industrial Pack documentation, adapters, samples, and community feedback within their scope.

## Open Questions

- Which adapter should ship first?
- Should Industrial Pack live in this repo or a separate repo?
- What is the minimal useful console experience?
- What should the Chinese documentation path look like?
- What should be the first "works in 10 minutes" sample?
