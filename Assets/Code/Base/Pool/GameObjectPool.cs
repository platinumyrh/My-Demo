using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 游戏对象池 通过预设路径创建游戏对象，并管理其生命周期
/// </summary>
public class GameObjectPool : BasePool<GameObject>
{
    public string prefabPath;
    public Transform parentTransform;

    public GameObjectPool(string prefabPath, Transform parent = null,int maxSize = 10)
        :base(null,null,null,null,maxSize)
    {
        this.prefabPath = prefabPath;
        this.parentTransform = parent;

        // 重写创建方法
        createFunc = CreateGameObject;
        onGet = OnGetObject;
        onRelease = OnReleaseObject;
        onDestroy = OnDestroyObject;
    }

    /// <summary>直接引用的预制体（prefabPath 与 prefabRef 二选一）</summary>
    public GameObject prefabRef;

    /// <summary>
    /// 直接引用预制体的构造重载（预制体不在 Resources 目录时用这个，
    /// 例如 Inspector 拖拽的特效引用）。取出回调不假设对象上有 Rigidbody
    /// </summary>
    public GameObjectPool(GameObject prefab, Transform parent = null, int maxSize = 10)
        : base(null, null, null, null, maxSize)
    {
        prefabRef = prefab;
        this.parentTransform = parent;

        createFunc = CreateFromRef;
        onGet = OnGetObjectSafe;
        onRelease = OnReleaseObject;
        onDestroy = OnDestroyObject;
    }

    private GameObject CreateFromRef()
    {
        if (prefabRef == null)
        {
            Debug.LogError("[GameObjectPool] prefabRef 未设置");
            return new GameObject("PoolObj_MissingPrefab");
        }
        GameObject obj = GameObject.Instantiate(prefabRef, parentTransform);
        obj.name = prefabRef.name; // 去掉 "(Clone)"
        return obj;
    }

    /// <summary>安全的取出回调：不假设对象上有 Rigidbody（特效等通用对象用）</summary>
    private void OnGetObjectSafe(GameObject obj)
    {
        obj.SetActive(true);
    }

    private GameObject CreateGameObject()
    {
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        //GameObject prefab = ResManager.GetInstance().LoadAsync<GameObject>(prefabPath,DoSomething);
        // GameObject prefab = ResManager.GetInstance().Load<GameObject>(prefabPath);//引入资源管理器，使用资源管理器加载预制体后出现了点问题 后面再改
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found at path: {prefabPath}");
            return new GameObject(prefabPath);
        }
        GameObject obj = GameObject.Instantiate(prefab, parentTransform);
        obj.name = prefab.name; // 去掉 "(Clone)"

        if (parentTransform != null)
        {
            obj.transform.SetParent(parentTransform);
        }
        return obj;
    }
    private void OnGetObject(GameObject obj)
    {
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        rb.velocity = Vector3.zero;
        obj.SetActive(true);
    }
    private void OnReleaseObject(GameObject obj)
    {
        obj.SetActive(false);
        if (parentTransform != null)
        {
            obj.transform.SetParent(parentTransform);
        }
    }

    private void OnDestroyObject(GameObject obj)
    {
        GameObject.Destroy(obj);
    }

    private void DoSomething(GameObject obj)
    {
        Debug.Log("name:" + obj.name);
    }
}
