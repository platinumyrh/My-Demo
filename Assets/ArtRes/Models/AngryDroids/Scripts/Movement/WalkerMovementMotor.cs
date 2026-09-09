using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class WalkerMovementMotor : FreeMovementMotor
    {
        public AnimationCurve horizontalScaleCurve;
        public float horizontalAxisScale = 0.15f;
        public float verticalAxisScale = 0.33f;
        public float jumpForce;

        private bool grounded;

        void Update()
        {
            if (ai.IsReady())
            {
                animator.SetFloat("turn", horizontalScaleCurve.Evaluate(rigidbody.angularVelocity.y));// rigidbody.angularVelocity.y * horizontalAxisScale);
                Vector3 localVelocity = transform.InverseTransformDirection(rigidbody.velocity);
                animator.SetFloat("vertical", localVelocity.z * verticalAxisScale);
                animator.SetFloat("horizontal", localVelocity.x * horizontalAxisScale);
            }
        }

        public void DoJump()
        {
            rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            grounded = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!grounded)
            {
                animator.SetTrigger("land");
                grounded = true;
            }
        }
    }
}