using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class ComponentHealth : MonoBehaviour, IHealth
    {
        public float maxHealth = 100.0f;
        public float health = 100.0f;
        public bool useMaterial = false;
        
        private Material material;
        private float damageUpdateSpeed = 10;
        private Coroutine materialDamageCoroutine;

        public bool Dead { get { return health <= 0; } set { health = value ? 0 : 100; } }

        void Awake()
        {
            if (useMaterial)
            {
                material = GetComponentInChildren<MeshRenderer>().material;
                MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer r in renderers) r.sharedMaterial = material;
            }
        }

        public virtual void OnDamage(DamageInfo damage)
        {
            if (damage.amount <= 0)
                return;

            health -= damage.amount;

            if (materialDamageCoroutine != null)
                StopCoroutine(materialDamageCoroutine);

            materialDamageCoroutine = StartCoroutine(DamageMaterialCoroutine(damage.amount));

            if (health <= 0)
            {
                health = 0;
                enabled = false;

                OnDeath();
            }

            if (damage.type != DamageType.Explosion)
                SendMessageUpwards("OnComponentDamage", damage, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void OnDeath() 
        {
            SendMessageUpwards("OnComponentDeath");
        }

        IEnumerator DamageMaterialCoroutine(float damage)
        {
            if (!useMaterial) yield break;

            Color damageColor = material.GetColor("_DamageColor");
            while (damageColor.a < 1)
            {
                damageColor.a += damageUpdateSpeed * Time.deltaTime;
                material.SetColor("_DamageColor", damageColor);
                yield return null;
            }

            yield return null;

            while (damageColor.a > 0)
            {
                damageColor.a -= damageUpdateSpeed / 2f * Time.deltaTime;
                material.SetColor("_DamageColor", damageColor);
                yield return null;
            }
        }
    }
}