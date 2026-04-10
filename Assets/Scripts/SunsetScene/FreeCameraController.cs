// Setup:
// Add this to the scene camera for simple fly navigation while look-dev is in progress.
// Hold right mouse to look, use WASD to move, Q/E for vertical motion, Shift to boost, and mouse wheel to change speed.
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrulyAnYauMoment.SunsetScene
{
    [DisallowMultipleComponent]
    public sealed class FreeCameraController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
        [SerializeField, Min(0.1f)] private float boostMultiplier = 3f;
        [SerializeField, Min(0.01f)] private float lookSensitivity = 2.4f;
        [SerializeField, Min(0.1f)] private float verticalSpeed = 6f;
        [SerializeField, Min(0.1f)] private float speedStep = 1.5f;
        [SerializeField] private bool requireRightMouseForLook = true;

        private float yaw;
        private float pitch;

        private void OnEnable()
        {
            Vector3 euler = transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            bool allowLook = !requireRightMouseForLook || (mouse != null && mouse.rightButton.isPressed);

            if (allowLook)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                Vector2 mouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
                yaw += mouseDelta.x * lookSensitivity * 0.1f;
                pitch -= mouseDelta.y * lookSensitivity * 0.1f;
                pitch = Mathf.Clamp(pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            else if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            float wheel = ReadWheelSteps(mouse);
            if (Mathf.Abs(wheel) > 0.001f)
            {
                moveSpeed = Mathf.Max(0.1f, moveSpeed + wheel * speedStep);
            }

            Vector3 input = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed)
                {
                    input += transform.forward;
                }

                if (keyboard.sKey.isPressed)
                {
                    input -= transform.forward;
                }

                if (keyboard.dKey.isPressed)
                {
                    input += transform.right;
                }

                if (keyboard.aKey.isPressed)
                {
                    input -= transform.right;
                }
            }

            if (keyboard != null && keyboard.eKey.isPressed)
            {
                input += Vector3.up * (verticalSpeed / Mathf.Max(0.1f, moveSpeed));
            }

            if (keyboard != null && keyboard.qKey.isPressed)
            {
                input += Vector3.down * (verticalSpeed / Mathf.Max(0.1f, moveSpeed));
            }

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            bool boost = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            float speed = moveSpeed * (boost ? boostMultiplier : 1f);
            transform.position += input * speed * Time.deltaTime;
        }

        private static float ReadWheelSteps(Mouse mouse)
        {
            if (mouse == null)
            {
                return 0f;
            }

            float raw = mouse.scroll.ReadValue().y;
            return Mathf.Abs(raw) > 1f ? raw / 120f : raw;
        }
    }
}
