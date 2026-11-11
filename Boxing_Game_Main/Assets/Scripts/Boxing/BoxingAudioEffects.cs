using UnityEngine;

namespace BoxingGame
{
    /// <summary>
    /// Proceduralni zvočni učinki za boks: udarci, koraki, izmik in zvonec.
    /// Zvoki se ustvarijo v pomnilniku, zato niso potrebne zunanje zvočne datoteke.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BoxingAudioEffects : MonoBehaviour
    {
        static AudioClip jabClip, crossClip, uppercutClip, walkClip, dashClip, bellClip;
        AudioSource source;
        float nextFootstep;

        // Ob zagonu pripravi AudioSource in enkrat ustvari vse kratke zvočne posnetke.
        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0.62f;
            source.pitch = 1f;
            CreateClips();
        }

        // Ustvari bolj naravne zvoke: udarci imajo kratek top udarec, dash zračni šum,
        // zvonec pa več rahlo razglašenih kovinskih tonov z mehkim pojemanjem.
        static void CreateClips()
        {
            if (jabClip != null) return;

            jabClip = MakeImpact("Jab", 0.19f, 115f, 0.72f, 0.20f);
            crossClip = MakeImpact("Cross", 0.23f, 88f, 0.88f, 0.24f);
            uppercutClip = MakeImpact("Uppercut", 0.27f, 72f, 1.0f, 0.27f);
            walkClip = MakeImpact("Footstep", 0.11f, 58f, 0.35f, 0.08f);
            dashClip = MakeWhoosh("Dash", 0.25f);
            bellClip = MakeBell("RoundBell", 1.25f);
        }

        // Sestavi kratek udarec iz nizkega tona in filtriranega šuma.
        static AudioClip MakeImpact(string clipName, float duration, float pitch, float gain, float noiseAmount)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * duration);
            float[] samples = new float[count];
            uint seed = StableSeed(clipName);
            float previousNoise = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float attack = Mathf.Clamp01(t / 0.006f);
                float decay = Mathf.Exp(-t * (clipName == "Footstep" ? 30f : 17f));
                seed = seed * 1664525u + 1013904223u;
                float white = ((seed >> 8) / 16777216f) * 2f - 1f;
                previousNoise = Mathf.Lerp(previousNoise, white, 0.22f);
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t * (1f - 0.22f * t / duration));
                float click = Mathf.Sin(2f * Mathf.PI * 1450f * t) * Mathf.Exp(-t * 95f) * 0.12f;
                samples[i] = (thump * 0.72f + previousNoise * noiseAmount + click) * decay * attack * gain;
            }

            AudioClip clip = AudioClip.Create(clipName, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Zračni "whoosh" z naraščajočim in nato padajočim šumom za izmik.
        static AudioClip MakeWhoosh(string clipName, float duration)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * duration);
            float[] samples = new float[count];
            uint seed = StableSeed(clipName);
            float filtered = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate / duration;
                seed = seed * 1664525u + 1013904223u;
                float white = ((seed >> 8) / 16777216f) * 2f - 1f;
                filtered = Mathf.Lerp(filtered, white, 0.12f);
                float envelope = Mathf.Sin(Mathf.PI * t);
                float airyTone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 180f, t) * (i / (float)rate));
                samples[i] = (filtered * 0.75f + airyTone * 0.12f) * envelope * 0.22f;
            }

            AudioClip clip = AudioClip.Create(clipName, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Mehkejši kovinski zvonec: več rahlo razglašenih parcialnih tonov namesto glasnega piska.
        static AudioClip MakeBell(string clipName, float duration)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 3.4f) * Mathf.Clamp01(t / 0.012f);
                float metal =
                    Mathf.Sin(2f * Mathf.PI * 620f * t) * 0.55f +
                    Mathf.Sin(2f * Mathf.PI * 925f * t) * 0.24f +
                    Mathf.Sin(2f * Mathf.PI * 1247f * t) * 0.13f +
                    Mathf.Sin(2f * Mathf.PI * 1610f * t) * 0.08f;
                samples[i] = metal * envelope * 0.16f;
            }

            AudioClip clip = AudioClip.Create(clipName, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Stabilno seme zagotovi enak zvok ob vsakem zagonu igre.
        static uint StableSeed(string value)
        {
            uint seed = 2166136261u;
            for (int i = 0; i < value.Length; i++)
                seed = (seed ^ value[i]) * 16777619u;
            return seed;
        }

        // Predvaja tišji zvonec ob začetku nove runde.
        public static void PlayBell()
        {
            CreateClips();
            GameObject bellObject = new GameObject("Round Bell Audio");
            AudioSource bellSource = bellObject.AddComponent<AudioSource>();
            bellSource.spatialBlend = 0f;
            bellSource.volume = 0.55f;
            bellSource.PlayOneShot(bellClip, 0.62f);
            Object.Destroy(bellObject, 1.6f);
        }

        // Predvaja zvok glede na vrsto izvedenega udarca.
        public void PlayPunch(PunchType type)
        {
            AudioClip clip = type == PunchType.Jab ? jabClip :
                             type == PunchType.Cross ? crossClip : uppercutClip;
            source.PlayOneShot(clip, 0.72f);
        }

        // Predvaja kratek zračni zvok izmika.
        public void PlayDash() { source.PlayOneShot(dashClip, 0.75f); }

        // Predvaja korake v enakomernih intervalih, ko se borec premika.
        public void UpdateMovementSound(bool moving, float speed)
        {
            if (moving && Time.time >= nextFootstep)
            {
                source.PlayOneShot(walkClip, Mathf.Clamp01(speed) * 0.65f);
                nextFootstep = Time.time + 0.38f;
            }
        }
    }
}
