using System;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reconnect.Player
{
    public class PlayerMovementsNetwork : PlayerNetwork
    {
        // parameters that can be edited in Editor
        [Header("Gravity force constant")]
        public float gravity = -9.81f; // gravity strength (default: -9.81, earth gravity)

        public float jumpHeight = 0.7f; // the height the player should jump

        [Header("Speed settings")] public float defaultSpeed = 6.0f; // the walking speed of the player

        public float sprintingFactor = 1.8f; // the sprinting speed modifier to be applied to the defaultSpeed
        public float crouchingFactor = 0.7f; // the crouching speed modifier to be applied to the defaultSpeed
        public float turnSmoothTime = 0.1f; // the time to smooth the rotation of the player (camera and keyboard)
        private Vector3 _currentMovement; // the movement to be applies to the player

        // movement
        private Vector2 _currentMovementInput;
        private bool _isCrouching;
        private int _isCrouchingHash;
        private bool _isDancing;
        private int _isDancingHash;
        private int _isFallingHash;
        private int _isGroundedHash;
        private bool _isJumping;
        private int _isJumpingHash;
        private bool _isJumpingPressed;

        // states memory
        private bool _isMovementPressed;
        private bool _isRunning;
        private int _isRunningHash;

        // animations states hashes
        private int _isWalkingHash;

        // internal values
        private float _turnSmoothVelocity;
        private float _velocityY; // the velocity on the Y axis (jumping and falling)

        // imported components
        protected Animator
            Animator; // the animator component on the 3D model of the Player inside the current GameObject

        protected Transform CameraTransform; // the MainCamera 3rd person inside the current GameObject

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        public override void Awake()
        {
            base.Awake();

            PlayerInput.actions["Move"].started += OnMove;
            PlayerInput.actions["Move"].performed += OnMove;
            PlayerInput.actions["Move"].canceled += OnMove;
            PlayerInput.actions["Sprint"].started += OnSprint;
            PlayerInput.actions["Sprint"].canceled += OnSprint;
            PlayerInput.actions["Crouch"].started += OnCrouch;
            PlayerInput.actions["Jump"].started += OnJump;
            PlayerInput.actions["Dance"].started += OnDance;

            Animator = GetComponent<Animator>() ??
                       throw new ArgumentException(
                           "There is no Animator component in the children of the current GameObject");

            CameraTransform = GameObject.FindGameObjectWithTag("MainCamera")?.transform
                              ?? throw new ArgumentException("There is no MainCamera tagged GameObject.");

            _isWalkingHash = Animator.StringToHash("isWalking");
            _isRunningHash = Animator.StringToHash("isRunning");
            _isCrouchingHash = Animator.StringToHash("isCrouching");
            _isJumpingHash = Animator.StringToHash("isJumping");
            _isFallingHash = Animator.StringToHash("isFalling");
            _isGroundedHash = Animator.StringToHash("isGrounded");
            _isDancingHash = Animator.StringToHash("isDancing");
        }

        // Update is called once per frame
        private void Update()
        {
            if (!isLocalPlayer) return;
            if (isLocked) HandleLockedPlayer();
            HandleInputs();
            HandleGravityAndJump();
            HandleAnimation();
            HandleRotation2();
            HandleMovements();
        }

        private void OnEnable()
        {
            PlayerInput.actions.Enable();
        }

        private void OnDisable()
        {
            PlayerInput.actions.Disable();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            // It's a good practice to unsubscribe from actions when the object is destroyed
            PlayerInput.actions["Move"].started -= OnMove;
            PlayerInput.actions["Move"].performed -= OnMove;
            PlayerInput.actions["Move"].canceled -= OnMove;
            PlayerInput.actions["Sprint"].started -= OnSprint;
            PlayerInput.actions["Sprint"].canceled -= OnSprint;
            PlayerInput.actions["Crouch"].started -= OnCrouch;
            PlayerInput.actions["Jump"].started -= OnJump;
            PlayerInput.actions["Dance"].started -= OnDance;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            if (isLocked)
                return;

            _currentMovementInput = context.ReadValue<Vector2>();
            _currentMovement.x = _currentMovementInput.x;
            _currentMovement.z = _currentMovementInput.y;
            _isMovementPressed = _currentMovement != Vector3.zero;
        }

        public void OnJump(InputAction.CallbackContext context)
        {
            if (isLocked)
                return;

            _isJumpingPressed = context.ReadValue<float>() != 0f;
        }

        public void OnCrouch(InputAction.CallbackContext context)
        {
            if (isLocked)
                return;

            _isCrouching = !_isCrouching;
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            if (isLocked)
                return;

            if (context.started)
                _isRunning = true;
            else if (context.canceled)
                _isRunning = false;
        }

        public void OnDance(InputAction.CallbackContext context)
        {
            if (isLocked)
                return;

            _isDancing = true;
        }

        private void HandleInputs()
        {
            if (_isJumpingPressed && _isCrouching)
            {
                _isCrouching = false; // jumping cancels crouching
                _isJumpingPressed = false;
            }
            
            if (_isJumpingPressed && !CharacterController.isGrounded)
            {
                _isJumpingPressed = false; // cancel double jump
            }
        }

        private void JumpAnimation()
        {
            if (_isJumpingPressed && !_isJumping)
            {
                Animator.SetBool(_isJumpingHash, true);
                _isJumping = true;
                _isJumpingPressed = false;
            }
            else if (CharacterController.isGrounded) // is character grounded, no more falling nor jumping
            {
                Animator.SetBool(_isGroundedHash, true);
                Animator.SetBool(_isJumpingHash, false);
                _isJumping = false;
                Animator.SetBool(_isFallingHash, false);
            }
            else // if the character is not grounded, then it is maybe falling
            {
                Animator.SetBool(_isGroundedHash, false);

                // if not grounded, it's falling if it's on the descending part of the jump or if it fell from a height (with velocityY threshold of -2f)
                if ((_velocityY < 0 && _isJumping) || _velocityY < -2f) Animator.SetBool(_isFallingHash, true);
            }
        }

        private void HandleAnimation()
        {
            var isWalking = Animator.GetBool(_isWalkingHash);
            var isRunning = Animator.GetBool(_isRunningHash);
            var isCrouching = Animator.GetBool(_isCrouchingHash);
            var isDancing = Animator.GetBool(_isDancingHash);
            JumpAnimation();

            if (_isDancing && !isDancing && !_isMovementPressed)
                Animator.SetBool(_isDancingHash, true);

            if (isDancing && _isMovementPressed)
            {
                Animator.SetBool(_isDancingHash, false);
                _isDancing = false;
            }

            if (_isMovementPressed && !isWalking)
                Animator.SetBool(_isWalkingHash, true);

            if (_isRunning && !isRunning)
                Animator.SetBool(_isRunningHash, true);

            if (_isCrouching && !isCrouching)
                Animator.SetBool(_isCrouchingHash, true);

            if (!_isMovementPressed && isWalking)
                Animator.SetBool(_isWalkingHash, false);

            // stop running if key released
            if (!_isRunning && isRunning) Animator.SetBool(_isRunningHash, false);

            // stop running if no more moving (even though the key is still pressed)
            if (!_isMovementPressed && isRunning)
                Animator.SetBool(_isRunningHash, false);

            if (!_isCrouching && isCrouching)
                Animator.SetBool(_isCrouchingHash, false);
        }

        private void HandleRotation2()
        {
            if (isLocked)
                return;

            // Extract input directions
            var x = _currentMovementInput.x;
            var z = _currentMovementInput.y;

            // Combine the input into a direction vector
            var direction = new Vector3(x, 0f, z).normalized;

            // Only process movement when there's a significant input
            if (direction.magnitude >= 0.1f)
            {
                // Calculate target rotation based on camera orientation
                var targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg +
                                  CameraTransform.eulerAngles.y;

                // Smooth the player's rotation
                var smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle,
                    ref _turnSmoothVelocity, turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

                // Calculate movement direction relative to the player's rotation
                _currentMovement = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            }
        }

        private void HandleMovements()
        {
            var speed = defaultSpeed;
            if (_isRunning)
                speed *= sprintingFactor;
            else if (_isCrouching) speed *= crouchingFactor;

            var move = new Vector3(_currentMovement.x * speed, _velocityY, _currentMovement.z * speed);
            CharacterController.Move(move * Time.deltaTime);
        }

        private void HandleGravityAndJump()
        {
            if (CharacterController.isGrounded && _isJumpingPressed) // jumping vertical velocity
                _velocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            else if (CharacterController.isGrounded) // on ground vertical velocity
                _velocityY = gravity * Time.deltaTime;
            else
                _velocityY += gravity * Time.deltaTime;
        }

        private void HandleLockedPlayer()
        {
            // overwrite the movement to avoid it from moving
            _currentMovement.x = 0;
            _currentMovement.z = 0;
            _isMovementPressed = false;
            _currentMovementInput = Vector2.zero;
        }
    }
}