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

        // Tactical view: an overhead camera for laying out the line between rounds.
        const float TacticalPanSpeed = 28f, TacticalTurnSpeed = 90f, TacticalMinHeight = 12f, TacticalMaxHeight = 110f;
        public bool Tactical { get; private set; }
        Vector3 tacticalFocus;
        float tacticalYaw, tacticalHeight = 40f;

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

        public bool InputLive => !GameManager.I.Paused && !GameManager.I.ShopOpen && PlayerHealth.I.Alive;

        /// <summary>
        /// Lifts the camera overhead (or brings it back down). Only between rounds:
        /// the fight is fought on foot.
        /// </summary>
        public void SetTactical(bool on)
        {
            if (on == Tactical || (on && GameManager.I.Phase != Phase.Build)) return;
            Tactical = on;
            if (on)
            {
                tacticalFocus = transform.position;
                tacticalYaw = yaw;
                Cam.transform.SetParent(null, true);
                PlaceTacticalCamera();
            }
            else
            {
                Cam.transform.SetParent(transform, false);
                Cam.transform.localPosition = new Vector3(0, EyeHeight, 0);
                Cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
        }

        /// <summary>Points the tactical view at a spot, from a given height.</summary>
        public void FocusTactical(Vector3 point, float height)
        {
            tacticalFocus = point;
            tacticalHeight = Mathf.Clamp(height, TacticalMinHeight, TacticalMaxHeight);
            if (Tactical) PlaceTacticalCamera();
        }

        void PlaceTacticalCamera()
        {
            var back = Quaternion.Euler(0, tacticalYaw, 0) * new Vector3(0, 1f, -0.75f);
            Cam.transform.position = tacticalFocus + back * tacticalHeight;
            Cam.transform.LookAt(tacticalFocus);
        }

        /// <summary>WASD pans, Q/E turn, the wheel zooms; T drops the player in at the point under the cursor.</summary>
        void UpdateTactical()
        {
            var map = GameManager.I.Map;
            var turn = Quaternion.Euler(0, tacticalYaw, 0);
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            float speed = TacticalPanSpeed * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f) * (tacticalHeight / 40f);
            tacticalFocus += turn * Vector3.ClampMagnitude(input, 1f) * speed * Time.unscaledDeltaTime;
            tacticalFocus.x = Mathf.Clamp(tacticalFocus.x, 0, map.Width);
            tacticalFocus.z = Mathf.Clamp(tacticalFocus.z, 0, map.Depth);
            if (Input.GetKey(KeyCode.Q)) tacticalYaw -= TacticalTurnSpeed * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) tacticalYaw += TacticalTurnSpeed * Time.unscaledDeltaTime;
            tacticalHeight = Mathf.Clamp(tacticalHeight * (1f - Input.mouseScrollDelta.y * 0.12f), TacticalMinHeight, TacticalMaxHeight);
            PlaceTacticalCamera();

            if (Input.GetKeyDown(KeyCode.T) &&
                Physics.Raycast(AimRay, out var hit, 400f, (1 << Layers.Road) | (1 << Layers.BuildGround), QueryTriggerInteraction.Ignore))
            {
                Teleport(hit.point);
                yaw = tacticalYaw;
                SetTactical(false);
            }
        }

        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position + Vector3.up * 0.1f;
            controller.enabled = true;
            verticalSpeed = 0;
        }

        void Update()
        {
            if (Tactical && GameManager.I.Phase != Phase.Build) SetTactical(false);
            if (InputLive && Input.GetKeyDown(KeyCode.M)) SetTactical(!Tactical);
            bool locked = InputLive && !Tactical;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (!InputLive) return;
            if (Tactical) { UpdateTactical(); return; }

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

        /// <summary>The ray from the centre of the screen, or from the mouse in the tactical view.</summary>
        public Ray AimRay => Tactical ? Cam.ScreenPointToRay(Input.mousePosition) : new Ray(Cam.transform.position, Cam.transform.forward);

        /// <summary>A small upward kick on firing.</summary>
        public void Kick(float degrees) => pitch = Mathf.Clamp(pitch - degrees, -85f, 85f);
    }
}
