using UnityEngine;
using System.Collections;

public class SmallObjectPool
{
    private static Transform container;
    private GameObject[] pool;
    private int cacheIndex;
    public string name;

    public SmallObjectPool(GameObject gameObject, int poolSize = 10)
    {
        if (container == null)
            container = new GameObject("_SmallObjectPoolContainer_").transform;

        name = gameObject.name;
        pool = new GameObject[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            pool[i] = GameObject.Instantiate(gameObject);
            pool[i].SetActive(false);
            pool[i].name = gameObject.name + i;
            pool[i].transform.parent = container;
        }
    }

    public GameObject Get()
    {
        GameObject obj = null;

        // The cacheIndex starts out at the position of the object created
        // the longest time ago, so that one is usually free,
        // but in case not, loop through the cache until we find a free one.
        for (int i = 0; i < pool.Length; i++)
        {
            obj = pool[cacheIndex];

            // If we found an inactive object in the cache, use that.
            if (!obj.activeSelf)
                break;

            // If not, increment index and make it loop around
            // if it exceeds the size of the cache
            cacheIndex = (cacheIndex + 1) % pool.Length;
        }

        // The object should be inactive. If it's not, log a warning and use
        // the object created the longest ago even though it's still active.
        if (obj.activeSelf)
        {
            Debug.LogWarning(
                "Spawn of " + name +
                " exceeds cache size of " + pool.Length +
                "! Reusing already active object.", obj);
            Destroy(obj);
        }

        // Increment index and make it loop around
        // if it exceeds the size of the cache
        cacheIndex = (cacheIndex + 1) % pool.Length;

        return obj;
    }

    //most of the time gameobjects are "selfDestructed" but if not use it
    public void Destroy(GameObject gameObject)
    {
        //if effect was attahed to something else 
        //reattach it to container
        if (gameObject.transform.parent != container)
            gameObject.transform.parent = container;

        gameObject.SetActive(false);
    }

    public void Clear() { System.Array.Clear(pool, 0, pool.Length); }
}