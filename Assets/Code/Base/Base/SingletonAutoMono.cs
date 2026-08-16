using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;

    public static T GetInstance()
    {
        if (instance == null)
        {
            GameObject obj =new GameObject(typeof(T).Name);
            instance = obj.AddComponent<T>();
           //Instantiate(obj);
           GameObject.DontDestroyOnLoad(obj);
        }


        return instance;
    }

    
}
