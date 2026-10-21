# Agent Harness

1. AgentStateStore.CreateSnapshot reads authoritative conversation/avatar/user/environment state. AgentStateSnapshot stores immutable serialized values and returns fresh JSON trees.
2. agent_state.validate_snapshot checks identity, version, age, shape and enums. Invalid observations degrade to minimal context rather than granting new authority.
3. AgentContextBuilder combines the frozen Persona, mode overlay, completed history pairs and bounded current-state context. General mode does not invent exercise telemetry. History is in-process, **not long-term memory**.
4. CapabilityRegistry exposes semantic schema-constrained tools. Backend and Unity executor validation are separate boundaries.
5. ToolExecutionBridge checks trace/turn/request/call identities and dispatches through the existing Orchestrator. The LLM cannot set arbitrary bones, world coordinates or priorities.
6. ToolResult returns status/reason/completion scope; current observations are refreshed without inventing completed physical outcomes.
7. TurnLifecycle/SpeechArbiter reject late results and protect speech/history ownership. Reliability distinguishes normal rejection from infrastructure failure.

Seven capabilities currently cover Nod, Wave, expression.set and Lead/Side/Trail/Nearby. These are not arbitrary functions. **Accepted is dispatch acknowledgement where the descriptor says so**, not proof of movement completion. Stationary Lead/Side illustrates the distinction.

Persona V2, Schema V2 and Orchestrator semantics are copied as reference, not redesigned. No adaptive intervention autonomy or durable memory is claimed.
