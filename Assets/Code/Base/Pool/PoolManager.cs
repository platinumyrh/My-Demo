using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PoolManager :BaseManager<PoolManager>
{
    private Dictionary<string,GameObjectPool> gameObjectPools = new Dictionary<string,GameObjectPool>();
    private Dictionary<string, EffectPool> effectPools = new Dictionary<string, EffectPool>();
    private Dictionary<string, UIPool> uiPools = new Dictionary<string, UIPool>();

    private Transform poolRoot;

    public void Initialize()
    {
        if (poolRoot == null)
        {
            GameObject root = new GameObject("PoolRoot");
            poolRoot = root.transform;
            GameObject.DontDestroyOnLoad(root);
        }
    }

    /// <summary>
    /// 游戏对象池相关
    /// <summary>
    public GameObjectPool CreateGameObjectPool(string poolKey, string prefabPath, int maxSize = 10)
    {
        if (gameObjectPools.ContainsKey(poolKey))
        { 
          Debug.LogWarning($"GameObjectPool with key {poolKey} already exists.");
            return gameObjectPools[poolKey];
        }

        // 创建新的游戏对象池
        GameObject container = new GameObject($"{poolKey}_Container");
        container.transform.SetParent(poolRoot);
        GameObjectPool newPool = new GameObjectPool(prefabPath, container.transform, maxSize);
        gameObjectPools.Add(poolKey, newPool);

        return newPool;
    }
    public GameObject GetGameObject(string poolKey)
    {
        if (gameObjectPools.TryGetValue(poolKey, out GameObjectPool pool))
        {
            return pool.Get();
        }
        else
        {
            Debug.LogError($"GameObjectPool with key {poolKey} does not exist.");
            return null;
        }
    }
    public void ReleaseGameObject(string poolKey, GameObject obj)
    {
        if (gameObjectPools.TryGetValue(poolKey, out GameObjectPool pool))
        {
            pool.Release(obj);
        }
        else
        {
            GameObject.Destroy(obj);
        }
    }

    /// <summary>
    /// 特效池相关
    /// <summary>
    public EffectPool CreateEffectPool(string poolKey, string prefabPath, int maxSize = 10)
    {
        if (effectPools.ContainsKey(poolKey))
        {
            Debug.LogWarning($"特效池 {poolKey} 已存在");
            return effectPools[poolKey];
        }

        GameObject container = new GameObject($"{poolKey}_Container");
        container.transform.SetParent(poolRoot);

        EffectPool pool = new EffectPool(prefabPath, container.transform, maxSize);
        effectPools.Add(poolKey, pool);

        return pool;
    }
    public ParticleSystem GetEffect(string poolKey)
    {
        if (effectPools.TryGetValue(poolKey, out EffectPool pool))
        {
            return pool.Get();
        }

        Debug.LogError($"找不到特效池: {poolKey}");
        return null;
    }
    public void ReleaseEffect(string poolKey, ParticleSystem effect)
    {
        if (effectPools.TryGetValue(poolKey, out EffectPool pool))
        {
            pool.Release(effect);
        }
        else
        {
            GameObject.Destroy(effect.gameObject);
        }
    }
    /// <summary>
    /// ui池相关
    /// <summary>
    public UIPool CreateUIPool(string poolKey, string prefabPath, int maxSize = 10)
    {
        if (uiPools.ContainsKey(poolKey))
        {
            Debug.LogWarning($"UI池 {poolKey} 已存在");
            return uiPools[poolKey];
        }

        GameObject container = new GameObject($"{poolKey}_Container");
        container.transform.SetParent(poolRoot);

        UIPool pool = new UIPool(prefabPath, container.transform, maxSize);
        uiPools.Add(poolKey, pool);

        return pool;
    }

    public RectTransform GetUI(string poolKey)
    {
        if (uiPools.TryGetValue(poolKey, out UIPool pool))
        {
            return pool.Get();
        }

        Debug.LogError($"找不到UI池: {poolKey}");
        return null;
    }

    public void ReleaseUI(string poolKey, RectTransform ui)
    {
        if (uiPools.TryGetValue(poolKey, out UIPool pool))
        {
            pool.Release(ui);
        }
        else
        {
            GameObject.Destroy(ui.gameObject);
        }
    }

    // ===== 通用方法 =====
    public void ClearAllPools()
    {
        foreach (var pool in gameObjectPools.Values)
        {
            pool.Clear();
        }
        gameObjectPools.Clear();

        foreach (var pool in effectPools.Values)
        {
            pool.Clear();
        }
        effectPools.Clear();

        foreach (var pool in uiPools.Values)
        {
            pool.Clear();
        }
        uiPools.Clear();
    }

}
