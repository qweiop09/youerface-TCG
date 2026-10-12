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
        public const string DefaultPresentationStyle = "default";

        /// <summary>카드 ID. 소환물처럼 다른 카드 안에 포함된 정의는 null일 수 있다.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public CardType Type { get; set; }
        public int Cost { get; set; }

        /// <summary>하수인 전용. 그 외 종류는 null.</summary>
        public int? Attack { get; set; }
        /// <summary>하수인 전용. 그 외 종류는 null (필드·동료·소환물은 체력 없음).</summary>
        public int? Health { get; set; }
        /// <summary>사용 횟수. 소환물은 필수, 동료는 선택(null = 영구 지속), 그 외 종류는 null.</summary>
        public int? Usage { get; set; }

        public List<Keyword> Keywords { get; set; } = new List<Keyword>();
        public CardArt Art { get; set; } = new CardArt();
        public CardSounds Sounds { get; set; } = new CardSounds();
        /// <summary>연출 방식 확장용 자리. 현재 "default"(카드 그대로 표시)만.</summary>
        public string PresentationStyle { get; set; } = DefaultPresentationStyle;
        public List<EffectDefinition> Effects { get; set; } = new List<EffectDefinition>();
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
