# AGV 调度系统 — 接口契约 (INTERFACE_PROFILE)

> **本文件是所有子代理必须遵守的唯一事实源 (single source of truth)。**
> 协议库 `AgvDispatch.Vda5050` 冻结后,MasterControl / Simulator / Tests 一律按本契约对接。
> 任何与本文件冲突的实现都视为 bug。目标协议:**VDA5050 v2.0.0**。

---

## 0. 解决方案布局

```
AgvDispatch.slnx
src/AgvDispatch.Vda5050/      协议库 (net8.0)         —— 子代理 A 独占
src/AgvDispatch.MasterControl/ WPF 主控 (net8.0-windows) —— 子代理 C 独占
src/AgvDispatch.Simulator/    WPF 模拟器 (net8.0-windows) —— 子代理 B 独占
tests/AgvDispatch.Tests/      xUnit (net8.0)           —— 子代理 D 独占
docs/INTERFACE_PROFILE.md     本文件 (只读)
```

包版本(已固定,勿改):MQTTnet 4.3.7.1207 · Microsoft.Data.Sqlite 8.0.10 · CommunityToolkit.Mvvm 8.3.2

---

## 1. 命名空间与根类型

- 根命名空间:`AgvDispatch.Vda5050`
- 子命名空间:`.Messages` `.Models` `.Enums` `.Topics` `.Json` `.StateMachines`
- 所有报文类继承 `Vda5050Header`(含 headerId/timestamp/version/manufacturer/serialNumber)。

---

## 2. MQTT 主题

格式:`interfaceName/majorVersion/manufacturer/serialNumber/topic`

- interfaceName = `uagv`
- majorVersion = `v2`
- topic ∈ { `connection`, `factsheet`, `state`, `order`, `instantActions`, `visualization` }

示例:`uagv/v2/AgvSim/AGV0001/state`

`Vda5050Topic` 提供:
```csharp
public static string Build(string manufacturer, string serialNumber, string topic);
public static bool TryParse(string topic, out Vda5050TopicInfo info); // 拆出 manufacturer/serial/topic
public const string InterfaceName = "uagv";
public const string MajorVersion  = "v2";
// 主题常量
public const string Connection="connection", Factsheet="factsheet", State="state",
                    Order="order", InstantActions="instantActions", Visualization="visualization";
```
主控订阅通配:`uagv/v2/#`。

---

## 3. 身份与端口(默认值,可被配置覆盖)

| 项 | 值 |
|----|----|
| Broker 监听 | `[IP]:1883`(端口可配置) |
| 模拟器连接 | `[IP]:1883` |
| interfaceName | `uagv` |
| manufacturer | `AgvSim` |
| serialNumber | 每个模拟器实例一个,默认 `AGV0001`(可配置) |
| mapId | `default`(全局唯一地图) |
| protocol version 字段 | `2.0.0` |

---

## 4. 节奏 / 定时(硬约定)

| 定时器 | 周期 | 归属 |
|--------|------|------|
| state 周期上报 | 30000 ms(与 factsheet.protocolLimits.timing.defaultStateInterval 一致) | Simulator |
| state 事件触发 | 立即(见 §7) | Simulator |
| visualization 高频位置 | 200 ms(5 Hz) | Simulator |
| movement tick | 100 ms,按 Stopwatch 实测时长插值 | Simulator |
| 主控地图重绘 | DispatcherTimer 100 ms,从最新快照重绘 | MasterControl |
| 在线看门狗 | 检查间隔 5 s;超过 **60 s** 未收到该 AGV 任何 state → 判 stale | MasterControl |
| 下发受理超时 | 10 s 未见匹配 orderId+orderUpdateId → 重发;3 次后升级可见错误 | MasterControl |
| Cancel 确认超时 | 10 s 未见 cancelOrder actionState=FINISHED → 提示"取消未确认" | MasterControl |

> connection 主题**不用作**健康检查通道(规范禁止)。健康判定 = last-will + state 看门狗。

---

## 5. QoS 与 retained

| 主题 | QoS | retained |
|------|-----|----------|
| connection | 1 | **是** |
| order | 0 | 否 |
| instantActions | 0 | 否 |
| state | 0 | 否 |
| factsheet | 0 | 否 |
| visualization | 0 | 否 |

---

## 6. JSON 序列化(`Vda5050Json` 统一入口)

- camelCase 属性名(严格匹配 schema:`headerId` `orderUpdateId` `nodeStates` `sequenceId` …)
- enum → 字符串(`JsonStringEnumConverter`)
- timestamp 格式固定:`yyyy-MM-dd'T'HH:mm:ss.fff'Z'`(UTC 时钟),不用 `"o"`
- 忽略 null 可选字段(`JsonIgnoreCondition.WhenWritingNull`);**必填字段即使为空数组也要输出**
- 提供:`Vda5050Json.Serialize<T>(T msg)` / `Vda5050Json.Deserialize<T>(string json)` / `Vda5050Json.Options`

---

## 7. State 报文(字段契约)

**必填**(即使空也输出):`orderId`(无=`""`) `orderUpdateId`(无=0) `lastNodeId`(无=`""`) `lastNodeSequenceId`(无=0) `driving` `nodeStates[]` `edgeStates[]` `actionStates[]` `batteryState` `operatingMode` `errors[]` `safetyState{eStop,fieldViolation}` `information[]`

**可选**(null 时省略):`agvPosition{x,y,theta,mapId,positionInitialized,localizationScore?,deviationRange?}` `velocity{vx,vy,omega}` `loads[]` `paused` `newBaseRequest` `distanceSinceLastNode` `zoneSetId`

**state 事件触发时机(除 30s 周期外,以下任一立即发)**:收到 order / 收到 order update / load 变化 / 新增 error 或 warning / 通过某节点(node traversed) / operatingMode 变化 / driving 变化 / nodeStates|edgeStates|actionStates 任一变化。

`operatingMode` 默认 `AUTOMATIC`。`safetyState` 默认 `{ eStop: NONE, fieldViolation: false }`。

---

## 8. 枚举(字符串值,大小写严格)

- `ConnectionState`: `ONLINE` `OFFLINE` `CONNECTIONBROKEN`
- `OperatingMode`: `AUTOMATIC` `SEMIAUTOMATIC` `MANUAL` `SERVICE` `TEACHIN`
- `ActionStatus`: `WAITING` `INITIALIZING` `RUNNING` `PAUSED` `FINISHED` `FAILED`
- `BlockingType`: `NONE` `SOFT` `HARD`
- `EStop`: `AUTOACK` `MANUAL` `REMOTE` `NONE`
- `ErrorLevel`: `WARNING` `FATAL`
- `InfoLevel`: `INFO` `DEBUG`

---

## 9. Order / Node / Edge

- `Order`: `orderId` `orderUpdateId` `zoneSetId?` `nodes[]` `edges[]`
- `Node`: `nodeId` `sequenceId` `nodeDescription?` `released` `nodePosition?{x,y,theta?,mapId,allowedDeviationXY?,allowedDeviationTheta?,mapDescription?}` `actions[]`
- `Edge`: `edgeId` `sequenceId` `edgeDescription?` `released` `startNodeId` `endNodeId` `maxSpeed?` `orientation?` `trajectory?` `actions[]`
- `Action`: `actionId` `actionType` `blockingType` `actionDescription?` `actionParameters[]{key,value}`
- **sequenceId 规则**:节点偶数、边奇数、交替递增(node0=0, edge0=1, node1=2 …);全序严格递增;更新中不可变。
- **released 前缀不变量**:released=true 的集合必须是连续前缀;边 released 要求两端节点都 released;未释放边之后不得再有已释放节点/边。
- 订单编辑器发布前校验:edge.endNodeId==下一 node.nodeId、坐标在地图范围、sequenceId 规则、released 前缀不变量。

---

## 10. InstantActions

`InstantActions`: `actions[]`(同 `Action` 结构)。预定义 actionType(本项目用到):
`startPause` `stopPause` `cancelOrder` `factsheetRequest` `stateRequest` `startCharging`/`charge`(见 §14)。
每个收到的 instant action 必须在 state.actionStates 里反映其生命周期。

---

## 11. Connection 状态机

- 连接时设 last-will:topic=`.../connection`,payload=`{header, connectionState: CONNECTIONBROKEN}`,QoS1,retained。
- 连接成功后立即发 `ONLINE`(QoS1,retained)。
- 优雅关闭:先发 `OFFLINE`(QoS1,retained),再 MQTT DISCONNECT。
- 断线重连:重连 → 重设 will → 发 `ONLINE`(直接 ONLINE 是正常的,**不需要**先经过 CONNECTIONBROKEN)→ 立即补发 factsheet(若曾被请求)与 state。
- 主控注册表状态:`Unknown / Online / Offline / ConnectionBroken`,全向可转;新 ONLINE 覆盖陈旧的 retained CONNECTIONBROKEN。

---

## 12. 订单受理 FSM(Simulator 侧)

按序判定:
1. JSON 解析 + 校验失败 → 丢弃 + 记日志(不抛异常出 MQTT 回调)。
2. 同 orderId 且 orderUpdateId **相等** → 忽略(去重)。
3. 同 orderId 且 orderUpdateId **更低** → 保留原订单 + errors[] 追加 `orderUpdateError`(WARNING)。
4. **新 orderId** → 接受并替换当前订单(见策略)。
5. 同 orderId 且 orderUpdateId **更高** → 校验拼接节点:新订单第一个 base 节点的 nodeId+sequenceId 必须等于上批最后一个 base 节点的 lastNodeId+lastNodeSequenceId;匹配 → 合并(从拼接节点之后替换 nodes/edges,拼接节点保留原指令),保持 released 前缀不变量;不匹配 → `orderUpdateError`(WARNING),拒绝。
- 每次状态迁移后立即发 state。
- **"执行中收到新 orderId"策略**:主控侧策略=不对有活动订单的 AGV 下发新 orderId(需先 cancel);模拟器侧策略=新 orderId 替换当前订单(健壮兜底)。

## 12b. horizon / 拼接下发(MasterControl 侧,完整模型)

- 下发时 released 只覆盖前缀(base ≥ 2 节点),其余为 horizon(released=false)。
- 模拟器只驶入 released 节点;接近最后一个 released 节点时置 `newBaseRequest=true`。
- 主控收到 newBaseRequest=true → 发 order update(同 orderId,orderUpdateId+1),把下一批翻为 released,保持前缀不变量与拼接节点一致。

---

## 13. Cancel 握手

Simulator 收到 `cancelOrder`(视为 HARD):
1. 立即停车(MovementEngine 停,driving=false)。
2. actionStates 追加 `{actionId(来自请求), actionType:"cancelOrder", actionStatus:RUNNING}`,立即发 state。
3. 删除订单:nodeStates=[]、edgeStates=[]、所有订单动作取消、paused=false、newBaseRequest=false。
4. 该 cancelOrder actionState 置 `FINISHED`,再发 state;**保留该 actionState 直到新订单到达**。
5. 若收到时**无活动订单**:errors[] 追加 `{errorType:"NO_ORDER_TO_CANCEL", errorLevel:WARNING, errorReferences:[{referenceKey:"actionId", referenceValue:<id>}]}`,该 action 置 `FAILED`。

MasterControl:记录自己发出的 cancel actionId;收到 state 中该 actionId=FINISHED 且 nodeStates/edgeStates 空 → 订单标记 CANCELLED 入库;10s 超时 → 提示"取消未确认";cancel 时若 AGV 处于 paused,握手需清除 paused。

---

## 14. 电量 / 充电

- `batteryState`: `batteryCharge`(%,必填) `charging`(必填) `batteryVoltage?` `batteryHealth?` `reach?`
- 行驶时耗电、静止微耗;充电时回升。
- **充电触发**:充电站节点携带 `charge` 动作(blockingType HARD);模拟器到站执行该动作 → charging=true、阻止移动、耗电率转为充电率;动作完成 → charging=false。
- `charge` 动作必须在 factsheet.protocolFeatures.agvActions 声明。
- 低电量(如 <20%)可发 manufacturer 特定 `batteryLow` WARNING(可选)。

---

## 15. 错误 / FATAL 门禁

- `Error`: `errorType` `errorLevel(WARNING|FATAL)` `errorDescription?` `errorHint?` `errorReferences[]{referenceKey,referenceValue}`(**对象数组**)。
- FATAL → 模拟器停车(driving=false)且**拒绝新订单**(订单受理 FSM 前置检查)。
- 模拟器"注入错误"按钮:能注入 WARNING/FATAL,也能清除(恢复)。
- 主控:对 FATAL 的 AGV 禁止下发;UI/注册表/SQLite 反映错误级别。

---

## 16. 订单完成状态机(MasterControl 侧)

`CREATED → DISPATCHED → ACCEPTED(state 现 orderId+updateId 匹配) → RUNNING(首节点通过/driving) → COMPLETED(nodeStates&edgeStates 空且 driving=false 且到达末 released 节点) / CANCELLED(§13) / FAILED(执行中 FATAL)`。每次迁移写 `order_events`。

---

## 17. Header 计数器

- headerId 为**每主题独立**的 uint32 计数器,每发一条 +1;重启从 0 开始允许。
- 主控必须容忍 headerId 跳变/乱序(QoS0 可能丢/乱序):告警但不失败。

---

## 18. SQLite(MasterControl 侧)

- 连接串:`Data Source=<可配置路径>;` + 打开后执行 `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;`
- `TaskRepository` 持有单个长连接,所有写经它串行(内部锁)。
- 表:
  - `orders(order_id TEXT PK, agv_serial TEXT, order_update_id INT, status TEXT, payload_json TEXT, created_at TEXT, dispatched_at TEXT, updated_at TEXT)`
  - `order_events(id INTEGER PK AUTOINCREMENT, order_id TEXT, agv_serial TEXT, event_type TEXT, ts TEXT, payload_json TEXT)`
  - `agv_events(id INTEGER PK AUTOINCREMENT, agv_serial TEXT, ts TEXT, event_type TEXT, payload_json TEXT)`
  - `errors(id INTEGER PK AUTOINCREMENT, agv_serial TEXT, ts TEXT, error_type TEXT, error_level TEXT, error_description TEXT, error_references_json TEXT)`
- 索引:`(agv_serial, ts)`、`(order_id, ts)`。存原始 JSON payload。

---

## 19. 线程契约(两个 WPF 端通用)

- MQTT 回调线程 → UI:一律 `Dispatcher.InvokeAsync`(**禁**同步 `Invoke`,防死锁)。
- ObservableCollection 变更必须在 UI 线程。
- **每主题单写者**:所有 publish 经一个串行发布器/锁,保证 headerId 顺序、避免交错。
- 所有 Timer 回调包 try/catch(异常会静默杀死 System.Timers.Timer)。
- MovementEngine 用 Stopwatch 实测时长插值;位置以快照(拷贝)交给 state 发布器,防撕裂读。
- MQTT 回调内解析失败只记日志,不抛出。

---

## 20. MQTT 监控(MasterControl 侧)

- 双侧捕获:Broker 端 `InterceptingPublishAsync` + 客户端自身 publish 事件。
- 主控自身发布的 order/instantActions 会被自己 `uagv/v2/#` 订阅回显 → 标注 "sent(echo)" 或过滤。
- 有界环形缓冲(500 条),批量刷新 UI;每条含:时间、方向、主题、payload JSON。

---

## 21. 协议库对外 API 冻结点(子代理 A 交付后不得破坏)

子代理 A 必须导出且保持稳定:
- 所有 §7/§8/§9/§10 报文与模型类型(public,camelCase JSON 映射)。
- `Vda5050Topic`(§2)、`Vda5050Json`(§6)。
- `Vda5050Header` 基类 + `HeaderIdCounter`(每主题计数,线程安全)。
- 便于双端复用的纯逻辑(无 UI、无 MQTT 依赖):
  - `OrderAcceptance`(§12 FSM:输入当前订单+新订单+lastNode → 判定结果枚举 Accept/Ignore/RejectLowerUpdate/StitchOk/StitchMismatch)。
  - `SequenceValidator`(§9 released 前缀不变量 + sequenceId 规则校验)。
- 版本常量:`Vda5050.ProtocolVersion = "2.0.0"`。

> 若子代理 A 需要微调签名,必须回报全局把控者更新本文件后再对齐,不得各自为政。

---

## 22. 【已冻结】协议库交付 API(子代理 A 完成,编译 0 错误 0 警告)

以下类型已实现并冻结,MasterControl / Simulator / Tests 直接 `using` 使用,不得修改协议库:

**命名空间根** `AgvDispatch.Vda5050`
- `Vda5050Constants.ProtocolVersion == "2.0.0"`(另有别名 `Vda5050.ProtocolVersion`)

**`.Enums`**:`ConnectionState` `OperatingMode` `ActionStatus` `BlockingType` `EStop` `ErrorLevel` `InfoLevel`(成员名即线值大写,如 `ConnectionState.ONLINE`、`OperatingMode.AUTOMATIC`、`BlockingType.HARD`)

**`.Json`**:`Vda5050Json.Options` / `Vda5050Json.Serialize<T>(T)` / `Vda5050Json.Deserialize<T>(string)`(返回 `T?`);`Vda5050DateTimeConverter` / `Vda5050DateTimeOffsetConverter`

**`.Models`**:`NodePosition` `Node` `Edge` `ActionParameter` `VdaAction`(**注意类名是 VdaAction,不是 Action**) `ControlPoint` `Trajectory` `BatteryState` `AgvPosition` `Velocity` `NodeState` `EdgeState` `ActionState` `ErrorReference` `Error` `InfoReference` `Info` `SafetyState` `Load`

**`.Messages`**:`Vda5050Header`(抽象基类:`HeaderId(uint)` `Timestamp(DateTime)` `Version` `Manufacturer` `SerialNumber`) `ConnectionMessage` `OrderMessage`(`Nodes`/`Edges` 为 `List<>`) `InstantActionsMessage`(`Actions` 为 `List<VdaAction>`,JSON 名 `actions`) `StateMessage`(§7 全字段) `VisualizationMessage` `FactsheetMessage`(+ 嵌套 `TypeSpecification` `PhysicalParameters` `ProtocolLimits` `TimingLimits` `ProtocolFeatures` `AgvActionSpecification` `AgvGeometry` `WheelDefinition` `Envelope2D` `LoadSpecification` `LoadSet` `LocalizationParameters`)

**`.Topics`**:`Vda5050Topic`(常量 `InterfaceName` `MajorVersion` `Connection` `Factsheet` `State` `Order` `InstantActions` `Visualization` `Wildcard`;方法 `Build(manufacturer, serial, topic)` / `TryParse(topic, out Vda5050TopicInfo)`) `Vda5050TopicInfo`(record struct:`Manufacturer` `SerialNumber` `Topic`) `HeaderIdCounter`(`Next(topic)` `GetCurrent` `Reset` `ResetAll` `Stamp(...)` 线程安全)

**`.StateMachines`**:`AcceptanceResult`(枚举:`Accept` `Ignore` `RejectLowerUpdate` `StitchOk` `StitchMismatch`)、`OrderAcceptanceResult`(别名枚举)、`OrderAcceptance.Evaluate(current, incoming, lastNodeId, lastNodeSequenceId)`、`ValidationResult`(`IsValid` `Errors` `AddError`)、`SequenceValidator.Validate(order)`(+ 分项 `ValidateSequenceIds`/`ValidateEdgeConnectivity`/`ValidateReleasedPrefix`)

> 注:`SequenceValidator.Validate` 目前要求 `edges.Count == nodes.Count - 1`(线性路径),满足本项目单链路订单;如需分叉再回报协调者扩展。
