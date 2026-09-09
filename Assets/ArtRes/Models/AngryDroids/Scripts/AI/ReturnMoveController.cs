using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class ReturnMoveController : AIBehaviour
    {
        public PatrolRoute patrolRoute;
        
        public delegate void UpdateAction();
        public UpdateAction onUpdate;

        private const float patrolPointRadius = 0.5f;
        private int nextPatrolPoint = 0;
        private int patrolDirection = 1;
        
        void OnEnable() 
        {
            if (ai.SupportsNavMesh())
            {
                ai.agent.SetDestination(ai.spawnPos);
                onUpdate = NavigationNavMesh;
            }
            else
            {
                if (patrolRoute != null)
                    onUpdate = NavigationPatrolSimple;
                else
                    onUpdate = NavigationSimple;
            }
        }

        void Update()
        {
            if (!ai.IsActive())
                ai.Activate(true);

            if (!ai.IsReady()) return;

            onUpdate();
        }

        void NavigationNavMesh() 
        {
            if (ai.agent.remainingDistance < ai.agent.stoppingDistance)
            {
                ai.Activate(false);
                enabled = false;
            }
        }

        void NavigationSimple() 
        {
            Vector3 movementDirection = ai.spawnPos - character.position;
            movementDirection.y = 0;
            motor.movementDirection = movementDirection;

            if (motor.movementDirection.sqrMagnitude > 1)
                motor.movementDirection = motor.movementDirection.normalized;

            if (motor.movementDirection.sqrMagnitude < 0.01f)
            {
                motor.movementTarget = Vector3.zero;
                motor.movementDirection = Vector3.zero;
                ai.Activate(false);
                enabled = false;
            }
        }

        void NavigationPatrolSimple() 
        {
            // Early out if there are no patrol points
            if (patrolRoute == null || patrolRoute.points.Count == 0)
                return;

            // Find the vector towards the next patrol point.
            Vector3 targetVector = patrolRoute.GetPoint(nextPatrolPoint) - character.position;
            targetVector.y = 0;

            // If the patrol point has been reached, select the next one.
            if (targetVector.sqrMagnitude < patrolPointRadius * patrolPointRadius)
            {
                nextPatrolPoint += patrolDirection;
                if (nextPatrolPoint < 0)
                {
                    nextPatrolPoint = 1;
                    patrolDirection = 1;
                }
                if (nextPatrolPoint >= patrolRoute.points.Count)
                {
                    if (patrolRoute.pingPong)
                    {
                        patrolDirection = -1;
                        nextPatrolPoint = patrolRoute.points.Count - 2;
                    }
                    else
                    {
                        nextPatrolPoint = 0;
                    }
                }
            }

            // Make sure the target vector doesn't exceed a length if one
            if (targetVector.sqrMagnitude > 1)
                targetVector.Normalize();

            // Set the movement direction.
            motor.movementDirection = targetVector;
            // Set the facing direction.
            motor.facingDirection = targetVector;
        }
    }
}