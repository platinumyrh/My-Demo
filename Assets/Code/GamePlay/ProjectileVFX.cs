using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 子弹/拖尾特效的控制脚本（纯表现，无任何战斗逻辑）
/// 特效 prefab 上不需要预先挂任何东西——Spawn 时自动补挂本组件
/// 行为：从起点沿方向匀速飞到终点，到达后回收入池
/// </summary>
public class ProjectileVFX : MonoBehaviour
{
    [Tooltip("飞行速度（米/秒）")]
    public float speed = 80f;

    [Tooltip("最长存活时间（保险，防止永不到达）")]
    public float maxLifetime = 2f;

    [Tooltip("特效视觉轴向偏移：拖尾方向不对时调这里（如 (90,0,0) / (0,90,0)）")]
    public Vector3 visualRotationOffset = Vector3.zero;

    private Vector3 _direction;
    private float _remainingDistance;
    private float _elapsed;
    private ProjectileVFX _template; // 本实例的克隆源头（池的键）

    // 弹道上的"真实位置"（根节点会被视觉偏移反向补偿，所以位置分两个字段存）
    private Vector3 _pathPosition;

    // 视觉中心相对根节点的本地偏移（模板创建时测算，用于把视觉体对准弹道线）
    private Vector3 _visualLocalOffset;

    // prefab → 模板实例（隐藏的、带本组件的克隆）
    private static readonly Dictionary<GameObject, ProjectileVFX> _templates = new();
    // 模板实例 → 空闲对象池
    private static readonly Dictionary<ProjectileVFX, Stack<ProjectileVFX>> _pools = new();

    /// <summary>
    /// 从池中取出一个特效并发射
    /// </summary>
    /// <param name="prefab">特效预制体（拖 EffectCore 的 Projectile prefab）</param>
    /// <param name="origin">起点（一般是枪口）</param>
    /// <param name="direction">飞行方向（单位向量）</param>
    /// <param name="distance">飞行距离（到命中点或最大射程）</param>
    public static ProjectileVFX Spawn(GameObject prefab, Vector3 origin, Vector3 direction, float distance)
    {
        if (prefab == null) return null;

        if (!_templates.TryGetValue(prefab, out var template))
        {
            var go = Object.Instantiate(prefab);
            go.SetActive(false);
            template = go.GetComponent<ProjectileVFX>();
            if (template == null) template = go.AddComponent<ProjectileVFX>();
            template.PrepareTemplate();
            _templates[prefab] = template;
            _pools[template] = new Stack<ProjectileVFX>();
        }

        var pool = _pools[template];
        var vfx = pool.Count > 0 ? pool.Pop() : Object.Instantiate(template);
        vfx._template = template;
        vfx._visualLocalOffset = template._visualLocalOffset;
        vfx.gameObject.SetActive(true);
        vfx.Launch(origin, direction.normalized, distance);
        return vfx;
    }

    /// <summary>
    /// 模板初始化：
    /// 1. 停用特效包自带的行为脚本（运动/朝向由本脚本驱动，避免打架）
    /// 2. 测算视觉中心相对根节点的偏移（这类 prefab 的视觉体常偏离轴心几米，
    ///    不补偿的话拖尾会整体偏离弹道线）
    /// </summary>
    private void PrepareTemplate()
    {
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is ProjectileVFX) continue;
            if (mb != null) mb.enabled = false;
        }

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            _visualLocalOffset = transform.InverseTransformPoint(bounds.center);
        }
    }

    private void Launch(Vector3 origin, Vector3 direction, float distance)
    {
        _pathPosition = origin;
        _direction = direction;
        _remainingDistance = distance;
        _elapsed = 0f;

        transform.rotation = Quaternion.LookRotation(_direction) * Quaternion.Euler(visualRotationOffset);
        transform.position = _pathPosition - transform.rotation * _visualLocalOffset;
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;

        if (step >= _remainingDistance)
        {
            Despawn(); // 到达终点
            return;
        }

        _pathPosition += _direction * step;
        _remainingDistance -= step;

        // 每帧强制对齐飞行方向（特效包自带的脚本可能持续修改朝向），
        // visualRotationOffset 用于纠正特效本身的轴向约定
        transform.rotation = Quaternion.LookRotation(_direction) * Quaternion.Euler(visualRotationOffset);

        // 视觉中心贴住弹道线：根位置 = 弹道点 - 旋转后的视觉偏移
        transform.position = _pathPosition - transform.rotation * _visualLocalOffset;

        _elapsed += Time.deltaTime;
        if (_elapsed >= maxLifetime) Despawn();
    }

    private void Despawn()
    {
        gameObject.SetActive(false);
        if (_template != null && _pools.TryGetValue(_template, out var pool))
        {
            pool.Push(this);
        }
        else
        {
            Destroy(gameObject); // 无池可回时兜底销毁
        }
    }
}
