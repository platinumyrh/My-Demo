using UnityEngine;

/// <summary>
/// 玩家载具配置（ScriptableObject 配置模板）
/// 一类载具一个资产：血量、移速、武器属性一体化（武装载具设定——武器与车体不可拆分）
///
/// ⚠️ 只读模板：所有同类型载具共享同一份资产，禁止在运行时写入任何字段——
/// 当前血量等运行时状态由 PlayerStats 组件持有（出生时从 maxHp 初始化）
/// </summary>
[CreateAssetMenu(menuName = "MyDemo/PlayerProfile", fileName = "PlayerProfile")]
public class PlayerProfileSO : ScriptableObject
{
    [Header("生存")]
    [Tooltip("血量上限（出生时满血）")]
    public int maxHp = 100;

    [Header("移动")]
    [Tooltip("走路速度（米/秒）")]
    public float walkSpeed = 5f;

    [Tooltip("冲刺速度（米/秒）")]
    public float sprintSpeed = 8f;

    [Header("武器（与车体一体）")]
    [Tooltip("单发伤害")]
    public int weaponDamage = 10;

    [Tooltip("射速（发/秒）")]
    public float weaponFireRate = 8f;

    [Tooltip("弹匣容量")]
    public int weaponMagazineSize = 30;

    [Tooltip("换弹耗时（秒）")]
    public float weaponReloadTime = 1.5f;

    [Tooltip("最大射程（米）")]
    public float weaponMaxRange = 50f;
}
