using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A cartoon bomb sitting on a detected AR plane. The model (body, cap, curved wick) is the
/// Blender-made Bomb.fbx in Resources. The wick burns down via the WickBurn shader, sparks ride
/// the burning tip along the WickPt_xx empties exported with the model.
/// Defuse it in time or it explodes.
/// </summary>
public class Bomb : MonoBehaviour
{
    public float fuseTime = 4f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BurnId = Shader.PropertyToID("_Burn");
    static readonly Color BodyColor = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color DangerColor = new Color(0.95f, 0.15f, 0.08f);

    GameManager _game;
    float _size;
    float _age;
    bool _done;
    bool _exploded;

    Renderer _bodyRenderer;
    Renderer _wickRenderer;
    MaterialPropertyBlock _bodyBlock;
    MaterialPropertyBlock _wickBlock;
    ParticleSystem _sparks;
    readonly List<Transform> _wickPath = new(); // base -> tip

    public bool IsLive => !_done;

    public void Build(GameManager game, float size)
    {
        _game = game;
        _size = size;
        _bodyBlock = new MaterialPropertyBlock();
        _wickBlock = new MaterialPropertyBlock();

        var model = Instantiate(Fx.BombModel, transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * size; // model is authored at 1m diameter

        // Body: black, gets a sphere collider for tap detection.
        var body = model.transform.Find("Body");
        _bodyRenderer = body.GetComponent<Renderer>();
        _bodyRenderer.sharedMaterial = Fx.BodyMat;
        _bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var bounds = _bodyRenderer.localBounds;
        var collider = body.gameObject.AddComponent<SphereCollider>();
        collider.center = bounds.center;
        collider.radius = bounds.extents.x;

        // Cap: same material, tinted grey.
        var capRenderer = model.transform.Find("Cap").GetComponent<Renderer>();
        capRenderer.sharedMaterial = Fx.BodyMat;
        capRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var capBlock = new MaterialPropertyBlock();
        capBlock.SetColor(BaseColorId, new Color(0.35f, 0.35f, 0.38f));
        capRenderer.SetPropertyBlock(capBlock);

        // Wick: burn shader, driven per-instance through a property block.
        _wickRenderer = model.transform.Find("Wick").GetComponent<Renderer>();
        _wickRenderer.sharedMaterial = Fx.WickMat;
        _wickRenderer.shadowCastingMode = ShadowCastingMode.Off;
        SetBurn(1f);

        // Wick centreline, exported as empties WickPt_00 (base) .. WickPt_NN (tip).
        var path = model.transform.Find("WickPath");
        for (int i = 0; i < path.childCount; i++) _wickPath.Add(path.GetChild(i));
        _wickPath.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Sparks ride the burning tip.
        _sparks = Fx.MakeSparks(transform, size);
        _sparks.transform.position = TipPosition(1f);
        _sparks.Play();

        transform.localScale = Vector3.zero; // pops in on first Update
    }

    void Update()
    {
        if (_done) return;

        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / fuseTime);

        float popIn = Mathf.SmoothStep(0f, 1f, _age / 0.25f);
        transform.localScale = Vector3.one * popIn;

        float remaining = 1f - t;
        SetBurn(remaining);
        _sparks.transform.position = TipPosition(remaining);

        // Last third of the fuse: body pulses red, faster the closer it gets.
        float danger = Mathf.InverseLerp(0.65f, 1f, t);
        float pulse = danger > 0f ? Mathf.Abs(Mathf.Sin(Time.time * (8f + 14f * danger))) * danger : 0f;
        _bodyRenderer.GetPropertyBlock(_bodyBlock);
        _bodyBlock.SetColor(BaseColorId, Color.Lerp(BodyColor, DangerColor, pulse));
        _bodyRenderer.SetPropertyBlock(_bodyBlock);

        if (_age >= fuseTime)
        {
            _done = true;
            Explode();
            _game.OnBombExploded(this);
        }
    }

    /// <summary>Player tapped it in time.</summary>
    public void Defuse()
    {
        if (_done) return;
        _done = true;
        _sparks.Stop();
        Fx.Puff(_sparks.transform.position, _size);
        StartCoroutine(Shrink());
    }

    /// <summary>Fuse ran out, or chain reaction from another bomb.</summary>
    public void Explode()
    {
        if (_exploded) return;
        _exploded = true;
        _done = true;
        Fx.Explosion(transform.position + Vector3.up * (_size * 0.5f), _size);
        Destroy(gameObject);
    }

    void SetBurn(float remaining)
    {
        _wickRenderer.GetPropertyBlock(_wickBlock);
        _wickBlock.SetFloat(BurnId, remaining);
        _wickRenderer.SetPropertyBlock(_wickBlock);
    }

    Vector3 TipPosition(float remaining)
    {
        if (_wickPath.Count == 0) return transform.position;
        float f = Mathf.Clamp01(remaining) * (_wickPath.Count - 1);
        int i = Mathf.FloorToInt(f);
        if (i >= _wickPath.Count - 1) return _wickPath[^1].position;
        return Vector3.Lerp(_wickPath[i].position, _wickPath[i + 1].position, f - i);
    }

    IEnumerator Shrink()
    {
        Vector3 start = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
        {
            transform.localScale = Vector3.Lerp(start, Vector3.zero, t);
            yield return null;
        }
        Destroy(gameObject);
    }
}
