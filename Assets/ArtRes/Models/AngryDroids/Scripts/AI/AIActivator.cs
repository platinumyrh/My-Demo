using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class AIActivator : MonoBehaviour
    {
        GameObject targetCharacter;
        SphereCollider sphereCollider;
        float activeRadius;

        void OnEnable()
        {
            if (targetCharacter == null)
            {
                targetCharacter = transform.parent.gameObject;
                sphereCollider = GetComponent<SphereCollider>();
                activeRadius = sphereCollider.radius;
            }

            bool playerInside = false;
            Collider[] colliders = Physics.OverlapSphere(sphereCollider.center, sphereCollider.radius);
            foreach (Collider c in colliders)
                if (c.tag == "Player")
                    playerInside = true;
            
            if(!playerInside)
                Deactivate();
        }

        void OnDisable()
        {
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.tag == "Player" && targetCharacter.transform.parent == transform)
            {
                Activate();
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other.tag == "Player")
            {
                Deactivate();
            }
        }

        public void Activate()
        {
            //attach AI to level 
            targetCharacter.transform.parent = transform.parent;
            targetCharacter.SetActive(true);
            //attach activator to AI
            transform.parent = targetCharacter.transform;
            sphereCollider.radius = activeRadius * 1.1f;
        }

        void Deactivate()
        {
            //attach activator to level
            transform.parent = targetCharacter.transform.parent;
            //make activator AI's parent
            targetCharacter.transform.parent = transform;
            targetCharacter.SetActive(false);
            sphereCollider.radius = activeRadius;
        }
    }
}