using System.Collections;
using TCG.Client.Common;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 타격 이펙트 (코드로 만드는 파티클 + 퍼지는 원). 돌진 공격이 부딪힐 때와 원거리 공격이 대상에 닿을 때 쓴다.
    /// 나중에 연출 프리셋(GameDesign §7.4)의 "파티클 표시" 단계로 옮기기 쉽게 기본 동작만 쓴다.
    /// </summary>
    public static class HitEffects
    {
        public static readonly Color ImpactColor = new Color(1f, 0.78f, 0.35f);
        public static readonly Color MagicColor = new Color(0.55f, 0.8f, 1f);

        /// <summary>at 위치에서 파티클을 터뜨리고 원을 퍼뜨린다. 기다리지 않는다(뒤에서 스스로 사라짐).</summary>
        public static void Burst(MonoBehaviour host, Transform parent, Vector3 at, Color color, float size = 1f, int count = 22)
        {
            var go = new GameObject("HitBurst");
            go.transform.SetParent(parent, false);
            go.transform.position = at + Vector3.up * 0.25f;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * size, 4.5f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * size, 0.16f * size);
            main.startColor = color;
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f * size;
            var lifetimeColor = ps.colorOverLifetime;
            lifetimeColor.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            lifetimeColor.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MaterialCache.Get(color, unlit: true);
            ps.Play();

            host.StartCoroutine(Ring(go.transform, color, size));
            Object.Destroy(go, 1.2f);
        }

        /// <summary>바닥에서 퍼지며 흐려지는 원 (부딪힌 자리 표시).</summary>
        static IEnumerator Ring(Transform parent, Color color, float size)
        {
            var ring = Shapes.Disc(parent, "Ring", Vector3.zero, 0.3f * size, 0.01f, color, unlit: true).transform;
            const float time = 0.22f;
            for (float t = 0; t < time && ring != null; t += PresentationTime.DeltaTime)
            {
                float k = t / time;
                ring.localScale = Vector3.one * Mathf.Lerp(0.4f, 2.2f, k);
                ring.localScale = new Vector3(ring.localScale.x, 0.01f, ring.localScale.z);
                yield return null;
            }
            if (ring != null)
                Object.Destroy(ring.gameObject);
        }
    }
}
