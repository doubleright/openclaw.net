# Dynamic Turn Routing 与 Model Profiles 协同指南

英文版： [dynamic-turn-routing-model-profiles.md](../dynamic-turn-routing-model-profiles.md)

本文说明 Dynamic Turn Routing（按回合路由）与 Model Profiles（模型档位）在 OpenClaw 中如何协同工作，重点回答三个问题：

1. 路由决策到底改了哪些会话字段
2. 模型档位选择在什么时候发生，以及优先级是什么
3. 如何把 tier 策略稳定映射到可运维的 profile 体系

## 一句话心智模型

Dynamic Turn Routing 不替换模型选择器，而是先在“当前回合”写入一组临时路由偏好（profile、tags、fallback、工具范围、响应策略），再由现有 Model Profiles 选择链路执行实际选模。

## 协同数据面

按回合路由会产出 TurnRoutingDecision，并投影到会话路由字段。常见关键字段：

- `ModelProfileId`：本回合首选 profile
- `DirectModelFallbackProfileId`：本回合优先 fallback profile
- `PreferredTags`：本回合偏好标签
- `AllowedTools` / `DisableTools`：本回合工具面收敛
- `ReasoningLevel`：本回合推理强度
- `ResponsePolicy`：本回合回答风格
- `Tier` / `Reason`：机器可读分层与原因

这些覆盖是 turn-scoped（按回合生效），回合结束后恢复原状态；`RouteModelTier` 保留 sticky 语义，用于跨回合策略（如 `EnableStickyTier`）。

## 执行顺序（谁先谁后）

一次请求的典型顺序：

1. 读取会话已有路由状态（手工 route、历史 tier、tags、fallback）
2. 执行 turn routing，产出本回合决策
3. 把决策临时写入 session 路由字段
4. 使用“写入后的 session 视图”执行 Model Profiles 选择
5. 发起模型调用（包含工具声明、系统提示、reasoning/response 设置）
6. 回合完成后恢复 session 到调用前快照（sticky 字段除外）

这意味着：

- Dynamic Turn Routing 是“前置策略层”
- Model Profiles 是“最终落地层”

## 优先级与覆盖关系

在“同一回合”内，建议按以下优先级理解：

1. TurnRoutingDecision 显式字段（最高）
2. 会话既有 route 字段（手工设置或历史状态）
3. 全局默认（`OpenClaw:Models:DefaultProfile` 与 `OpenClaw:Llm:*`）

关键点：

- `AllowedTools=[]` 在 MAF 路径保留“不要覆盖手工 allowlist”的语义（空不覆盖，非空才覆盖）
- `DirectModelFallbackProfileId` 会被提升到当前回合 fallback 顺序前位
- `ReasoningLevel` / `ResponsePolicy` 会影响本回合请求构造，但在回合后恢复

## 推荐配置模式

### 1) 用 tier 绑定 profile（主路径）

在 `DynamicTurnRouting.Policy.Tiers` 中给每个层级定义明确的 `ModelProfileId`：

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

### 2) 用 tags 做同层弹性

当一个 tier 对应多个可替代 profile 时，保留 `ModelProfileId` 作为主选，同时设置 `PreferredTags` 引导同能力池内选择（如 `local`、`cheap`、`tool-reliable`）。

### 3) 用 direct fallback 控制降级路径

对于高能力 tier（如 T2/T3），建议显式配置 `DirectModelFallbackProfileId`，避免 fallback 回到不满足能力约束的默认 profile。

## 设计建议（避免常见坑）

- 保持 tier 语义稳定：T0/T1 省成本，T2/T3 保能力，不要频繁重定义
- `Policy.Tiers` 中引用的 profile 必须在 `OpenClaw:Models:Profiles` 存在
- fallback 链尽量同能力域，避免“降级后失去工具/结构化输出能力”
- 对 `ReasoningLevel` 与 `ResponsePolicy` 做 profile 能力校验，避免配置了但上游不支持
- native 与 MAF 改动应同步落测试，避免单路径漂移

## 联调与排障清单

1. 先看 tier 决策：是否命中预期 `T0..T3`
2. 再看 profile 投影：`ModelProfileId`、`PreferredTags`、`FallbackModelProfileIds` 是否按预期生效
3. 检查工具面：`DisableTools` 与 `AllowedTools` 是否符合预期
4. 检查回合后恢复：非 sticky 字段是否正确回滚
5. 对照 native/MAF 一致性：同输入同配置下决策是否一致

## 与现有文档的关系

- 动态路由架构与 ONNX 边界： [opensquilla-dynamic-turn-routing.md](opensquilla-dynamic-turn-routing.md)
- 模型档位能力与选择约束： [../MODEL_PROFILES.md](../MODEL_PROFILES.md)
- MAF 路径说明： [../integrations/microsoft-agent-framework.md](../integrations/microsoft-agent-framework.md)

如果你在做生产落地，建议把本文作为“协同操作手册”，把 [opensquilla-dynamic-turn-routing.md](opensquilla-dynamic-turn-routing.md) 作为“能力边界与风险说明”。
