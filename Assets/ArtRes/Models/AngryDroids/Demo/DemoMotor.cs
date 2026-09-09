using UnityEngine;
using System.Collections;
using GravityBox.AngryDroids;

public class DemoMotor : MovementMotor {

    public float walkingSpeed = 5.0f;
    public float walkingSnappyness = 50f;

    void FixedUpdate()
    {
        // Handle the movement of the character
        Vector3 targetVelocity = movementDirection * walkingSpeed;
        Vector3 deltaVelocity = targetVelocity - rigidbody.velocity;
        if (rigidbody.useGravity)
            deltaVelocity.y = 0;
        rigidbody.AddForce(deltaVelocity * walkingSnappyness, ForceMode.Acceleration);
        Debug.DrawRay(transform.position, movementDirection, Color.blue);
    }
}
