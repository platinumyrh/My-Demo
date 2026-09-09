using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class WheelerAttackMoveController : AIBehaviour
    {
        public float targetDistanceFront = 2.5f;
        public float targetDistanceBack = 1.0f;

        public float targetDistanceMin = 2.0f;
        public float targetDistanceMax = 3.0f;

        private bool inRange = false;
        private float noticeTime = -0.7f;
        private float lostTime = -1f;

        void Update()
        {
            if (!ai.IsActive())
                ai.Activate(true);

            if (!ai.IsReady()) return;

            // For a short moment after noticing player,
            // only look at him but don't walk towards or attack yet.
            if (Time.time < noticeTime + 0.8f)
            {
                motor.movementDirection = Vector3.zero;
                return;
            }

            Vector3 playerDirection = ai.sight.playerDirectionHorizontal;
            Vector3 playerVelocity = ai.sight.playerVelocity;
            
            float playerDist = playerDirection.magnitude;
            playerDirection /= playerDist;

            if (inRange && playerDist > targetDistanceMax)
                inRange = false;
            if (!inRange && playerDist < targetDistanceMin)
                inRange = true;

            if (inRange)
            {
                //if player moving towards droid
                if (Vector3.Angle(playerDirection, playerVelocity) > 160f)
                    motor.movementDirection = playerVelocity.normalized * 0.6f;
                else
                {
                    motor.movementDirection = Vector3.zero;
                }

                weapon.Seek(player);

                if (weapon.IsInRange(player))
                    weapon.Fire();
            }
            else
            {
                if (ai.sight.CanSeePlayer())
                {
                    if (playerDist < targetDistanceMin)
                        motor.movementDirection = -playerDirection;
                    else
                        motor.movementDirection = playerDirection;

                    lostTime = -1f;
                }
                else
                {
                    if (lostTime < 0)
                        lostTime = Time.time;

                    if (Time.time > lostTime + 3f)
                    {
                        lostTime = -1f;
                        ai.OnLostTrack();
                    }
                }
            }
            motor.facingDirection = playerDirection;
        }
    }
}