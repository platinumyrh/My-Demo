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

## 服务端

由于当前执行环境对 `E:\unity\Server_MyDemo` 没有写权限，服务端修改以补丁和完整包形式保存在这里：

- `server_lockstep_only.patch`
- `server_lockstep_only.zip`

应用补丁：

```cmd
cd /d "E:\unity\Server_MyDemo"
git apply "E:\unity\My Demo\Docs\LockstepRefactor\server_lockstep_only.patch"
```

或者解压 `server_lockstep_only.zip`，用其中的 `Server_MyDemo` 目录覆盖当前服务端。

服务端变更：

- 删除旧 `Utils/FrameSyncManager.cs`
- 删除 `VehicleConfigStore.cs`
- `LockstepRelayManager.cs` 移到 `Server/Lockstep/`
- `GameServer` 只保留 lockstep 指令中继和封帧广播
- 协议只保留 lockstep 指令消息
- `Player` 模型移除旧位置/血量等状态字段
