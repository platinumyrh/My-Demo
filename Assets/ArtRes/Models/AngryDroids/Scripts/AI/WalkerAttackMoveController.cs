using UnityEngine;

namespace GravityBox.AngryDroids
{
    public class WalkerAttackMoveController : AIBehaviour
    {
        public float targetDistanceMin = 2.0f;
        public float targetDistanceMax = 3.0f;

        private bool inRange = false;
        private float noticePlayerTime = -0.7f;
        private float lostPlayerTime = -1f;

        public delegate void UpdateAction();
        public UpdateAction onUpdate;

        void OnEnable()
        {
            inRange = false;
            noticePlayerTime = Time.time;
            if (ai.SupportsNavMesh())
                onUpdate = NavigationNavMesh;
            else
                onUpdate = NavigationSimple;
        }

        void Update()
        {
            if (!ai.IsActive())
                ai.Activate(true);

            if (!ai.IsReady()) return;

            onUpdate();
            //// For a short moment after noticing player,
            //// only look at him but don't walk towards or attack yet.
            //if (Time.time < noticePlayerTime + 0.8f)
            //{
            //    motor.movementDirection = Vector3.zero;
            //    return;
            //}

            //// Calculate the direction from the player to this character
            //Vector3 playerDirection = ai.sight.playerDirectionHorizontal;
            //Vector3 playerVelocity = ai.sight.playerVelocity;
            //float playerDist = playerDirection.magnitude;
            //playerDirection /= playerDist;

            //if (inRange && playerDist > targetDistanceMax)
            //    inRange = false;
            //if (!inRange && playerDist < targetDistanceMin)
            //    inRange = true;

            //if (inRange)
            //{
            //    //if player moving towards droid
            //    if (Vector3.Angle(playerDirection, playerVelocity) > 160f)
            //        motor.movementDirection = playerVelocity.normalized * 0.6f;
            //    else
            //    {
            //        //if player is about to aim strafe
            //        if (Vector3.Angle(player.forward, playerDirection) > 160f)
            //            motor.movementDirection = transform.right * (Mathf.PingPong(Time.time, 2.0f) - 1f);
            //        else
            //            motor.movementDirection = Vector3.zero;
            //    }

            //    if (weapon.IsInRange(player))
            //        weapon.Fire();
            //}
            //else
            //{
            //    if (ai.sight.CanSeePlayer())
            //    {
            //        if (playerDist < targetDistanceMin)
            //            motor.movementDirection = playerDirection.normalized * 0.6f;
            //        else
            //            motor.movementDirection = playerDirection;

            //        lostPlayerTime = -1f;
            //    }
            //    else
            //    {
            //        if(lostPlayerTime < 0)
            //            lostPlayerTime = Time.time;

            //        if (Time.time > lostPlayerTime + 3f)
            //        {
            //            lostPlayerTime = -1f;
            //            ai.OnLostTrack();
            //        }
            //    }
            //}

            //// Set this character to face the player,
            //// that is, to face the direction from this character to the player
            //motor.facingDirection = playerDirection;

            if (ai.sight.IsPlayerDead())
                ai.OnLostTrack();
        }

        void NavigationNavMesh() 
        {
            // For a short moment after noticing player,
            // only look at him but don't walk towards or attack yet.
            if (Time.time < noticePlayerTime + 0.8f)
            {
                return;
            }

            // Calculate the direction from the player to this character
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
                    //motor.movementDirection = playerVelocity.normalized * 0.6f;
                    ai.agent.Move(playerVelocity.normalized * 0.6f);//.SetDestination(ai.sight.player.position);
                else
                {
                    //if player is about to aim strafe
                    if (Vector3.Angle(player.forward, playerDirection) > 160f)
                        //motor.movementDirection = transform.right * (Mathf.PingPong(Time.time, 2.0f) - 1f);
                        ai.agent.Move(transform.right * (Mathf.PingPong(Time.time, 2.0f) - 1f));//.SetDestination(transform.right * (Mathf.PingPong(Time.time, 2.0f) - 1f));
                    else
                        //motor.movementDirection = Vector3.zero; //stop
                        ai.agent.Move(Vector3.zero);//.Stop();
                }

                if (weapon.IsInRange(player))
                    weapon.Fire();
            }
            else
            {
                if (ai.sight.CanSeePlayer())
                {
                    if (playerDist < targetDistanceMin)
                    {
                        //motor.movementDirection = playerDirection.normalized * 0.6f;
                        ai.agent.Move(playerDirection.normalized * 0.6f);//.SetDestination(ai.sight.player.position);
                        //ai.agent.speed *= 0.6f;
                    }
                    else
                    {
                        //motor.movementDirection = playerDirection;
                        ai.agent.Move(playerDirection);//.SetDestination(ai.sight.player.position);

                    }

                    lostPlayerTime = -1f;
                }
                else
                {
                    if (lostPlayerTime < 0)
                        lostPlayerTime = Time.time;

                    if (Time.time > lostPlayerTime + 3f)
                    {
                        lostPlayerTime = -1f;
                        ai.OnLostTrack();
                    }
                }
            }

            // Set this character to face the player,
            // that is, to face the direction from this character to the player
            motor.facingDirection = playerDirection;
        }

        void NavigationSimple() 
        {
            // For a short moment after noticing player,
            // only look at him but don't walk towards or attack yet.
            if (Time.time < noticePlayerTime + 0.8f)
            {
                motor.movementDirection = Vector3.zero;
                return;
            }

            // Calculate the direction from the player to this character
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
                    //if player is about to aim strafe
                    if (Vector3.Angle(player.forward, playerDirection) > 160f)
                        motor.movementDirection = transform.right * (Mathf.PingPong(Time.time, 2.0f) - 1f);
                    else
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
                        motor.movementDirection = playerDirection.normalized * 0.6f;
                    else
                        motor.movementDirection = playerDirection;

                    lostPlayerTime = -1f;
                }
                else
                {
                    if (lostPlayerTime < 0)
                        lostPlayerTime = Time.time;

                    if (Time.time > lostPlayerTime + 3f)
                    {
                        lostPlayerTime = -1f;
                        ai.OnLostTrack();
                    }
                }
            }

            // Set this character to face the player,
            // that is, to face the direction from this character to the player
            motor.facingDirection = playerDirection;
        }
    }
}