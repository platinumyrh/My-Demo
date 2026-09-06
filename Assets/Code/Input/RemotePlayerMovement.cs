using UnityEngine;
using GameNetwork;
using System.Collections.Generic;

/// <summary>
/// 远程玩家移动控制器
/// 位置来源：服务器随帧广播的权威位置状态（对方客户端物理模拟后的真实位置）
/// 本地只做两件事：按目标速度前馈匀速移动 + 指数衰减校正逼近目标；
/// 偏差超过 snapDistance 时直接吸附（传送），保证永远不会持续性偏离
/// 垂直方向同样跟随权威状态的 Y（对方跳，这边也跳），由自己的 CharacterController 做碰撞
/// </summary>
public class RemotePlayerMovement : MonoBehaviour
{
    #region 配置参数

    [Header("插值设置")]
    public float interpolationDelay = 0.1f; // 延迟插值，补偿网络抖动
    public float correctionRate = 10f;      // 校正速率（每秒衰减的比例）
    public float snapDistance = 3f;         // 偏差超过该距离直接吸附
    public float rotationSpeed = 15f;       // 旋转平滑速度

    [Header("调试")]
    public string playerId = "Unknown";     // 玩家ID（用于标识）

    #endregion

    #region 私有字段

    // 位置缓冲区（用于延迟插值）
    private class PositionSnapshot
    {
        public uint FrameId;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Timestamp;
    }

    private Queue<PositionSnapshot> _positionBuffer;
    private PositionSnapshot _currentTarget;

    // 目标点的移动速度（前馈用）：按这个速度匀速移动，避免"走到就停"的顿挫
    private Vector3 _targetVelocity;
    private bool _hasLastTargetPosition;
    private Vector3 _lastTargetPosition;
    private float _lastTargetTimestamp;

    // 自己的 CharacterController：碰撞与落地由它解决
    private CharacterController _cc;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        _positionBuffer = new Queue<PositionSnapshot>(20); // 缓冲最近20帧
        _cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (_currentTarget == null) return;

        // 水平 = 前馈（按目标速度匀速走） + 校正（指数衰减逼近权威位置，消除累计偏差）
        Vector3 motion = _targetVelocity * Time.deltaTime;
        Vector3 error = _currentTarget.Position - transform.position;

        // 大偏差保护：超过吸附距离直接传送，不做缓慢追赶
        if (error.magnitude > snapDistance)
        {
            SnapToTarget();
            return;
        }

        motion += error * Mathf.Min(1f, correctionRate * Time.deltaTime);

        if (_cc != null)
        {
            _cc.Move(motion);
        }
        else
        {
            transform.position += motion; // 没有CC时兜底直接位移
        }

        // 旋转插值
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            _currentTarget.Rotation,
            rotationSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// 大偏差吸附：直接传送到权威位置并清空缓冲，重新开始插值
    /// </summary>
    private void SnapToTarget()
    {
        Debug.Log($"[RemotePlayerMovement] ⚡ [{playerId}] 偏差超过 {snapDistance}m，吸附到权威位置 {_currentTarget.Position}");
        SetPosition(_currentTarget.Position);
    }

    #endregion

    #region 核心方法 - 接收服务器状态

    /// <summary>
    /// 接收服务器广播的该玩家权威位置状态
    /// 由 FrameSyncManager.OnServerFrame 调用
    /// </summary>
    public void OnServerState(PlayerState state)
    {
        var position = new Vector3(state.PosX, state.PosY, state.PosZ);
        var rotation = Quaternion.Euler(0f, state.RotY, 0f);

        // 诊断：第一次收到该玩家的状态时打一行，确认分发链路到达
        if (_positionBuffer.Count == 0)
        {
            Debug.Log($"[RemotePlayerMovement] 🎮 [{playerId}] 首次应用权威位置 {position}");
        }

        var snapshot = new PositionSnapshot
        {
            FrameId = 0,
            Timestamp = Time.time,
            Position = position,
            Rotation = rotation
        };

        // 加入缓冲区
        _positionBuffer.Enqueue(snapshot);

        // 保持缓冲区大小
        while (_positionBuffer.Count > 20)
        {
            _positionBuffer.Dequeue();
        }

        UpdateTarget();
    }

    /// <summary>
    /// （已废弃）旧输入积分接口，仅为兼容保留
    /// 位置同步已改为服务器权威状态（OnServerState）
    /// </summary>
    public void OnReceiveFrameState(FrameStateResp frameState) { }

    #endregion

    #region 私有方法 - 目标更新

    /// <summary>
    /// 更新插值目标（考虑延迟补偿），并计算目标移动速度用于前馈
    /// </summary>
    private void UpdateTarget()
    {
        if (_positionBuffer.Count == 0) return;

        // 找到合适的快照（考虑 interpolationDelay）
        float targetTime = Time.time - interpolationDelay;
        PositionSnapshot bestSnapshot = null;

        foreach (var snapshot in _positionBuffer)
        {
            if (snapshot.Timestamp <= targetTime)
            {
                bestSnapshot = snapshot;
            }
            else
            {
                break; // 时间太近的快照不用
            }
        }

        if (bestSnapshot != null)
        {
            // 计算目标点的移动速度（前馈），带一点平滑滤掉网络抖动
            if (_hasLastTargetPosition)
            {
                float dt = bestSnapshot.Timestamp - _lastTargetTimestamp;
                if (dt > 0.001f)
                {
                    Vector3 newVelocity = (bestSnapshot.Position - _lastTargetPosition) / dt;
                    _targetVelocity = Vector3.Lerp(_targetVelocity, newVelocity, 0.5f);
                }
            }
            _lastTargetPosition = bestSnapshot.Position;
            _lastTargetTimestamp = bestSnapshot.Timestamp;
            _hasLastTargetPosition = true;

            _currentTarget = bestSnapshot;
        }
    }

    #endregion

    #region 公开方法 - 设置/清理

    /// <summary>
    /// 设置玩家ID
    /// </summary>
    public void SetPlayerId(string id)
    {
        playerId = id;
        gameObject.name = $"RemotePlayer_{id}";
    }

    /// <summary>
    /// 强制设置位置（如加入房间时的初始位置），并重置插值状态
    /// </summary>
    public void SetPosition(Vector3 position)
    {
        _targetVelocity = Vector3.zero;
        _hasLastTargetPosition = false;
        _positionBuffer?.Clear();
        _currentTarget = null;
        transform.position = position;
    }

    /// <summary>
    /// 清理缓冲区（玩家离开时调用）
    /// </summary>
    public void ClearBuffer()
    {
        _positionBuffer.Clear();
        _currentTarget = null;
    }

    #endregion

    #region 调试可视化

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // 显示玩家ID
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 1f);

        // 如果有目标点，显示插值路径
        if (_currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _currentTarget.Position);
        }
    }

    #endregion
}
