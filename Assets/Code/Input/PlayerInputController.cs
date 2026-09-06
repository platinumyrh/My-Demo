using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家输入控制器
/// 使用新 Input System 收集输入，打包成 Protobuf 发送
/// </summary>
public class PlayerInputController : MonoBehaviour
{
    #region 输入引用

    private PlayerInputActions _inputActions;
    private Vector2 _moveInput;           // 移动方向 (-1~1)
    private bool _isSprinting = false;   // 是否冲刺
    private bool _isAiming = false;      // 是否瞄准
    private bool _fire = false;          // 是否开火
    private bool _jump = false;          // 是否跳跃
    private float _rotation = 0f;        // 视角旋转

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 初始化 Input Actions
        _inputActions = new PlayerInputActions();
        
        // 注册输入回调
        _inputActions.Gameplay.Move.performed += OnMovePerformed;
        _inputActions.Gameplay.Move.canceled += OnMoveCanceled;
        _inputActions.Gameplay.Sprint.performed += ctx => _isSprinting = true;
        _inputActions.Gameplay.Sprint.canceled += ctx => _isSprinting = false;
        _inputActions.Gameplay.Aim.performed += ctx => _isAiming = true;
        _inputActions.Gameplay.Aim.canceled += ctx => _isAiming = false;
        _inputActions.Gameplay.Fire.performed += ctx => _fire = true;
        _inputActions.Gameplay.Fire.canceled += ctx => _fire = false;
        _inputActions.Gameplay.Jump.performed += ctx => _jump = true;
        _inputActions.Gameplay.Jump.canceled += ctx => _jump = false;
        _inputActions.Gameplay.Jump.canceled += ctx => _jump = false;
    }

    private void OnEnable()
    {
        _inputActions.Enable();
    }

    private void OnDisable()
    {
        _inputActions.Disable();
    }

    #endregion

    #region 输入事件处理

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        _moveInput = context.ReadValue<Vector2>();
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        _moveInput = Vector2.zero;
    }

    #endregion

    #region 公开方法 - 获取当前输入状态

    /// <summary>
    /// 获取当前的输入状态（用于发送给服务器）
    /// </summary>
    public GameNetwork.PlayerInputReq GetCurrentInput(uint frameId)
    {
        return new GameNetwork.PlayerInputReq
        {
            FrameId = frameId,
            MoveX = _moveInput.x,      // 水平方向 (A/D 或 左右摇杆)
            MoveY = _moveInput.y,      // 垂直方向 (W/S 或 上下摇杆)
            IsSprinting = _isSprinting,
            IsAiming = _isAiming,
            Fire = _fire,
            Jump = _jump,
            Rotation = _rotation       // 鼠标视角或右摇杆
        };
    }

    /// <summary>
    /// 更新视角旋转（由摄像机控制脚本调用）
    /// </summary>
    public void UpdateRotation(float rotation)
    {
        _rotation = rotation;
    }

    #endregion

    #region 调试信息

    private void OnGUI()
    {
        if (Debug.isDebugBuild)
        {
            GUI.Label(new Rect(10, 10, 300, 100), 
                $"Move: {_moveInput}\n" +
                $"Sprint: {_isSprinting}\n" +
                $"Aim: {_isAiming}\n" +
                $"Fire: {_fire}");
        }
    }

    #endregion
}
