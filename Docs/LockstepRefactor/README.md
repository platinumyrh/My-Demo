# Lockstep-only 重构说明

## 客户端

客户端当前在 `refactor/lockstep-only` 分支，已清理旧状态同步链路：

- 删除 `FrameSyncManager` 和旧 `S2C_FrameState` 处理
- 删除 `LocalPlayerMovement / RemotePlayerMovement / LocalPlayerCombat`
- 新增 `PlayerView`，作为本地/远端玩家的纯表现层组件
- `LockstepGameDriver` 负责 lockstep 会话、渲染插值和 `LogicEvent` 表现
- 目录整理：
  - `Lockstep/Core`
  - `Lockstep/Client`
  - `Lockstep/Network`
  - `Lockstep/Presentation`
  - `Gameplay/Input`
- 协议只保留心跳、房间、登录、游戏控制和 lockstep 指令消息

## 服务端（已完成）

服务端已按 lockstep-only 方案重构：

- 删除 `Utils/FrameSyncManager.cs`、`Utils/VehicleConfigStore.cs` 及旧状态同步/战斗事件消息
- 新增 `Network/MessageRouter.cs`：泛型注册 parser，统一分发与异常兜底
- 消息处理按领域拆分到 `Handlers/`：`LoginHandler`、`HeartbeatHandler`、`RoomHandler`、`LockstepHandler`
- `GameServer` 只负责组装、生命周期、心跳超时扫描和跨模块广播/断线处理
- `LockstepRelayManager` 移动到 `Services/`
- 目录结构：`Network/`、`Handlers/`、`Services/`、`Models/`、`Lockstep/`
- 协议只保留心跳、房间、登录、游戏控制和 lockstep 指令消息
- `Player` / `Room` 模型移除旧位置、血量等状态字段

`server_lockstep_only.patch` / `.zip` 为当时的交付存档，改动已直接应用，无需再次执行。
