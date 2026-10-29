using System;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Harin's two models in the scene: the summer outfit B (the phone, the emergence and act 2) and the indoor
    /// outfit B2 (only the day 12 prologue at home). Exactly one is active; <see cref="Current"/> is the one in
    /// use. Both carry the same HarinPuppet settings, layer and rendering layers; each has its own cheek mask.
    /// The game swaps only at the start of the prologue and while the rewind screen is black.
    /// </summary>
    public class HarinWardrobe : MonoBehaviour
    {
        public enum Outfit { Summer, Indoor }

        [SerializeField] HarinPuppet summer;
        [SerializeField] HarinPuppet indoor;

        public HarinPuppet Summer => summer;
        public HarinPuppet Indoor => indoor;
        /// <summary>The outfit worn now (Summer when there is no indoor model).</summary>
        public Outfit Wearing => indoor != null && indoor.gameObject.activeSelf && (summer == null || !summer.gameObject.activeSelf)
            ? Outfit.Indoor : Outfit.Summer;
        /// <summary>The HarinPuppet in use now.</summary>
        public HarinPuppet Current => Get(Wearing) ?? summer ?? indoor;

        /// <summary>Fired after a swap with the puppet now in use.</summary>
        public event Action<HarinPuppet> Changed;

        public HarinPuppet Get(Outfit outfit) => outfit == Outfit.Indoor ? indoor : summer;

        /// <summary>Editor and tests: sets the two models.</summary>
        public void Set(HarinPuppet summerModel, HarinPuppet indoorModel)
        {
            summer = summerModel;
            indoor = indoorModel;
        }

        /// <summary>Turns <paramref name="outfit"/> on and the other model off. No-op if that model is missing.</summary>
        public void Wear(Outfit outfit)
        {
            var on = Get(outfit);
            if (on == null) return;
            var off = outfit == Outfit.Indoor ? summer : indoor;
            bool changed = !on.gameObject.activeSelf || (off != null && off.gameObject.activeSelf);
            if (off != null && off != on && off.gameObject.activeSelf)
            {
                off.SetBlush(0f);
                off.gameObject.SetActive(false);
            }
            on.gameObject.SetActive(true);
            if (changed) Changed?.Invoke(on);
        }

        /// <summary>The puppet in use: the wardrobe's current one, else the first active HarinPuppet.</summary>
        public static HarinPuppet ActivePuppet()
        {
            var w = FindFirstObjectByType<HarinWardrobe>();
            if (w != null && w.Current != null) return w.Current;
            return FindFirstObjectByType<HarinPuppet>();
        }
    }
}
