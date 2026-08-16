using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// 事件总线类，负责管理事件的订阅、取消订阅和发布
/// </summary>
public class GameEventBus :BaseManager<GameEventBus>
{
    private Dictionary<GameEventType, List<Delegate>> eventListeners = new Dictionary<GameEventType, List<Delegate>>();

    //订阅事件 把观察者的回调函数添加到事件字典中
    public void Subscribe<T>(GameEventType eventType,Action<T> listener)//事件类型 回调函数 通过GameEventType和泛型参数T来指定事件类型和回调函数的参数类型
        where T : GameEventData
    {
        if (!eventListeners.ContainsKey(eventType))
        {
            eventListeners[eventType] = new List<Delegate>();//如果事件类型不存在，则创建一个新的回调列表
        }
        eventListeners[eventType].Add(listener);//将回调函数添加到事件类型对应的回调列表中
    }

    //取消订阅事件 从事件字典中移除观察者的回调函数
    public void Unsubscribe<T>(GameEventType eventType, Action<T> listener) 
        where T : GameEventData
    {
        if (eventListeners.ContainsKey(eventType))
        {
            eventListeners[eventType].Remove(listener);//如果事件类型存在，则从回调列表中移除指定的回调函数
        }
    }


    //发布事件 触发事件字典中对应事件类型的所有回调函数(含参)
    public void Publish<T>(GameEventType eventType, T eventData) 
        where T : GameEventData
    {
        if (eventListeners.TryGetValue(eventType, out var listeners))//如果事件类型存在，则获取对应的回调列表
        {
            foreach (var listener in listeners)
            {
                (listener as Action<T>)?.Invoke(eventData);//遍历回调列表，调���每个回调函数，并传递事件数据作为参数
            }
        }
    }

    // 简化版发布（无参数事件）
    public void Publish(GameEventType eventType)
    {
        if (eventListeners.TryGetValue(eventType, out var listeners))//如果事件类型存在，则获取对应的回调列表
        {
            foreach (var listener in listeners)
            {
                (listener as Action)?.Invoke();//遍历回调列表，调用每个回调函数
            }
        }
    }
}
