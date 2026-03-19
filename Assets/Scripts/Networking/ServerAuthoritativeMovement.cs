using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ServerAuthoritativeMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 7f;
    public float sprintSpeed = 12f;
    public float groundDrag = 12f;
    public float airDrag = 0f;

    [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;
    [SerializeField] private float crouchSpeedMultiplier = 0.5f;

    [Header("Crouch Collider")]
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private float standingHeight = 6f;
    [SerializeField] private float crouchHeight = 3.5f;

    [Header("Crouch Camera (optional)")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float standingCamY = 0f;
    [SerializeField] private float crouchCamY = -0.5f;

    [Header("Death / Flop")]
    [SerializeField] private float deathToppleForce = 6f;
    [SerializeField] private float deathUpForce = 1.5f;
    [SerializeField] private float deadDrag = 1f;

    private bool inCrouch;
    private float yaw;
    private float inX, inZ;
    private bool inSprint, inJump;
    private Rigidbody rb;
    private bool grounded;
    private bool readyToJump = true;
    private bool deathApplied;

    private Transform yawSource;

    public bool logOwnerInput = true;
    public bool logServerRpc = true;
    public bool logServerApply = true;

    [Tooltip("Acceleration force multiplier. Lower = less slippery.")]
    public float accelForce = 6f;

    [Tooltip("Braking force when no input on ground.")]
    public float brakeForce = 10f;

    [Header("Jump")]
    public float jumpForce = 18f;
    public float jumpCooldown = 0.5f;

    [Header("Ground Check")]
    public float playerHeight = 2f;
    public LayerMask whatIsGround;

    [Header("Kill Plane")]
    [SerializeField] private float killY = -10f;

    [Header("Orientation (optional)")]
    public Transform orientation;

    [Header("Crouch Visual Body (mesh)")]
    [SerializeField] private Transform visualBody;

    private NetworkVariable<bool> netCrouch = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private float visualStandScaleY;
    private float visualStandLocalY;
    private bool visualCached;

    private Transform crouchVisualTf;
    private float cachedStandY;
    private bool cachedStandYSet = false;

    private PlayerHealth playerHealth;

    public override void OnNetworkSpawn()
    {
        if (visualBody == null)
        {
            var t = transform.Find("PlayerBody+Camera/PlayerObj");
            if (t != null) visualBody = t;
            else
            {
                foreach (var tr in GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == "PlayerObj")
                    {
                        visualBody = tr;
                        break;
                    }
                }
            }
        }

        if (visualBody != null && !visualCached)
        {
            visualStandScaleY = visualBody.localScale.y;
            visualStandLocalY = visualBody.localPosition.y;
            visualCached = true;
        }

        playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();

        if (playerHealth != null)
            playerHealth.OnDeadStateChanged += OnDeadStateChanged;

        ApplyCrouchLocal(netCrouch.Value);

        netCrouch.OnValueChanged += OnCrouchChanged;

        if (playerHealth != null && playerHealth.isDead.Value)
            ApplyDeathState();

        Debug.Log($"[SrvMove] OnNetworkSpawn name={name} IsOwner={IsOwner} OwnerClientId={OwnerClientId} IsServer={IsServer} netCrouch={netCrouch.Value} visualBody={(visualBody != null ? visualBody.name : "NULL")}");
    }

    public override void OnNetworkDespawn()
    {
        netCrouch.OnValueChanged -= OnCrouchChanged;

        if (playerHealth != null)
            playerHealth.OnDeadStateChanged -= OnDeadStateChanged;
    }

    private void OnCrouchChanged(bool previousValue, bool newValue)
    {
        ApplyCrouchLocal(newValue);

        Debug.Log($"[SrvMove] CrouchChanged owner={OwnerClientId} prev={previousValue} next={newValue} IsServer={IsServer} IsOwner={IsOwner}");
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        if (capsule == null)
            capsule = GetComponentInChildren<CapsuleCollider>(true);

        if (cameraHolder == null)
        {
            var t = transform.Find("CameraHolder");
            if (t != null) cameraHolder = t;
            else
            {
                t = transform.Find("PlayerBody+Camera");
                if (t != null) cameraHolder = t;
            }
        }

        if (capsule == null)
            Debug.LogError($"[SrvMove] No CapsuleCollider found on '{name}' or children. Crouch cannot work.");

        Debug.Log($"[SrvMove] Awake '{name}' capsule={(capsule != null ? capsule.name : "NULL")} camHolder={(cameraHolder != null ? cameraHolder.name : "NULL")}");
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (!NetworkMapSync.IsGameplayReady())
            return;

        if (RoundManager.Instance != null && RoundManager.Instance.MatchOver)
            return;

        if (!IsMatchReady())
            return;

        if (playerHealth != null && playerHealth.isDead.Value)
            return;

        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        bool sprint = Input.GetKey(KeyCode.LeftShift);
        bool jump = Input.GetKeyDown(KeyCode.Space);

        bool crouch = Input.GetKey(crouchKey);
        float yawDegrees = GetYawDegreesForMovement();

        if (logOwnerInput)
            Debug.Log($"[SrvMove] OWNER send owner={OwnerClientId} in=({x},{z}) sprint={sprint} crouch={crouch} yaw={yawDegrees:0.0} jump={jump}");

        SubmitInputServerRpc(x, z, sprint, jump, yawDegrees, crouch);
    }

    private float GetYawDegreesForMovement()
    {
        if (yawSource == null)
        {
            var t = transform.Find("CameraHolder/PlayerCam");
            if (t != null) yawSource = t;
            else
            {
                foreach (var tr in GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == "PlayerCam")
                    {
                        yawSource = tr;
                        break;
                    }
                }
            }

            if (logOwnerInput)
                Debug.Log($"[SrvMove] OWNER yawSource={(yawSource != null ? yawSource.name : "NULL")}");
        }

        if (yawSource != null) return yawSource.eulerAngles.y;
        return transform.eulerAngles.y;
    }

    [ServerRpc(RequireOwnership = true)]
    private void SubmitInputServerRpc(float x, float z, bool sprint, bool jump, float yawDegrees, bool crouch)
    {
        if (playerHealth != null && playerHealth.isDead.Value)
        {
            inX = 0f;
            inZ = 0f;
            inSprint = false;
            inJump = false;
            inCrouch = false;
            return;
        }

        inX = x;
        inZ = z;
        inSprint = sprint;
        yaw = yawDegrees;
        inCrouch = crouch;
        netCrouch.Value = inCrouch;

        if (jump) inJump = true;

        //if (logServerRpc)
        //    Debug.Log($"[SrvMove] SERVER recv owner={OwnerClientId} in=({inX},{inZ}) sprint={inSprint} crouch={inCrouch} yaw={yaw:0.0} jump={inJump}");
    }

    private void ApplyCrouchLocal(bool crouch)
    {
        CapsuleCollider col = capsule;
        if (col == null && rb != null) col = rb.GetComponentInChildren<CapsuleCollider>(true);

        if (col != null)
        {
            float target = crouch ? crouchHeight : standingHeight;

            if (Mathf.Abs(col.height - target) > 0.01f)
            {
                float oldHeight = col.height;
                float delta = target - oldHeight;

                col.height = target;
                col.center = new Vector3(col.center.x, col.center.y + delta * 0.5f, col.center.z);
            }
        }

        if (visualBody != null && visualCached)
        {
            float ratio = (standingHeight > 0.001f) ? (crouchHeight / standingHeight) : 1f;

            var s = visualBody.localScale;
            var p = visualBody.localPosition;

            if (crouch)
            {
                s.y = visualStandScaleY * ratio;
                p.y = visualStandLocalY - (standingHeight - crouchHeight) * 0.5f;
            }
            else
            {
                s.y = visualStandScaleY;
                p.y = visualStandLocalY;
            }

            visualBody.localScale = s;
            visualBody.localPosition = p;
        }

        ApplyCrouchOwnerVisual(crouch);
    }
    private bool IsMatchReady()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.ConnectedClientsList == null)
            return false;

        return nm.ConnectedClientsList.Count >= 2;
    }
    public void ResetForNextRound()
    {
        deathApplied = false;

        inX = 0f;
        inZ = 0f;
        inSprint = false;
        inJump = false;
        inCrouch = false;
        grounded = false;
        readyToJump = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.freezeRotation = true;
            rb.linearDamping = groundDrag;
            rb.angularDamping = 0.05f;
        }

        netCrouch.Value = false;
        ApplyCrouchLocal(false);

        Transform t = transform;
        Vector3 e = t.eulerAngles;
        t.rotation = Quaternion.Euler(0f, e.y, 0f);

        //Debug.Log($"[SrvMove] ResetForNextRound owner={OwnerClientId}");
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (!NetworkMapSync.IsGameplayReady() || (RoundManager.Instance != null && RoundManager.Instance.MatchOver))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        if (!IsMatchReady())
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        if (playerHealth != null && !playerHealth.isDead.Value && transform.position.y <= killY)
        {
            playerHealth.TakeDamage(playerHealth.currentHealth.Value, "KillPlane");
            Debug.Log($"[SrvMove] Kill plane triggered for owner={OwnerClientId} at y={transform.position.y:0.00}");
            return;
        }

        if (playerHealth != null && playerHealth.isDead.Value)
        {
            rb.linearDamping = deadDrag;
            return;
        }

        grounded = Physics.Raycast(transform.position, Vector3.down, (playerHeight * 0.5f) + 0.2f, whatIsGround);

        rb.linearDamping = grounded ? groundDrag : airDrag;

        float speed = (inSprint && grounded) ? sprintSpeed : walkSpeed;
        if (inCrouch) speed *= crouchSpeedMultiplier;

        ApplyCrouchServer(inCrouch);
        ApplyCrouchOwnerVisual(inCrouch);

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 fwd = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;

        Vector3 moveDir = (fwd * inZ) + (right * inX);
        Vector3 moveDirNorm = (moveDir.sqrMagnitude > 0.0001f) ? moveDir.normalized : Vector3.zero;

        if (moveDirNorm != Vector3.zero)
        {
            rb.AddForce(moveDirNorm * speed * accelForce, ForceMode.Acceleration);
        }
        else if (grounded)
        {
            Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(-flatVel * brakeForce, ForceMode.Acceleration);
        }

        if (inJump && readyToJump && grounded)
        {
            readyToJump = false;

            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

            Invoke(nameof(ResetJumpServer), jumpCooldown);
        }

        inJump = false;

        Vector3 flat = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (flat.magnitude > speed)
        {
            Vector3 limited = flat.normalized * speed;
            rb.linearVelocity = new Vector3(limited.x, rb.linearVelocity.y, limited.z);
        }

        //if (logServerApply)
        //    Debug.Log($"[SrvMove] SERVER apply owner={OwnerClientId} grounded={grounded} speed={speed:0.0} flat={flat.magnitude:0.00} vel={rb.linearVelocity}");
    }

    private void ApplyCrouchServer(bool crouch)
    {
        if (!IsServer) return;

        CapsuleCollider col = capsule;

        if (col == null && rb != null)
            col = rb.GetComponent<CapsuleCollider>();

        if (col == null)
        {
            Debug.LogWarning($"[SrvMove] ApplyCrouchServer: No CapsuleCollider found (owner={OwnerClientId}).");
            return;
        }

        float target = crouch ? crouchHeight : standingHeight;

        if (Mathf.Abs(col.height - target) < 0.01f)
            return;

        float oldHeight = col.height;
        Vector3 oldCenter = col.center;

        float delta = target - oldHeight;

        col.height = target;
        col.center = new Vector3(col.center.x, col.center.y + delta * 0.5f, col.center.z);

        //Debug.Log(
        //    $"[SrvMove] SERVER crouchApply owner={OwnerClientId} crouch={crouch} " +
        //    $"colObj={col.gameObject.name} height {oldHeight:0.00}->{col.height:0.00} " +
        //    $"centerY {oldCenter.y:0.00}->{col.center.y:0.00}"
        //);
    }

    private void ApplyCrouchOwnerVisual(bool crouch)
    {
        if (!IsOwner) return;

        if (crouchVisualTf == null)
        {
            var t = transform.Find("PlayerBody+Camera/CameraPos");
            if (t != null) crouchVisualTf = t;

            if (crouchVisualTf == null)
            {
                t = transform.Find("CameraHolder/PlayerCam");
                if (t != null) crouchVisualTf = t;
            }

            if (crouchVisualTf == null)
            {
                foreach (var tr in GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == "CameraPos" || tr.name == "PlayerCam")
                    {
                        crouchVisualTf = tr;
                        break;
                    }
                }
            }

            //Debug.Log($"[SrvMove] OWNER visualTf={(crouchVisualTf != null ? crouchVisualTf.name : "NULL")}");
        }

        if (crouchVisualTf == null)
            return;

        if (!cachedStandYSet)
        {
            cachedStandY = crouchVisualTf.localPosition.y;
            cachedStandYSet = true;
            standingCamY = cachedStandY;
            //Debug.Log($"[SrvMove] OWNER cachedStandY={cachedStandY:0.00} on {crouchVisualTf.name}");
        }

        float targetY = crouch ? crouchCamY : standingCamY;

        Vector3 lp = crouchVisualTf.localPosition;
        lp.y = targetY;
        crouchVisualTf.localPosition = lp;

        //Debug.Log($"[SrvMove] OWNER visual crouch={crouch} set {crouchVisualTf.name}.localY={lp.y:0.00}");
    }

    private void OnDeadStateChanged(bool deadNow)
    {
        if (deadNow)
            ApplyDeathState();
    }

    private void ApplyDeathState()
    {
        if (deathApplied)
            return;

        deathApplied = true;

        inX = 0f;
        inZ = 0f;
        inSprint = false;
        inJump = false;
        inCrouch = false;

        if (IsServer && rb != null)
        {
            rb.freezeRotation = false;
            rb.linearDamping = deadDrag;
            rb.angularDamping = 0.05f;

            Vector3 toppleDir = transform.right + (Vector3.up * 0.25f);
            toppleDir.Normalize();

            rb.AddForce(toppleDir * deathToppleForce + Vector3.up * deathUpForce, ForceMode.Impulse);
            rb.AddTorque(transform.forward * deathToppleForce, ForceMode.Impulse);

            //Debug.Log($"[SrvMove] Death flop applied owner={OwnerClientId}");
        }
    }

    private void ResetJumpServer()
    {
        readyToJump = true;
    }
}