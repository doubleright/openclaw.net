# Project Governance

OpenClaw.NET is governed to keep the runtime useful, self-hostable, secure, and vendor-neutral for .NET developers and operators.

Governance decisions should protect the project architecture, contributor trust, and downstream freedom to build commercial or community systems on top of OpenClaw.NET.

## Principles

- Keep OpenClaw.NET self-hostable and local-first by default.
- Preserve a NativeAOT-friendly core path.
- Keep optional integrations explicit, isolated, and documented.
- Prefer reusable infrastructure over product-specific workflow logic.
- Maintain clear review boundaries for runtime, gateway, security, release, and extension changes.
- Keep the project vendor-neutral, including when commercial contributors or sponsors participate.
- Require intentional contribution under the project license.

## Roles

### Core Maintainers

Core maintainers are responsible for final decisions that affect the long-term health of the project. Their responsibilities include:

- runtime architecture
- security-sensitive changes
- release authority
- package and project boundaries
- default provider behavior
- public roadmap direction
- final review of changes that cross subsystem boundaries

Core maintainer approval is required for changes that affect the runtime core, gateway security posture, public release process, package boundaries, or default operational behavior.

### Area Maintainers

Area maintainers may own scoped project areas such as:

- documentation
- Regional community
- industrial extensions
- samples
- specific adapters
- testing and compatibility documentation

Area maintainers can review and guide pull requests in their scope. They do not have final authority over core runtime architecture, security-sensitive changes, release authority, package boundaries, or project-wide roadmap decisions unless they are also core maintainers.

### Contributors

Contributors improve the project through code, documentation, tests, examples, issues, review, design discussion, and ecosystem feedback.

Contributions should be intentionally submitted under the project license. Contributors should keep pull requests focused, document behavior changes, and disclose when a contribution directly supports their company or customer use case.

### Sponsors

Sponsors support the project through funding, infrastructure, services, documentation help, examples, community support, or other resources.

Sponsorship does not grant:

- roadmap control
- release authority
- maintainer status
- exclusive ownership of a project direction
- priority control over project architecture
- vendor-specific defaults

Sponsors may be recognized by mutual agreement, but the project remains vendor-neutral.

## Maintainer Status

Maintainer status is based on sustained contribution, trust, technical judgment, reliability in review, and community conduct.

Maintainer roles are scoped. A person may be a trusted contributor or area maintainer in one project area without having authority over core runtime, security, release, or package-boundary decisions.

## Commercial Use

Commercial use of OpenClaw.NET is welcome.

Companies and contributors may build products, integrations, adapters, services, or internal platforms on top of OpenClaw.NET. When a contribution directly supports a company or customer use case, the contributor should disclose that context in the issue, pull request, or proposal. Disclosure helps reviewers evaluate scope, neutrality, security posture, and whether the work belongs in core or an extension.

Commercial contributors should avoid introducing vendor-specific defaults, customer-specific workflows, or product-specific business logic into the core project.

## Vendor Neutrality

OpenClaw.NET must remain vendor-neutral.

The project may support many providers, adapters, protocols, and downstream deployment styles, but no sponsor, company, or contributor owns an ecosystem direction. Integrations should preserve user choice and avoid making a vendor or customer workflow the default path.

## Industrial Pack Boundary

Industrial Pack work should remain reusable infrastructure, adapters, docs, examples, and templates.

Appropriate Industrial Pack contributions include:

- protocol abstractions
- telemetry ingestion contracts
- adapter examples
- simulated device or machine samples
- diagnostic and observability examples
- deployment guidance
- English and Chinese documentation

Industrial Pack contributions should not include:

- customer-specific factory logic
- proprietary digital employee workflows
- closed commercial UX
- vendor-exclusive defaults
- product-specific business rules

Changes that require core runtime behavior, gateway security posture, package boundaries, or release process updates need core maintainer review.
