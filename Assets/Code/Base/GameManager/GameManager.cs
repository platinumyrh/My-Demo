﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

public class GameManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        print(Application.persistentDataPath);
        NetworkManager.Instance.Connect();
      
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ResLoad.GetInstance().Instantiate("test/TestCube");
        }
    }
}
