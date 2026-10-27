using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 输入采集层：只把本地设备输入暴露给 lockstep 驱动，不参与网络同步。
/// 旧状态同步链路删除后，这里只提供设备输入，不生成任何网络状态。
/// </summary>
public class PlayerInputController : MonoBehaviour
{
    private PlayerInputActions _inputActions;
    private Vector2 _moveInput;
    private bool _isSprinting;
    private bool _isAiming;
    private bool _fire;
    private bool _jump;

    /// <summary>鼠标 / 指针屏幕坐标（lockstep 驱动做瞄准射线）</summary>
    public Vector2 LookScreenPosition { get; private set; }

    public Vector2 MoveAxes => _moveInput;
    public bool FireHeld => _fire;
    public bool AimHeld => _isAiming;
    public bool SprintHeld => _isSprinting;
    public bool JumpHeld => _jump;
    public bool ReloadPressed => _inputActions != null && _inputActions.Gameplay.Reload.WasPressedThisFrame();

    private void Awake()
    {
        _inputActions = new PlayerInputActions();
        _inputActions.Gameplay.Move.performed += OnMovePerformed;
        _inputActions.Gameplay.Move.canceled += OnMoveCanceled;
        _inputActions.Gameplay.Sprint.performed += _ => _isSprinting = true;
        _inputActions.Gameplay.Sprint.canceled += _ => _isSprinting = false;
        _inputActions.Gameplay.Aim.performed += _ => _isAiming = true;
        _inputActions.Gameplay.Aim.canceled += _ => _isAiming = false;
        _inputActions.Gameplay.Fire.performed += _ => _fire = true;
        _inputActions.Gameplay.Fire.canceled += _ => _fire = false;
        _inputActions.Gameplay.Jump.performed += _ => _jump = true;
        _inputActions.Gameplay.Jump.canceled += _ => _jump = false;
    }

    private void Update()
    {
        if (Mouse.current != null)
            LookScreenPosition = Mouse.current.position.ReadValue();
        else if (Pointer.current != null)
            LookScreenPosition = Pointer.current.position.ReadValue();
    }

    private void OnEnable() => _inputActions.Enable();
    private void OnDisable() => _inputActions.Disable();

    private void OnMovePerformed(InputAction.CallbackContext context) => _moveInput = context.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext context) => _moveInput = Vector2.zero;
}
