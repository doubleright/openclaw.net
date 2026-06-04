## Description

[Provide a brief description of the changes in this PR. What problem do they solve?]

## Summary

[Summarize the user-facing or repository-facing impact.]

## Related Issues

[Fixes #123, Contributes to #456]

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Tests
- [ ] Build/CI
- [ ] Governance/process
- [ ] Refactoring (no functional changes)

## Validation

- [ ] `dotnet restore OpenClaw.Net.slnx`
- [ ] `dotnet build OpenClaw.Net.slnx --configuration Release --no-restore`
- [ ] `dotnet test OpenClaw.Net.slnx --configuration Release --no-build`
- [ ] `dotnet run --project samples/OpenClaw.HelloAgent -c Release --no-build`

## Review Notes

- [ ] I considered NativeAOT compatibility
- [ ] I considered security posture and unsafe defaults
- [ ] I updated docs/tests where needed
- [ ] This PR is scoped and does not mix unrelated changes

## Commercial or Customer-Driven Contribution Disclosure

If this PR directly supports a company, customer, or downstream commercial product use case, disclose that context here. Commercial use is welcome, but OpenClaw.NET should remain vendor-neutral.

## Checklist

- [ ] I have read the [CONTRIBUTING](../CONTRIBUTING.md) guidelines
- [ ] My code follows the code style implementation of this project
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] All new and existing tests passed locally (`dotnet test`)
- [ ] I have updated the documentation (README.md, comments) if required
- [ ] I have checked for security implications (input validation, authorization)
- [ ] I have checked the relevant [maintainer review checklist](../docs/maintainers/review-checklist.md)
- [ ] I have disclosed whether this directly supports a company or customer use case

## Screenshots (if applicable)

[Add screenshots for UI/UX changes]

## Benchmarks (if applicable)

[Did this change affect performance? Provide benchmark results.]
