using UnityEngine;

namespace LastLight
{
    /// <summary>Short procedural cues. They stand in for controller vibration and warn about off-beam danger.</summary>
    public class SoundKit
    {
        public AudioClip grab, commit, cancel, sweep, hit, rumble, brake, gameOver, coin, powerup, scrape, shieldBreak, spit, splash, gun, smash, thunder, skid, gazeArm;
        readonly AudioSource source, rain;

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
            spit = Tone("spit", 0.22f, 260f, 620f, 0.3f);
            splash = Noise("splash", 0.35f, 0.4f, 5f);
            gun = Noise("gun", 0.05f, 0.5f, 9f);
            smash = Noise("smash", 0.4f, 0.55f, 1.5f);
            thunder = Noise("thunder", 2.2f, 0.75f, 0.25f);
            skid = Noise("skid", 0.45f, 0.3f, 8f);
            gazeArm = Tone("gazearm", 0.1f, 990f, 1480f, 0.25f);

            // A looping hiss of rain, faded with the storm.
            rain = host.AddComponent<AudioSource>();
            rain.playOnAwake = false;
            rain.spatialBlend = 0f;
            rain.loop = true;
            rain.volume = 0f;
            rain.clip = Noise("rain", 2f, 0.35f, 9f, true);

            // The BGM loops, rendered offline by the scripts in Tools/Audio. They stream, so they cost little memory.
            // The run family (run, low health, horde) share tempo, length and chords, and play together in sync.
            runMusic = MusicSource(host, "LastLightAudio/LL_BGM_Run");
            lowHealthMusic = MusicSource(host, "LastLightAudio/LL_BGM_LowHealth");
            hordeMusic = MusicSource(host, "LastLightAudio/LL_BGM_Horde");
            menuMusic = MusicSource(host, "LastLightAudio/LL_BGM_Menu");
        }

        // Full levels for each loop; the low-health loop is denser in the bass, so it sits a little lower.
        const float MusicRun = 0.34f, MusicLowHealth = 0.3f, MusicHorde = 0.34f, MusicMenu = 0.3f;
        const float PausedScale = 0.35f, GameOverScale = 0.2f;   // the run family on pause and on the run-over panel
        const float MusicFade = 0.4f;     // volume per second between menu and run: a crossfade of about a second
        const float DangerFadeIn = 0.9f;  // into a danger loop quickly, so it lands with the danger
        const float DangerFadeOut = 0.35f;
        const float HordeCloseEnter = 0.3f, HordeCloseExit = 0.4f;   // fractions of the full lead (as the HUD warning)
        readonly AudioSource runMusic, lowHealthMusic, hordeMusic, menuMusic;
        bool runFamilyPlaying, hordeClose;

        static AudioSource MusicSource(GameObject host, string clip)
        {
            var s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.loop = true;
            s.volume = 0f;
            s.priority = 0;
            s.clip = Resources.Load<AudioClip>(clip);
            return s;
        }

        /// <summary>
        /// Crossfades between the menu loop (main menu, tutorial, garage) and the run family. Within a run the
        /// three run loops play in lockstep and only their volumes move: the horde loop takes over while the horde is
        /// close (lead under 30% of the full gap, released above 40%), otherwise the low-health loop while the jeep is
        /// down to one bar, otherwise the run loop. The family plays softer on pause (and in the pause menu) and nearly gone on the run-over
        /// panel. A loop that fades out stops, so it starts again from the top next time.
        /// </summary>
        /// <param name="health">The jeep's health in bars.</param>
        /// <param name="hordeLead">The lead on the horde as a fraction of the full lead (Gap / maxGap).</param>
        public void TickMusic(RunState state, float dt, int health, float hordeLead)
        {
            if (!Application.isPlaying) return;
            bool menu = state == RunState.Menu || state == RunState.Tutorial || state == RunState.Garage;
            // Any other state mid-run (Paused, the pause menu) counts as paused: the music drops but keeps its place.
            bool live = !menu && state != RunState.GameOver;
            hordeClose = live && (hordeClose ? hordeLead < HordeCloseExit : hordeLead < HordeCloseEnter);
            bool lowHealth = live && health == 1;
            float scale = menu ? 0f : state switch
            {
                RunState.Running => 1f,
                RunState.GameOver => GameOverScale,
                _ => PausedScale,
            };

            float run = 0f, low = 0f, horde = 0f;
            if (hordeClose) horde = MusicHorde * scale;
            else if (lowHealth) low = MusicLowHealth * scale;
            else run = MusicRun * scale;

            if (!menu && !runFamilyPlaying) StartRunFamily();
            // Danger loops come in fast and leave slowly; the run loop gets out of the way just as fast.
            FadeLayer(runMusic, run, dt, DangerFadeOut, DangerFadeIn);
            FadeLayer(lowHealthMusic, low, dt, DangerFadeIn, DangerFadeOut);
            FadeLayer(hordeMusic, horde, dt, DangerFadeIn, DangerFadeOut);
            if (menu && runFamilyPlaying && runMusic.volume <= 0f && lowHealthMusic.volume <= 0f && hordeMusic.volume <= 0f)
            {
                runMusic.Stop();
                lowHealthMusic.Stop();
                hordeMusic.Stop();
                runFamilyPlaying = false;
            }
            FadeMusic(menuMusic, menu ? MusicMenu : 0f, dt);
        }

        /// <summary>Starts the three run loops on the same audio clock tick, so they stay sample-locked.</summary>
        void StartRunFamily()
        {
            double start = AudioSettings.dspTime + 0.1;
            foreach (var s in new[] { runMusic, lowHealthMusic, hordeMusic })
            {
                if (s.clip == null) continue;
                s.volume = 0f;
                s.PlayScheduled(start);
            }
            runFamilyPlaying = true;
        }

        static void FadeLayer(AudioSource s, float target, float dt, float upSpeed, float downSpeed)
        {
            if (s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, target, (target > s.volume ? upSpeed : downSpeed) * dt);
        }

        static void FadeMusic(AudioSource s, float target, float dt)
        {
            if (s.clip == null) return;
            if (target > 0f && !s.isPlaying) s.Play();
            s.volume = Mathf.MoveTowards(s.volume, target, MusicFade * dt);
            if (target <= 0f && s.volume <= 0f && s.isPlaying) s.Stop();
        }

        /// <summary>Sets the rain loop's volume, starting or stopping it as needed.</summary>
        public void SetRain(float volume)
        {
            if (!Application.isPlaying) return;
            rain.volume = volume;
            if (volume > 0.001f && !rain.isPlaying) rain.Play();
            else if (volume <= 0.001f && rain.isPlaying) rain.Stop();
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

        static AudioClip Noise(string name, float seconds, float vol, float smooth, bool loop = false)
        {
            int rate = 44100, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f, k = Mathf.Clamp01(smooth / 10f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                y = Mathf.Lerp(y, (float)rng.NextDouble() * 2f - 1f, k);
                // A loop keeps a steady level so its seam doesn't pulse.
                data[i] = y * vol * (loop ? 1f : Mathf.Sin(t * Mathf.PI));
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
