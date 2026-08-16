using UnityEngine;
using GameNetwork;
using System.Collections.Generic;

/// <summary>
/// 远程玩家移动控制器
/// 负责显示其他玩家（根据服务器广播的帧状态更新）
/// </summary>
public class RemotePlayerMovement : MonoBehaviour
{
    #region 配置参数

    [Header("插值设置")]
    public float interpolationDelay = 0.1f; // 延迟插值，补偿网络延迟
    public float smoothingSpeed = 15f;      // 平滑速度

    [Header("调试")]
    public string playerId = "Unknown";     // 玩家ID（用于标识）

    #endregion

    #region 私有字段

    // 位置缓冲区（用于插值）
    private class PositionSnapshot
    {
        public uint FrameId;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Timestamp;
        
        public PlayerInput Input; // 该帧的输入
    }

    private Queue<PositionSnapshot> _positionBuffer;
    private PositionSnapshot _currentTarget;
    
    // 当前显示的状态
    private Vector3 _displayPosition;
    private Quaternion _displayRotation;

    // 组件引用
    // private Transform _transform;  // 注释掉：避免与 MonoBehaviour.transform 冲突

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        _positionBuffer = new Queue<PositionSnapshot>(20); // 缓冲最近20帧
        _displayPosition = transform.position;  // 直接使用基类属性
        _displayRotation = transform.rotation;
    }

    private void Update()
    {
        if (_currentTarget != null)
        {
            // 平滑插值到目标位置
            InterpolateToTarget();
        }
    }

    #endregion

    #region 核心方法 - 接收服务器状态

    /// <summary>
    /// 接收服务器广播的帧状态，更新该玩家的位置
    /// 由 NetworkManager 的 Handler 调用
    /// </summary>
    /// <param name="frameState">完整的一帧状态</param>
    public void OnReceiveFrameState(FrameStateResp frameState)
    {
        // 查找该玩家在这一帧的输入
        if (!frameState.PlayerInputs.ContainsKey(playerId))
        {
            return; // 这一帧没有这个玩家的数据
        }

        var playerInput = frameState.PlayerInputs[playerId];
        
        // 创建位置快照
        var snapshot = new PositionSnapshot
        {
            FrameId = frameState.FrameId,
            Timestamp = Time.time,
            Input = playerInput,
            Position = CalculatePredictedPosition(playerInput), // 根据输入计算目标位置
            Rotation = Quaternion.Euler(0, playerInput.Rotation * Mathf.Rad2Deg, 0)
        };

        // 加入缓冲区
        _positionBuffer.Enqueue(snapshot);
        
        // 保持缓冲区大小
        while (_positionBuffer.Count > 20)
        {
            _positionBuffer.Dequeue();
        }

        // 更新当前目标（考虑网络延迟）
        UpdateTarget();
    }

    #endregion

    #region 私有方法 - 位置计算

    /// <summary>
    /// 根据输入计算目标位置（简化版）
    /// 实际应该使用与 LocalPlayerMovement 相同的物理逻辑
    /// </summary>
    private Vector3 CalculatePredictedPosition(PlayerInput input)
    {
        // 简化版本：直接根据输入方向移动
        float speed = input.IsSprinting ? 8f : 5f;
        
        Vector3 moveDir = new Vector3(input.MoveX, 0, input.MoveY);
        
        if (moveDir.sqrMagnitude > 0.01f)
        {
            moveDir.Normalize();
        }
        
        // 计算这一帧的位移（假设固定时间步长）
        Vector3 displacement = moveDir * speed * (1f / 30f); // 30fps
        
        // 应用到当前位置
        return _displayPosition + displacement;
    }

    /// <summary>
    /// 更新插值目标（考虑延迟补偿）
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
            _currentTarget = bestSnapshot;
        }
    }

    /// <summary>
    /// 平滑插值到目标位置
    /// </summary>
    private void InterpolateToTarget()
    {
        if (_currentTarget == null) return;

        // 位置插值
        _displayPosition = Vector3.Lerp(
            _displayPosition, 
            _currentTarget.Position, 
            smoothingSpeed * Time.deltaTime
        );

        // 旋转插值
        _displayRotation = Quaternion.Slerp(
            _displayRotation,
            _currentTarget.Rotation,
            smoothingSpeed * Time.deltaTime
        );

        // 应用到 Transform
        transform.position = _displayPosition;
        transform.rotation = _displayRotation;
    }

    #endregion

    #region 公开方法 - 设置/清理

    /// <summary>
    /// 设置玩家ID
    /// </summary>
    public void SetPlayerId(string id)
    {
        playerId = id;
        gameObject.name = $"RemotePlayer_{id}";  // 修复：添加赋值符号
    }

    /// <summary>
    /// 强制设置位置（如加入房间时的初始位置）
    /// </summary>
    public void SetPosition(Vector3 position)
    {
        _displayPosition = position;
        // _predictedPosition = position;  // 注释掉：字段未使用（CS0168警告）
        transform.position = position;
    }

    /// <summary>
    /// 设置目标位置（用于帧同步插值）
    /// </summary>
    public void SetTargetPosition(Vector3 targetPosition)
    {
        // 创建一个新的目标快照
        _currentTarget = new PositionSnapshot
        {
            FrameId = 0,
            Position = targetPosition,
            Rotation = transform.rotation,
            Timestamp = Time.time
        };

        // 更新显示位置的目标
        _displayPosition = transform.position;
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
        
        // 如果有目标点，显示预测路径
        if (_currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _currentTarget.Position);
        }
    }

    #endregion
}
