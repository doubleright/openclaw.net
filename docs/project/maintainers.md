# Maintainers

This page defines OpenClaw.NET maintainer roles and review expectations. It does not name current maintainers or grant authority by sponsorship, employment, or commercial relationship.

Maintainer status is based on sustained contribution, trust, technical judgment, reliability in review, and community conduct.

## Current Structure

### Core Maintainers

Core maintainers are responsible for project-wide technical direction and final approval for changes that affect:

- runtime architecture
- security-sensitive behavior
- release authority
- package and project boundaries
- default provider behavior
- public roadmap decisions
- cross-subsystem compatibility

Core maintainer approval is required for runtime/core, gateway security, release, and package-boundary changes.

### Area Maintainers

Area maintainers guide scoped areas of the project. They may review pull requests, maintain docs, shape examples, and coordinate contributor work inside their scope.

Area maintainer authority is scoped. Core runtime, security-sensitive, release, and package-boundary changes still require core maintainer approval.

### Reviewers / Trusted Contributors

Reviewers and trusted contributors help evaluate correctness, test coverage, documentation clarity, compatibility, and scope. They may provide strong review input without holding maintainer authority.

Trusted contributor status can be a path toward area maintainer status when paired with sustained contribution, sound judgment, and healthy community conduct.

## Area Maintainer Scope

Area maintainer scope should be explicit. A scope may include a documentation area, sample family, adapter group, regional community effort, or extension pack.

Example area:

| Area | Scope | Boundary |
| --- | --- | --- |
| China Community & Industrial Extensions | Chinese-language documentation, community feedback, industrial examples, reusable industrial adapter guidance | Does not grant authority over core runtime, security, releases, package boundaries, vendor defaults, or product-specific workflows |

Additional areas can be added when the project has sustained contributors and clear ownership needs.

## Review Expectations

Maintainers and reviewers should check:

- correctness against the stated behavior
- sufficient tests or an explicit docs-only rationale
- NativeAOT compatibility and trimming impact
- security posture and unsafe-operation gating
- documentation updates for behavior, setup, or compatibility changes
- scope control and package-boundary discipline
- fail-fast behavior for unsupported modes
- vendor neutrality for industrial or commercial contributions
- disclosure when a contribution directly supports a company or customer use case

## Approval Boundaries

Area maintainers can approve or guide work within their scope when the change does not affect core runtime architecture, gateway security, release authority, or package boundaries.

Core maintainer approval is required when a pull request:

- changes core runtime contracts or behavior
- changes gateway security posture or public-bind safeguards
- changes release flow, package output, or supported artifact boundaries
- changes default provider behavior
- adds heavy optional integrations without a clear extension boundary
- introduces reflection, dynamic loading, or hidden JIT requirements in AOT-sensitive paths
- creates a new project direction or roadmap commitment

When in doubt, reviewers should ask whether the work belongs in core, an optional extension, documentation, or a downstream product.
