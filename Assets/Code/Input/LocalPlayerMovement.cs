using UnityEngine;
using GameNetwork;

/// <summary>
/// 本地玩家移动控制器
/// 负责客户端预测：立即响应输入，不等待服务器
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputController))]
public class LocalPlayerMovement : MonoBehaviour
{
    #region 配置参数

    [Header("移动参数")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float rotationSpeed = 10f;

    [Header("网络同步")]
    [Tooltip("每秒发送多少次输入到服务器")]
    public int sendRate = 30; // 与服务器帧率一致

    #endregion

    #region 私有字段

    private CharacterController _controller;
    private PlayerInputController _playerInput;
    private uint _localFrameId;  // 本地帧ID（客户端预测用）
    private float _sendTimer;

    // 客户端预测相关
    private Vector3 _predictedPosition;
    private Quaternion _predictedRotation;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        _controller = GetComponent<CharacterController>();
        _playerInput = GetComponent<PlayerInputController>();
        _localFrameId = 0;
        _predictedPosition = transform.position;
        _predictedRotation = transform.rotation;
    }

    private void Update()
    {
        if (!NetworkManager.Instance.IsConnected) return;

        // 1. 获取当前输入
        var inputReq = _playerInput.GetCurrentInput(_localFrameId);

        // 2. 立即本地移动（客户端预测）⭐ 关键！
        MoveLocally(inputReq);

        // 3. 定时发送输入到服务器
        _sendTimer += Time.deltaTime;
        if (_sendTimer >= 1f / sendRate)
        {
            SendInputToServer(inputReq);
            _sendTimer = 0f;
            _localFrameId++;
        }
    }

    #endregion

    #region 核心方法 - 本地移动（客户端预测）

    /// <summary>
    /// 立即在本地应用移动（不等服务器）
    /// 这是"客户端预测"的核心
    /// </summary>
    private void MoveLocally(PlayerInputReq input)
    {
        // 计算移动速度
        float currentSpeed = input.IsSprinting ? sprintSpeed : walkSpeed;

        // 根据摄像机方向计算移动方向
        Vector3 moveDirection = new Vector3(input.MoveX, 0, input.MoveY);
        
        // 如果有输入，相对于摄像机方向移动
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            // 将移动方向转换到世界坐标（考虑摄像机朝向）
            moveDirection = Camera.main.transform.TransformDirection(moveDirection);
            moveDirection.y = 0; // 保持水平移动
            moveDirection.Normalize();
        }

        // 应用重力（简单版本）
        Vector3 verticalVelocity = Vector3.down * 9.81f;
        if (_controller.isGrounded && input.Jump)
        {
            verticalVelocity = Vector3.up * 5f; // 跳跃力度
        }

        // 合并移动
        Vector3 finalMove = moveDirection * currentSpeed * Time.deltaTime + verticalVelocity * Time.deltaTime;

        // 移动角色
        _controller.Move(finalMove);

        // 更新视角旋转
        if (input.IsAiming || Mathf.Abs(input.Rotation) > 0.01f)
        {
            Quaternion targetRotation = Quaternion.Euler(0, input.Rotation * Mathf.Rad2Deg, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // 保存预测位置（用于后续校正）
        _predictedPosition = transform.position;
        _predictedRotation = transform.rotation;
    }

    #endregion

    #region 网络通信 - 发送输入

    /// <summary>
    /// 发送当前输入状态到服务器
    /// </summary>
    private void SendInputToServer(PlayerInputReq input)
    {
        try
        {
            NetworkManager.Instance.Send(MsgID.C2SPlayerInput, input);
            
            // 调试日志（可以关闭以提升性能）
            // Debug.Log($"[LocalPlayer] 发送输入 Frame#{input.FrameId}: " +
            //          $"Move({input.MoveX:F1}, {input.MoveY:F1}) Fire={input.Fire}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LocalPlayer] 发送输入失败: {ex.Message}");
        }
    }

    #endregion

    #region 服务器校正（预留接口）

    /// <summary>
    /// 接收服务器的位置校正信息
    /// 当客户端预测与服务器不一致时调用
    /// </summary>
    /// <param name="serverPosition">服务器权威位置</param>
    /// <param name="serverFrameId">对应的帧ID</param>
    public void OnServerCorrection(Vector3 serverPosition, uint serverFrameId)
    {
        // TODO: 实现插值校正算法
        // 
        // 简单方案：直接瞬移到服务器位置（会有抖动）
        // transform.position = serverPosition;
        //
        // 高级方案：平滑插值 + 重模拟中间帧
        // StartCoroutine(SmoothCorrection(serverPosition));
        
        // 暂时使用简单方案
        transform.position = serverPosition;
        _predictedPosition = serverPosition;
        
        Debug.Log($"[LocalPlayer] 位置校正: Frame {serverFrameId}");
    }

    #endregion
}
