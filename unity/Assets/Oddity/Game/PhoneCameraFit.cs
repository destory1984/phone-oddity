using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Keeps the camera looking straight at the phone screen from a fixed distance and sets the field of
    /// view so the whole screen just fills the frame, whatever the aspect ratio. Act 1 uses this camera;
    /// the emergence sequence (Task 9) turns the component off before it pulls the camera back.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PhoneCameraFit : MonoBehaviour
    {
        [SerializeField] float screenWidth = 0.072f;
        [SerializeField] float screenHeight = 0.128f;
        [SerializeField] float distance = 0.2389f;

        Camera _camera;

        void Awake() => _camera = GetComponent<Camera>();

        void LateUpdate()
        {
            // The screen must fit in both directions; the tighter one decides the vertical field of view.
            float halfHeight = Mathf.Max(screenHeight * 0.5f, screenWidth * 0.5f / _camera.aspect);
            _camera.fieldOfView = 2f * Mathf.Atan(halfHeight / distance) * Mathf.Rad2Deg;
        }
    }
}
