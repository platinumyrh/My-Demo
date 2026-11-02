using UnityEngine;

/// <summary>
/// 池化特效的统一入口：从 PoolManager 取出、摆放、挂自动归还组件
/// 供枪口火光/命中火花这类一次性特效使用（弹道拖尾走 ProjectileVFX 自己的驱动）
/// </summary>
public static class PooledVFX
{
    /// <summary>
    /// 从池中取出特效并摆放，lifetime 秒后自动归还
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null) return null;

        var poolManager = PoolManager.GetInstance();
        poolManager.Initialize();

        string poolKey = $"VFX_{prefab.GetInstanceID()}";
        if (!poolManager.HasGameObjectPool(poolKey))
        {
            poolManager.CreateGameObjectPool(poolKey, prefab, maxSize: 20);
        }

        GameObject go = poolManager.GetGameObject(poolKey);
        if (go == null) return null;

        go.transform.position = position;
        go.transform.rotation = rotation;

        var release = go.GetComponent<PooledEffectAutoRelease>();
        if (release == null) release = go.AddComponent<PooledEffectAutoRelease>();
        release.Begin(poolKey, lifetime);

        return go;
    }
}
