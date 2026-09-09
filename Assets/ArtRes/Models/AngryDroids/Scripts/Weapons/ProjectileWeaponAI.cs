using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class ProjectileWeaponAI : WeaponAI, IWeapon
    {
        [SerializeField]
        private GameObject projectilePrefab;
        private SmallObjectPool projectiles;

        public Transform[] spawnPositions;
        public GameObject muzzle;
        public float projectileDelay = 0.3f;

        int _next = 0;

        protected override void OnAwake()
        {
            projectiles = new SmallObjectPool(projectilePrefab);
        }

        public override bool CanFire()
        {
            return !isFiring && !IsCoolingDown();
        }

        protected override IEnumerator FireCoroutine()
        {
            if (muzzle != null)
            {
                muzzle.transform.position = spawnPositions[_next].position;
                muzzle.SetActive(true);
            }
            yield return new WaitForSeconds(projectileDelay);
            GameObject bullet = projectiles.Get();
            bullet.transform.position = spawnPositions[_next].position;
            bullet.transform.rotation = weaponTransform.rotation;
            _next = (_next + 1) % spawnPositions.Length;
            bullet.SetActive(true);
            isFiring = false;
        }
    }
}