using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class DroidDestroyer : MonoBehaviour
    {
        public Rigidbody _rigidbody;
        public GameObject effect;
        public GameObject[] partsToDestroy;
        public GameObject[] burnEffects;
        public bool deactivateOnly;
        public float destructionDelay;
        public float destructionSpeed;
        public bool addTorque = false;
        public float torqueScale = 1f;
        public ForceMode mode = ForceMode.Force;
        public Material burnMaterial;

        private Material material;

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1);
            
            //make ragdoll play if droid has one
            if(_rigidbody!=null)
                _rigidbody.isKinematic = false;

            material = GetComponentInChildren<MeshRenderer>().material;
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer r in renderers) r.sharedMaterial = material;
            
            //start explosion
            effect.SetActive(true);
            
            //darkening droid
            float burnValue = material.GetFloat("_BurnLevel");
            while (burnValue < 1)
            {
                burnValue += Time.deltaTime * destructionSpeed;
                material.SetFloat("_BurnLevel", burnValue);
                yield return null;
            }
            
            //removing parts and enabling burn particles
            foreach (GameObject go in partsToDestroy)
                go.SetActive(false);

            foreach (GameObject go in burnEffects)
                go.SetActive(true);

            //assigning burn material
            Material burn = Instantiate(burnMaterial);
            foreach (MeshRenderer r in renderers) r.sharedMaterial = burn;

            //removing physics from models
            Joint[] joints = GetComponentsInChildren<Joint>();
            foreach (Joint j in joints)
                Destroy(j);
            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody b in bodies)
                Destroy(b);
            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders) 
                Destroy(c);

            //fading out material
            burnValue = burn.GetFloat("_Visibility");
            while (burnValue > 0)
            {
                burnValue -= Time.deltaTime * 0.25f;
                burn.SetFloat("_Visibility", burnValue);
                yield return null;
            }

            yield return new WaitForSeconds(1f);
            if (deactivateOnly)
                gameObject.SetActive(false);
            else
                Destroy(gameObject);
        }

        //aligning replacement model to droid
        //and adding movement to it same as original has
        public void SetTarget(Transform target)
        {
            gameObject.SetActive(false);
            AlignToTransform(target, transform);

            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();
            Rigidbody _targetRigidbody = target.GetComponent<Rigidbody>();
            if (_rigidbody != null && _targetRigidbody != null)
            {
                _rigidbody.velocity = _targetRigidbody.velocity;
                if (!addTorque)
                    _rigidbody.angularVelocity = _targetRigidbody.angularVelocity;
                else
                    _rigidbody.AddRelativeTorque(Random.onUnitSphere * torqueScale, mode);
            }
            GameObject _objectToDestroy = target.gameObject;
            _objectToDestroy.SetActive(false);
            gameObject.SetActive(true);
        }

        static void AlignToTransform(Transform target, Transform ragdoll)
        {
            Transform[] ragdollJoints = ragdoll.GetComponentsInChildren<Transform>(true);
            Transform[] currentJoints = target.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < ragdollJoints.Length; i++)
            {
                for (int q = 0; q < currentJoints.Length; q++)
                {
                    if (currentJoints[q].name.CompareTo(ragdollJoints[i].name) == 0)
                    {
                        ragdollJoints[i].position = currentJoints[q].position;
                        ragdollJoints[i].rotation = currentJoints[q].rotation;
                        break;
                    }
                }
            }
        }

        //remove debris if player does not see it
        //collider has to be pretty large
        void OnTriggerExit(Collider other)
        {
            if (other.tag == "Player")
                Destroy(gameObject);
        }
    }
}