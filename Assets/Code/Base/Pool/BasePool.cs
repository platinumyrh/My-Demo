using System;
using System.Collections.Generic;

/// <summary>
/// 对象池抽象基类
/// </summary>
public abstract class BasePool<T>
{
    protected List<T> pool;
    protected Func<T> createFunc;
    protected Action<T> onGet;
    protected Action<T> onRelease;
    protected Action<T> onDestroy;

    protected int maxSize = 10;

    public int countAll { get; protected set; }
    public int countActive => countAll - countInactive;
    public int countInactive => pool.Count;
    public bool collectionCheck = true;

    protected BasePool(Func<T> createFunc, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, int maxSize = 10)
    {
        this.createFunc = createFunc;
        this.onGet = onGet;
        this.onRelease = onRelease;
        this.onDestroy = onDestroy;
        this.maxSize = maxSize;
        pool = new List<T>();
    }

    public virtual T Get()
    {
        T element;
        if (pool.Count == 0)
        {
            element = createFunc();
            countAll++;
        }
        else
        {
            int index = pool.Count - 1;
            element = pool[index];
            pool.RemoveAt(index);
        }
        onGet?.Invoke(element);
        return element;
    }

    public virtual void Release(T element)
    {
        if (collectionCheck && pool.Contains(element))
        {
            
            return;
        }

        onRelease?.Invoke(element);

        if (pool.Count < maxSize)
        {
            pool.Add(element);
        }
        else
        {
            onDestroy?.Invoke(element);
            countAll--;
        }
    }

    public virtual void Clear()
    {
        foreach (var item in pool)
        {
            onDestroy?.Invoke(item);
        }
        pool.Clear();
        countAll -= countInactive;
    }
}