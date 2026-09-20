using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime-built particle effects, materials and sounds. Nothing here needs an imported asset.
/// </summary>
public static class Fx
{
    public static Material BodyMat;
    public static Material WickMat;
    public static Material SparkMat;
    public static Material SmokeMat;
    public static GameObject BombModel;

    static AudioClip _boom;
    static AudioClip _pop;
    static bool _ready;

    public static void Init()
    {
        if (_ready) return;
        _ready = true;

        var soft = MakeSoftCircle(64);
        BombModel = Resources.Load<GameObject>("Bomb");
        BodyMat = Resources.Load<Material>("BombMat");
        WickMat = Resources.Load<Material>("WickMat");
        SparkMat = new Material(Resources.Load<Material>("SparkMat")) { mainTexture = soft };
        SmokeMat = new Material(Resources.Load<Material>("SmokeMat")) { mainTexture = soft };

        _boom = MakeBoom();
        _pop = MakePop();
    }

    // ---- Effects -------------------------------------------------------------

    /// <summary>Continuous sparks that sit at the burning end of a wick.</summary>
    public static ParticleSystem MakeSparks(Transform parent, float size)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z, so point it up

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.05f, size * 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.5f), new Color(1f, 0.55f, 0.1f));
        main.gravityModifier = 0.6f;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 60f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = size * 0.01f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white);

        SetupRenderer(ps, SparkMat);
        return ps;
    }

    /// <summary>Flash + sparks + smoke, plus sound and a buzz on the phone.</summary>
    public static void Explosion(Vector3 pos, float size)
    {
        var root = new GameObject("Explosion");
        root.transform.position = pos;

        // Flash: one big bright particle that dies fast.
        var flash = Burst(root.transform, SparkMat, 1, 0f, 0f, 0.12f, 0.12f, size * 3.5f, size * 3.5f,
            new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f), 0f);
        var flashSize = flash.sizeOverLifetime;
        flashSize.enabled = true;
        flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));

        // Sparks flung outward.
        Burst(root.transform, SparkMat, 90, 1.5f, 3.5f, 0.4f, 0.9f, size * 0.12f, size * 0.22f,
            new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.05f), 1.3f);

        // Smoke that grows and drifts.
        var smoke = Burst(root.transform, SmokeMat, 14, 0.3f, 1.0f, 0.9f, 1.6f, size * 1.0f, size * 1.8f,
            new Color(0.25f, 0.25f, 0.25f, 0.8f), new Color(0.45f, 0.42f, 0.4f, 0.8f), -0.05f);
        var smokeSize = smoke.sizeOverLifetime;
        smokeSize.enabled = true;
        smokeSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));
        var smokeMain = smoke.main;
        smokeMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        Object.Destroy(root, 2.5f);

        if (_boom != null) AudioSource.PlayClipAtPoint(_boom, pos, 1f);
#if UNITY_ANDROID || UNITY_IOS
        if (!Application.isEditor) Handheld.Vibrate();
#endif
    }

    /// <summary>Small puff of smoke when a wick is snuffed out.</summary>
    public static void Puff(Vector3 pos, float size)
    {
        var root = new GameObject("Puff");
        root.transform.position = pos;
        var smoke = Burst(root.transform, SmokeMat, 6, 0.15f, 0.4f, 0.5f, 0.8f, size * 0.4f, size * 0.7f,
            new Color(0.6f, 0.6f, 0.6f, 0.7f), new Color(0.8f, 0.8f, 0.8f, 0.7f), -0.1f);
        var grow = smoke.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
        Object.Destroy(root, 1.5f);

        if (_pop != null) AudioSource.PlayClipAtPoint(_pop, pos, 0.8f);
    }

    static ParticleSystem Burst(Transform parent, Material mat, int count, float speedMin, float speedMax,
        float lifeMin, float lifeMax, float sizeMin, float sizeMax, Color a, Color b, float gravity)
    {
        var go = new GameObject("Burst");
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.maxParticles = Mathf.Max(count, 1);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white);

        SetupRenderer(ps, mat);
        ps.Play();
        return ps;
    }

    static void SetupRenderer(ParticleSystem ps, Material mat)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static ParticleSystem.MinMaxGradient FadeOut(Color tint)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    // ---- Generated assets ----------------------------------------------------

    static Texture2D MakeSoftCircle(int res)
    {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[res * res];
        float half = res * 0.5f;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
            float a = Mathf.SmoothStep(1f, 0f, d);
            px[y * res + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static AudioClip MakeBoom()
    {
        const int rate = 22050;
        const float duration = 0.8f;
        int n = (int)(rate * duration);
        var data = new float[n];
        var rng = new System.Random(7);
        float lowpass = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowpass += (noise - lowpass) * 0.12f;                 // rumble rather than hiss
            float body = lowpass * Mathf.Exp(-t * 5f) * 2.2f;
            float thump = Mathf.Sin(2f * Mathf.PI * 50f * t) * Mathf.Exp(-t * 9f);
            data[i] = Mathf.Clamp(body + thump, -1f, 1f);
        }
        var clip = AudioClip.Create("Boom", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip MakePop()
    {
        const int rate = 22050;
        const float duration = 0.12f;
        int n = (int)(rate * duration);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float freq = Mathf.Lerp(1100f, 500f, t / duration);  // quick downward chirp
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 35f);
        }
        var clip = AudioClip.Create("Pop", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
