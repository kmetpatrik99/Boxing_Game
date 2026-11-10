using UnityEngine;

namespace BoxingGame
{
    /// <summary>
    /// Ustvari kratke zvočne učinke kar v igri. Ni glasbe, publike ali ambientalnega zvoka.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BoxingAudioEffects : MonoBehaviour
    {
        static AudioClip jabClip, crossClip, uppercutClip, walkClip, dashClip, bellClip;
        AudioSource source;
        float nextFootstep;
        bool wasWalking;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f; // zvok je jasno slišen ne glede na položaj borca
            source.volume = 0.8f;
            CreateClips();
        }

        static void CreateClips()
        {
            if (jabClip != null) return;
            // Sintetični zvoki pomenijo, da projekt ne potrebuje dodatnih zvočnih datotek.
            jabClip = MakeClip("Jab", 0.16f, 180f, 0.10f, 0.75f);
            crossClip = MakeClip("Cross", 0.22f, 125f, 0.16f, 0.85f);
            uppercutClip = MakeClip("Uppercut", 0.28f, 95f, 0.22f, 0.95f);
            walkClip = MakeClip("Footstep", 0.09f, 75f, 0.035f, 0.45f);
            dashClip = MakeClip("Dash", 0.18f, 260f, 0.07f, 0.65f);
            bellClip = MakeClip("RoundBell", 0.9f, 880f, 0.24f, 0.8f);
        }

        // Kratek ton z nekaj šuma, da zveni bolj kot udarec in ne kot glasba.
        static AudioClip MakeClip(string clipName, float duration, float frequency, float noise, float volume)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * duration);
            float[] samples = new float[count];
            uint seed = (uint)clipName.GetHashCode();
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * (clipName == "RoundBell" ? 2.8f : 18f));
                seed = seed * 1664525u + 1013904223u;
                float random = ((seed >> 8) / 16777216f) * 2f - 1f;
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float extra = clipName == "RoundBell" ? 0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 2.01f * t) : 0f;
                samples[i] = (tone + extra + random * noise) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(clipName, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static void PlayBell()
        {
            CreateClips();
            // Zvonček pred začetkom vsake runde.
            GameObject bellObject = new GameObject("Round Bell Audio");
            AudioSource bellSource = bellObject.AddComponent<AudioSource>();
            bellSource.spatialBlend = 0f;
            bellSource.PlayOneShot(bellClip, 0.85f);
            Object.Destroy(bellObject, 1.2f);
        }

        public void PlayPunch(PunchType type)
        {
            AudioClip clip = type == PunchType.Jab ? jabClip :
                             type == PunchType.Cross ? crossClip : uppercutClip;
            source.PlayOneShot(clip);
        }

        public void PlayDash() { source.PlayOneShot(dashClip); }

        // Korake predvajamo v intervalih, samo ko se borec dejansko premika.
        public void UpdateMovementSound(bool moving, float speed)
        {
            if (moving && Time.time >= nextFootstep)
            {
                source.PlayOneShot(walkClip, Mathf.Clamp01(speed));
                nextFootstep = Time.time + 0.34f;
            }
            wasWalking = moving;
        }
    }
}
