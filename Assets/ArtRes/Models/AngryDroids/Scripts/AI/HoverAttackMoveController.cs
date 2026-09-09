using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class HoverAttackMoveController : AIBehaviour
    {
        public float targetDistanceMin = 0.3f;

        void Update()
        {
            if (!ai.IsActive())
                ai.Activate(true);

            if (!ai.IsReady()) return;

            Vector3 targetDir = ai.sight.GetPlayerNearestDirection(0.5f);
            Debug.DrawRay(character.transform.position, targetDir);
            
            if (targetDir.magnitude < targetDistanceMin)
                motor.movementDirection *= 0.1f;
            else
                motor.movementDirection = targetDir.normalized;

            if (weapon.IsInRange(player))
                weapon.Fire();
        }
    }
}