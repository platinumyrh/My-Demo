using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public abstract class AIBehaviour : MonoBehaviour
    {
        public Transform player { get { return ai.player; } }

        public Transform character { get { return ai.character; } }

        public IMovementMotor motor { get { return ai.motor; } }

        public IWeapon weapon { get { return ai.weapon; } }

        private AI _ai;
        public AI ai { get { if (_ai == null) _ai = transform.parent.GetComponentInChildren<AI>(); return _ai; } }
    }
}