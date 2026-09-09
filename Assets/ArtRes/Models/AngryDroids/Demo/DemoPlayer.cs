using UnityEngine;
using System.Collections;
using GravityBox.AngryDroids;

public class DemoPlayer : MonoBehaviour {

    private MovementMotor motor;

    void Start() 
    {
        motor = GetComponent<MovementMotor>();
    }

	void Update () 
    {
        Vector3 input = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        input = transform.TransformDirection(input);
        input.y = 0;
        motor.movementDirection = input.normalized;
	}
}
