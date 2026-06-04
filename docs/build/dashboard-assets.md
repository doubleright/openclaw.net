# Dashboard Asset Build

The gateway serves Dashboard static assets from `wwwroot/dashboard`, but the Dashboard project is not a normal Gateway `ProjectReference` for static web assets. The manual copy targets in `src/OpenClaw.Gateway/OpenClaw.Gateway.csproj` avoid Static Web Assets conflicts while still materializing Blazor WASM files for local builds and publish outputs.

## Default Behavior

`CopyDashboardFiles` runs after a normal Gateway build and publishes `src/OpenClaw.Dashboard` into the Gateway output folder:

```bash
dotnet build src/OpenClaw.Gateway/OpenClaw.Gateway.csproj -c Release
```

The target copies Dashboard `wwwroot` files into:

```text
src/OpenClaw.Gateway/bin/<Configuration>/<TFM>/wwwroot/dashboard
```

It also creates non-fingerprinted `blazor.webassembly.js` and `dotnet.js` copies so the Dashboard `index.html` resolves correctly when served by `UseStaticFiles`.

## Fast Build Loops

Set `OpenClawSkipDashboardBuild=true` when a build only needs Gateway code and tests, not materialized Dashboard assets:

```bash
dotnet build OpenClaw.Net.slnx -c Release -p:OpenClawSkipDashboardBuild=true
dotnet build src/OpenClaw.Gateway/OpenClaw.Gateway.csproj -c Release -p:OpenClawSkipDashboardBuild=true
```

The skip path removes `wwwroot/dashboard` from the Gateway build output so stale Dashboard files are not served accidentally from an old build. Do not use it for release publish validation or any check that needs to inspect served Dashboard assets.

## Publish Behavior

`CopyDashboardFilesPublish` runs after Gateway publish and always includes Dashboard assets in the publish directory:

```bash
dotnet publish src/OpenClaw.Gateway/OpenClaw.Gateway.csproj -c Release
```

This keeps release artifacts and smoke scripts on the full asset path even when ordinary build loops opt into the skip property.

## Troubleshooting

- If `/dashboard` returns missing `_framework` files, rebuild or publish Gateway without `OpenClawSkipDashboardBuild=true`.
- If static web asset resolution fails during Gateway builds, keep Dashboard out of a normal static-web-asset `ProjectReference`; update the manual copy targets instead.
- If the Blazor boot script name changes, update the non-fingerprinted copy patterns in the Gateway project file.
