using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WarehouseASRS
{
    /// <summary>
    /// Orbit camera (left-drag rotate, right/middle-drag pan, wheel zoom). A click without dragging is reported through onClick.
    /// The projection is shifted so the view centre sits between the UI panels.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Vector3 focus = new Vector3(12, 0, 0);
        public float yaw = 0, pitch = 52, distance = 95;
        [Tooltip("Left/right UI panel widths in screen pixels")] public float panelLeft, panelRight;
        public Func<Vector2, bool> isOverUI;
        public Func<Vector3?> follow;
        public event Action<Vector2> onClick;
        [NonSerialized] public Vector2 mouse;

        Camera cam;
        Vector2 pressPos, last;
        bool pressed, dragging, panDrag;
        public Camera Cam => cam != null ? cam : cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (Application.isPlaying) HandleInput();
            if (follow != null)
            {
                var f = follow();
                if (f.HasValue)
                {
                    float k = Application.isPlaying ? 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3) : 1;
                    focus = Vector3.Lerp(focus, f.Value, k);
                }
            }
            Place();
        }

        void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return;
            Vector2 pos = m.position.ReadValue();
            mouse = pos;
            bool overUI = isOverUI != null && isOverUI(pos);
            bool l = m.leftButton.isPressed, r = m.rightButton.isPressed || m.middleButton.isPressed;
            if ((m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) && !overUI)
            {
                pressed = true; dragging = false; pressPos = last = pos; panDrag = !m.leftButton.wasPressedThisFrame;
            }
            if (pressed && (l || r))
            {
                var d = pos - last; last = pos;
                if ((pos - pressPos).sqrMagnitude > 25) dragging = true;
                if (dragging)
                {
                    if (panDrag) Pan(d);
                    else { yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, 8f, 89f); }
                }
            }
            if (pressed && !l && !r)
            {
                if (!dragging && !panDrag) onClick?.Invoke(pos);
                pressed = false; dragging = false;
            }
            float wheel = m.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !overUI) distance = Mathf.Clamp(distance * (wheel > 0 ? 0.88f : 1.13f), 4f, 260f);
#endif
        }

        void Pan(Vector2 d)
        {
            var c = Cam;
            float wpp = distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2 / Mathf.Max(1, c.pixelHeight);
            follow = null;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            focus -= (right * d.x + fwd * d.y / Mathf.Max(0.3f, Mathf.Sin(pitch * Mathf.Deg2Rad))) * wpp;
        }

        public void Place()
        {
            var c = Cam;
            c.ResetProjectionMatrix();
            c.orthographic = false;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.position = focus + rot * new Vector3(0, 0, -distance);
            transform.rotation = rot;
            c.nearClipPlane = Mathf.Max(0.05f, distance * 0.004f); c.farClipPlane = 1500;
            if (Screen.width > 0 && (panelLeft > 0 || panelRight > 0))
            {
                float f = (panelLeft - panelRight) / Mathf.Max(1, c.pixelWidth);
                var p = c.projectionMatrix;
                p.m02 = -f;
                c.projectionMatrix = p;
            }
        }

        public void View(Vector3 f, float y, float p, float d) { focus = f; yaw = y; pitch = p; distance = d; follow = null; }
    }
}
