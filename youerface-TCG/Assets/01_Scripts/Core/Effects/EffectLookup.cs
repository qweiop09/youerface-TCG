using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Effects
{
    /// <summary>
    /// 카드의 효과 목록 (카드 정의의 효과, 그다음 키워드 효과). 이벤트의 EffectIndex는 이 목록 기준이다.
    /// 규칙 엔진과 연출(Client)이 같은 순서를 쓰도록 한 곳에 둔다.
    /// </summary>
    public static class EffectLookup
    {
        public static void Collect(Entity entity, EffectRegistry registry, List<EffectDefinition> results)
        {
            var own = entity.Definition.Effects;
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                    results.Add(own[i]);
            }

            for (int k = 0; k < entity.Keywords.Count; k++)
            {
                if (!registry.TryGetKeyword(entity.Keywords[k], out var info))
                    continue;
                for (int i = 0; i < info.ImplicitEffects.Count; i++)
                    results.Add(info.ImplicitEffects[i]);
            }
        }

        /// <summary>효과 하나. 범위를 벗어나면 null.</summary>
        public static EffectDefinition Get(Entity entity, int effectIndex, EffectRegistry registry)
        {
            if (entity == null)
                return null;
            var list = new List<EffectDefinition>();
            Collect(entity, registry, list);
            return effectIndex >= 0 && effectIndex < list.Count ? list[effectIndex] : null;
        }
    }
}
