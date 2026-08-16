using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// ui对象池，专门用来存放ui对象的对象池
/// </summary>
public class UIPool :BasePool<RectTransform>
{
    public string prefabPath { get; private set; }
    public Transform parentTransform { get; set; }

    public UIPool(string prefabPath, Transform parent = null, int maxSize = 10)
        : base(null, null, null, null, maxSize)
    {
        this.prefabPath = prefabPath;
        this.parentTransform = parent;

        createFunc = CreateUI;
        onGet = OnGetUI;
        onRelease = OnReleaseUI;
        onDestroy = OnDestroyUI;
    }

    private RectTransform CreateUI()
    {
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到UI预制体: {prefabPath}");
            return null;
        }

        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = prefab.name;

        if (parentTransform != null)
        {
            obj.transform.SetParent(parentTransform);
        }

        RectTransform rect = obj.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        return rect;
    }

    private void OnGetUI(RectTransform ui)
    {
        ui.gameObject.SetActive(true);
    }

    private void OnReleaseUI(RectTransform ui)
    {
        ui.gameObject.SetActive(false);

        if (parentTransform != null)
        {
            ui.SetParent(parentTransform);
        }
    }

    private void OnDestroyUI(RectTransform ui)
    {
        GameObject.Destroy(ui.gameObject);
    }
}
