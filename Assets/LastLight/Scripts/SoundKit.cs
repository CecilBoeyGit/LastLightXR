using UnityEngine;

namespace LastLight
{
    /// <summary>Short procedural cues. They stand in for controller vibration and warn about off-beam danger.</summary>
    public class SoundKit
    {
        public AudioClip grab, commit, cancel, sweep, hit, rumble, brake, gameOver, coin, powerup, scrape, shieldBreak;
        readonly AudioSource source;

        public SoundKit(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            grab = Tone("grab", 0.06f, 880f, 880f, 0.3f);
            commit = Tone("commit", 0.16f, 660f, 1320f, 0.35f);
            cancel = Tone("cancel", 0.12f, 520f, 300f, 0.25f);
            sweep = Noise("sweep", 0.25f, 0.35f, 2.5f);
            hit = Thud("hit", 0.35f);
            rumble = Noise("rumble", 0.9f, 0.45f, 0.4f);
            brake = Tone("brake", 0.1f, 300f, 240f, 0.2f);
            gameOver = Tone("gameover", 0.8f, 440f, 110f, 0.4f);
            coin = Tone("coin", 0.14f, 1320f, 1760f, 0.3f);
            powerup = Tone("powerup", 0.35f, 440f, 1320f, 0.35f);
            scrape = Noise("scrape", 0.22f, 0.25f, 6f);
            shieldBreak = Tone("shieldbreak", 0.3f, 900f, 200f, 0.4f);
        }

        public void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null && Application.isPlaying) source.PlayOneShot(clip, volume);
        }

        static AudioClip Tone(string name, float seconds, float f0, float f1, float vol)
        {
            int rate = 44100, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
                float env = Mathf.Min(1f, t * 30f) * (1f - t);
                data[i] = Mathf.Sin(phase) * env * vol;
            }
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Noise(string name, float seconds, float vol, float smooth)
        {
            int rate = 44100, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f, k = Mathf.Clamp01(smooth / 10f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                y = Mathf.Lerp(y, (float)rng.NextDouble() * 2f - 1f, k);
                data[i] = y * vol * Mathf.Sin(t * Mathf.PI);
            }
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Thud(string name, float seconds)
        {
            int rate = 44100, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-t * 9f);
                data[i] = (Mathf.Sin(2f * Mathf.PI * 70f * i / rate) * 0.8f + ((float)rng.NextDouble() - 0.5f) * 0.4f) * env * 0.6f;
            }
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
