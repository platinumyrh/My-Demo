
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestSingleton : SingletonAutoMono<TestSingleton>
{
    private void Start()
    {
        //Debug.Log(TestSingleton.GetInstance().name);
    }
}
