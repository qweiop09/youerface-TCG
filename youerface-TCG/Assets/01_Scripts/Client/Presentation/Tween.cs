using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>연출 시간. 모든 연출은 이 배속을 따른다 (빨리 감기·스킵용).</summary>
    public static class PresentationTime
    {
        /// <summary>1 = 기본 속도. 클수록 빠르다.</summary>
        public static float Speed = 1f;
        public static float DeltaTime => Time.deltaTime * Mathf.Max(0.01f, Speed);
    }

    /// <summary>코루틴 기반 간단한 이동·크기·흔들기 (외부 트윈 라이브러리 없이).</summary>
    public static class Tween
    {
        static float Ease(float t) => t * t * (3f - 2f * t);

        public static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += PresentationTime.DeltaTime)
                yield return null;
        }

        /// <summary>위치·회전·크기를 함께 옮긴다. arc &gt; 0이면 위로 포물선을 그린다.</summary>
        public static IEnumerator Move(Transform target, Vector3 position, Quaternion rotation, float duration, float arc = 0f, Vector3? scale = null)
        {
            if (target == null)
                yield break;
            var fromPos = target.localPosition;
            var fromRot = target.localRotation;
            var fromScale = target.localScale;
            var toScale = scale ?? fromScale;
            for (float t = 0; t < duration; t += PresentationTime.DeltaTime)
            {
                if (target == null)
                    yield break;
                float k = Ease(Mathf.Clamp01(t / duration));
                target.localPosition = Vector3.Lerp(fromPos, position, k) + Vector3.up * (arc * 4f * k * (1f - k));
                target.localRotation = Quaternion.Slerp(fromRot, rotation, k);
                target.localScale = Vector3.Lerp(fromScale, toScale, k);
                yield return null;
            }
            if (target == null)
                yield break;
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = toScale;
        }

        /// <summary>잠깐 커졌다 돌아온다.</summary>
        public static IEnumerator Punch(Transform target, float amount, float duration)
        {
            if (target == null)
                yield break;
            var baseScale = target.localScale;
            for (float t = 0; t < duration; t += PresentationTime.DeltaTime)
            {
                if (target == null)
                    yield break;
                float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                target.localScale = baseScale * (1f + amount * k);
                yield return null;
            }
            if (target != null)
                target.localScale = baseScale;
        }

        /// <summary>좌우로 흔든다.</summary>
        public static IEnumerator Shake(Transform target, float magnitude, float duration)
        {
            if (target == null)
                yield break;
            var basePos = target.localPosition;
            for (float t = 0; t < duration; t += PresentationTime.DeltaTime)
            {
                if (target == null)
                    yield break;
                float fade = 1f - Mathf.Clamp01(t / duration);
                target.localPosition = basePos + new Vector3(Mathf.Sin(t * 70f) * magnitude * fade, 0, 0);
                yield return null;
            }
            if (target != null)
                target.localPosition = basePos;
        }

        /// <summary>여러 연출을 동시에 시작해 모두 끝날 때까지 기다린다.</summary>
        public static IEnumerator All(MonoBehaviour host, IList<IEnumerator> routines)
        {
            int running = 0;
            for (int i = 0; i < routines.Count; i++)
            {
                if (routines[i] == null)
                    continue;
                running++;
                host.StartCoroutine(Track(routines[i], () => running--));
            }
            while (running > 0)
                yield return null;
        }

        /// <summary>delay 뒤에 시작.</summary>
        public static IEnumerator Delayed(float delay, IEnumerator routine)
        {
            if (delay > 0f)
                yield return Wait(delay);
            yield return routine;
        }

        static IEnumerator Track(IEnumerator routine, System.Action done)
        {
            yield return Safe(routine);
            done();
        }

        /// <summary>
        /// 연출 하나가 예외로 멈춰도 전체 재생이 멈추지 않게, 중첩 코루틴까지 직접 돌리며 예외를 기록하고 끝낸다.
        /// </summary>
        public static IEnumerator Safe(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool moved;
                object current = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved)
                        current = top.Current;
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    yield break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return current;
            }
        }
    }
}
