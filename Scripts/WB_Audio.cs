// «Война шариков» — procedural sound effects and music (no audio files: everything is synthesised at startup).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public class Synth
    {
        public float[] buf; public int sr; float len;
        public Synth(int sampleRate, float seconds) { sr = sampleRate; len = seconds; buf = new float[Mathf.CeilToInt(sampleRate * seconds)]; }
        public static float Mtof(float m) { return 440f * Mathf.Pow(2f, (m - 69f) / 12f); }
        static float Env(float x, float a, float d, float v)
        {
            if (x < 0 || x > d) return 0;
            if (x < a) return 0.0001f * Mathf.Pow(v / 0.0001f, x / a);
            return v * Mathf.Pow(0.0001f / v, (x - a) / Mathf.Max(1e-4f, d - a));
        }
        // a biquad filter (RBJ cookbook)
        class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public void Set(string type, float f, float q, int sr)
            {
                f = Mathf.Clamp(f, 20, sr * 0.45f); float w = 2 * Mathf.PI * f / sr, cs = Mathf.Cos(w), sn = Mathf.Sin(w), al = sn / (2 * Mathf.Max(0.05f, q)), a0;
                if (type == "lowpass") { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2; }
                else if (type == "highpass") { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2; }
                else { b0 = al; b1 = 0; b2 = -al; }
                a0 = 1 + al; a1 = -2 * cs; a2 = 1 - al; b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            }
            public float Run(float x) { float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
        }
        static System.Random rng = new System.Random(1);
        public void Tone(float f, float d, string type = "sine", float vol = 0.2f, float delay = 0, float to = 0, float a = 0.005f, float lp = 0, float q = 1, float det = 0, bool wrap = false)
        {
            int s0 = Mathf.RoundToInt(delay * sr), n = Mathf.RoundToInt((d + 0.05f) * sr); float ph = 0; Biquad bq = lp > 0 ? new Biquad() : null; if (bq != null) bq.Set("lowpass", lp, q, sr);
            float fm = Mathf.Pow(2f, det / 1200f);
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i; if (!wrap && k >= buf.Length) break;
                float x = (float)i / sr, fr = (to > 0 ? f * Mathf.Pow(Mathf.Max(20, to) / f, Mathf.Min(1, x / d)) : f) * fm;
                ph += fr / sr; ph -= Mathf.Floor(ph);
                float w;
                switch (type)
                {
                    case "square": w = ph < 0.5f ? 1 : -1; break;
                    case "sawtooth": w = 2 * ph - 1; break;
                    case "triangle": w = ph < 0.5f ? 4 * ph - 1 : 3 - 4 * ph; break;
                    default: w = Mathf.Sin(2 * Mathf.PI * ph); break;
                }
                if (bq != null) w = bq.Run(w);
                buf[wrap ? k % buf.Length : k] += w * Env(x, a, d, vol);
            }
        }
        public void Noise(float d, float f = 1000, float to = 0, string ft = "bandpass", float q = 1, float vol = 0.2f, float a = 0.005f, float delay = 0, bool wrap = false)
        {
            int s0 = Mathf.RoundToInt(delay * sr), n = Mathf.RoundToInt((d + 0.05f) * sr); Biquad bq = new Biquad(); bq.Set(ft, f, q, sr);
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i; if (!wrap && k >= buf.Length) break;
                float x = (float)i / sr;
                if (to > 0 && (i & 15) == 0) bq.Set(ft, f * Mathf.Pow(to / f, Mathf.Min(1, x / d)), q, sr);
                float w = bq.Run((float)rng.NextDouble() * 2 - 1);
                buf[wrap ? k % buf.Length : k] += w * Env(x, a, d, vol);
            }
        }
        public AudioClip Clip(string name, float gain = 1f)
        {
            float peak = 0; for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i] * gain));
            float k = peak > 0.95f ? 0.95f / peak : 1f; float[] o = new float[buf.Length]; for (int i = 0; i < buf.Length; i++) o[i] = buf[i] * gain * k;
            AudioClip c = AudioClip.Create(name, o.Length, 1, sr, false); c.SetData(o, 0); return c;
        }
    }

    public static class Snd
    {
        public static bool on = true;
        static AudioSource sfx, mus; static bool wantMusic;
        static Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static int SR = 22050;
        public static void Init(GameObject host, bool enabled)
        {
            on = enabled; clips.Clear();
            sfx = host.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0; sfx.volume = 0.75f;
            mus = host.AddComponent<AudioSource>(); mus.playOnAwake = false; mus.loop = true; mus.spatialBlend = 0; mus.volume = 0.3f;
            mus.clip = WarMusic();
            AudioListener.volume = on ? 1f : 0f;
        }
        public static void SetOn(bool v)
        {
            on = v; AudioListener.volume = v ? 1f : 0f;
            if (mus == null) return;
            if (!v) mus.Pause(); else if (wantMusic) { if (mus.time > 0) mus.UnPause(); else mus.Play(); }
        }
        public static void Music(bool play)
        {
            wantMusic = play; if (mus == null) return;
            if (play) { if (on && !mus.isPlaying) mus.Play(); } else mus.Stop();
        }
        public static void Fx(string n, int arg = 0)
        {
            if (!on || sfx == null) return;
            string key = (n == "place" || n == "coin") ? n + (arg % 11) : n;
            AudioClip c; if (!clips.TryGetValue(key, out c)) { c = Make(n, arg); clips[key] = c; }
            if (c != null) sfx.PlayOneShot(c, 1f);
        }
        static AudioClip Make(string n, int i)
        {
            Synth s;
            switch (n)
            {
                case "click": s = new Synth(SR, 0.12f); s.Tone(880, 0.06f, "triangle", 0.12f); break;
                case "select": s = new Synth(SR, 0.2f); s.Tone(660, 0.08f, "triangle", 0.14f); s.Tone(990, 0.1f, "sine", 0.08f, 0.03f); break;
                case "coin": { s = new Synth(SR, 0.35f); float b = 1320 * Mathf.Pow(1.06f, i % 5); s.Tone(b, 0.09f, "square", 0.07f, 0, 0, 0.005f, 4000); s.Tone(b * 1.5f, 0.18f, "square", 0.06f, 0.06f, 0, 0.005f, 5000); break; }
                case "whoosh": s = new Synth(SR, 0.26f); s.Noise(0.18f, 500, 2600, "bandpass", 1.4f, 0.16f, 0.03f); break;
                case "crash": s = new Synth(SR, 0.8f); s.Noise(0.7f, 1800, 120, "lowpass", 0.7f, 0.5f); s.Tone(110, 0.6f, "sawtooth", 0.18f, 0, 40, 0.005f, 600); break;
                case "place": { int[] sc = { 60, 62, 64, 67, 69, 72, 74, 76, 79, 81, 84 }; s = new Synth(SR, 0.45f); s.Tone(Synth.Mtof(sc[Mathf.Min(10, i)]), 0.35f, "triangle", 0.18f); break; }
                case "chop": s = new Synth(SR, 0.2f); s.Noise(0.12f, 2500, 0, "bandpass", 2, 0.12f); s.Tone(240, 0.12f, "square", 0.05f, 0, 120, 0.005f, 1200); break;
                case "win": s = new Synth(SR, 1.1f); { int[] nn = { 60, 64, 67, 72, 76, 79, 84 }; for (int k = 0; k < nn.Length; k++) s.Tone(Synth.Mtof(nn[k]), 0.5f, "triangle", 0.12f, k * 0.07f); } break;
                case "lose": s = new Synth(SR, 1.2f); { int[] nn = { 67, 63, 60, 55 }; for (int k = 0; k < nn.Length; k++) s.Tone(Synth.Mtof(nn[k]), 0.5f, "triangle", 0.13f, k * 0.16f); } break;
                case "horn": s = new Synth(SR, 1.7f); { int[] nn = { 50, 50, 57 }; for (int k = 0; k < 3; k++) { float d = k < 2 ? 0.28f : 0.9f; s.Tone(Synth.Mtof(nn[k]), d, "sawtooth", 0.1f, k * 0.32f, 0, 0.005f, 1200); s.Tone(Synth.Mtof(nn[k] + 12), d, "sawtooth", 0.04f, k * 0.32f, 0, 0.005f, 1600); } } break;
                case "alarm": s = new Synth(SR, 0.55f); for (int k = 0; k < 3; k++) s.Tone(k % 2 == 1 ? 740 : 880, 0.14f, "square", 0.06f, k * 0.16f, 0, 0.005f, 2500); break;
                case "capture": s = new Synth(SR, 1.2f); { int[] nn = { 60, 67, 72, 79, 84 }; for (int k = 0; k < nn.Length; k++) s.Tone(Synth.Mtof(nn[k]), 0.6f, "sawtooth", 0.09f, k * 0.11f, 0, 0.005f, 1800); } break;
                case "shot": s = new Synth(SR, 0.12f); s.Noise(0.07f, 1800, 600, "bandpass", 0.8f, 0.11f, 0.002f); s.Tone(160, 0.06f, "square", 0.04f, 0, 80, 0.005f, 900); break;
                case "mg": s = new Synth(SR, 0.1f); s.Noise(0.05f, 1400, 500, "bandpass", 0.8f, 0.09f, 0.002f); break;
                case "snipe": s = new Synth(SR, 0.35f); s.Noise(0.25f, 3200, 400, "bandpass", 0.6f, 0.16f, 0.001f); s.Tone(90, 0.18f, "sine", 0.12f, 0, 40); break;
                case "boom": s = new Synth(SR, 1f); s.Noise(0.9f, 900, 60, "lowpass", 0.6f, 0.42f, 0.004f); s.Tone(70, 0.6f, "sine", 0.3f, 0, 30); break;
                case "cannon": s = new Synth(SR, 0.7f); s.Noise(0.6f, 1200, 80, "lowpass", 1, 0.38f, 0.002f); s.Tone(55, 0.5f, "sine", 0.32f, 0, 28); break;
                case "throw": s = new Synth(SR, 0.22f); s.Noise(0.16f, 600, 1400, "bandpass", 1.2f, 0.06f, 0.02f); break;
                case "whistle": s = new Synth(SR, 0.8f); s.Tone(1400, 0.7f, "sine", 0.06f, 0, 500); break;
                case "splash": s = new Synth(SR, 0.6f); s.Noise(0.5f, 700, 250, "bandpass", 0.7f, 0.14f, 0.02f); break;
                default: return null;
            }
            return s.Clip(n);
        }
        // the 'war' style loop: drums, bass and a dark pad, 4 bars at 96 bpm
        static AudioClip WarMusic()
        {
            float bpm = 96, sp = 60f / bpm / 4f; int steps = 64; Synth s = new Synth(SR, steps * sp);
            int[][] chords = { new[] { 45, 48, 52 }, new[] { 41, 45, 48 }, new[] { 43, 47, 50 }, new[] { 40, 44, 47 } };
            int[] bass = { 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 };
            for (int st = 0; st < steps; st++)
            {
                int bar = (st / 16) % 4, i = st % 16; int[] ch = chords[bar]; float t = st * sp;
                if (i == 0 || i == 6 || i == 8) s.Tone(95, 0.3f, "sine", 0.42f, t, 50, 0.005f, 0, 1, 0, true);
                if (i == 12 || i == 14) s.Tone(140, 0.16f, "sine", 0.2f, t, 90, 0.005f, 0, 1, 0, true);
                if (i == 4 || i == 12) s.Noise(0.1f, 1200, 0, "bandpass", 0.8f, 0.08f, 0.005f, t, true);
                if (bass[i] == 1) s.Tone(Synth.Mtof(ch[0] - 12), sp * 1.6f, "sawtooth", 0.16f, t, 0, 0.005f, 500, 1, 0, true);
                if (i == 0) foreach (int nn in ch) s.Tone(Synth.Mtof(nn), sp * 16 * 1.05f, "sawtooth", 0.035f, t, 0, 0.4f, 900, 1, 0, true);
            }
            return s.Clip("war_music", 1.4f);
        }
    }
}
