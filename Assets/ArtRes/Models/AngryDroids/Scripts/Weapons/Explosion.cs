using UnityEngine;
using GravityBox.AngryDroids;
using System.Collections;

public class Explosion : MonoBehaviour {

    public float effectRadius = 5f;
    public float effectDamage = 5f;
    public float effectForce = 5f;
    public LayerMask effectLayers;

    private Collider[] hits;
    private Rigidbody targetRigidbody;
    private IHealth targetHealth;

    void OnEnable() 
    {
        hits = Physics.OverlapSphere(transform.position, effectRadius, effectLayers.value);

        foreach (Collider c in hits)
        {
            // Don't collide with triggers
            if (c.isTrigger)
                continue;

            // calculate distance factor
            Vector3 effectDir = c.transform.position - transform.position;
            float damageScale = Mathf.InverseLerp(effectRadius*effectRadius, 0, effectDir.sqrMagnitude);

            //apply damage to any health within radius
            targetHealth = c.GetComponent(typeof(IHealth)) as IHealth;
            if (targetHealth != null)
            {               
                targetHealth.OnDamage(new DamageInfo(DamageType.Explosion, effectDamage * damageScale, -transform.forward));
            }

            //apply impulse inside radius
            targetRigidbody = c.GetComponent<Rigidbody>();
            if (targetRigidbody != null)
            {
                Vector3 force = effectDir * damageScale * effectForce;
                force.y = 0;
                targetRigidbody.AddForce(force, ForceMode.Impulse);
            }
        }
    }
}
