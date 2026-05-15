# Canvas and A2UI

Canvas and A2UI are supported as a first-party visual workspace for websocket clients. OpenClaw.NET supports the existing A2UI v0.8 JSONL flow and A2UI v0.9 structured surface operations. It is not a replacement for the browser tool and does not support arbitrary remote webpage navigation or remote-page script execution.

## What Ships

- Gateway Canvas configuration under `OpenClaw:Canvas`.
- Typed websocket envelopes for Canvas commands, A2UI pushes, structured v0.9 operations, results, and user events/actions.
- Native agent tools: `canvas_present`, `canvas_hide`, `canvas_navigate`, `canvas_snapshot`, `a2ui_push`, `a2ui_reset`, `a2ui_eval`, `a2ui_create_surface`, `a2ui_update_components`, `a2ui_update_data_model`, `a2ui_delete_surface`, and `a2ui_sync_ui_to_data`.
- A broker that forwards commands only to the current websocket session, tracks request acknowledgements/results, records runtime events, negotiates catalogs, and locks the selected catalog per surface lifecycle.
- Webchat Canvas host with a side panel, sandboxed local HTML iframe, per-surface A2UI rendering, lightweight snapshots, and conservative fallbacks for advanced components.
- Companion Canvas tab with native Avalonia A2UI rendering, per-surface state, event/action feedback, and snapshot feedback.

## Capability Scope

Supported:

- A2UI v0.8 JSONL frames through `a2ui_push`.
- A2UI v0.9 structured surface operations: `createSurface`, `updateComponents`, `updateDataModel`, `deleteSurface`, `action`, `syncUIToData`, and `error`.
- Multiple independent surfaces routed by `surfaceId`.
- Per-surface component trees, data models, diagnostics, and snapshots.
- Catalog negotiation through `supportedCatalogIds`.
- Present/hide/reset/push/snapshot command flow.
- Local HTML navigation by passing `html` to `canvas_navigate` in webchat.
- `about:blank` navigation.
- v0.8 `a2ui_event` and v0.9 `a2ui_action` feedback returned to the same conversation as structured session turns.
- Lightweight JSON snapshots containing rendered tree state, component values, diagnostics, data model state, and local HTML text when available.

Unsupported:

- `http:` and `https:` Canvas navigation.
- Script execution against remote webpages.
- Cross-session Canvas sharing.
- Native local HTML/WebView rendering in Companion. Companion returns an explicit unsupported diagnostic for local HTML navigation and A2UI eval.

## Configuration

Canvas settings live under `OpenClaw:Canvas`:

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | Enabled for loopback/local use. |
| `AllowOnPublicBind` | `false` | Required to keep Canvas enabled on non-loopback binds. |
| `MaxCommandBytes` | `262144` | Maximum serialized command size. |
| `MaxSnapshotBytes` | `262144` | Maximum returned snapshot size. |
| `CommandTimeoutSeconds` | `10` | Pending command timeout. |
| `MaxFramesPerPush` | `100` | Maximum A2UI frames in one push. |
| `EnableLocalHtml` | `true` | Allows `canvas_navigate` with inline `html`. |
| `EnableRemoteNavigation` | `false` | Remote webpage Canvas navigation remains unsupported. |
| `EnableEval` | `true` | Allows capability-gated `a2ui_eval` commands when a client advertises `a2ui.eval`. First-party clients do not advertise it. |

When strict public-bind hardening is applied and `AllowOnPublicBind=false`, Canvas is disabled. Without strict mode, public-bind startup refuses unsafe Canvas forwarding unless you explicitly opt in.

## Protocol

Server-to-client websocket envelope types:

- `canvas_present`
- `canvas_hide`
- `canvas_navigate`
- `canvas_snapshot`
- `a2ui_push`
- `a2ui_reset`
- `a2ui_eval`
- `a2ui_create_surface`
- `a2ui_update_components`
- `a2ui_update_data_model`
- `a2ui_delete_surface`
- `a2ui_sync_ui_to_data`

Client-to-server websocket envelope types:

- `canvas_ready`
- `canvas_ack`
- `canvas_snapshot_result`
- `canvas_eval_result`
- `a2ui_event`
- `a2ui_action`
- `a2ui_error`
- `a2ui_sync_result`

Common Canvas fields include `requestId`, `sessionId`, `surfaceId`, `contentType`, `frames`, `html`, `url`, `script`, `snapshotMode`, `snapshotJson`, `componentId`, `event`, `action`, `valueJson`, `dataModelJson`, `sequence`, `capabilities`, `supportedCatalogIds`, `catalogId`, `components`, `parametersJson`, `syncMode`, `success`, and `error`.

Example `canvas_ready` with v0.9 support:

```json
{
  "type": "canvas_ready",
  "capabilities": [
    "a2ui.v0_8",
    "a2ui.v0_9",
    "canvas.present",
    "canvas.hide",
    "canvas.local_html",
    "snapshot.state"
  ],
  "supportedCatalogIds": [
    "urn:a2ui:catalog:openclaw_v0_8",
    "urn:a2ui:catalog:agenui_catalog"
  ]
}
```

Example A2UI v0.8 push:

```json
{
  "type": "a2ui_push",
  "sessionId": "session_123",
  "surfaceId": "main",
  "contentType": "application/x-a2ui+jsonl;version=0.8",
  "frames": "{\"type\":\"text\",\"id\":\"summary\",\"text\":\"Ready\"}\n{\"type\":\"button\",\"id\":\"approve\",\"label\":\"Approve\"}"
}
```

Example A2UI v0.9 `createSurface`:

```json
{
  "type": "a2ui_create_surface",
  "operation": "createSurface",
  "requestId": "canvas_123",
  "sessionId": "session_123",
  "surfaceId": "details",
  "catalogId": "urn:a2ui:catalog:agenui_catalog",
  "surfaceTitle": "Details",
  "components": [
    "{\"type\":\"Text\",\"id\":\"title\",\"text\":\"Order details\"}",
    "{\"type\":\"Button\",\"id\":\"approve\",\"label\":\"Approve\"}"
  ],
  "dataModelJson": "{\"order\":{\"status\":\"ready\"}}"
}
```

Example A2UI v0.9 `updateComponents`:

```json
{
  "type": "a2ui_update_components",
  "operation": "updateComponents",
  "requestId": "canvas_124",
  "sessionId": "session_123",
  "surfaceId": "details",
  "components": [
    "{\"type\":\"Text\",\"id\":\"title\",\"text\":\"Updated order\"}"
  ]
}
```

Example A2UI v0.9 `updateDataModel`:

```json
{
  "type": "a2ui_update_data_model",
  "operation": "updateDataModel",
  "requestId": "canvas_125",
  "sessionId": "session_123",
  "surfaceId": "details",
  "dataModelJson": "{\"order\":{\"status\":\"approved\"}}"
}
```

Example A2UI v0.9 action feedback:

```json
{
  "type": "a2ui_action",
  "operation": "action",
  "sessionId": "session_123",
  "surfaceId": "details",
  "componentId": "approve",
  "action": "click",
  "valueJson": "true",
  "sequence": 12
}
```

Example A2UI v0.9 `deleteSurface`:

```json
{
  "type": "a2ui_delete_surface",
  "operation": "deleteSurface",
  "requestId": "canvas_126",
  "sessionId": "session_123",
  "surfaceId": "details"
}
```

## A2UI v0.8 JSONL Validation

`a2ui_push` accepts JSONL only: one JSON object per line. The gateway validates payload size, frame count, required `type` and `id` fields, component-specific required fields, and supported component types before forwarding.

Examples:

```jsonl
{"type":"markdown","id":"summary","text":"## Run ready\nReview the table before approving."}
{"type":"table","id":"checks","columns":["Check","Status"],"rows":[["Tests","Passed"],["Policy","Pending"]]}
{"type":"input","id":"note","label":"Operator note","value":""}
{"type":"button","id":"approve","label":"Approve"}
{"type":"progress","id":"deploy","value":0.45}
```

The v0.8 JSONL validator still rejects v0.9 `createSurface` frames with an explicit diagnostic. Use the structured v0.9 tools instead.

## A2UI v0.9 Surfaces

Each v0.9 surface has its own state:

- `surfaceId`
- selected `catalogId`
- title
- component tree/list
- data model JSON/object
- component values
- diagnostics
- active/deleted state

Lifecycle behavior:

- `a2ui_create_surface` creates or replaces the target surface and locks the negotiated catalog for `senderId + sessionId + surfaceId`.
- `a2ui_update_components` replaces only the targeted surface component tree.
- `a2ui_update_data_model` updates only the targeted surface data model and lets clients refresh bound UI without resending component definitions.
- `a2ui_sync_ui_to_data` asks the client to return its current UI/data state for the target surface.
- `a2ui_delete_surface` removes the target surface and clears its catalog lock.
- `a2ui_reset` remains the v0.8 compatibility clear operation and clears the client Canvas state.
- `canvas_snapshot` returns surface-specific state when `surfaceId` is present.

## Catalog Negotiation

Built-in catalog IDs:

- `urn:a2ui:catalog:openclaw_v0_8`
- `urn:a2ui:catalog:agenui_catalog`

Selection rules:

1. The client advertises `supportedCatalogIds` in `canvas_ready`.
2. If `a2ui_create_surface` specifies `catalogId`, the broker validates that the client advertised it.
3. If no `catalogId` is specified, the broker prefers `urn:a2ui:catalog:agenui_catalog` when available, otherwise `urn:a2ui:catalog:openclaw_v0_8`.
4. The chosen catalog is locked per surface lifecycle.
5. Later updates for the same surface must use the locked catalog until `a2ui_delete_surface` clears the surface entry.

The AGenUI catalog supports at least these component types:

- `Text`, `Image`, `Icon`, `Divider`, `Video`, `AudioPlayer`, `Markdown`, `Button`, `TextField`, `CheckBox`, `Slider`, `ChoicePicker`, `DateTimeInput`, `Row`, `Column`, `Card`, `List`, `Tabs`, `Modal`, `Table`, `Carousel`, `Web`, `RichText`, and `Chart`.

Compatibility aliases map v0.8 lowercase names to catalog names, including `text` to `Text`, `markdown` to `Markdown`, `card` to `Card`, `button` to `Button`, `input` to `TextField`, `select` to `ChoicePicker`, `checklist` to `CheckBox`, `table` to `Table`, `image` to `Image`, `progress` to `Slider`, and `chart` to `Chart`.

## Client Behavior

Webchat advertises:

- `a2ui.v0_8`
- `a2ui.v0_9`
- `canvas.present`
- `canvas.hide`
- `canvas.local_html`
- `snapshot.state`
- supported catalogs `urn:a2ui:catalog:openclaw_v0_8` and `urn:a2ui:catalog:agenui_catalog`

Webchat renders v0.8 pushes into the default `main` surface and renders v0.9 operations into per-surface tabs. It supports renderer mappings for the AGenUI catalog and uses safe built-in placeholders for advanced media, carousel, and web components when a native renderer or sandbox is not available. Basic binding fields include `valueBinding`, `textBinding`, `itemsBinding`, `visibleBinding`, and `disabledBinding`, plus simple `{path.to.value}` interpolation.

Companion advertises:

- `a2ui.v0_8`
- `a2ui.v0_9`
- `canvas.present`
- `canvas.hide`
- `snapshot.state`
- supported catalogs `urn:a2ui:catalog:openclaw_v0_8` and `urn:a2ui:catalog:agenui_catalog`

Companion renders supported components natively, keeps one surface collection with an active surface selector, routes v0.8 pushes into `main`, and records diagnostics for unsupported advanced AGenUI components instead of failing the surface update.

The gateway uses advertised capabilities before forwarding commands. If a session is not websocket-backed, if the client is disconnected, or if the client lacks a required capability/catalog, Canvas tools fail with a normal tool error instead of attempting a best-effort send. No first-party client advertises `a2ui.eval`; the tool remains capability-gated for future local sandboxes.

## Security Model

Canvas inherits the gateway’s local-first posture:

- Commands are scoped to the current websocket session sender.
- Non-websocket sessions cannot receive Canvas commands.
- Public-bind deployments must explicitly opt into Canvas forwarding.
- Remote webpage Canvas navigation is rejected.
- Remote media and web components are rendered conservatively as placeholders or safe links unless a client explicitly supports a safe local rendering path.
- A2UI eval is capability-gated and never targets third-party pages.
- Command and result sizes are bounded.
- Canvas command lifecycle events are written to the runtime event log.

Use the existing browser tool for webpage automation, DOM inspection, and remote navigation.
