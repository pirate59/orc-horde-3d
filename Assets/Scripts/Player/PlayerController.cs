using UnityEngine;

namespace OrcHordeFps
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public static PlayerController I { get; private set; }

        const float WalkSpeed = 6f, SprintSpeed = 9.5f, JumpSpeed = 5.5f, Gravity = 18f;
        const float EyeHeight = 1.65f;

        public float Sensitivity = 2f;
        public Camera Cam { get; private set; }

        CharacterController controller;
        float pitch, yaw, verticalSpeed;

        public static PlayerController Create(Vector3 position, float yaw)
        {
            var go = new GameObject("Player");
            go.layer = Layers.Player;
            go.transform.position = position + Vector3.up * 0.1f;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.45f;
            cc.slopeLimit = 50f;

            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0, EyeHeight, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            camGo.AddComponent<AudioListener>();

            var pc = go.AddComponent<PlayerController>();
            pc.Cam = cam;
            pc.yaw = yaw;
            go.AddComponent<PlayerHealth>();
            go.AddComponent<PlayerWeapon>();
            go.AddComponent<PlayerAbilities>();
            go.AddComponent<BuildController>();
            return pc;
        }

        void Awake()
        {
            I = this;
            controller = GetComponent<CharacterController>();
        }

        public bool InputLive => !GameManager.I.Paused && PlayerHealth.I.Alive;

        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position + Vector3.up * 0.1f;
            controller.enabled = true;
            verticalSpeed = 0;
        }

        void Update()
        {
            Cursor.lockState = InputLive ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !InputLive;
            if (!InputLive) return;

            yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, -85f, 85f);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);
            float speed = Input.GetKey(KeyCode.LeftShift) ? SprintSpeed : WalkSpeed;
            var move = transform.TransformDirection(input) * speed;

            if (controller.isGrounded)
            {
                verticalSpeed = -2f;
                if (Input.GetKeyDown(KeyCode.Space)) verticalSpeed = JumpSpeed;
            }
            else verticalSpeed -= Gravity * Time.deltaTime;

            move.y = verticalSpeed;
            controller.Move(move * Time.deltaTime);
        }

        /// <summary>The ray from the centre of the screen.</summary>
        public Ray AimRay => new Ray(Cam.transform.position, Cam.transform.forward);

        /// <summary>A small upward kick on firing.</summary>
        public void Kick(float degrees) => pitch = Mathf.Clamp(pitch - degrees, -85f, 85f);
    }
}
