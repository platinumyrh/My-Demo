using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class StaticAttackController : AIBehaviour
    {
        public bool lockVerticalRotation = true;
        public float rotationSpeed = 1f;
        public float rotationAngle = 360;
        [SerializeField]
        private Transform head;

        void OnEnable()
        {
            ai.Activate(true);
        }

        void Update()
        {
            if (ai.IsReady())
            {
                Vector3 enemyDirection = lockVerticalRotation ? ai.sight.playerDirectionHorizontal : ai.sight.playerDirection;

                Quaternion newRotation = Quaternion.LookRotation(Vector3.Lerp(head.forward, enemyDirection, Time.deltaTime * rotationSpeed), Vector3.up);
                if (Quaternion.Angle(transform.rotation, newRotation) < rotationAngle / 2f)
                    head.rotation = newRotation;

                if (weapon.IsInRange(player))
                    weapon.Fire();
            }
        }
    }
}