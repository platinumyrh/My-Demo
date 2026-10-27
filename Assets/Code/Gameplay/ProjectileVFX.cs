using UnityEngine;

/// <summary>
/// 子弹/拖尾特效（纯表现，无战斗逻辑）
/// 对象管理走项目自带的 PoolManager（GameObjectPool，直接引用预制体的重载）：
/// 同一特效预制体共用一个池，取出后驱动飞行，到达终点归还入池
/// 特效 prefab 上不需要预先挂任何东西——首次取出时自动补挂本组件并测算视觉偏移
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

    // 本实例所属的池 key（归还时用）
    private string _poolKey;

    // 弹道上的"真实位置"（根节点会被视觉偏移反向补偿，所以位置分两个字段存）
    private Vector3 _pathPosition;

    // 视觉偏移是否已在本实例上测算过（实例被池复用，测算一次即可）
    private bool _offsetMeasured;

    // 视觉中心相对根节点的本地偏移（把视觉体对准弹道线用）
    private Vector3 _visualLocalOffset;

    /// <summary>
    /// 发射一发拖尾特效（自动经 PoolManager 池化）
    /// </summary>
    /// <param name="prefab">特效预制体（拖 EffectCore 的 Projectile prefab）</param>
    /// <param name="origin">起点（一般是枪口）</param>
    /// <param name="direction">飞行方向（单位向量）</param>
    /// <param name="distance">飞行距离（到命中点或最大射程）</param>
    public static void Spawn(GameObject prefab, Vector3 origin, Vector3 direction, float distance)
    {
        if (prefab == null) return;

        var poolManager = PoolManager.GetInstance();
        poolManager.Initialize(); // 确保 PoolRoot 就绪

        // 同一预制体共用一个池（key 用实例ID保证唯一）
        string poolKey = $"TracerVFX_{prefab.GetInstanceID()}";

        if (!poolManager.HasGameObjectPool(poolKey))
        {
            poolManager.CreateGameObjectPool(poolKey, prefab, maxSize: 20);
        }

        GameObject go = poolManager.GetGameObject(poolKey);
        if (go == null) return;

        var vfx = go.GetComponent<ProjectileVFX>();
        if (vfx == null) vfx = go.AddComponent<ProjectileVFX>();

        vfx._poolKey = poolKey;
        if (!vfx._offsetMeasured)
        {
            vfx.MeasureVisualOffset();
        }
        vfx.Launch(origin, direction.normalized, distance);
    }

    /// <summary>
    /// 测算视觉中心相对根节点的本地偏移（只统计 MeshRenderer，
    /// ParticleSystemRenderer 的 bounds 含粒子扩散范围，不可靠）
    /// </summary>
    private void MeasureVisualOffset()
    {
        _offsetMeasured = true;

        var meshes = GetComponentsInChildren<MeshRenderer>(true);
        if (meshes.Length > 0)
        {
            Bounds bounds = meshes[0].bounds;
            for (int i = 1; i < meshes.Length; i++) bounds.Encapsulate(meshes[i].bounds);
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

        // 每帧强制对齐飞行方向；visualRotationOffset 纠正特效本身的轴向约定
        transform.rotation = Quaternion.LookRotation(_direction) * Quaternion.Euler(visualRotationOffset);

        // 视觉中心贴住弹道线：根位置 = 弹道点 - 旋转后的视觉偏移
        transform.position = _pathPosition - transform.rotation * _visualLocalOffset;

        _elapsed += Time.deltaTime;
        if (_elapsed >= maxLifetime) Despawn();
    }

    private void Despawn()
    {
        if (!string.IsNullOrEmpty(_poolKey))
        {
            PoolManager.GetInstance().ReleaseGameObject(_poolKey, gameObject);
        }
        else
        {
            gameObject.SetActive(false); // 无池可归还时仅隐藏兜底
        }
    }
}
