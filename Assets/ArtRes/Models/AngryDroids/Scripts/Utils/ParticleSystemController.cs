using UnityEngine;
using System.Collections;

namespace GravityBox.Utils
{
    public class ParticleSystemController : MonoBehaviour 
    {
        public float checkFrequency = 0.5f;
	    public bool OnlyDeactivate;

        void OnEnable() { StartCoroutine("CheckIfAlive"); }
	
	    IEnumerator CheckIfAlive ()
	    {
            while (true)
            {
                yield return new WaitForSeconds(checkFrequency);
                if (!GetComponent<ParticleSystem>().IsAlive(true))
                {
                    if (OnlyDeactivate)
                        this.gameObject.SetActive(false);
                    else
                        GameObject.Destroy(this.gameObject);
                    yield break;
                }
            }
	    }
    }
}