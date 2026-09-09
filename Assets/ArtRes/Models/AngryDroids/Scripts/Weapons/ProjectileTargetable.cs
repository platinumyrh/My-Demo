using UnityEngine;

namespace GravityBox.AngryDroids
{
    public class ProjectileTargetable : Projectile
    {
        public float acceleration = 1f;
        public float seekPrecision = 1.3f;
        public float noise = 0.0f;

        public float effectRadius = 5f;
        public float effectDamage = 5f;
        public float effectForce = 5f;

        private Vector3 dir;
        [SerializeField]
        private GameObject targetObject;
        private Collider targetCollider;
        private float currentSpeed;

        protected override void OnEnable () 
        {
            base.OnEnable();

	        dir = transform.forward;
            currentSpeed = 0f;

            targetObject = GameObject.FindWithTag("Player");
            if(targetObject!=null)
                targetCollider = targetObject.GetComponent<Collider>();
        }

        protected override void Move () 
        {
            if (targetObject != null)
            {
                //move like a homing missile
                Vector3 targetPos = targetCollider.bounds.center;
                targetPos += transform.right * (Mathf.PingPong(Time.time, 1.0f) - 0.5f) * noise;
                Vector3 targetDir = (targetPos - tr.position);
                float targetDist = targetDir.magnitude;
                targetDir /= targetDist;

                if(targetDist < 1f)
                    targetDir += Vector3.down * 0.25f;

                int precisionScale = Vector3.Angle(transform.forward, targetDir) > 90f ? 1 : 2;
                dir = Vector3.Slerp(dir, targetDir, Time.deltaTime * seekPrecision * precisionScale);
            
                tr.rotation = Quaternion.LookRotation(dir);
                currentSpeed = Mathf.Lerp(currentSpeed, speed, Time.deltaTime*acceleration);
                tr.position += dir * currentSpeed * Time.deltaTime;
                lifeDistance += currentSpeed * Time.deltaTime;
            }
            else
            {
                //move like a simple missile
                base.Move();
            }
        }
    }
}