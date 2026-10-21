# Native tool lifecycle

```mermaid
sequenceDiagram
  participant U as Unity Agent
  participant B as Backend
  participant L as DeepSeek
  participant X as ToolExecutionBridge
  participant O as Orchestrator
  U->>B: snapshot and turn identity
  B->>B: validate and expose available schemas
  B->>L: context and native tools
  L-->>B: tool call chunks
  B->>B: assemble and validate
  B-->>X: validated request
  X->>X: identity arguments and dedup
  X->>O: semantic request
  O-->>X: accepted or rejected dispatch
  X->>B: ToolResult and completion scope
  B->>L: tool-role result and observations
  L-->>B: grounded speech stream
  B-->>U: sentences and final
```

The canonical manifest mirrors byte-for-byte into Unity Resources. Provider-compatible aliases preserve internal names. PendingToolResults binds responses to active identity; call IDs enforce once-only dispatch. Timeout means execution unknown, not permission to blindly repeat a body action.

Selection uses a narrow explicit-intent tool-choice policy and grounding instructions; it is not unconstrained model choice. Body authority remains the Orchestrator. Speech should describe only what results establish. Stationary promise/outcome mismatches remain open.

agent_streaming.install/native_tools.install connect channels; result feedback uses /agent/tool-result. Read source for limits. Legacy behavior envelopes remain compatible, but native requests must not execute twice via legacy and native paths.
