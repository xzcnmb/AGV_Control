# AGV 调度控制系统 · VDA5050

> 基于 **.NET 8 + WPF** 的 AGV(自动导引车)调度仿真系统,完整实现 **VDA5050 v2.0.0** 主控与车载通信协议。内嵌 MQTT Broker,主控与 AGV 模拟器双程序独立运行,通过标准协议实时交互。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20HandyControl-2C3E50)
![Protocol](https://img.shields.io/badge/VDA5050-v2.0.0-0284C7)
![MQTT](https://img.shields.io/badge/MQTT-MQTTnet%204.3-660066)
![Tests](https://img.shields.io/badge/tests-79%20passing-2DA641)

---

## 一、项目简介

这是一套可离线运行、开箱即用的 **AGV 车队调度系统教学 / 原型平台**。它把工业界事实标准的 VDA5050 协议完整落到代码里,并配一个高保真的 AGV 模拟器,让你无需真实小车就能验证调度中心的完整业务闭环:

- **调度中心(MasterControl)**:内嵌 MQTT Broker,可视化地图下单、车队管理、订单全生命周期跟踪、协议报文抓包、SQLite 任务审计。
- **AGV 模拟器(Simulator)**:扮演一台真实 AGV,连接调度中心,接收订单后沿路径插值移动,实时上报位姿/电量/状态,支持暂停/恢复/取消/故障注入,并在自己的实时地图上画出行驶轨迹。

两个程序通过 VDA5050 标准主题在 MQTT 上通信,完全解耦——你可以把模拟器换成真实 AGV,或把主控接入真实车队,协议层不用改。

### 为什么做这个

VDA5050 是德国汽车工业协会(VDA)与德国机械设备制造业联合会(VDMA)联合发布的 **AGV 与上位调度系统统一通信接口**,是目前多品牌 AGV 混合车队互联的主流标准。但公开的、协议完整且带可视化联调的开源实现很少。这个项目把协议里最容易踩坑的部分——**订单拼接(horizon stitching)、序号交替校验、取消握手、连接遗嘱(last-will)、状态机时序**——都做成了可运行、可观测、有测试覆盖的代码。

---

## 二、功能清单(16 项,全部端到端可用)

| # | 功能 | 说明 |
|---|------|------|
| 1 | AGV 上线/离线 | ONLINE 连接消息、CONNECTIONBROKEN 遗嘱、优雅 OFFLINE、60s 看门狗 |
| 2 | Factsheet | 车辆参数上报,响应 factsheetRequest 即时动作 |
| 3 | State 上报 | 30s 周期 + 事件驱动,全字段(位姿/电量/节点边/动作/错误) |
| 4 | 创建订单 | 地图点选 / 预设路线生成节点与边 |
| 5 | Node/Edge | 节点偶数、边奇数交替序号,released 基准段 / horizon 远景段 |
| 6 | 下发订单 | MQTT 下发,10s 受理确认超时重发(最多 3 次) |
| 7 | AGV 模拟移动 | 100ms 沿已释放边插值,精确速度积分 |
| 8 | 实时位置 | 5Hz(200ms) visualization 高频上报,双端地图实时刷新 |
| 9 | Pause | startPause 即时动作,小车停走,动作置 PAUSED |
| 10 | Resume | stopPause 即时动作,恢复移动 |
| 11 | Cancel | cancelOrder 握手:清空节点/边、动作 FINISHED、订单转 CANCELLED |
| 12 | Error | WARNING 不阻断 + FATAL 门禁(拒绝新订单),错误上报 state.errors |
| 13 | 电量模拟 | 行驶耗电、充电回电,batteryState 实时上报 |
| 14 | WPF 地图 | 网格标尺、已释放/远景路径、AGV 位姿朝向、电量条、图例、防遮挡标签 |
| 15 | MQTT 报文监控 | 双向抓包(Broker 拦截 + 客户端收发/回显),方向+关键词过滤,500 条环形缓冲 |
| 16 | SQLite 任务记录 | orders / order_events 等表,WAL 模式,状态迁移全量落库 |

---

## 三、架构设计

### 3.1 总体结构

```
┌──────────────────────────────────────────────────────────────────────┐
│                       AgvDispatch.MasterControl (WPF)                  │
│                            调度中心 · 上位机                            │
│  ┌────────────┐  ┌───────────┐  ┌────────────┐  ┌──────────────────┐  │
│  │ 内嵌 Broker │  │ 订单服务   │  │ AGV 注册表  │  │ SQLite 任务仓库   │  │
│  │ (1883)     │  │ 完成状态机 │  │ 看门狗      │  │ WAL 审计          │  │
│  └─────┬──────┘  └─────┬─────┘  └─────┬──────┘  └──────────────────┘  │
│        │  ┌────────────┴──────────────┴─────┐  ┌───────────────────┐  │
│        │  │       MQTT 客户端(单写者)         │  │ WPF 地图 / 报文监控│  │
│        │  └───────────────┬──────────────────┘  └───────────────────┘  │
└────────┼──────────────────┼─────────────────────────────────────────┘
         │ TCP 1883         │  uagv/v2/<mfr>/<serial>/<topic>
         │  ┌───────────────▼──────────────────────────────────────┐
         │  │              MQTT 传输(MQTTnet 4.3)                   │
         │  └───────────────┬──────────────────────────────────────┘
         │                  │
┌────────▼──────────────────▼─────────────────────────────────────────┐
│                       AgvDispatch.Simulator (WPF)                     │
│                            AGV 模拟器 · 车载                           │
│  ┌────────────┐  ┌───────────┐  ┌────────────┐  ┌──────────────────┐ │
│  │ MQTT 客户端 │  │ 移动引擎   │  │ 电量模拟    │  │ 协调器(状态机)   │ │
│  │ last-will  │  │ 100ms插值  │  │ 耗电/充电   │  │ §7/11/12/13/15   │ │
│  └────────────┘  └───────────┘  └────────────┘  └──────────────────┘ │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  实时地图(订单路径 + AGV 移动 + 历史轨迹) / 事件流 / 状态看板   │   │
│  └──────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘

         共享: AgvDispatch.Vda5050 (协议库,双端引用,API 冻结)
```

### 3.2 三层项目划分

| 项目 | 类型 | 职责 |
|------|------|------|
| **AgvDispatch.Vda5050** | 类库 | 协议核心(冻结):全报文模型、主题构造/解析、JSON 契约、订单受理与序号校验状态机。双端共享,单一事实来源。 |
| **AgvDispatch.MasterControl** | WPF | 调度中心:内嵌 Broker、MQTT 客户端、AGV 注册表、订单服务(拼接/重发/取消/完成机)、自绘地图、报文监控、SQLite 持久化。 |
| **AgvDispatch.Simulator** | WPF | AGV 模拟器:连接管理、移动引擎、电量模拟、动作与错误状态机、实时地图与轨迹、协议事件流。 |
| **AgvDispatch.Tests** | xUnit | 75 单元测试(序列化/主题/序号/受理机)+ 4 条真实 MQTT 线级集成测试 = 79。 |

### 3.3 通信契约(要点)

- **主题格式**:`uagv/v2/<manufacturer>/<serial>/<topic>`,topic ∈ {connection, factsheet, state, order, instantActions, visualization}
- **身份**:manufacturer=`AgvSim`,serial=`AGV0001`,mapId=`default`,version=`2.0.0`
- **QoS**:connection = QoS1 + retained(含 CONNECTIONBROKEN 遗嘱 retained);其余 QoS0
- **节奏**:state 30s、visualization 200ms(5Hz)、移动 100ms、地图重绘 100ms、看门狗 60s、下发/取消确认 10s
- **序号规则**:节点偶数(0,2,4…)、边奇数(1,3,5…)交替递增,不可变;released 前缀连续,horizon 在后
- **线程模型**:单写者串行发布 + 每主题 headerId 计数器;UI 更新经 Dispatcher 隔离;MQTT 回调解析失败不外抛

> 完整契约见 [`docs/INTERFACE_PROFILE.md`](docs/INTERFACE_PROFILE.md)(§1–§22),这是所有模块共同遵守的规范。

### 3.4 关键状态机

**连接状态机(§11)**:模拟器连接即发 ONLINE(retained);设遗嘱 CONNECTIONBROKEN(retained),掉线由 Broker 自动补发;优雅退出发 OFFLINE。主控 60s 看门狗:超时未收 state 判定失联。

**订单完成状态机(§16)**:`CREATED → DISPATCHED → ACCEPTED → RUNNING → COMPLETED / CANCELLED / FAILED`,由收到的 state 报文驱动迁移,每步写入 SQLite 审计。

**Horizon 拼接(§12b)**:订单分「已释放基准段(base)」+「远景段(horizon)」下发;AGV 接近基准末端时置 `newBaseRequest=true`,主控据此释放下一批节点,保持前缀不变量与拼接节点一致。

**取消握手(§13)**:主控下发 cancelOrder 即时动作并启 10s 定时器;AGV 立即刹车、清空 node/edge 状态、动作置 FINISHED、**保留上报 orderId**;主控据此确认并转 CANCELLED。

---

## 四、快速开始

### 环境要求
- Windows 10/11
- .NET 8 SDK(含 WPF 工作负载)

### 编译
```bat
git clone https://github.com/xzcnmb/AGV_Control.git
cd AGV_Control
dotnet build AgvDispatch.slnx
```

### 运行(先主控,后模拟器)
```
# 1. 先启动调度中心(内嵌 Broker,监听 1883)
src\AgvDispatch.MasterControl\bin\Debug\net8.0-windows\AgvDispatch.MasterControl.exe

# 2. 再启动 AGV 模拟器,点"连接"
src\AgvDispatch.Simulator\bin\Debug\net8.0-windows\AgvDispatch.Simulator.exe
```
> 顺序关键:Broker 由主控托管。若模拟器先开,会按退避策略自动重连,无需手动干预。

### 运行测试
```bat
dotnet test AgvDispatch.slnx
```
当前:**79 个测试全部通过**(75 单元 + 4 MQTT 线级集成)。

---

## 五、操作演示(端到端一遍)

1. 主控开着 → 模拟器点「连接」→ 主控右侧车队列表出现 `AGV0001`「在线」,收到 Factsheet。
2. 主控「订单编辑」→ 点「方形路线」预设(或地图点选)→ 拖「基准大小」滑块设 released 段 →「下发订单」。
3. 看**模拟器实时地图**:路径画出(绿实线=已释放、橙虚线=远景),小车沿路径移动,蓝色历史轨迹跟随延伸;下方事件流刷出「收到订单/到达节点」。
4. 主控地图同步显示小车位姿;试「暂停/恢复」,两端联动。
5. 「取消订单」→ 小车刹停、订单转 CANCELLED。
6. 模拟器「注入 FATAL」→ 小车急停,主控再下单被门禁拒绝;「清除错误」恢复。
7. 主控「报文监控」看双向 VDA5050 报文;「任务记录」刷新看 SQLite 全流程落库。

---

## 六、界面截图

> 截图位于 [`Image/`](Image/) 目录。

### 调度中心 · 实时地图
![调度中心实时地图](Image/master-map.png)

### 调度中心 · 订单编辑与下发
![订单编辑](Image/master-order.png)

### 调度中心 · MQTT 报文监控
![报文监控](Image/master-monitor.png)

### 调度中心 · SQLite 任务记录
![任务记录](Image/master-tasks.png)

### AGV 模拟器 · 实时路径与事件流
![模拟器实时地图](Image/simulator-map.png)

---

## 七、项目结构

```
AGV_Control/
├─ AgvDispatch.slnx                  解决方案(SLNX 新格式)
├─ docs/
│  └─ INTERFACE_PROFILE.md           接口契约(唯一事实来源 §1–§22)
├─ src/
│  ├─ AgvDispatch.Vda5050/           协议库(冻结)
│  │  ├─ Enums/ Json/ Models/ Messages/ Topics/ StateMachines/
│  ├─ AgvDispatch.MasterControl/     调度中心 WPF
│  │  ├─ Services/  (EmbeddedBroker, MqttClientService, OrderService,
│  │  │             AgvRegistry, TaskRepository)
│  │  ├─ ViewModels/ Controls/(MapCanvas) Models/
│  └─ AgvDispatch.Simulator/         AGV 模拟器 WPF
│     ├─ Services/  (SimMqttClient, MovementEngine, BatterySim,
│     │             SimulatorCoordinator)
│     ├─ ViewModels/ Controls/(SimMapCanvas) Models/
├─ tests/
│  └─ AgvDispatch.Tests/             79 测试(xUnit + 真实 MQTT 集成)
└─ Image/                            界面截图
```

---

## 八、技术栈

| 领域 | 选型 |
|------|------|
| 运行时 | .NET 8 |
| UI | WPF + MVVM(CommunityToolkit.Mvvm) |
| UI 美化 | HandyControl 3.5.1(开源,无边框 hc:Window) |
| MQTT | MQTTnet 4.3(内嵌 Broker + 客户端) |
| 持久化 | Microsoft.Data.Sqlite(WAL 模式) |
| 序列化 | System.Text.Json(自定义 VDA5050 时间格式转换器) |
| 测试 | xUnit |

---

## 九、安全说明

内嵌 Broker 监听 `[IP]:1883` 且**未启用鉴权**,仅供本地 / 局域网演示与联调。若部署到不受信网络,需增加 TLS 与账号鉴权。协议底层枚举(ONLINE、AUTOMATIC 等)保持 VDA5050 v2.0.0 线值不变,界面仅做中文显示层转换。

---

## 十、许可证

MIT License,详见 [LICENSE](LICENSE)。
