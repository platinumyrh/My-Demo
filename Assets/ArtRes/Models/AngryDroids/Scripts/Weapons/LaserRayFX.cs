using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public abstract class ScriptedAnimation : MonoBehaviour
    {
        public float animationTime = 0f;
        public OnAnimationEnd onAnimationEnd;

        float time = 0;
        float normalizedTime = 0;

        public enum OnAnimationEnd
        {
            DoNothing,
            Loop,
            DisableScript,
            HideGameObject,
            DestroyGameObject
        }

        void Awake() 
        {
            OnAwake();
        }

        protected virtual void OnAwake() { }

        void OnEnable()
        {
            time = 0;
            OnEnableScript();
        }

        protected virtual void OnEnableScript() { }

        void OnDisable()
        {
            OnDisableScript();
        }

        protected virtual void OnDisableScript() { }

        void OnDestroy() 
        {
            OnDestroyGameObject();
        }

        protected virtual void OnDestroyGameObject() { }

        void Update()
        {
            //handle time
            time += Time.deltaTime;
            normalizedTime = time / animationTime;

            //update effect
            UpdateAnimation(normalizedTime);          

            if (normalizedTime > 1)
            {
                switch (onAnimationEnd)
                {
                    case OnAnimationEnd.Loop:
                        time = 0;
                        break;

                    case OnAnimationEnd.DisableScript:
                        enabled = false;
                        break;

                    case OnAnimationEnd.HideGameObject:
                        gameObject.SetActive(false);
                        break;

                    case OnAnimationEnd.DestroyGameObject:
                        Destroy(gameObject);
                        break;

                    default:
                        break;
                }
            }
        }

        protected virtual void UpdateAnimation(float normalizedTime) { }
    }

    public class LaserRayFX : ScriptedAnimation
    {
        public Gradient startColor;
        public Gradient endColor;
        public AnimationCurve lengthAnimation;
        public ParticleSystem laserTipParticle;
        public Light laserMainLight;

        public float length = 10f;
        public float width = 0.35f;

        public float damage;
        public float damageInterval;
        public GameObject hitPrefab;

        private float nextDamageTime = 0;
        private LineRenderer ren;
        private Material mat;
        private GameObject hitEffect;
        //private LaserRayTrail trail;
        private IHealth lastHealth;

        public RaycastHit hit { get { return _hit; } set { hit = value; } }
        RaycastHit _hit;

        protected override void OnEnableScript()
        {
            if (ren == null)
                ren = GetComponent<LineRenderer>();
            if (mat == null)
                mat = ren.material;
            //if (trail == null)
            //trail = GetComponent<LaserRayTrail>();

            laserTipParticle.gameObject.SetActive(true);

            ren.SetColors(startColor.Evaluate(0), endColor.Evaluate(0));

            UpdateRay();
            //trail.emit = true;

            lastHealth = null;
            nextDamageTime = Time.time;
        }

        protected override void OnDisableScript()
        {
            hitEffect = null;
            //trail.emit = false;
            laserTipParticle.gameObject.SetActive(false);
        }

        protected override void OnDestroyGameObject()
        {
            Destroy(mat);
        }

        protected override void UpdateAnimation(float normalizedTime)
        {
            UpdateRay();
            ren.SetColors(startColor.Evaluate(normalizedTime), endColor.Evaluate(normalizedTime));
            ren.SetWidth(width * lengthAnimation.Evaluate(normalizedTime), width * lengthAnimation.Evaluate(normalizedTime));
            //trail.color = endColor.Evaluate(normalizedTime);

            laserMainLight.color = startColor.Evaluate(normalizedTime);
        }

        void UpdateRay()
        {
            Vector3 endPosition;

            if (Physics.Raycast(transform.position, transform.forward, out _hit))
            {
                endPosition = new Vector3(0, 0, _hit.distance);

                if (_hit.collider != null)
                    HandleDamage(_hit.collider);

                if (hitEffect == null)
                {
                    hitEffect = Instantiate(hitPrefab, _hit.point, Quaternion.LookRotation(_hit.normal, Vector3.up)) as GameObject;
                }
                else
                {
                    hitEffect.transform.position = _hit.point;
                    hitEffect.transform.rotation = Quaternion.LookRotation(_hit.normal, Vector3.up);
                }

                hitEffect.transform.parent = _hit.transform;
            }
            else
            {
                endPosition = new Vector3(0, 0, 100f);
                hitEffect = null;
            }

            ren.SetPosition(1, endPosition);
            laserTipParticle.transform.localPosition = endPosition;
        }

        void HandleDamage(Collider body)
        {
            lastHealth = body.GetComponent(typeof(IHealth)) as IHealth;
            if (lastHealth != null && Time.time > nextDamageTime)
            {
                lastHealth.OnDamage(new DamageInfo(DamageType.Laser, damage, transform.forward));
                nextDamageTime = Time.time + damageInterval;
            }
        }
    }
}