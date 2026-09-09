using UnityEngine;

namespace GravityBox.AngryDroids
{
    public class MovementMotor : MonoBehaviour, IMovementMotor
    {
        // The direction the character wants to move in, in world space.
        // The vector should have a length between 0 and 1.
        public Vector3 movementDirection { get; set; }

        // Simpler motors might want to drive movement based on a target purely
        public Vector3 movementTarget { get; set; }

        // The direction the character wants to face towards, in world space.
        public Vector3 facingDirection { get; set; }

        private Rigidbody _rigidbody;
        public new Rigidbody rigidbody 
        {
            get 
            {
                if (_rigidbody == null)
                    _rigidbody = GetComponent<Rigidbody>(); 
                return _rigidbody; 
            }
        }
    }

    public interface IMovementMotor
    {
        Vector3 movementDirection { get; set; }
        Vector3 movementTarget { get; set; }
        Vector3 facingDirection { get; set; }
    }
}