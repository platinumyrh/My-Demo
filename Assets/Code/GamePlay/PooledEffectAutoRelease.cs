using UnityEngine;

/// <summary>
/// 池化特效的自动归还组件（运行时挂到池实例上）
/// 用途：枪口火光/命中火花这类"播完即弃"的特效——
/// 生成时 Begin(池key, 存活时长)，计时结束自动归还所属对象池
/// 实例被池复用时组件仍在，Begin 重置计时即可
/// </summary>
public class PooledEffectAutoRelease : MonoBehaviour
{
    private string _poolKey;
    private float _lifetime;
    private float _elapsed;

    public void Begin(string poolKey, float lifetime)
    {
        _poolKey = poolKey;
        _lifetime = Mathf.Max(0.05f, lifetime);
        _elapsed = 0f;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime)
        {
            _elapsed = 0f;
            PoolManager.GetInstance().ReleaseGameObject(_poolKey, gameObject);
        }
    }
}
