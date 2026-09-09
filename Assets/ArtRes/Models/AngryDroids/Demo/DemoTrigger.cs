using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class DemoTrigger : MonoBehaviour {

    public UnityEvent onEnter;
    public UnityEvent onExit;
    public bool triggeredOnce = true;
    private bool triggered;
    
    void OnTriggerEnter(Collider other)
    {
        if (other.tag != "Player" || triggered) return;

        if(onEnter!=null)
            onEnter.Invoke();

        triggered = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.tag != "Player") return;

        if(onExit!=null)
            onExit.Invoke();

        if (!triggeredOnce)
            triggered = false;
    }
}
