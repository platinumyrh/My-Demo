using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class DroidHealth : Health
    {
        public GameObject debris;

        private AI _ai;
        public AI ai { get { if (_ai == null) _ai = GetComponentInChildren<AI>(); return _ai; } }

        protected override void OnDeath()
        {
            GameObject d = Instantiate(debris);
            d.transform.position = transform.position;
            d.transform.rotation = transform.rotation;
            d.GetComponent<DroidDestroyer>().SetTarget(transform);
        }

        public override void OnDamage(DamageInfo damage)
        {
            if (!ai.IsReady()) return;

            base.OnDamage(damage);
            
            if (!dead)
                ai.OnSpotted();
        }
    }
}