using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class Projectile : MonoBehaviour
    {
        public float speed = 10f;
        public float lifeTime = 0.5f;
        public float dist = 10000f;
        public float damageAmount = 5f;
        public float forceAmount = 5f;
        public float radius = 1.0f;
        public LayerMask ignoreLayers;
        public GameObject explosionPrefab;
        public GameObject particleEffect;

        protected float spawnTime = 0.0f;
        protected Transform tr;
        protected Renderer rn;
        protected float lifeDistance = 0;

        private bool collided = false;
        private Collider[] hits;
        private Collider hit;
        private ParticleSystem[] particles;
        private RaycastHit raycast;

        //private static CreateQuad shotMark;

        protected virtual void OnEnable()
        {
            //if (shotMark == null) shotMark = FindObjectOfType<CreateQuad>();

            tr = transform;
            rn = GetComponent<Renderer>();
            spawnTime = Time.time;
            lifeDistance = 0;
            collided = false;
            hit = null;

            if(rn != null) rn.enabled = true;
            particleEffect.SetActive(true);
            particles = particleEffect.GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem p in particles) p.loop = true;
        }

        void Update()
        {
            if (collided) return;

            Move();

            HandleCollision();

            if (!collided && (Time.time > spawnTime + lifeTime || lifeDistance > dist))
                StartCoroutine(Explode());
        }
        
        protected virtual void Move()
        {
            tr.position += tr.forward * speed * Time.deltaTime;
            lifeDistance += speed * Time.deltaTime;
        }

        protected void HandleCollision()
        {
            //skip collision check for 1 meter 
            //or projectile will blowup weapon owner when fired 
            if (lifeDistance < 1f) return;
            collided = false;
            
            hits = Physics.OverlapSphere(tr.position, radius, ~ignoreLayers.value);

            foreach(Collider c in hits)
            {
                // Don't collide with triggers
                if(c.isTrigger)
                    continue;

                hit = c;
                collided = true;
            }

            if (collided)
                StartCoroutine(Explode());
        }

        private IEnumerator Explode()
        {
            collided = true;
            if (rn != null) rn.enabled = false;
            foreach (ParticleSystem p in particles) p.loop = false;

            GameObject expl;
            if (Physics.Raycast(transform.position, transform.forward, out raycast, radius, ~ignoreLayers.value))
            {
                expl = (GameObject)Instantiate(explosionPrefab, raycast.point, Quaternion.LookRotation(raycast.normal));
                //shotMark.target = expl.transform;
                //shotMark.emit = true;    
            }
            else
                expl = (GameObject)Instantiate(explosionPrefab, transform.position, transform.rotation);
            
            if(hit!=null)
                expl.transform.parent = hit.transform;

            while (particleEffect.activeInHierarchy)
                yield return null;

            gameObject.SetActive(false);
        }
    }
}