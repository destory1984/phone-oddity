using System;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// The 10 second "Harin comes out of the phone" scene, written as a script instead of a Timeline:
    /// <see cref="Evaluate"/> sets every object from the time alone, so tests can run it faster with
    /// <see cref="Speed"/> and screenshots can jump to any moment with <see cref="Seek"/>.
    ///   0 → 2  glitch 0 → 1, she looks to the side (towards the room)
    ///   2 → 4  her right hand pushes up out of the phone screen (she is shrunk and placed under the phone)
    ///   4      MoveTo(RoomAnchor) at full size, phone UI hidden
    ///   4 → 9  MainCamera pulls back from the phone to a view of the whole room
    ///   9 → 10 glitch 1 → 0, surprised, looking down at her hand
    /// Harin is never disabled or replaced; only her transform, pose and face change.
    /// </summary>
    public class EmergenceSequence : MonoBehaviour
    {
        public const float HandStart = 2f;
        public const float RoomAt = 4f;
        public const float PullbackEnd = 9f;

        const float HandScale = 0.3f;
        const float HandEmergeMax = 0.3f;      // model units of arm above the screen at 4 s
        const float HandElevation = 30f;       // degrees between the arm and the screen
        const float HandOffsetAlongScreen = 0.04f; // metres towards the top of the screen
        const float FinalFov = 60f;

        static readonly Vector3 RoomAnchorPosition = new Vector3(0.1f, 0f, 0.5f);
        static readonly Vector3 FinalCameraPosition = new Vector3(0.75f, 1.45f, -1.7f);
        static readonly Vector3 PullbackControl = new Vector3(1.6f, 1.9f, 0.3f);
        const float LookAtHeight = 0.95f;

        [SerializeField] HarinPuppet harin;
        [SerializeField] ChatView chat;
        [SerializeField] Camera mainCamera;
        [SerializeField] Transform phone;
        [SerializeField] Transform roomAnchor;

        /// <summary>Playback speed multiplier (tests use more than 1).</summary>
        public float Speed = 1f;

        public event Action Finished;

        public float Duration => 10f;
        public float Time { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool HasFinished { get; private set; }
        public Transform RoomAnchor => roomAnchor;
        public Camera MainCamera => mainCamera;

        Transform _home;       // where Harin stood in the app stage
        Vector3 _homeScale;
        Transform _handAnchor; // moved every frame during the hand part
        Vector3 _camStartPos;
        Quaternion _camStartRot;
        float _camStartFov;
        PhoneCameraFit _fit;
        bool _prepared;

        void Awake() => FindReferences();

        void FindReferences()
        {
            if (harin == null) harin = FindFirstObjectByType<HarinPuppet>();
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (mainCamera == null)
            {
                var go = GameObject.Find("MainCamera");
                mainCamera = go != null ? go.GetComponent<Camera>() : Camera.main;
            }
            if (phone == null)
            {
                var go = GameObject.Find("Phone");
                if (go != null) phone = go.transform;
            }
            if (roomAnchor == null)
            {
                var go = GameObject.Find("RoomAnchor");
                if (go == null)
                {
                    go = new GameObject("RoomAnchor");
                    var facing = FinalCameraPosition - RoomAnchorPosition;
                    facing.y = 0f;
                    go.transform.SetPositionAndRotation(RoomAnchorPosition, Quaternion.LookRotation(facing));
                }
                roomAnchor = go.transform;
            }
        }

        public void Play()
        {
            Prepare();
            Time = 0f;
            HasFinished = false;
            IsPlaying = true;
            Evaluate(0f);
        }

        /// <summary>Jumps to time t (seconds) and holds there while <see cref="Speed"/> is 0.</summary>
        public void Seek(float t)
        {
            if (!IsPlaying && !HasFinished) Play();
            Time = Mathf.Clamp(t, 0f, Duration);
            Evaluate(Time);
            if (Time >= Duration) Finish();
        }

        void Update()
        {
            if (!IsPlaying) return;
            Time = Mathf.Min(Duration, Time + UnityEngine.Time.deltaTime * Speed);
            Evaluate(Time);
            if (Time >= Duration) Finish();
        }

        void Finish()
        {
            if (HasFinished) return;
            IsPlaying = false;
            HasFinished = true;
            Finished?.Invoke();
        }

        void Prepare()
        {
            if (_prepared) return;
            FindReferences();
            if (harin == null || mainCamera == null || phone == null)
                throw new InvalidOperationException("EmergenceSequence: Harin, MainCamera or Phone missing");

            var ht = harin.transform;
            _home = new GameObject("HarinHome").transform;
            _home.SetPositionAndRotation(ht.position, ht.rotation);
            _home.SetParent(ht.parent, true);
            _homeScale = ht.localScale;
            _handAnchor = new GameObject("HarinHandAnchor").transform;

            _camStartPos = mainCamera.transform.position;
            _camStartRot = mainCamera.transform.rotation;
            _camStartFov = mainCamera.fieldOfView;
            _fit = mainCamera.GetComponent<PhoneCameraFit>();

            // let the room lights (rendering layer 1) light her too once she is in the room
            foreach (var r in harin.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= 1u;
            _prepared = true;
        }

        /// <summary>Sets every object for time t. Safe to call repeatedly and in any order.</summary>
        public void Evaluate(float t)
        {
            if (!_prepared) Prepare();
            var ht = harin.transform;

            // glitch: 0 → 1 over 0..2, hold, 1 → 0 over 9..10
            float glitch = t < HandStart ? t / HandStart : t < PullbackEnd ? 1f : Mathf.Clamp01(Duration - t);
            harin.SetGlitch(glitch);
            if (chat != null) chat.SetGlitch(Mathf.Min(glitch, 1f));

            if (t < HandStart)
            {
                ht.localScale = _homeScale;
                harin.MoveTo(_home);
                harin.SetFace("relaxed");
                harin.SetRightArmReach(null);
                harin.SetHeadTurn(35f * Smooth(t / 1f), 0f);
            }
            else if (t < RoomAt)
            {
                PlaceHand(Smooth((t - HandStart) / (RoomAt - HandStart)));
            }
            else
            {
                ht.localScale = _homeScale;
                harin.MoveTo(roomAnchor);
                if (t < PullbackEnd)
                {
                    harin.SetFace("relaxed");
                    harin.SetRightArmReach(null);
                    harin.SetHeadTurn(0f, 0f);
                }
                else
                {
                    // looks down at the hand that came through
                    harin.SetFace("surprised");
                    harin.SetRightArmReach(new Vector3(0.15f, -0.25f, 1f), Vector3.up);
                    harin.SetHeadTurn(-10f, 28f);
                }
            }

            bool inPhone = t < RoomAt;
            if (chat != null) chat.SetVisible(inPhone);
            if (_fit != null) _fit.enabled = inPhone;
            if (inPhone)
            {
                mainCamera.transform.SetPositionAndRotation(_camStartPos, _camStartRot);
                if (_fit == null) mainCamera.fieldOfView = _camStartFov;
            }
            else
            {
                PlaceCamera(Smooth((t - RoomAt) / (PullbackEnd - RoomAt)));
            }
        }

        // Shrunk Harin under the phone, diving up through the screen with her right arm overhead.
        // The desk and the phone body hide everything below the screen, so only the arm shows.
        void PlaceHand(float u)
        {
            var ht = harin.transform;
            float s = HandScale * _homeScale.x;
            ht.localScale = _homeScale * HandScale;

            float a = HandElevation * Mathf.Deg2Rad;
            var screenUp = phone.forward; // top of the screen as the camera sees it
            var d = (Vector3.up * Mathf.Sin(a) - screenUp * Mathf.Cos(a)).normalized; // reach direction
            var front = Vector3.Cross(d, phone.right).normalized;
            if (front.y < 0f) front = -front;
            var rot = Quaternion.LookRotation(front, d); // her up = d, her front faces the camera

            float screenY = phone.position.y + 0.0085f;
            var p = new Vector3(phone.position.x, screenY, phone.position.z) + screenUp * HandOffsetAlongScreen;
            float emerged = HandEmergeMax * u;
            var shoulder = p - d * ((harin.RightArmLength - emerged) * s);
            var root = shoulder - rot * (harin.RightShoulderLocal * s);

            _handAnchor.SetPositionAndRotation(root, rot);
            harin.MoveTo(_handAnchor);
            harin.SetFace("relaxed");
            harin.SetHeadTurn(0f, 0f);
            harin.SetRightArmReach(Vector3.up, Vector3.forward); // overhead, palm towards her front
        }

        void PlaceCamera(float u)
        {
            var harinChest = roomAnchor.position + Vector3.up * LookAtHeight;
            // quadratic Bezier that swings out past her side instead of passing through her
            var a = Vector3.Lerp(_camStartPos, PullbackControl, u);
            var b = Vector3.Lerp(PullbackControl, FinalCameraPosition, u);
            var pos = Vector3.Lerp(a, b, u);
            // the camera keeps looking at something: the phone first, Harin soon after
            var target = Vector3.Lerp(phone.position, harinChest, Smooth(u * 1.6f));
            var startUp = _camStartRot * Vector3.up;
            var up = Vector3.Slerp(startUp, Vector3.up, Smooth(u * 3f));
            var rot = u >= 1f ? Quaternion.LookRotation(harinChest - pos) : Quaternion.LookRotation(target - pos, up);
            mainCamera.transform.SetPositionAndRotation(pos, rot);
            mainCamera.fieldOfView = Mathf.Lerp(_camStartFov, FinalFov, u);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
