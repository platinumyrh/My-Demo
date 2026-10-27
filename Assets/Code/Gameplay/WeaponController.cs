using System;
using UnityEngine;

/// <summary>
/// 武器表现层：只负责播放枪口火光、弹道拖尾、命中特效和开火/换弹动画事件。
/// 命中、伤害、弹匣、射速和换弹状态全部在确定性 LogicWorld 中，本类不再做本地判定。
/// </summary>
public class WeaponController : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("枪口挂点（留空则用调用方传入的 origin）")]
    public Transform muzzle;

    [Header("特效")]
    public GameObject tracerPrefab;
    public GameObject muzzleFlashPrefab;
    public GameObject impactPrefab;
    public float vfxLifetime = 1.5f;
    [Range(0f, 0.5f)] public float tracerStartRatio = 0.25f;
    [SerializeField] private Vector3 muzzleFlashRotationOffset = new Vector3(-90f, 0f, 0f);

    public event Action<ShotInfo> OnFired;
    public event Action OnReloadStarted;

    public struct ShotInfo
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 EndPoint;
        public Vector3 HitNormal;
    }

    /// <summary>远端/本地统一的表现入口：只播特效，不做任何命中或弹药判定。</summary>
    public void PlayFireVisual(Vector3 origin, Vector3 direction, Vector3 endPoint,
                               bool hitSomething, Vector3 impactNormal)
    {
        SpawnMuzzleFlash(origin, direction);
        SpawnTracer(origin, direction, Vector3.Distance(origin, endPoint));

        if (hitSomething)
        {
            Vector3 normal = impactNormal;
            if (normal.sqrMagnitude < 0.001f) normal = -direction;
            normal.Normalize();
            SpawnImpact(endPoint, normal);
        }

        OnFired?.Invoke(new ShotInfo
        {
            Origin = origin,
            Direction = direction,
            EndPoint = endPoint,
            HitNormal = -direction
        });
    }

    /// <summary>换弹表现：驱动 WeaponAnimationController，不改任何逻辑状态。</summary>
    public void PlayReloadVisual()
    {
        OnReloadStarted?.Invoke();
    }

    private void SpawnMuzzleFlash(Vector3 position, Vector3 direction)
    {
        if (muzzleFlashPrefab == null) return;
        Quaternion rot = Quaternion.LookRotation(direction) * Quaternion.Euler(muzzleFlashRotationOffset);
        PooledVFX.Spawn(muzzleFlashPrefab, position, rot, vfxLifetime);
    }

    private void SpawnTracer(Vector3 origin, Vector3 direction, float distance)
    {
        if (tracerPrefab == null) return;
        float startDistance = distance * tracerStartRatio;
        float remain = distance - startDistance;
        if (remain < 0.05f) return;
        ProjectileVFX.Spawn(tracerPrefab, origin + direction * startDistance, direction, remain);
    }

    private void SpawnImpact(Vector3 position, Vector3 normal)
    {
        if (impactPrefab == null) return;
        PooledVFX.Spawn(impactPrefab, position, Quaternion.LookRotation(normal), vfxLifetime);
    }
}
