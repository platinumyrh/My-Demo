using UnityEngine;

namespace GravityBox.AngryDroids
{
    public class AI : MonoBehaviour
    {
        public AIBehaviour behaviourOnSpotted;        
        public AIBehaviour behaviourOnLostTrack;

        [SerializeField]
        private AIActivator activator;

        private Transform _character;
        public Transform character
        {
            get { return _character; }
            set { _character = value; }
        }

        private Vector3 _spawnPos;
        public Vector3 spawnPos
        {
            get { return _spawnPos; }
            set { _spawnPos = value; }
        }

        private Animator _animator;
        public Animator animator
        {
            get { return _animator; }
            set { _animator = value; }
        }

        private AISight _sight;
        public AISight sight
        {
            get { return _sight; }
            set { _sight = value; }
        }

        public Transform player
        {
            get { return _sight.player; }
            set { _sight.player = value; }
        }

        private IMovementMotor _motor;
        public IMovementMotor motor
        {
            get { return _motor; }
            set { _motor = value; }
        }

        private IWeapon _weapon;
        public IWeapon weapon
        {
            get { return _weapon; }
            set { _weapon = value; }
        }

        private UnityEngine.AI.NavMeshAgent _agent;
        public UnityEngine.AI.NavMeshAgent agent
        {
            get { return _agent; }
            set { _agent = value; }
        }

        void Awake()
        {
            if (_character == null)
            {
                _character = transform.parent;
                _spawnPos = transform.position;
                _animator = GetComponentInParent<Animator>();
                _sight = GetComponentInParent<AISight>();
                _motor = character.GetComponent(typeof(IMovementMotor)) as IMovementMotor;                
                _weapon = character.GetComponent(typeof(IWeapon)) as IWeapon;
                _agent = character.GetComponent<UnityEngine.AI.NavMeshAgent>();
            }
        }

        void Start()
        {
            behaviourOnLostTrack.enabled = true;
            behaviourOnSpotted.enabled = false;

            activator.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            behaviourOnLostTrack.enabled = false;
            behaviourOnSpotted.enabled = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.transform == _sight.player)
                OnSpotted();
        }

        public void OnSpotted()
        {
            if (!IsActive())
                Activate(true);

            if (!behaviourOnSpotted.enabled)
            {
                behaviourOnSpotted.enabled = true;
                behaviourOnLostTrack.enabled = false;
            }
        }

        public void OnLostTrack()
        {
            if (!behaviourOnLostTrack.enabled)
            {
                behaviourOnLostTrack.enabled = true;
                behaviourOnSpotted.enabled = false;
            }
        }

        public bool CanSeePlayer()
        {
            return sight.CanSeePlayer();
        }

        public void Activate(bool active) { _animator.SetBool("active", active); }
        public bool IsActive() { return _animator.GetBool("active"); }
        public bool IsReady() { return _animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Active"); }
        public bool IsOffline() { return _animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Inactive"); }
        public bool IsAttacking() { return behaviourOnSpotted.enabled; }
        public bool IsReturning() { return behaviourOnLostTrack.enabled; }
        public bool SupportsNavMesh() { return _agent != null; }
    }
}