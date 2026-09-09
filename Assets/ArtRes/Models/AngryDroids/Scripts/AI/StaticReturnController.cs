using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class StaticReturnController : AIBehaviour
    {
        public float rotationSpeed = 1f;
        [SerializeField]
        private Transform head;
        private Quaternion defaultRotation;

        void Awake() 
        {
            if(head != null)
                defaultRotation = head.localRotation;
        }

        void OnEnable()
        {
            ai.Activate(false);
        }

        void Update()
        {
            if(head != null)
                head.localRotation = Quaternion.Lerp(head.localRotation, defaultRotation, Time.deltaTime * rotationSpeed);
        }
    }
}