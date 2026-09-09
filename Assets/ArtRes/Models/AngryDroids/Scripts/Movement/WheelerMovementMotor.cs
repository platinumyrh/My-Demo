using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class WheelerMovementMotor : FreeMovementMotor
    {
        public float horizontalAxisScale = 0.15f;
        public float verticalAxisScale = 0.33f;

        [SerializeField]
        private Transform rightWheel;
        [SerializeField]
        private Transform leftWheel;
        [SerializeField]
        private float wheelMoveSpeed = 1f;
        [SerializeField]
        private float wheelTurnSpeed = 1f;

        private Vector3 oldPosition;
        private float oldAngle;

        void Start()
        {
            oldPosition = transform.position;
            oldAngle = transform.rotation.eulerAngles.y;
        }

        void Update()
        {
            if (ai.IsReady())
            {
                Vector3 offset = transform.position - oldPosition;
                offset = Vector3.Project(offset, transform.forward);
                float dir = (Vector3.Angle(offset, transform.forward) > 90f ? -1 : 1);

                float angle = transform.rotation.eulerAngles.y - oldAngle;

                rightWheel.Rotate(new Vector3(dir * offset.magnitude * wheelMoveSpeed - angle * wheelTurnSpeed, 0f, 0f), Space.Self);
                leftWheel.Rotate(new Vector3(dir * offset.magnitude * wheelMoveSpeed + angle * wheelTurnSpeed, 0f, 0f), Space.Self);

                oldAngle = transform.rotation.eulerAngles.y;
                oldPosition = transform.position;

                animator.SetFloat("horizontal", rigidbody.angularVelocity.y * horizontalAxisScale);
                animator.SetFloat("vertical", rigidbody.velocity.magnitude * verticalAxisScale);
            }
        }
    }
}