using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>
    /// 유저가 만든 카드 1장의 원본 정의. 대전 중 바뀌는 값(현재 체력 등)은 <see cref="State.Entity"/>에 둔다.
    /// 점수는 저장하지 않는다. 항상 <see cref="Scoring.ScoreCalculator"/>로 계산한다.
    /// JSON 형식과 schemaVersion은 <see cref="Serialization.CardSerializer"/>가 다룬다.
    /// </summary>
    public sealed class CardDefinition
    {
        /// <summary>카드 ID. 소환물처럼 다른 카드 안에 포함된 정의는 null일 수 있다.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public CardType Type { get; set; }
        public int Cost { get; set; }

        /// <summary>기물 전용. 그 외 종류는 null.</summary>
        public int? Attack { get; set; }
        /// <summary>기물 전용. 그 외 종류는 null (필드·동료·소환물은 체력 없음).</summary>
        public int? Health { get; set; }
        /// <summary>사용 횟수. 소환물은 필수, 동료는 선택(null = 영구 지속), 그 외 종류는 null.</summary>
        public int? Usage { get; set; }
        /// <summary>같은 소환물이 또 생성될 때의 처리. 소환물은 필수, 그 외 종류는 null.</summary>
        public SummonDuplicateMode? OnDuplicate { get; set; }

        public List<Keyword> Keywords { get; set; } = new List<Keyword>();
        public CardArt Art { get; set; } = new CardArt();
        public CardSounds Sounds { get; set; } = new CardSounds();
        /// <summary>등장·퇴장 연출 설정 (GameDesign §7.3).</summary>
        public CardPresentation Presentation { get; set; } = new CardPresentation();
        public List<EffectDefinition> Effects { get; set; } = new List<EffectDefinition>();
        /// <summary>카드 설명(설정 문구, GameDesign §11.1). 점수·규칙에 영향 없음. 없으면 null.</summary>
        public string Flavor { get; set; }
    }

    /// <summary>카드 이미지. 내용 해시("sha256:...")로 참조한다.</summary>
    public sealed class CardArt
    {
        public string Image { get; set; }
    }

    /// <summary>카드 사운드. 내용 해시로 참조한다.</summary>
    public sealed class CardSounds
    {
        /// <summary>등장(소환/발동)</summary>
        public string Enter { get; set; }
        /// <summary>퇴장(사망/파괴/교체)</summary>
        public string Exit { get; set; }
    }
}
