using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Synthesises every sound the game uses and writes it to Assets/Resources/Audio
/// as 16-bit mono WAV, so nothing is generated at runtime and each sound can be
/// listened to, or replaced with a recorded one under the same name.
///
/// Variants of a sound are numbered (name, name_1, name_2 ...); the game picks
/// one at random. Menu: Orc Horde/Bake Sounds. Headless:
///   Unity -batchmode -quit -projectPath . -executeMethod SoundBaker.Run
/// </summary>
public static class SoundBaker
{
    const int SR = 44100;
    const string OutDir = "Assets/Resources/Audio";
    static System.Random rng;

    [MenuItem("Orc Horde/Bake Sounds")]
    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        foreach (var f in Directory.GetFiles(OutDir, "*.wav")) File.Delete(f);
        rng = new System.Random(1234);

        Variants("rifle_shot", 3, RifleShot);
        Save("rifle_reload", RifleReload());
        Save("ads_up", AdsUp());
        Save("hit", HitTick());
        Save("headshot", Headshot());

        Variants("mg_shot", 3, MgShot);
        Save("cannon_fire", CannonFire());
        Variants("explosion", 3, Explosion);
        Variants("laser_zap", 2, LaserZap);
        Variants("arc_zap", 3, ArcZap);
        Variants("flame_burst", 3, FlameBurst);

        Variants("orc_grunt", 6, OrcGrunt);
        Variants("orc_death", 4, OrcDeath);
        Variants("orc_swing", 2, OrcSwing);
        Variants("boss_roar", 2, BossRoar);
        Variants("wall_hit", 3, WallHit);
        Save("wall_break", WallBreak());
        Save("heal", Heal());

        Save("mortar_whistle", MortarWhistle());
        Save("breach", Breach());
        Save("horn_wave", HornWave());
        Save("horn_boss", HornBoss());
        Save("round_won", RoundWon());
        Save("round_lost", RoundLost());

        Save("ui_place", UiPlace());
        Save("ui_remove", UiRemove());
        Save("ui_invalid", UiInvalid());
        Save("ui_buy", UiBuy());
        Save("ui_click", UiClick());

        foreach (var surface in new[] { "gravel", "snow", "grass" }) Variants("step_" + surface, 4, i => Step(surface, i));
        Save("land", Land());
        foreach (var biome in new[] { "dirt", "canyon", "snow", "grassland" }) Save("wind_" + biome, Wind(biome));

        AssetDatabase.Refresh();
        ConfigureImporters();
        Debug.Log("[SoundBaker] Baked sounds to " + OutDir);
    }

    // ================================================================ output

    static void Variants(string name, int count, Func<int, float[]> make)
    {
        for (int i = 0; i < count; i++) Save(i == 0 ? name : $"{name}_{i}", make(i));
    }

    static void Save(string name, float[] x)
    {
        float peak = 0, sum = 0;
        foreach (var v in x) { peak = Mathf.Max(peak, Mathf.Abs(v)); sum += v * v; }
        Debug.Log($"[SoundBaker] {name,-16} {x.Length / (float)SR:0.00}s peak={peak:0.00} rms={Mathf.Sqrt(sum / Mathf.Max(1, x.Length)):0.000}");
        using var w = new BinaryWriter(File.Create(Path.Combine(OutDir, name + ".wav")));
        int bytes = x.Length * 2;
        w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(bytes);
        foreach (var v in x) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
    }

    static void ConfigureImporters()
    {
        foreach (var path in Directory.GetFiles(OutDir, "*.wav"))
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (imp == null) continue;
            bool loop = Path.GetFileName(path).StartsWith("wind_");
            var s = imp.defaultSampleSettings;
            // Short sounds fire many times a second: keep them decoded. The long wind loops stream compressed.
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
    }

    // ================================================================ building blocks

    static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * SR)];
    static float T(int i) => (float)i / SR;
    static float Rand() => (float)(rng.NextDouble() * 2 - 1);
    static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Linear attack, then exponential decay with the given time constant.</summary>
    static float Env(float t, float attack, float decay) => t < attack ? t / Mathf.Max(attack, 1e-5f) : Mathf.Exp(-(t - attack) / decay);

    static float[] Noise(float seconds, Func<float, float> amp)
    {
        var x = Buf(seconds);
        for (int i = 0; i < x.Length; i++) x[i] = Rand() * amp(T(i));
        return x;
    }

    /// <summary>Brownish noise: white noise, integrated and leaking, for rumbles.</summary>
    static float[] Rumble(float seconds, Func<float, float> amp)
    {
        var x = Buf(seconds);
        float y = 0;
        for (int i = 0; i < x.Length; i++) { y = y * 0.995f + Rand() * 0.1f; x[i] = y * amp(T(i)); }
        return x;
    }

    enum Wave { Sine, Saw, Square, Tri }

    static float[] Tone(float seconds, Func<float, float> freq, Func<float, float> amp, Wave wave = Wave.Sine)
    {
        var x = Buf(seconds);
        double phase = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float t = T(i);
            phase += freq(t) / SR;
            float p = (float)(phase - Math.Floor(phase));
            float v = wave switch
            {
                Wave.Sine => Mathf.Sin(p * Mathf.PI * 2),
                Wave.Saw => p * 2 - 1,
                Wave.Square => p < 0.5f ? 1 : -1,
                _ => 1 - 4 * Mathf.Abs(p - 0.5f),
            };
            x[i] = v * amp(t);
        }
        return x;
    }

    static void Lowpass(float[] x, Func<float, float> cutoff)
    {
        float y = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff(T(i)) / SR);
            y += a * (x[i] - y);
            x[i] = y;
        }
    }

    static void Highpass(float[] x, float cutoff)
    {
        float y = 0, a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / SR);
        for (int i = 0; i < x.Length; i++) { y += a * (x[i] - y); x[i] -= y; }
    }

    /// <summary>A resonant band-pass (RBJ biquad) whose centre may move over time.</summary>
    static void Bandpass(float[] x, Func<float, float> centre, float q)
    {
        float x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 0, b2 = 0, a1 = 0, a2 = 0, a0 = 1;
        for (int i = 0; i < x.Length; i++)
        {
            if (i % 16 == 0)
            {
                float w0 = 2 * Mathf.PI * Mathf.Clamp(centre(T(i)), 20, SR * 0.45f) / SR;
                float alpha = Mathf.Sin(w0) / (2 * q);
                b0 = alpha; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * Mathf.Cos(w0); a2 = 1 - alpha;
            }
            float y = (b0 * x[i] + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = y;
            x[i] = y;
        }
    }

    static float[] Bandpassed(float[] x, Func<float, float> centre, float q)
    {
        var y = (float[])x.Clone();
        Bandpass(y, centre, q);
        return y;
    }

    static void Drive(float[] x, float amount)
    {
        float norm = (float)Math.Tanh(amount);
        for (int i = 0; i < x.Length; i++) x[i] = (float)Math.Tanh(x[i] * amount) / norm;
    }

    static void Normalize(float[] x, float peak = 0.9f)
    {
        float m = 0;
        foreach (var v in x) m = Mathf.Max(m, Mathf.Abs(v));
        if (m < 1e-6f) return;
        for (int i = 0; i < x.Length; i++) x[i] *= peak / m;
    }

    static void Mix(float[] into, float[] src, float gain = 1f, float at = 0f)
    {
        int o = Mathf.RoundToInt(at * SR);
        for (int i = 0; i < src.Length && i + o < into.Length; i++) if (i + o >= 0) into[i + o] += src[i] * gain;
    }

    /// <summary>A feedback delay: room for explosions and horns to ring in.</summary>
    static void Echo(float[] x, float delay, float feedback, float mix)
    {
        int d = Mathf.Max(1, Mathf.RoundToInt(delay * SR));
        var line = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            float back = i >= d ? line[i - d] : 0;
            line[i] = x[i] + back * feedback;
            x[i] += back * mix;
        }
    }

    static void FadeOut(float[] x, float seconds)
    {
        int n = Mathf.Min(x.Length, Mathf.RoundToInt(seconds * SR));
        for (int i = 0; i < n; i++) x[x.Length - 1 - i] *= (float)i / n;
    }

    static void FadeIn(float[] x, float seconds)
    {
        int n = Mathf.Min(x.Length, Mathf.RoundToInt(seconds * SR));
        for (int i = 0; i < n; i++) x[i] *= (float)i / n;
    }

    /// <summary>A short metallic click: a burst of bright noise and a decaying ping.</summary>
    static float[] Click(float pitch, float length = 0.05f)
    {
        var x = Noise(length, t => Env(t, 0.0005f, 0.004f));
        Highpass(x, 2000);
        Mix(x, Tone(length, _ => pitch, t => Env(t, 0.0005f, 0.012f) * 0.6f));
        Mix(x, Tone(length, _ => pitch * 2.76f, t => Env(t, 0.0005f, 0.007f) * 0.3f));
        return x;
    }

    /// <summary>
    /// An orc's voice: a buzzing saw with a wavering pitch, shaped by two vocal
    /// formants, roughened with a growl. Vowel formants move from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    static float[] Voice(float seconds, Func<float, float> pitch, (float f1, float f2) from, (float f1, float f2) to, float growl, Func<float, float> amp)
    {
        float wobble = Range(0, 10);
        var src = Tone(seconds, t => pitch(t) * (1 + 0.03f * Mathf.Sin(t * 37 + wobble) + 0.02f * Rand()), amp, Wave.Saw);
        // A slow amplitude flutter is what makes it a growl rather than a hum.
        for (int i = 0; i < src.Length; i++) src[i] *= 1 - growl * (0.5f + 0.5f * Mathf.Sin(T(i) * 2 * Mathf.PI * 28));
        Mix(src, Noise(seconds, t => amp(t) * 0.12f));
        float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t / seconds);
        var f1 = Bandpassed(src, t => Lerp(from.f1, to.f1, t), 5);
        var f2 = Bandpassed(src, t => Lerp(from.f2, to.f2, t), 7);
        var low = (float[])src.Clone();
        Lowpass(low, _ => 350);
        var x = Buf(seconds);
        Mix(x, f1, 1f); Mix(x, f2, 0.6f); Mix(x, low, 0.5f);
        Drive(x, 1.5f + growl * 2);
        return x;
    }

    // ================================================================ the player's rifle

    static float[] RifleShot(int v)
    {
        var x = Buf(0.45f);
        var crack = Noise(0.45f, t => Env(t, 0.0003f, 0.006f));
        Highpass(crack, 1500);
        Mix(x, crack, 1.1f);
        var body = Rumble(0.45f, t => Env(t, 0.001f, 0.045f));
        Lowpass(body, _ => 1200);
        Mix(x, body, 3.5f);
        Mix(x, Tone(0.45f, t => 95 - 50 * t / 0.45f + v * 6, t => Env(t, 0.001f, 0.05f) * 0.9f));
        var tail = Noise(0.45f, t => Env(t, 0.01f, 0.13f) * 0.25f);
        Lowpass(tail, t => 2500 - 2000 * t / 0.45f);
        Mix(x, tail);
        Drive(x, 2.5f);
        Normalize(x, 0.9f);
        return x;
    }

    /// <summary>Magazine out, a beat, a fresh one seated, the bolt racked: timed to the 1.5 s reload.</summary>
    static float[] RifleReload()
    {
        var x = Buf(1.5f);
        Mix(x, Click(1900), 0.8f, 0.12f);
        Mix(x, Click(1500, 0.08f), 0.5f, 0.16f);
        var slide = Noise(0.12f, t => Env(t, 0.01f, 0.04f) * 0.4f);
        Bandpass(slide, t => 1200 + t * 8000, 2);
        Mix(x, slide, 1f, 0.6f);
        Mix(x, Click(1300, 0.1f), 1f, 0.82f);
        var thunk = Rumble(0.12f, t => Env(t, 0.001f, 0.03f));
        Lowpass(thunk, _ => 500);
        Mix(x, thunk, 3f, 0.82f);
        Mix(x, Click(2200), 0.9f, 1.2f);
        Mix(x, Click(1700), 1f, 1.3f);
        Normalize(x, 0.7f);
        return x;
    }

    static float[] AdsUp()
    {
        var x = Noise(0.2f, t => Env(t, 0.06f, 0.05f) * 0.5f);
        Bandpass(x, _ => 1800, 1.2f);
        Mix(x, Click(2600, 0.04f), 0.25f, 0.13f);
        Normalize(x, 0.35f);
        return x;
    }

    static float[] HitTick()
    {
        var x = Tone(0.06f, _ => 2300, t => Env(t, 0.0005f, 0.012f));
        Mix(x, Click(3200, 0.04f), 0.4f);
        Normalize(x, 0.5f);
        return x;
    }

    static float[] Headshot()
    {
        var x = Tone(0.25f, _ => 1650, t => Env(t, 0.001f, 0.05f) * 0.7f);
        Mix(x, Tone(0.25f, _ => 2480, t => Env(t, 0.001f, 0.08f) * 0.6f), 1, 0.03f);
        Mix(x, Click(4000, 0.05f), 0.5f);
        Normalize(x, 0.6f);
        return x;
    }

    // ================================================================ towers

    static float[] MgShot(int v)
    {
        var x = Buf(0.22f);
        var crack = Noise(0.22f, t => Env(t, 0.0003f, 0.004f));
        Highpass(crack, 2200);
        Mix(x, crack);
        var body = Rumble(0.22f, t => Env(t, 0.0005f, 0.025f));
        Lowpass(body, _ => 1600);
        Mix(x, body, 2.5f);
        Mix(x, Tone(0.22f, t => 140 + v * 12 - 60 * t, t => Env(t, 0.0005f, 0.02f) * 0.6f));
        Drive(x, 3f);
        Normalize(x, 0.85f);
        return x;
    }

    static float[] CannonFire()
    {
        var x = Buf(1.6f);
        var crack = Noise(1.6f, t => Env(t, 0.0005f, 0.01f));
        Highpass(crack, 800);
        Mix(x, crack, 0.8f);
        var boom = Rumble(1.6f, t => Env(t, 0.003f, 0.25f));
        Lowpass(boom, t => 700 - 400 * Mathf.Clamp01(t / 0.5f));
        Mix(x, boom, 4f);
        Mix(x, Tone(1.6f, t => 62 - 28 * Mathf.Clamp01(t / 0.6f), t => Env(t, 0.002f, 0.28f)));
        Drive(x, 2.2f);
        Echo(x, 0.11f, 0.35f, 0.3f);
        FadeOut(x, 0.3f);
        Normalize(x, 0.95f);
        return x;
    }

    /// <summary>A blast: a sharp front, a falling roar, a deep thump, then debris pattering down.</summary>
    static float[] Explosion(int v)
    {
        float len = 2f;
        var x = Buf(len);
        var roar = Noise(len, t => Env(t, 0.002f, 0.35f + v * 0.05f));
        Lowpass(roar, t => 4000 * Mathf.Exp(-t * 3f) + 250);
        Mix(x, roar, 1.4f);
        var rumble = Rumble(len, t => Env(t, 0.005f, 0.5f));
        Lowpass(rumble, _ => 300);
        Mix(x, rumble, 4f);
        Mix(x, Tone(len, t => 55 - 20 * Mathf.Clamp01(t / 0.8f) + v * 4, t => Env(t, 0.002f, 0.35f)));
        for (int i = 0; i < 26; i++)
        {
            float at = Range(0.15f, 1.5f);
            Mix(x, Click(Range(700, 2600), 0.04f), Range(0.05f, 0.25f) * Mathf.Exp(-at * 1.2f) * 3, at);
        }
        Drive(x, 2.5f);
        Echo(x, 0.13f, 0.4f, 0.25f);
        FadeOut(x, 0.4f);
        Normalize(x, 0.95f);
        return x;
    }

    /// <summary>A charged discharge: a falling sweep with a bright edge and a fizz.</summary>
    static float[] LaserZap(int v)
    {
        var x = Tone(0.35f, t => (2600 + v * 300) * Mathf.Exp(-t * 9) + 400, t => Env(t, 0.003f, 0.09f), Wave.Square);
        Lowpass(x, _ => 5000);
        Mix(x, Tone(0.35f, t => 1300 * Mathf.Exp(-t * 7) + 200, t => Env(t, 0.003f, 0.12f) * 0.6f));
        var fizz = Noise(0.35f, t => Env(t, 0.001f, 0.06f) * 0.3f);
        Highpass(fizz, 4000);
        Mix(x, fizz);
        Normalize(x, 0.6f);
        return x;
    }

    /// <summary>An electric snap: a dense crackle of sparks over a mains hum.</summary>
    static float[] ArcZap(int v)
    {
        var x = Buf(0.5f);
        for (int i = 0; i < 70; i++)
        {
            float at = Mathf.Pow(Range(0, 1), 2.2f) * 0.42f;
            var spark = Noise(0.01f, t => Env(t, 0.0002f, 0.0015f));
            Mix(x, spark, Range(0.3f, 1f) * Mathf.Exp(-at * 6), at);
        }
        Bandpass(x, _ => 3200 + v * 400, 0.8f);
        Mix(x, Tone(0.5f, _ => 120, t => Env(t, 0.002f, 0.12f) * 0.35f, Wave.Saw));
        Mix(x, Tone(0.5f, _ => 240, t => Env(t, 0.002f, 0.1f) * 0.2f, Wave.Square));
        Drive(x, 2f);
        Normalize(x, 0.7f);
        return x;
    }

    /// <summary>A gout of flame: roaring, fluttering noise.</summary>
    static float[] FlameBurst(int v)
    {
        var x = Noise(0.45f, t => Env(t, 0.03f, 0.18f) * (0.65f + 0.35f * Mathf.Sin(t * 2 * Mathf.PI * (24 + v * 5))));
        Lowpass(x, t => 1500 - 900 * t);
        var hiss = Noise(0.45f, t => Env(t, 0.01f, 0.08f) * 0.25f);
        Highpass(hiss, 3000);
        Mix(x, hiss);
        Mix(x, Rumble(0.45f, t => Env(t, 0.02f, 0.2f) * 2f));
        Drive(x, 1.6f);
        Normalize(x, 0.6f);
        return x;
    }

    // ================================================================ orcs

    static readonly (float, float)[] Vowels = { (600, 1000), (750, 1200), (450, 850), (650, 1100), (520, 950), (700, 1050) };

    static float[] OrcGrunt(int v)
    {
        float len = Range(0.25f, 0.42f);
        float pitch = Range(85, 130);
        var vowel = Vowels[v % Vowels.Length];
        var x = Voice(len, t => pitch * (1 - 0.25f * t / len), vowel, (vowel.Item1 * 0.85f, vowel.Item2 * 0.9f), Range(0.3f, 0.6f),
            t => Env(t, 0.02f, len * 0.4f));
        FadeOut(x, 0.05f);
        Normalize(x, 0.75f);
        return x;
    }

    static float[] OrcDeath(int v)
    {
        float len = 0.9f;
        float pitch = Range(95, 125);
        var x = Voice(len, t => pitch * (1 - 0.5f * t / len), (700, 1150), (450, 800), 0.5f, t => Env(t, 0.02f, 0.35f));
        FadeOut(x, 0.2f);
        var thud = Rumble(len, t => Env(t, 0.002f, 0.05f));
        Lowpass(thud, _ => 250);
        Mix(x, thud, 2f, 0.55f);
        Normalize(x, 0.8f);
        return x;
    }

    static float[] OrcSwing(int v)
    {
        var x = Noise(0.32f, t => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f)) * 0.6f);
        Bandpass(x, t => 400 + 1400 * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f)) + v * 150, 2.5f);
        Normalize(x, 0.5f);
        return x;
    }

    static float[] BossRoar(int v)
    {
        float len = 1.8f;
        float pitch = 62 + v * 8;
        var x = Voice(len, t => pitch * (1 + 0.15f * Mathf.Sin(Mathf.PI * t / len)), (650, 1050), (800, 1250), 0.75f,
            t => Env(t, 0.15f, 0.9f));
        Mix(x, Voice(len, t => pitch * 0.5f, (500, 900), (600, 1000), 0.9f, t => Env(t, 0.2f, 0.8f)), 0.6f);
        Drive(x, 2f);
        Echo(x, 0.17f, 0.3f, 0.25f);
        FadeOut(x, 0.4f);
        Normalize(x, 0.9f);
        return x;
    }

    /// <summary>A blow on a timber barricade: a hollow thud and a creak.</summary>
    static float[] WallHit(int v)
    {
        var x = Rumble(0.4f, t => Env(t, 0.001f, 0.07f));
        Lowpass(x, _ => 600);
        Mix(x, Tone(0.4f, _ => 150 + v * 25, t => Env(t, 0.001f, 0.08f) * 0.4f));
        var knock = Noise(0.4f, t => Env(t, 0.0005f, 0.012f));
        Bandpass(knock, _ => 900 + v * 200, 3);
        Mix(x, knock, 1.5f);
        Normalize(x, 0.75f);
        return x;
    }

    static float[] WallBreak()
    {
        var x = Buf(1.4f);
        for (int i = 0; i < 9; i++) Mix(x, WallHit(i % 3), Range(0.4f, 1f), Mathf.Pow(Range(0, 1), 1.5f) * 0.7f);
        var crash = Noise(1.4f, t => Env(t, 0.002f, 0.25f));
        Bandpass(crash, t => 2500 - 1500 * t, 0.7f);
        Mix(x, crash, 0.8f);
        Drive(x, 1.8f);
        FadeOut(x, 0.3f);
        Normalize(x, 0.9f);
        return x;
    }

    static float[] Heal()
    {
        var x = Buf(0.7f);
        float[] notes = { 660, 830, 990 };
        for (int i = 0; i < 3; i++)
            Mix(x, Tone(0.6f, _ => notes[i], t => Env(t, 0.02f, 0.2f) * 0.5f, Wave.Tri), 1, i * 0.06f);
        Echo(x, 0.09f, 0.3f, 0.3f);
        Normalize(x, 0.4f);
        return x;
    }

    // ================================================================ abilities and the round

    static float[] MortarWhistle()
    {
        var x = Tone(0.95f, t => 1900 - 1100 * t / 0.95f + 25 * Mathf.Sin(t * 40), t => Mathf.Clamp01(t / 0.6f) * 0.5f);
        Mix(x, Noise(0.95f, t => Mathf.Clamp01(t / 0.8f) * 0.08f));
        FadeOut(x, 0.03f);
        Normalize(x, 0.5f);
        return x;
    }

    /// <summary>The base is hit: a deep clang and a short two-tone alarm.</summary>
    static float[] Breach()
    {
        var x = Buf(0.9f);
        var clang = Tone(0.9f, _ => 180, t => Env(t, 0.001f, 0.3f), Wave.Tri);
        Mix(clang, Tone(0.9f, _ => 497, t => Env(t, 0.001f, 0.2f) * 0.5f));
        Mix(x, clang, 0.8f);
        for (int i = 0; i < 2; i++)
            Mix(x, Tone(0.16f, _ => i == 0 ? 880 : 660, t => Env(t, 0.005f, 0.2f) * 0.35f, Wave.Square), 1, 0.15f + i * 0.17f);
        Lowpass(x, _ => 4000);
        Normalize(x, 0.7f);
        return x;
    }

    /// <summary>A war horn: a brassy note that swells, bends up, holds, and dies away into the canyon.</summary>
    static float[] Horn(float seconds, float pitch, float beat, int blasts)
    {
        var x = Buf(seconds);
        float blast = seconds / blasts * 0.8f;
        for (int b = 0; b < blasts; b++)
        {
            Func<float, float> amp = t => Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((blast - t) / 0.3f);
            Func<float, float> f = t => pitch * (0.94f + 0.06f * Mathf.Clamp01(t / 0.3f));
            var tone = Tone(blast, f, amp, Wave.Saw);
            Mix(tone, Tone(blast, t => f(t) + beat, amp, Wave.Saw), 0.7f);
            Mix(tone, Tone(blast, t => f(t) * 2, t => amp(t) * 0.3f, Wave.Saw));
            Lowpass(tone, t => 500 + 700 * Mathf.Clamp01(t / 0.4f));
            Mix(x, tone, 1, b * seconds / blasts);
        }
        Drive(x, 1.6f);
        Echo(x, 0.23f, 0.45f, 0.4f);
        FadeOut(x, 0.4f);
        Normalize(x, 0.8f);
        return x;
    }

    static float[] HornWave() => Horn(2.4f, 110, 0.8f, 1);
    static float[] HornBoss() => Horn(3f, 82, 5.5f, 2);

    static float[] RoundWon()
    {
        var x = Buf(1.8f);
        float[] notes = { 392, 494, 587, 784 };
        for (int i = 0; i < notes.Length; i++)
        {
            float n = notes[i];
            float len = i == notes.Length - 1 ? 1.1f : 0.25f;
            Mix(x, Tone(len, _ => n, t => Env(t, 0.01f, len * 0.5f) * 0.6f, Wave.Square), 1, i * 0.14f);
            Mix(x, Tone(len, _ => n / 2, t => Env(t, 0.01f, len * 0.5f) * 0.4f, Wave.Tri), 1, i * 0.14f);
        }
        Lowpass(x, _ => 3500);
        Echo(x, 0.15f, 0.3f, 0.3f);
        Normalize(x, 0.6f);
        return x;
    }

    static float[] RoundLost()
    {
        var x = Buf(2.4f);
        float[] chord = { 98, 116.5f, 146.8f };
        foreach (var n in chord)
            Mix(x, Tone(2.4f, t => n * (1 - 0.06f * t / 2.4f), t => Env(t, 0.05f, 1f) * 0.5f, Wave.Saw));
        Lowpass(x, t => 900 - 300 * t / 2.4f);
        Echo(x, 0.21f, 0.35f, 0.3f);
        FadeOut(x, 0.6f);
        Normalize(x, 0.6f);
        return x;
    }

    // ================================================================ building and menus

    static float[] UiPlace()
    {
        var x = Rumble(0.3f, t => Env(t, 0.001f, 0.04f));
        Lowpass(x, _ => 700);
        Mix(x, Click(1400, 0.06f), 0.6f);
        Mix(x, Tone(0.25f, _ => 1320, t => Env(t, 0.003f, 0.07f) * 0.25f), 1, 0.04f);
        Normalize(x, 0.6f);
        return x;
    }

    static float[] UiRemove()
    {
        var x = Noise(0.25f, t => Env(t, 0.01f, 0.06f));
        Bandpass(x, t => 2000 - 6000 * t, 2);
        Mix(x, Tone(0.2f, t => 700 - 1500 * t, t => Env(t, 0.003f, 0.05f) * 0.3f));
        Normalize(x, 0.45f);
        return x;
    }

    static float[] UiInvalid()
    {
        var x = Tone(0.18f, _ => 140, t => Env(t, 0.005f, 0.1f), Wave.Square);
        Mix(x, Tone(0.18f, _ => 148, t => Env(t, 0.005f, 0.1f), Wave.Square));
        Lowpass(x, _ => 1500);
        Normalize(x, 0.4f);
        return x;
    }

    static float[] UiBuy()
    {
        var x = Buf(0.45f);
        Mix(x, Tone(0.3f, _ => 1568, t => Env(t, 0.002f, 0.08f) * 0.6f), 1, 0f);
        Mix(x, Tone(0.35f, _ => 2093, t => Env(t, 0.002f, 0.12f) * 0.6f), 1, 0.07f);
        Mix(x, Click(4200, 0.04f), 0.3f);
        Normalize(x, 0.5f);
        return x;
    }

    static float[] UiClick()
    {
        var x = Click(2600, 0.03f);
        Normalize(x, 0.35f);
        return x;
    }

    // ================================================================ footsteps and wind

    static float[] Step(string surface, int v)
    {
        var x = Buf(0.22f);
        var thud = Rumble(0.22f, t => Env(t, 0.002f, 0.03f));
        Lowpass(thud, _ => 300);
        Mix(x, thud, 2f);
        switch (surface)
        {
            case "snow":
            {
                // Packed snow: a soft, drawn-out squeak-crunch.
                var crunch = Noise(0.22f, t => Env(t, 0.01f, 0.06f) * (0.6f + 0.4f * Mathf.Sin(t * 900 + v)));
                Bandpass(crunch, _ => 2200 + v * 200, 1.2f);
                Mix(x, crunch, 0.9f);
                break;
            }
            case "grass":
            {
                var rustle = Noise(0.22f, t => Env(t, 0.008f, 0.05f) * 0.6f);
                Bandpass(rustle, _ => 3500 + v * 300, 1f);
                Mix(x, rustle, 0.6f);
                break;
            }
            default:
                // Gravel and dust: a scatter of small stones.
                for (int i = 0; i < 9; i++) Mix(x, Click(Range(1800, 4200), 0.02f), Range(0.1f, 0.3f), Range(0, 0.06f));
                break;
        }
        Normalize(x, 0.35f);
        return x;
    }

    static float[] Land()
    {
        var x = Rumble(0.3f, t => Env(t, 0.002f, 0.06f));
        Lowpass(x, _ => 250);
        Mix(x, Step("gravel", 0), 0.8f);
        Normalize(x, 0.5f);
        return x;
    }

    /// <summary>
    /// A seamless wind loop: noise through a slowly swaying band, gusting.
    /// Built long and crossfaded end into start so it repeats without a seam.
    /// </summary>
    static float[] Wind(string biome)
    {
        float loop = 8f, overlap = 1f;
        var (centre, swing, q, gust, level) = biome switch
        {
            "snow" => (900f, 600f, 2.5f, 0.6f, 0.5f),   // howling, tonal
            "canyon" => (500f, 300f, 0.9f, 0.5f, 0.45f), // dry, airy
            "grassland" => (700f, 250f, 0.7f, 0.35f, 0.3f),
            _ => (350f, 150f, 0.8f, 0.4f, 0.4f),          // low quarry rumble
        };
        var x = Noise(loop + overlap, t => 0.6f + gust * (0.5f + 0.5f * Mathf.Sin(t * 0.7f) * Mathf.Sin(t * 0.31f + 1)));
        Bandpass(x, t => centre + swing * Mathf.Sin(t * 0.45f) * Mathf.Sin(t * 0.17f + 2), q);
        var outp = new float[Mathf.RoundToInt(loop * SR)];
        int n = outp.Length, o = Mathf.RoundToInt(overlap * SR);
        for (int i = 0; i < n; i++)
        {
            float v = x[i];
            if (i < o) { float k = (float)i / o; v = x[i] * k + x[i + n] * (1 - k); }
            outp[i] = v;
        }
        Normalize(outp, level);
        return outp;
    }
}
