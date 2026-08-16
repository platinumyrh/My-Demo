using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MonoManager : BaseManager<MonoManager>
{
    public MonoController controller { get; private set; }

    public MonoManager()
    {
        GameObject obj = new GameObject("MonoController");
        controller = obj.AddComponent<MonoController>();
    }
    /// <summary>
    /// 为外部添加帧更新事件的函数
    /// </summary>
    /// <param name="action"></param>
    public void AddUpdateListener(UnityAction action)
    {
       controller.AddUpdateListener(action);
    }
    /// <summary>
    /// 为外部移除帧更新事件的函数
    /// </summary>
    /// <param name="action"></param>
    public void RemoveUpdateListener(UnityAction action)
    {
       controller.RemoveUpdateListener(action);
    }
    ///<summary>
    ///为外部提供可以开启协程的函数
    ///<summary>
    public Coroutine StartCoroutine(IEnumerator routine)
    {
        return controller.StartCoroutine(routine);
    }
    ///<summary>
    ///关闭协程的函数
    ///<summary>
    public void StopCoroutine(IEnumerator routine)
    {
        controller.StopCoroutine(routine);
    }

}
