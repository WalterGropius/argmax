using System;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>RTS camera: arrows / edge pan / middle-drag, wheel zoom, space to snap home.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 Focus = new Vector3(32, 0, 32);
        public float Height = 46f;
        public float Pitch = 58f;
        public Func<bool> PointerOverUi = () => false;
        public Func<bool> KeyboardCaptured = () => false;
        public bool EdgePan = true;
        Vector2 mapSize = new Vector2(64, 64);
        Vector3 dragAnchor;
        bool dragging;
        float shake;

        /// <summary>Screen shake from big deaths, fusions and core hits. Decays in a fraction of a second.</summary>
        public void AddShake(float amount) { shake = Mathf.Min(0.7f, shake + amount); }

        public static CameraRig Create()
        {
            var go = new GameObject("Camera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 400f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            go.tag = "MainCamera";
            go.AddComponent<AudioListener>();
            go.AddComponent<NeonPost>();
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = cam;
            return rig;
        }

        public void Frame(int w, int h, Vector3 focus, float height)
        {
            mapSize = new Vector2(w, h);
            Focus = focus;
            Height = height;
            Place();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float speed = Height * 0.9f;
            var move = Vector3.zero;
            if (!KeyboardCaptured())
            {
                if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1;
                if (Input.GetKey(KeyCode.RightArrow)) move.x += 1;
                if (Input.GetKey(KeyCode.UpArrow)) move.z += 1;
                if (Input.GetKey(KeyCode.DownArrow)) move.z -= 1;
            }
            var mp = Input.mousePosition;
            bool inWindow = mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height && Application.isFocused;
            if (EdgePan && inWindow && !dragging)
            {
                const float edge = 6f;
                if (mp.x < edge) move.x -= 1;
                if (mp.x > Screen.width - edge) move.x += 1;
                if (mp.y < edge) move.z -= 1;
                if (mp.y > Screen.height - edge) move.z += 1;
            }
            Focus += move * speed * dt;

            if (Input.GetMouseButtonDown(2) && !PointerOverUi()) { dragging = Ground(mp, out dragAnchor); }
            if (Input.GetMouseButtonUp(2)) dragging = false;
            if (dragging && Ground(mp, out var now)) Focus += dragAnchor - now;

            if (!PointerOverUi() && inWindow)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) Height = Mathf.Clamp(Height * (1f - wheel * 0.1f), 12f, 90f);
            }
            Focus.x = Mathf.Clamp(Focus.x, -4, mapSize.x + 4);
            Focus.z = Mathf.Clamp(Focus.z, -4, mapSize.y + 4);
            shake = Mathf.Max(0f, shake - dt * 2.2f);
            Place();
        }

        void Place()
        {
            var rot = Quaternion.Euler(Pitch, 0, 0);
            float back = Height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
            var jitter = Vector3.zero;
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 38f, k = shake * shake * Height * 0.02f;
                jitter = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, 0, Mathf.PerlinNoise(0.7f, t) - 0.5f) * k;
            }
            transform.SetPositionAndRotation(Focus + jitter + new Vector3(0, Height, -back), rot);
        }

        /// <summary>Screen point to ground plane (y = 0).</summary>
        public bool Ground(Vector3 screen, out Vector3 hit)
        {
            var ray = Cam.ScreenPointToRay(screen);
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }
    }
}
