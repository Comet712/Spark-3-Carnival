using System.Collections.Generic;
using UnityEngine;

namespace CarnivalMod
{
    public class CompanionController : MonoBehaviour
    {
        // --- Tuning ---
        private const float FollowSpeed          = 14f;
        private const float FollowDistance       = 5f;
        private const float DeadZone             = 2.5f;
        private const float SlowZone             = 6f;   // distance at which approach speed starts ramping down toward zero
        private const float WarpDistance         = 60f;
        private const float RayOriginUp          = 0.5f;
        private const float RayLength            = 1.6f;  // capsule center rests ~1.0 above ground; ray origin is +0.5 up, so needs >1.5 to reach surface
        private const float GroundSphereRadius   = 0.3f;  // SphereCast radius — larger surface catches sloped geometry a thin ray misses
        private const float CoyoteTime           = 0.15f; // seconds of grace before animator switches to air state (physics uses real grounded)
        private const float SlopeForwardBias     = 0.8f;  // how far forward the angled slope cast leans (higher = shallower angle from horizontal)
        private const float JumpDelay            = 0.5f;
        private const float JumpMultiplier       = 5f;
        private const float DoubleJumpMultiplier = 1.25f;
        private const float ModelScale           = 1.0f;
        private const float SpeedMatchRate       = 2f;
        private const float StuckTimeThreshold   = 0.8f;
        private const float StuckHeightThreshold = 1.5f;
        private const int   MaxStuckJumps        = 3;

        // Companion always shows Spark regardless of what the player equips.
        private const int CompanionCharIndex = 0;

        private Rigidbody     _rb;
        private PlayerBhysics _player;
        private bool          _grounded;
        private bool          _wasPlayerGrounded;
        private bool          _wasDoubleJumpAvailable;

        // Model
        private bool       _modelAttached;
        private GameObject _modelRoot;
        private GameObject _skinCopyRoot;
        private Animator   _companionAnimator;

        // Animation state
        private bool  _forceJumpThisFrame;
        private bool  _doubleJumpThisFrame;
        private bool  _wasGrounded;
        private float _airtimeTimer;
        private float _jumpTimeCounter;
        private float _coyoteTimer;       // counts down after leaving ground; animator stays grounded until it hits zero

        // Jumps
        private float _jumpTimer = -1f;

        // Stuck detection
        private float _currentFollowSpeed;
        private float _stuckTimer;
        private float _lastXZDist;
        private int   _stuckJumpCount;
        private bool  _stuckDoubleJumped;

        // ---------------------------------------------------------------

        private void Start()
        {
            DontDestroyOnLoad(gameObject);

            _rb = GetComponent<Rigidbody>();
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _currentFollowSpeed = FollowSpeed;

            _player = PlayerBhysics.Player;
            if (_player == null)
                Plugin.Log.LogWarning("[CarnivalMod] PlayerBhysics not found on Start — will retry.");
        }

        private void Update()
        {
            if (!_modelAttached)
                TryAttachModel();
            else
                UpdateAnimator();
        }

        private void UpdateAnimator()
        {
            if (_companionAnimator == null) return;

            float xzSpeed = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z).magnitude;

            // Animator uses coyote-buffered grounded so brief slope bounces don't trigger the air state.
            // Physics (_grounded) stays real so gravity and stuck-detection remain correct.
            bool animGrounded = _coyoteTimer > 0f;

            // --- Jump event flags (set in FixedUpdate, consumed here) ---
            if (_forceJumpThisFrame)
            {
                _companionAnimator.SetBool("ForceJump",  true);
                _companionAnimator.SetBool("DoubleJump", false);
                _jumpTimeCounter   = 0f;
                _forceJumpThisFrame = false;
            }
            else if (_doubleJumpThisFrame)
            {
                _companionAnimator.SetBool("DoubleJump", true);
                _companionAnimator.SetBool("ForceJump",  true);
                _doubleJumpThisFrame = false;
            }

            // --- Landing ---
            if (!_wasGrounded && animGrounded)
            {
                _companionAnimator.SetBool("ForceJump",  false);
                _companionAnimator.SetBool("DoubleJump", false);
                if (_airtimeTimer > 0.2f)
                    _companionAnimator.SetTrigger("GroundTrigger");
                _airtimeTimer = 0f;
            }

            // --- Airtime / jump timer ---
            if (!animGrounded)
            {
                _airtimeTimer    += Time.deltaTime;
                _jumpTimeCounter += Time.deltaTime;
            }

            // --- Core parameters ---
            _companionAnimator.SetInteger("Action",        animGrounded ? 0 : 1);
            _companionAnimator.SetBool   ("Grounded",      animGrounded);
            _companionAnimator.SetFloat  ("SpeedMagXZ",   xzSpeed,              0.12f, Time.deltaTime);
            _companionAnimator.SetFloat  ("NormalSpeed",  xzSpeed,              0.12f, Time.deltaTime);
            _companionAnimator.SetFloat  ("GroundSpeed",  _rb.velocity.magnitude, 0.12f, Time.deltaTime);
            _companionAnimator.SetFloat  ("YSpeed",        _rb.velocity.y);
            _companionAnimator.SetFloat  ("JumpTime",      _jumpTimeCounter);
            _companionAnimator.SetFloat  ("RunningMulti",  1f);
            _companionAnimator.SetBool   ("isRolling",     false);
            _companionAnimator.SetBool   ("FallingFast",   false);

            _wasGrounded = animGrounded;
        }

        private void DetachModel()
        {
            if (_modelRoot    != null) { Object.Destroy(_modelRoot);    _modelRoot    = null; }
            if (_skinCopyRoot != null) { Object.Destroy(_skinCopyRoot); _skinCopyRoot = null; }
            _companionAnimator = null;
            _modelAttached     = false;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = true;
        }

        private void FixedUpdate()
        {
            if (_player == null)
            {
                _player = PlayerBhysics.Player;
                return;
            }

            WarpIfTooFar();
            GroundCheck();
            ApplyGravity();
            UpdateFollowSpeed();
            FollowPlayer();
            CheckForJump();
            CheckIfStuck();
            FaceMovementDirection();

            _wasPlayerGrounded      = _player.Grounded;
            _wasDoubleJumpAvailable = _player.Actions.Action01.DoubleJumpAvailable;
        }

        // ---------------------------------------------------------------

        private void WarpIfTooFar()
        {
            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > WarpDistance)
            {
                transform.position = _player.transform.position
                    - _player.transform.forward * 8f
                    + Vector3.up * 12f;
                _rb.velocity = Vector3.zero;
                ResetStuck();
            }
        }

        private void GroundCheck()
        {
            Vector3 origin   = transform.position + Vector3.up * RayOriginUp;
            float   castDist = RayLength - GroundSphereRadius;

            // Primary cast: straight down — handles flat and gentle slopes.
            bool hitDown = Physics.SphereCast(
                origin, GroundSphereRadius, Vector3.down, out _,
                castDist, _player.RayableGround
            );

            // Secondary cast: angled in the movement direction — catches steep downslopes
            // where the ground is ahead-and-below rather than directly underneath.
            bool hitSlope = false;
            Vector3 horizVel = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
            if (!hitDown && horizVel.sqrMagnitude > 1f)
            {
                Vector3 slopeDir = (Vector3.down + horizVel.normalized * SlopeForwardBias).normalized;
                hitSlope = Physics.SphereCast(
                    origin, GroundSphereRadius, slopeDir, out _,
                    castDist * 1.6f, _player.RayableGround
                );
            }

            _grounded = hitDown || hitSlope;

            if (_grounded)
                _coyoteTimer = CoyoteTime;
            else
                _coyoteTimer -= Time.fixedDeltaTime;
        }

        private void ApplyGravity()
        {
            if (_grounded)
            {
                if (_rb.velocity.y < 0f)
                {
                    Vector3 v = _rb.velocity;
                    v.y = 0f;
                    _rb.velocity = v;
                }
                return;
            }

            _rb.velocity += _player.Gravity * PlayerBhysics.TimeStep;

            if (_rb.velocity.y < _player.MaxFallingSpeed)
            {
                Vector3 v = _rb.velocity;
                v.y = _player.MaxFallingSpeed;
                _rb.velocity = v;
            }
        }

        private void UpdateFollowSpeed()
        {
            float playerXZSpeed = new Vector3(_player.rigid.velocity.x, 0f, _player.rigid.velocity.z).magnitude;
            float targetSpeed   = Mathf.Max(FollowSpeed, playerXZSpeed);
            _currentFollowSpeed = Mathf.Lerp(_currentFollowSpeed, targetSpeed, Time.fixedDeltaTime * SpeedMatchRate);
        }

        private void FollowPlayer()
        {
            Vector3 targetPos = _player.transform.position
                - _player.transform.forward * FollowDistance
                + _player.transform.right  * FollowDistance * 0.5f;

            Vector3 toTarget = targetPos - transform.position;
            toTarget.y = 0f;

            Vector3 vel = _rb.velocity;

            float dist = toTarget.magnitude;
            if (dist > DeadZone)
            {
                // Ramp speed down linearly as companion enters the slow zone.
                // At SlowZone distance: full speed. At DeadZone: speed = 0.
                // Prevents charge-and-jitter when the player decelerates.
                float speedScale = Mathf.Clamp01((dist - DeadZone) / (SlowZone - DeadZone));
                float moveSpeed  = _currentFollowSpeed * speedScale;
                Vector3 dir = toTarget.normalized;
                vel.x = dir.x * moveSpeed;
                vel.z = dir.z * moveSpeed;
            }
            else
            {
                vel.x = Mathf.Lerp(vel.x, 0f, 0.25f);
                vel.z = Mathf.Lerp(vel.z, 0f, 0.25f);
            }

            _rb.velocity = vel;
        }

        private void CheckForJump()
        {
            float jumpForce       = _player.Actions.Action01.JumpSpeed       * JumpMultiplier;
            float doubleJumpForce = _player.Actions.Action01.DoubleJumpForce * DoubleJumpMultiplier;

            if (_wasPlayerGrounded && !_player.Grounded)
                _jumpTimer = JumpDelay;

            // If the player already landed before the timer fired, they were only
            // briefly airborne (slope seam / micro-bump). Cancel — not a real jump.
            if (_jumpTimer >= 0f && _player.Grounded)
                _jumpTimer = -1f;

            if (_wasDoubleJumpAvailable && !_player.Actions.Action01.DoubleJumpAvailable && !_player.Grounded)
            {
                Vector3 v = _rb.velocity;
                v.y = doubleJumpForce;
                _rb.velocity = v;
                _doubleJumpThisFrame = true;
            }

            if (_jumpTimer >= 0f)
            {
                _jumpTimer -= Time.fixedDeltaTime;
                if (_jumpTimer <= 0f)
                {
                    _jumpTimer = -1f;
                    Vector3 vel = _rb.velocity;
                    vel.y = jumpForce;
                    _rb.velocity    = vel;
                    _forceJumpThisFrame = true;
                }
            }
        }

        private void CheckIfStuck()
        {
            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            float xzDist = toPlayer.magnitude;

            if (xzDist > DeadZone)
            {
                if (xzDist >= _lastXZDist - 0.05f)
                {
                    _stuckTimer += Time.fixedDeltaTime;

                    if (_stuckTimer >= StuckTimeThreshold && _stuckJumpCount < MaxStuckJumps)
                    {
                        float heightDiff = _player.transform.position.y - transform.position.y;

                        // Skip if the player is actively running up a slope.
                        // A slope produces positive Y velocity while grounded; a platform does not.
                        bool playerRisingOnSlope = _player.Grounded && _player.rigid.velocity.y > 1.5f;

                        if (heightDiff > StuckHeightThreshold && _player.Grounded && !playerRisingOnSlope)
                        {
                            _stuckTimer = 0f;
                            _stuckJumpCount++;

                            float jumpForce = _player.Actions.Action01.JumpSpeed * JumpMultiplier;
                            float djForce   = _player.Actions.Action01.DoubleJumpForce * DoubleJumpMultiplier;

                            if (_grounded)
                            {
                                Vector3 vel = _rb.velocity;
                                vel.y = jumpForce;
                                _rb.velocity        = vel;
                                _forceJumpThisFrame = true;
                                _stuckDoubleJumped  = false;
                            }
                            else if (!_stuckDoubleJumped)
                            {
                                Vector3 vel = _rb.velocity;
                                vel.y = djForce;
                                _rb.velocity         = vel;
                                _doubleJumpThisFrame = true;
                                _stuckDoubleJumped   = true;
                            }
                        }
                    }
                }
                else
                {
                    ResetStuck();
                }
            }
            else
            {
                ResetStuck();
            }

            _lastXZDist = xzDist;
        }

        private void ResetStuck()
        {
            _stuckTimer        = 0f;
            _stuckJumpCount    = 0;
            _stuckDoubleJumped = false;
        }

        private void FaceMovementDirection()
        {
            Vector3 horizontal = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
            if (horizontal.sqrMagnitude > 0.5f)
                transform.rotation = Quaternion.LookRotation(horizontal, Vector3.up);
        }

        // ---------------------------------------------------------------

        private Transform FindBoneRoot(CharacterAnimatorChange charChange, int charIndex)
        {
            if (charIndex < 0 || charIndex >= charChange.Skins.Length) return null;
            var skin = charChange.Skins[charIndex];
            if (skin == null) return null;

            SkinnedMeshRenderer smr = skin.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                foreach (var s in charChange.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (s.gameObject.activeInHierarchy) { smr = s; break; }
            }
            if (smr == null) return null;

            Transform root = smr.rootBone ?? (smr.bones.Length > 0 ? smr.bones[0] : null);
            if (root == null) return null;
            while (root.parent != null && root.parent != charChange.transform)
                root = root.parent;
            return root;
        }

        private void TryAttachModel()
        {
            var charChange = CharacterAnimatorChange.StaticReference;
            if (charChange == null) return;

            Transform boneRoot = FindBoneRoot(charChange, CompanionCharIndex);
            if (boneRoot == null)
            {
                Plugin.Log.LogWarning("[CarnivalMod] Spark bone root not found.");
                _modelAttached = true;
                return;
            }

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;

            // animRoot is the Animator's GameObject — bone paths in the Avatar are
            // relative to this, e.g. "SparkModel/Hip/Spine/...".
            GameObject animRoot = new GameObject("CompanionAnimRoot");
            animRoot.transform.SetParent(transform);
            animRoot.transform.localPosition = new Vector3(0f, -0.8f, 0f);
            animRoot.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            animRoot.transform.localScale    = Vector3.one;

            GameObject boneRootCopy = Object.Instantiate(boneRoot.gameObject);
            boneRootCopy.name = boneRoot.name;  // strip "(Clone)" so Avatar paths resolve
            boneRootCopy.transform.SetParent(animRoot.transform);
            boneRootCopy.transform.localPosition = Vector3.zero;
            boneRootCopy.transform.localRotation = Quaternion.identity;
            boneRootCopy.transform.localScale    = boneRoot.lossyScale * ModelScale;

            var boneMap = new Dictionary<Transform, Transform>();
            BuildBoneMap(boneRoot, boneRootCopy.transform, boneMap);

            bool skinInsideBoneRoot = boneRoot.GetComponentInChildren<SkinnedMeshRenderer>() != null;
            Plugin.Log.LogInfo($"[CarnivalMod] boneRoot={boneRoot.name} skinInside={skinInsideBoneRoot}");

            if (!skinInsideBoneRoot)
            {
                var activeSkin = charChange.Skins[CompanionCharIndex];
                GameObject skinCopy = Object.Instantiate(activeSkin);
                skinCopy.SetActive(true);
                skinCopy.transform.SetParent(animRoot.transform);
                skinCopy.transform.localPosition = Vector3.zero;
                skinCopy.transform.localRotation = Quaternion.identity;
                foreach (var smr in skinCopy.GetComponentsInChildren<SkinnedMeshRenderer>())
                    RemapBones(smr, boneMap);
                foreach (var mb in skinCopy.GetComponentsInChildren<MonoBehaviour>()) Object.Destroy(mb);
                foreach (var c  in skinCopy.GetComponentsInChildren<Rigidbody>())    Object.Destroy(c);
                foreach (var c  in skinCopy.GetComponentsInChildren<Collider>())     Object.Destroy(c);
                _skinCopyRoot = skinCopy;
            }
            else
            {
                foreach (var smr in boneRootCopy.GetComponentsInChildren<SkinnedMeshRenderer>())
                    RemapBones(smr, boneMap);
            }

            foreach (var mb in boneRootCopy.GetComponentsInChildren<MonoBehaviour>()) Object.Destroy(mb);
            foreach (var c  in boneRootCopy.GetComponentsInChildren<Rigidbody>())    Object.Destroy(c);
            foreach (var c  in boneRootCopy.GetComponentsInChildren<Collider>())     Object.Destroy(c);

            // ---- Independent Animator ----
            var srcAnim = charChange.GetComponent<Animator>();
            _companionAnimator = animRoot.AddComponent<Animator>();
            _companionAnimator.runtimeAnimatorController = srcAnim.runtimeAnimatorController;
            _companionAnimator.avatar       = charChange.CharacterAvatar[CompanionCharIndex];
            _companionAnimator.cullingMode  = AnimatorCullingMode.AlwaysAnimate;
            _companionAnimator.updateMode   = AnimatorUpdateMode.Normal;
            _companionAnimator.Rebind();

            // Seed all params before Play() so the state machine starts in the right place
            _companionAnimator.SetInteger("Character",   CompanionCharIndex);
            _companionAnimator.SetInteger("Action",      0);
            _companionAnimator.SetBool   ("Grounded",    true);
            _companionAnimator.SetFloat  ("SpeedMagXZ",  0f);
            _companionAnimator.SetFloat  ("NormalSpeed", 0f);
            _companionAnimator.SetFloat  ("GroundSpeed", 0f);
            _companionAnimator.SetFloat  ("YSpeed",      0f);
            _companionAnimator.SetFloat  ("JumpTime",    0f);
            _companionAnimator.SetBool   ("ForceJump",    false);
            _companionAnimator.SetBool   ("DoubleJump",  false);
            _companionAnimator.SetFloat  ("RunningMulti", 1f);
            _companionAnimator.SetBool   ("isRolling",   false);
            _companionAnimator.SetBool   ("FallingFast", false);

            // Spark uses layer 0 only — all extra layers at zero weight
            for (int i = 1; i <= 13; i++)
                _companionAnimator.SetLayerWeight(i, 0f);

            _companionAnimator.Play("[00] Grounded Idle", 0, 0f);

            _modelRoot     = animRoot;
            _modelAttached = true;
            _wasGrounded   = true;

            Plugin.Log.LogInfo("[CarnivalMod] Spark companion attached with independent Animator.");
        }

        private void RemapBones(SkinnedMeshRenderer smr, Dictionary<Transform, Transform> boneMap)
        {
            var newBones = new Transform[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
                newBones[i] = (smr.bones[i] != null && boneMap.TryGetValue(smr.bones[i], out var b)) ? b : smr.bones[i];
            smr.bones = newBones;
            if (smr.rootBone != null && boneMap.TryGetValue(smr.rootBone, out var nr))
                smr.rootBone = nr;
        }

        private void BuildBoneMap(Transform original, Transform clone, Dictionary<Transform, Transform> map)
        {
            map[original] = clone;
            for (int i = 0; i < original.childCount && i < clone.childCount; i++)
                BuildBoneMap(original.GetChild(i), clone.GetChild(i), map);
        }
    }
}
