using System;
using System.Collections;
using Oddity.Core;
using UnityEngine;

namespace Oddity.Game
{
    public enum PokeHit { None, Window, Head, Cheek }

    /// <summary>
    /// Where a touch on Harin's window lands, from the screen position of her Head bone. All sizes are in
    /// model metres (divided by her scale) measured from the eye line (Head + 7.5 cm), so a swapped
    /// model gets the zones from its own bones.
    /// </summary>
    public static class PokeZones
    {
        public const float EyeAboveHead = 0.075f;
        /// <summary>Head zone: above the eye line, this wide on each side and this high above it.</summary>
        public const float HeadHalfWidth = 0.09f, HeadTop = 0.14f;
        /// <summary>Cheek zone: from the eye line down to the chin, this wide on each side.</summary>
        public const float CheekHalfWidth = 0.075f, ChinBelowEye = 0.09f;
        /// <summary>Below the chin, this far to each side is body (neck, shoulders, chest): no reaction.</summary>
        public const float BodyHalfWidth = 0.22f;

        /// <summary>Viewport point of the camera for a spot dx to the right and dy above the eye line (model metres).</summary>
        public static Vector2 ViewportOf(Camera cam, HarinPuppet harin, float dx, float dy)
        {
            float scale = harin.transform.lossyScale.y;
            Vector3 eyes = harin.BonePosition(HumanBodyBones.Head) + harin.transform.up * (EyeAboveHead * scale);
            Vector3 v = cam.WorldToViewportPoint(eyes + cam.transform.right * (dx * scale) + cam.transform.up * (dy * scale));
            return new Vector2(v.x, v.y);
        }

        public static PokeHit Classify(Camera cam, HarinPuppet harin, Vector2 viewport)
        {
            if (cam == null || harin == null) return PokeHit.Window;
            Vector3 head = harin.BonePosition(HumanBodyBones.Head);
            if (head == Vector3.zero) return PokeHit.Window;
            float scale = Mathf.Max(0.0001f, harin.transform.lossyScale.y);
            Vector3 eyes = head + harin.transform.up * (EyeAboveHead * scale);
            var plane = new Plane(cam.transform.forward, eyes);
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            if (!plane.Raycast(ray, out float dist)) return PokeHit.Window;
            Vector3 d = ray.GetPoint(dist) - eyes;
            float dx = Mathf.Abs(Vector3.Dot(d, cam.transform.right)) / scale;
            float dy = Vector3.Dot(d, cam.transform.up) / scale;
            if (dy > 0f) return dx <= HeadHalfWidth && dy <= HeadTop ? PokeHit.Head : PokeHit.Window;
            if (dy >= -ChinBelowEye) return dx <= CheekHalfWidth ? PokeHit.Cheek : PokeHit.Window;
            return dx <= BodyHalfWidth ? PokeHit.None : PokeHit.Window;
        }
    }

    /// <summary>
    /// Touch reactions of Harin in the phone. Applies <see cref="PokeRules"/>, shows one bubble (not a script
    /// line, the script does not move) with her face and motion, and gives the face back after 2.5 s.
    /// Off in the prologue, the emergence and act 2; ignored while she types or her voice plays;
    /// only a short eye contact while choices are on screen.
    /// </summary>
    public class PokeController : MonoBehaviour
    {
        public const float FaceSeconds = 2.5f;
        public const string EyeContactMotion = "눈맞춤";

        /// <summary>Tests and shots replace the clock (seconds).</summary>
        public static Func<double> ClockOverride;

        GameBootstrap _boot;
        ChatView _chat;
        HarinPuppet _harinFromInit;
        // the model worn now (the bootstrap's wardrobe), else the one given to Init
        HarinPuppet _harin => _boot != null && _boot.Harin != null ? _boot.Harin : _harinFromInit;
        Camera _camera;
        PokeRules _rules;
        Coroutine _face;

        public PokeHit LastHit { get; private set; } = PokeHit.None;
        /// <summary>Reactions with a line shown since the scene started.</summary>
        public int Reactions { get; private set; }
        /// <summary>Touches answered with eye contact only (choices on screen, or the daily cap).</summary>
        public int EyeContacts { get; private set; }
        public PokeLine LastLine { get; private set; }
        public int Stage => _rules != null ? _rules.Stage : 0;
        public int PokesToday => _rules != null ? _rules.PokesToday : 0;

        static double Now() => ClockOverride != null ? ClockOverride() : Time.unscaledTimeAsDouble;

        public void Init(GameBootstrap boot, ChatView chat, HarinPuppet harin, GameState state, PokeTable table)
        {
            _boot = boot;
            _chat = chat;
            _harinFromInit = harin;
            _rules = new PokeRules(state, table, Environment.TickCount);
            var go = GameObject.Find("AppStageCamera");
            if (go != null) _camera = go.GetComponent<Camera>();
            _chat.WindowPoked += OnPoked;
        }

        void OnDestroy()
        {
            if (_chat != null) _chat.WindowPoked -= OnPoked;
        }

        /// <summary>Stops the face timer without touching her face (a script line takes over).</summary>
        public void Cancel()
        {
            if (_face != null) { StopCoroutine(_face); _face = null; }
        }

        bool Enabled => _boot != null && !_boot.InPrologue && !_boot.InRoom && !_boot.EmergenceBusy
            && _chat.IsVisible && !_chat.IsAskingName && _harin != null && _rules != null;

        void OnPoked(Vector2 viewport)
        {
            LastHit = PokeZones.Classify(_camera, _harin, viewport);
            if (!Enabled || LastHit == PokeHit.None) return;
            if (_chat.IsTyping || (_boot.Voice != null && _boot.Voice.IsPlaying)) return;
            if (_chat.VisibleButtonTexts.Count > 0)
            {
                EyeContact(); // choices on screen: no line, not counted
                return;
            }
            var zone = LastHit == PokeHit.Head ? PokeZone.Head : LastHit == PokeHit.Cheek ? PokeZone.Cheek : PokeZone.Window;
            PokeResult result = _rules.Poke(zone, Now());
            if (result.Outcome == PokeOutcome.EyeContact) EyeContact();
            else if (result.Outcome == PokeOutcome.Reaction) Show(result.Line);
        }

        void EyeContact()
        {
            EyeContacts++;
            _harin.PlayPokeMotion(EyeContactMotion);
        }

        void Show(PokeLine line)
        {
            Reactions++;
            LastLine = line;
            _chat.ShowHarinNote(line.Text);
            _harin.SetFace(HarinPuppet.FaceForBeat(line.Face, null));
            _harin.BlushForLine(line.Face, null);
            _harin.PlayPokeMotion(line.Motion);
            Cancel();
            if (isActiveAndEnabled) _face = StartCoroutine(FaceBack());
        }

        IEnumerator FaceBack()
        {
            yield return new WaitForSeconds(FaceSeconds);
            _face = null;
            if (_boot != null && !_boot.InRoom && _harin != null) _harin.SetFace(HarinPuppet.IdleFace);
        }
    }
}
