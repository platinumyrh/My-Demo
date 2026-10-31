using GameLockstep;
using UnityEngine;

/// <summary>
/// 本地输入采样：渲染帧读键鼠/摄像机 → 逻辑帧量化成 PlayerCommand。
/// 只回答“玩家想做什么”，不碰 World、不碰网络。
/// </summary>
public sealed class LocalInputSampler
{
    private PlayerInputController _input;
    private Transform _localTransform;

    private float _aimAngleDeg;
    private Vector2 _moveAxes;
    private bool _fireHeld, _sprintHeld, _jumpHeld, _reloadSticky;

    /// <summary>表现层用：本地玩家当前是否按住开火</summary>
    public bool FireHeld => _fireHeld;

    public void Bind(PlayerInputController input, Transform localTransform)
    {
        _input = input;
        _localTransform = localTransform;
    }

    /// <summary>渲染帧采样。suppressed = 菜单打开等，人物停手停脚</summary>
    public void Sample(bool suppressed)
    {
        if (_input == null) return;

        if (suppressed)
        {
            _moveAxes = Vector2.zero;
            _fireHeld = false;
            _sprintHeld = false;
            _jumpHeld = false;
            _reloadSticky = false;
            return;
        }

        // 换弹是边沿触发（WasPressedThisFrame 只在渲染帧有效），粘性捕获到下一个逻辑帧
        if (_input.ReloadPressed) _reloadSticky = true;

        _moveAxes = _input.MoveAxes;
        _fireHeld = _input.FireHeld;
        _sprintHeld = _input.SprintHeld;
        _jumpHeld = _input.JumpHeld;

        UpdateAim();
    }

    /// <summary>逻辑帧量化：摄像机相对轴 → 世界轴，换弹边沿在这里消费掉</summary>
    public PlayerCommand BuildCommand()
    {
        var cmd = new PlayerCommand
        {
            AimAngle = FPAngle.FromDegrees(_aimAngleDeg),
            Buttons = BuildButtons(),
        };

        Vector3 worldMove = new Vector3(_moveAxes.x, 0f, _moveAxes.y);
        if (worldMove.sqrMagnitude > 0.0001f && Camera.main != null)
        {
            worldMove = Camera.main.transform.TransformDirection(worldMove);
            worldMove.y = 0f;
            worldMove.Normalize();
        }
        cmd.MoveX = PlayerCommand.QuantizeAxis(worldMove.x);
        cmd.MoveZ = PlayerCommand.QuantizeAxis(worldMove.z);
        _reloadSticky = false;

        return cmd;
    }

    /// <summary>鼠标 → 世界瞄准点 → 目标朝向角</summary>
    private void UpdateAim()
    {
        if (_localTransform == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(_input.LookScreenPosition);

        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
        {
            aimPoint = hit.point;
        }
        else
        {
            var plane = new Plane(Vector3.up, Vector3.up * _localTransform.position.y);
            if (!plane.Raycast(ray, out float d)) return;
            aimPoint = ray.GetPoint(d);
        }

        Vector3 flat = aimPoint - _localTransform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0004f) return; // 鼠标压在角色身上：保持上一帧朝向

        _aimAngleDeg = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
    }

    private byte BuildButtons()
    {
        byte b = 0;
        if (_fireHeld) b |= PlayerCommand.BTN_FIRE;
        if (_sprintHeld) b |= PlayerCommand.BTN_SPRINT;
        if (_reloadSticky) b |= PlayerCommand.BTN_RELOAD;
        if (_jumpHeld) b |= PlayerCommand.BTN_JUMP;
        return b;
    }
}
