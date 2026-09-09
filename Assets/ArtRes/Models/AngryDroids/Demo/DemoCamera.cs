using UnityEngine;
using System.Collections;
using GravityBox.AngryDroids;

public class DemoCamera : MonoBehaviour {

    public Transform target = null;
    
    private MovementMotor motor;
    private Vector3 offset;
    private RaycastHit hit;

    void Start () {
        offset = transform.position - target.position;
        motor = target.GetComponent<MovementMotor>();
	}
	
	void FixedUpdate () {
        transform.position = Vector3.Lerp(transform.position, target.position + offset, Time.deltaTime * 5f);
	}

    void Update()
    {
#if UNITY_EDITOR
        Vector3 input = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        input = transform.TransformDirection(input);
        input.y = 0;
        motor.movementDirection = input.normalized;
#else
#if UNITY_IPHONE || UNITY_ANDROID || UNITY_WP8 || UNITY_WP_8_1 || UNITY_BLACKBERRY || UNITY_TIZEN
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                if (Physics.Raycast(Camera.main.ScreenPointToRay(touch.position), out hit))
                {
                    Vector3 position = hit.point;
                    position.y = 0;
                    motor.movementDirection = (position - target.position).normalized;
                }
            }
        }
#endif
#endif
    }
}
