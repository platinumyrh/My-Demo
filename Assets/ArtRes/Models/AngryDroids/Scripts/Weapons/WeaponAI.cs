using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public interface IUnityObject
    {
        GameObject gameObject { get; }
        Transform transform { get; }
    }

    public interface IWeapon : IUnityObject
    {
        float fov { get; }
        float rangeMin { get; }
        float rangeMax { get; }

        bool CanFire();
        bool IsInRange(Transform target);
        void Fire();
        void Seek(Transform target);
    }

    public class WeaponAI : MonoBehaviour, IWeapon
    {
        public Transform weaponTransform;

        public float cooldown = 2f;

        //seeking target
        public bool seek;
        public float seekSpeed;
        public float seekMaxAngle;

        [SerializeField]
        private Animator _animator;

        [SerializeField]
        private float _rangeMax = 1000f;
        public float rangeMax { get { return _rangeMax; } }
        
        [SerializeField]
        private float _rangeMin = 1f;
        public float rangeMin { get { return _rangeMin; } }
        
        [SerializeField]
        private float _fov = 180;
        public float fov { get { return _fov; } }
        
        private bool _isFiring = false;
        public bool isFiring { get { return _isFiring; } set { _isFiring = value; } }

        private float _lastFireTime;
        
        void Awake()
        {            
            if (weaponTransform == null)
                weaponTransform = transform;

            _lastFireTime = -cooldown;
            OnAwake();
        }

        protected virtual void OnAwake() { }

        public virtual bool IsInRange(Transform target)
        {
            return Sight.IsVisible(target.GetComponent<Collider>().bounds, weaponTransform, fov, rangeMin, rangeMax);
        }

        public virtual bool CanFire()
        {
            return !_isFiring;
        }

        public bool IsCoolingDown() 
        {
            return (_lastFireTime + cooldown > Time.time);
        }

        public virtual void Fire()
        {
            if (IsCoolingDown() || !CanFire()) return;

            _isFiring = true;
            _lastFireTime = Time.time;

            if(_animator!=null)
                _animator.SetTrigger("fire");

            StartCoroutine(FireCoroutine());
        }

        public virtual void Seek(Transform target)
        {
            if (!seek) return;

            Vector3 targetVector = target.GetComponent<Collider>().bounds.center;
            targetVector = targetVector - weaponTransform.position;
            Quaternion targetRotation = Quaternion.LookRotation(targetVector);
            float angle = targetRotation.eulerAngles.x;
            angle = angle > 180f ? Mathf.Clamp(angle, (360f-seekMaxAngle), 360f) : Mathf.Clamp(angle, 0, seekMaxAngle);
            targetRotation = Quaternion.Euler(new Vector3(angle, 0, 0));
            weaponTransform.localRotation = targetRotation;
        }

        protected virtual IEnumerator FireCoroutine() 
        {
            yield return null;
            _isFiring = false;
        }
    }
}