using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.UI.Image;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    private float moveSpeed;

    public float groundDrag;


    bool isCrouched = false;
    bool readyToCrouch = true;

    [Header("Sprinting")]
    public float walkSpeed = 7f;
    public float sprintSpeed = 12f;
    public KeyCode sprintKey = KeyCode.LeftShift;


    //Jump
    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    public float crouchCooldown;
    bool readyToJump;
   
    [Header("Extra Gravity")]
    public float extraFallGravity = 10f;   // tweak in Inspector

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode crouchKey = KeyCode.LeftControl;
    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    bool grounded;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;


        readyToJump = true;
        moveSpeed = walkSpeed; 
    }

    private void Update()
    {
        //ground check
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);
        
        MyInput();
        SpeedControl();
        if (isCrouched && readyToCrouch)
        {
            transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * 0.5f, transform.localScale.z);
            readyToCrouch = false;
        }
        else if (!isCrouched && !readyToCrouch)
        {
            transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * 2f, transform.localScale.z);
            readyToCrouch = true;
        }

        //handle drag
        if (grounded)
        {
            rb.linearDamping = groundDrag;

        }
        else
        {
            rb.linearDamping = 0;
        }


        if (Input.GetKey(sprintKey) && grounded)
        {
            moveSpeed = sprintSpeed;
        }
        else
        {
            moveSpeed = walkSpeed;
        }
    }

    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;

            Jump();

            Invoke(nameof(ResetJump), jumpCooldown);
        }


        if (Input.GetKeyDown(crouchKey))
        {
            if (isCrouched == true)
            { 
                isCrouched = false; 
            }
            else
            { 
                isCrouched = true; 
            }
                

        }

    }


    private void FixedUpdate()
    {
        MovePlayer();


        // Extra gravity only when NOT grounded
        if (!grounded)
        {
            // Accelerate downward
            rb.AddForce(Vector3.down * extraFallGravity, ForceMode.Acceleration);
        }
    }


    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;
        // On ground
        if (grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
        }
        // in Air
        else if (!grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
        }

    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        //limit velocity if needed
        if (flatVel.magnitude > moveSpeed)
        { 
        
        Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    
    }

    private void ResetJump()
    {
        readyToJump = true;
    }

    private void ResetCrouch()
    {
        readyToCrouch = true;
    }

}
