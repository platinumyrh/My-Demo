using UnityEngine;

namespace GravityBox.AngryDroids
{
    public class AISight : MonoBehaviour
    {
        public Transform sight;

        public float FOV = 110f;
        public float rangeMax = 1000f;
        public float rangeMin = 0f;
        public float hearingRange = 30;

        private Collider playerCollider;
        private Rigidbody playerRigidbody;
        private IHealth playerHealth;
        private RaycastHit hit;
        private Transform _player;
        public Transform player
        {
            get { return _player; }
            set { _player = value; }
        }
        
        public bool playerVisible
        {
            get
            {
                if(!Sight.IsVisible(playerCollider.bounds, sight, FOV, rangeMin, rangeMax))
                    return false;

                Vector3 playerDirection = (_player.position - sight.position);
                Physics.Raycast(sight.position, playerDirection, out hit, playerDirection.magnitude);
                if (hit.collider && hit.collider.transform == _player)
                    return true;

                return false;
            }
        }
        public Vector3 playerCenter { get { return playerCollider.bounds.center; } }
        public Vector3 playerDirection { get { return playerCollider.bounds.center - sight.position; } }
        public Vector3 playerDirectionHorizontal 
        { 
            get 
            {
                Vector3 direction = playerDirection;
                direction.y = 0;
                return direction;
            }
        }
        public Vector3 playerVelocity { get { return playerRigidbody.velocity; } }
        
        public Vector3 targetPosition { get; set; }
        public Vector3 lastTargetPosition { get; set; }

        public bool CanSeePlayer() { return playerVisible; }
        public bool CanHearPlayer()
        {
            return (_player.position - sight.position).sqrMagnitude < hearingRange * hearingRange;
        }
        public bool IsPlayerDead() { return playerHealth.Dead; }

        public Vector3 GetPlayerNearestDirection(float offsetFromTarget)
        {
            return playerDirection - playerDirectionHorizontal.normalized * offsetFromTarget;
        }

        void Start()
        {
            if (sight == null) sight = transform;
            _player = GameObject.FindWithTag("Player").transform;
            playerCollider = _player.GetComponent<Collider>();
            playerRigidbody = _player.GetComponent<Rigidbody>();
            playerHealth = _player.GetComponent(typeof(IHealth)) as IHealth;
        }

        void OnDrawGizmosSelected()
        {
            Color oldColor = Gizmos.color;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Vector3 pos = Vector3.zero;
            Gizmos.matrix = sight != null ? sight.localToWorldMatrix : transform.localToWorldMatrix;
            if (playerCollider != null && sight != null)
                Gizmos.color = CanSeePlayer() ? Color.green : Color.red;
            Gizmos.DrawFrustum(pos, FOV, rangeMax, rangeMin, 1);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }
    }
}