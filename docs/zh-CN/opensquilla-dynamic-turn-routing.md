# OpenSquilla Dynamic Turn Routing

英文版： [opensquilla-dynamic-turn-routing.md](../opensquilla-dynamic-turn-routing.md)

OpenClaw.NET 现在提供了一个可选的、参考 OpenSquilla 思路实现的动态回合路由层。它可以在每个用户回合开始时，将请求归类到 `T0` 到 `T3` 之一，然后把该决策投影到 OpenClaw 现有的模型档位选择、工具过滤和系统提示词机制上。

这不是一套独立的新路由栈。它复用了现有的会话级路由字段：

- `Session.ModelProfileId`
- `Session.PreferredModelTags`
- `Session.RouteAllowedTools`
- `Session.SystemPromptOverride`
- `Session.RouteModelTier`
- `Session.RouteReason`

因此，这个能力更像是“每个回合的轻量决策层”，而不是替换 OpenClaw 原有模型选择逻辑。

## 它做什么（What It Does）

当所有回合都把完整系统提示词、完整工具声明和同一套昂贵模型配置发给上游模型时，简单请求和复杂请求会走同样的成本路径。

动态回合路由的目标是：

- 对简单回合使用更轻量的模型档位
- 对只读或窄范围任务只暴露必要工具
- 用更短的路由指令（route instruction）收紧提示词体积
- 在不重写 OpenClaw 主执行栈的前提下，为未来本地分类器留出稳定接缝

在每个回合开始时，运行时会先解析一份 `TurnRoutingDecision`，其中包含：

- tier（如 `T0` 到 `T3`）
- 可选的模型档位覆盖
- 可选的工具允许列表（allowlist）
- 可选的档位标签偏好
- 可选的路由指令（route instruction）后缀
- 机器可读原因（machine-readable reason）

这份决策只对当前回合生效，回合结束后会恢复原始会话路由状态。

有一个字段是刻意保留的 sticky 语义：`Session.RouteModelTier` 会跨回合保留，以便 `EnableStickyTier` 策略在 Native/MAF 路径上保持一致。

## 架构形态（Architecture Shape）

实现分成三层。

### 1. Core 配置契约

`OpenClaw.Core` 只保存配置和校验模型：

- `DynamicTurnRoutingConfig`
- `DynamicTurnRoutingAssetsConfig`
- `DynamicTurnRoutingPolicyConfig`
- `DynamicTurnRoutingClassifierConfig`
- `DynamicTurnRoutingEmbeddingsConfig`
- `DynamicTurnRoutingTierMap`
- `DynamicTurnRoutingTierTarget`
- `ResolvedDynamicTurnRoutingConfig`
- `ResolvedDynamicTurnRoutingAssets`

这样可以保证 `OpenClaw.Core` 不直接依赖 ONNX 或 tokenizer 运行时。

### 2. 运行时抽象层

`OpenClaw.Agent` 只定义运行时抽象：

- `ITurnRoutingPolicy`
- `TurnRoutingRequest`
- `TurnRoutingDecision`
- `NoopTurnRoutingPolicy`

native `AgentRuntime` 和 MAF `MafAgentRuntime` 都基于这个抽象，在实际模型调用前应用 turn-scoped（按回合作用域）覆盖，并在回合完成后恢复原始会话状态。

### 3. 可选 ONNX 实现层

`OpenClaw.Routing.Onnx` 是可选实现边界。只有当 `OpenClaw:DynamicTurnRouting:Enabled=true` 时，网关才会组合这层实现。

这保持了仓库当前的边界原则：

- `OpenClaw.Core` 保持 AOT-friendly
- `OpenClaw.Agent` 只依赖接口，不依赖 ONNX 细节
- `OpenClaw.Gateway` 决定是否启用 ONNX 路由实现

## 配置（Configuration）

配置入口是 `OpenClaw:DynamicTurnRouting`。

OpenClaw 当前支持两种面向运维方（operator）的输入形态：

- 直接通过 `Assets` 和 `Policy` 提供 OpenClaw 配置
- 通过 `BundlePath` 导入 OpenSquilla 风格 bundle 目录

启动时两种输入都会先归一化成一份内部归一化路由模型（resolved routing model），再交给 ONNX 路由策略使用。

推荐的现代配置形态：

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

兼容模式配置形态：

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

旧的 `Classifier` / `Embeddings` / `Tiers` 配置仍受支持，但定位为兼容模式。

## Tier 映射模型（Tier Mapping Model）

每个 tier target（分层目标）当前可控制以下几个维度：

- `ModelProfileId`：当前回合使用哪个现有模型档位
- `AllowedTools`：当前回合暴露哪些工具
- `PreferredTags`：当前回合偏好哪些档位标签
- `PromptMode` / `DisableTools`：为当前回合追加路由指令（route instruction）

当前内置的路由指令（route instruction）保持最小化：

- `minimal`：`Respond directly with minimal reasoning.`
- `compact`：`Keep the reply short and skip planning.`
- `DisableTools=true`：`Respond directly and do not call tools.`

## 校验规则

启动期校验现在同时覆盖 legacy（兼容）和 modern（现代）两种路由（routing）配置形态。

当前会快速失败（fail-fast）的主要场景包括：

- 配置的 tier 指向了不存在于 `OpenClaw:Models:Profiles` 的模型档位
- 分类器（classifier）资产存在但向量化模型/分词器（embedding / tokenizer）配置不完整

对于 bundle 资产缺失或解析失败，当前契约是允许启动，运行时按回退（fallback）语义降级到 `T2` 并记录机器可读原因（machine-readable reason），而不是在启动阶段中断进程。

## 当前运行时行为（Current Runtime Behavior）

当前实现可以分成两部分理解：

1. turn-scoped（按回合作用域）路由契约与运行时（runtime）接线已实现并覆盖测试
2. 可选 ONNX 路由策略在资产存在时会执行真实本地推理路径

这套能力目前已经完成：

- 动态回合路由的配置契约
- tier 到模型档位的启动期校验
- native `AgentRuntime` 的 turn-scoped（按回合作用域）路由接线
- `MafAgentRuntime` 的 turn-scoped（按回合作用域）路由接线
- 路由状态（route state）的应用/恢复语义
- Gateway 的依赖注入（DI）组合
- 回退（fallback）到 `T2` 的基础行为

也就是说，运行时接缝、配置入口、恢复语义和测试覆盖都已经建立好了。

但这仍然只是一个实验性路由能力，不是已经调优完成的生产级分类器。运行时接线是稳定的，但提示词特征和阈值默认值都刻意保持保守，不能据此宣称已经达到 OpenSquilla 等价精度。

## 当前 ONNX 能力边界

当前生产构造下的 `OnnxTurnRoutingPolicy` 已经不再是纯脚手架（scaffold），而是会在资产存在时真正执行一条本地推理流水线。

目前已经完成的 ONNX 侧能力包括：

- `LocalOnnxEmbeddingGenerator` 会加载 Hugging Face 风格的 BPE `tokenizer.json`
- 它会对用户回合文本做分词编码（tokenization），调用向量化 ONNX 模型（embedding），并把输出整理成固定维度向量
- `PromptFeatureExtractor` 会把提示词（prompt）的轻量手工特征和向量化结果拼成分类器输入特征
- `OnnxTurnRoutingPolicy` 会把该特征向量送入分类器 ONNX 模型（classifier），并把预测结果映射回配置里的 `T0` 到 `T3`

当前的回退（fallback）语义仍然保留：

- 如果缺少模型文件，当前行为会回退到 `T2`，而不是中断整条回合流程
- 如果本地推理阶段抛异常，也会回退到 `T2`，并记录机器可读原因（machine-readable reason）
- 分类器（classifier）输出既支持整数标签（label），也支持浮点 logits，后者会取 `argmax`

因此当前实现状态更准确的说法是：

- 运行时接线（runtime wiring）：已完成
- 运维方配置面（operator config surface）：已完成
- 本地向量化 + 分类器推理路径（classifier inference path）：已完成
- 回退（fallback）与 tier 映射契约（tier mapping contract）：已完成

仍然存在的限制主要是：

- 分词器加载器（tokenizer loader）当前只支持 Hugging Face 风格的 BPE `tokenizer.json`，并不覆盖所有 tokenizer 家族
- 向量化模型（embedding）侧目前按常见 transformer 输入名和常见输出形态适配，不是任意 ONNX embedding 图都能直接套上
- 提示词（prompt）特征和分类阈值仍然比较保守，不能直接宣称已经达到 OpenSquilla 等价精度

## Native 与 MAF 语义（Native And MAF Semantics）

动态回合路由现在同时覆盖两条编排器（orchestrator）路径：

- native `AgentRuntime`
- MAF `MafAgentRuntime`

这一点很重要，因为 `MafAgentRuntime` 不是通过 `NativeAgentRuntimeFactory` 构造的独立路径。如果只给 native 运行时（runtime）接线，MAF 路径会漏掉动态回合路由。

目前这条缺口已经补上，并且也加入了测试。

MAF 还有一个额外语义：如果路由决策（routing decision）返回的是空 `AllowedTools`，运行时不会覆盖用户手工预先设置的 `Session.RouteAllowedTools`。这样可以避免把原有的手工路由允许列表（allowlist）语义冲掉。

## 运维建议（Operator Guidance）

适合：

- 需要为未来本地分类器预留稳定接缝
- 想把简单回合映射到更便宜的模型档位
- 想对读文件、只读总结这类回合缩小工具面
- 想在不改主执行栈的情况下实验路由策略（routing policy）

暂时不适合把它当成：

- 已成熟的 OpenSquilla 等价本地智能分类系统
- 已完成调优的生产级 ONNX 分类器流水线（classifier pipeline）

如果你需要更强的能力声明，先运行离线路由评测基线，再用样本集对比报告确认没有回退后再扩大使用范围。

## 相关文档

- [LOCAL_MODELS.md](../LOCAL_MODELS.md)：本地模型和本地资产说明
- [ARCHITECTURE_BOUNDARIES.md](../ARCHITECTURE_BOUNDARIES.md)：为什么 ONNX 路由（routing）不进入 `OpenClaw.Core`
- [MODEL_PROFILES.md](../MODEL_PROFILES.md)：被路由的模型档位（routed profile）如何继续走现有模型选择策略
- [integrations/microsoft-agent-framework.md](../integrations/microsoft-agent-framework.md)：MAF 运行时（runtime）路径说明
- [OpenSquilla 动态路由实现研究.md](../OpenSquilla%20动态路由实现研究.md)：最初的研究报告
