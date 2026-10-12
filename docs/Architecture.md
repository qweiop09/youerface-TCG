# 기술 설계서

> 기획 내용·용어는 [GameDesign.md](GameDesign.md)가 기준이다. 이 문서는 "어떻게 만드는가"만 다룬다.
> 최종 갱신: 2026-10-12

## 1. 큰 그림

```
 [입력/UI] ──Command──▶ [TCG.Core: GameEngine] ──GameEvent 목록──▶ [연출 큐] ──▶ 화면/사운드
                              ▲      │
                     (네트워크)│      ▼
                        ITransport  GameState (단일 진실 원천)
```

- **Core(규칙 엔진)**: 게임 상태와 규칙만 다룬다. Unity를 모른다. 순수 C#.
- **Client**: 입력을 Command로 바꿔 Core에 넘기고, Core가 돌려준 GameEvent를 차례로 연출한다.
- 여기서 "엔진"은 Unity 같은 게임 엔진이 아니라 **게임 규칙을 계산하는 코드**를 말한다. Unity는 그대로 사용한다.

## 2. 폴더 / 어셈블리 구조

Unity 프로젝트 루트는 `youerface-TCG/` 하위 폴더다.

```
youerface-TCG/Assets/
├─ 00_Scenes/
├─ 01_Scripts/
│  ├─ Core/                     TCG.Core.asmdef      (noEngineReferences: true)
│  │  ├─ Data/                  CardDefinition, EffectDefinition, RuleConfig, 열거형
│  │  ├─ State/                 GameState, PlayerState, Entity, Zone (소멸 영역 Exile 포함)
│  │  ├─ Commands/              PlayCard, Attack, EndTurn …
│  │  ├─ Events/                CardEntered, CardExited, DamageDealt …
│  │  ├─ Rules/                 GameEngine (Validate / Apply)
│  │  ├─ Effects/               트리거·조건·대상·행동 블록 + 레지스트리
│  │  ├─ Scoring/               ScoreCalculator (static), ScoreTable
│  │  ├─ Random/                DeterministicRng
│  │  └─ Serialization/         JSON 직렬화
│  ├─ Client/                   TCG.Client.asmdef    (Core 참조)
│  │  ├─ Board/                 영역·카드 배치 뷰
│  │  ├─ Cards/                 카드 뷰
│  │  ├─ Presentation/          이벤트 큐, 연출 해석기 (상세는 사용자와 논의 후)
│  │  ├─ CardMaker/             카드 제작 UI  ※ 폴더명 "Editor" 금지(Unity 특수 폴더)
│  │  ├─ DeckBuilder/
│  │  ├─ Net/                   ITransport 구현, 세션
│  │  ├─ UserAssets/            IFilePicker, IUserAssetStore
│  │  └─ Common/                Singleton, SceneLoader 등
│  └─ Tests/
│     └─ Core/                  TCG.Core.Tests.asmdef (EditMode, Core 참조)
├─ 02_Prefabs/
├─ 03_Datas/                    RuleConfig 기본값, 기본 제공 카드 JSON
├─ 04_TimeLines/
└─ 05_Animations/
```

### asmdef(Assembly Definition)란
- 폴더 단위로 스크립트를 별도 DLL로 컴파일하게 하는 Unity 기능.
- `TCG.Core`는 **No Engine References**를 켜서 `UnityEngine`(MonoBehaviour, GameObject, Debug.Log 등)을 쓸 수 없게 막는다.
- 효과:
  1. 규칙과 화면이 섞이는 것을 컴파일 단계에서 차단한다.
  2. 규칙 테스트를 Play 모드 없이 빠르게 돌릴 수 있다.
  3. 호스트가 화면과 무관하게 규칙을 돌리고, 나중에 전용 서버(.NET)로 옮길 수 있다.
  4. 연출을 바꿔도 규칙이 깨지지 않는다.
- JSON: `com.unity.nuget.newtonsoft-json` 패키지를 추가하고 `TCG.Core.asmdef`에서 `overrideReferences: true`, `precompiledReferences: ["Newtonsoft.Json.dll"]`로 참조한다.

## 3. Core 작성 규칙

1. `UnityEngine` 사용 금지 (asmdef가 강제). 로그는 Core에 `ILogger` 인터페이스를 두고 Client에서 `Debug.Log` 구현을 주입.
2. **상태 변경은 오직 `GameEngine`을 통해서만**:
   - `ValidationResult Validate(GameState, Command)` — 낼 수 있는지 검사
   - `IReadOnlyList<GameEvent> Apply(GameState, Command)` — 상태를 바꾸고 일어난 일을 이벤트로 반환
3. **결정론(같은 입력 → 항상 같은 결과)**:
   - 수치는 **int만** 사용. 배율은 `value * 150 / 100`처럼 정수 연산. (float은 CPU·플랫폼마다 미세하게 결과가 다를 수 있음)
   - 난수는 **`DeterministicRng`**(xorshift 등 자체 구현)만 사용하고, 시드와 내부 상태를 `GameState`에 저장. `UnityEngine.Random`, `System.Random` 금지.
   - 게임 로직에서 `Dictionary`/`HashSet` 순회 순서에 의존 금지. 순서가 필요하면 List 또는 정렬.
   - `DateTime.Now`, `Guid.NewGuid()` 등을 로직에 사용 금지. 대전 중 Entity ID는 순차 int.
   - 효과 처리 순서(동시 발동 시)를 명확히 정의하고 문서화.
4. 결정론의 이점: 리플레이(시드 + Command 목록만 저장), 버그 재현, 양측 상태 해시 비교로 동기화 검증.
5. 모든 규칙 수치는 `RuleConfig`에서 읽는다 ([GameDesign.md §3.2](GameDesign.md#32-규칙-수치--미정-임시값은-ruleconfig에-두고-하드코딩-금지)).
6. Core 기능에는 EditMode 테스트를 함께 작성한다.

## 4. 데이터: 카드 정의 (JSON) — 초안

```json
{
  "schemaVersion": 1,
  "id": "c_7f3a...",
  "name": "불꽃 정령",
  "type": "Minion",
  "cost": 3,
  "attack": 3,
  "health": 2,
  "keywords": ["Taunt"],
  "art":    { "image": "sha256:ab12..." },
  "sounds": { "enter": "sha256:cd34...", "exit": "sha256:ef56..." },
  "presentationStyle": "default",
  "effects": [
    {
      "trigger":    { "type": "OnEnter" },
      "conditions": [],
      "target":     { "type": "RandomEnemyMinion", "count": 1 },
      "action":     { "type": "Damage", "amount": 2 }
    }
  ]
}
```

- `type`: `Minion` | `Spell` | `Field` | `Companion` | `Summon`
- `attack`/`health`는 `Minion`만 가진다. 필드·동료·소환물은 체력이 없고 공격 대상이 아니다.
- `usage`(사용 횟수): `Summon` 필수, `Companion` 선택(없으면 영구 지속).
- `Summon`은 덱·손패에 들어가지 않는다. `CreateSummon` 행동 블록이 소환물 정의를 **내부에 포함**하고, 점수 계산 시 포함된 소환물 점수를 부모 카드에 합산한다.

```json
{ "action": { "type": "CreateSummon",
  "summon": { "name": "불씨", "type": "Summon", "usage": 2, "onDuplicate": "AddNew", "art": { "image": "sha256:..." },
              "effects": [ { "trigger": { "type": "OnTurnEnd" }, "conditions": [],
                             "target": { "type": "EnemyHero" }, "action": { "type": "Damage", "amount": 1 } } ] } } }
```
- `onDuplicate`(소환물 필수): `AddNew`(하나 더 놓기) | `RefreshUsage`(기존 소환물 사용 횟수 갱신). "같은 소환물" = 같은 카드 ID의 같은 효과 순번이 만든 소환물.
- `effects[].activation`: `Mandatory`(강제, 기본값) | `Optional`(선택). 없으면 강제로 읽는다.
- `keywords`: `Taunt`(도발) | `Stealth`(은신) | `Vanguard`(선봉) | `Exhaust`(일회성, 주문 전용) | `TreasureChest`(보물상자). 붙일 수 있는 카드 종류는 레지스트리가 정하고 `CardValidator`가 검사한다.
- **점수는 저장하지 않는다.** 항상 `ScoreCalculator.Calculate(card, scoreTable)`로 계산 (패치 시 자동 반영).
- `presentationStyle`: 연출 방식 확장용 자리. 현재 `"default"`(카드 그대로 표시)만.
- 사운드는 일반화해 `enter`(등장: 소환/발동) / `exit`(퇴장: 사망/파괴/교체)로 둔다.
- 스키마 변경 시 `schemaVersion`을 올리고 마이그레이션 코드를 둔다.

## 4.1 규칙 엔진 처리 순서 (M2.5 체인 기준)

- **효과 블록 레지스트리** (`EffectRegistry`): 조건 `IConditionEvaluator`, 대상 `ITargetSelector`, 행동 `IActionHandler`(상시 가능하면 `IAuraActionHandler`), 키워드 `KeywordInfo`. 새 블록 = 열거형 항목 + 구현 클래스 + `CreateDefault()`에 등록 한 줄. 엔진과 기존 블록은 수정하지 않는다.
- **키워드**는 두 종류다. 효과형(선봉 = 등장 시 자신의 주인 1장 드로우, 보물상자 = 퇴장 시 1장 드로우)은 레지스트리에 암묵 효과로 등록한다. 판정형(도발·은신·일회성)은 엔진이 해당 시점에 검사한다.
- **체인** (GameDesign §7.2, 유희왕식): 체인 상태(`Chain`, `PendingTriggers`, `Decision`, `PendingActions`)는 전부 `GameState`에 있다. 엔진은 처리 도중 플레이어 선택이 필요하면 멈추고 `Decision`을 남긴다. 클라이언트·AI는 `ChooseTriggersCommand` / `PassChainCommand`로 답한다.
  1. 행동이 일으킨 발동은 `PendingTriggers`로 모인다 (조건은 이때 검사).
  2. 체인 만들기: 현재 턴 플레이어 → 상대 순. 한 플레이어의 대기 효과가 강제 1개뿐이면 자동, 둘 이상이거나 선택 효과가 있으면 `ChooseTriggers` 선택 요청 (앞에 고른 것이 먼저 체인에 올라감 = 나중에 처리).
  3. 대응 기회: 마지막 링크 발동자의 상대 → 발동자. 대응 가능한 효과가 없으면 자동 패스(`HasResponses`, 대응 블록은 아직 없음). 둘 다 패스하면 체인이 닫힌다.
  4. 마지막 링크부터 처리. 체력 0 이하는 피해 즉시 사망. 처리 중 생긴 발동은 다음 체인.
  5. 체인도 대기 효과도 없으면 `PendingActions`(전투 피해, 턴 넘기기)를 실행한다. 공격은 "공격 시" 체인 → 전투 피해, 턴 종료는 "턴 종료 시" 체인 → 상대 턴 시작.
  6. 매 단계마다 상시 효과 재계산 → 사망 → 승패 판정(`Settle`). 영웅 체력 0 이하면 즉시 대전 종료, 남은 체인은 버린다.
  7. 플레이어 행동 1개(선택 응답 포함)에서 처리한 체인 수가 `MaxTriggerResolutionsPerAction`을 넘으면 남은 체인·대기 효과를 버린다.
- **이벤트**: 체인에 올라감 `EffectActivatedEvent`(발동 연출) → 처리 시작 `EffectResolvingEvent`(처리 연출, 뒤따르는 이벤트가 결과) / 무효화 `EffectNegatedEvent` → `ChainEndedEvent`. 선택 대기 `DecisionRequestedEvent`.
- **카드 내기**: 코스트 지불 → 손패에서 제거 → 즉시 배치(필드는 기존 필드 퇴장 먼저) / 주문은 바로 묘지(일회성은 소멸) → 발동한 효과들(내 보드의 "카드 사용 시", 낸 카드의 "등장 시", 교체된 필드의 "퇴장 시")로 체인.
- **턴 시작**: 최대 코스트 증가·충전 → 기물 공격 가능 상태로 → 1장 드로우 → "턴 시작 시" 체인.
- **수치 모델**: 공격력·체력·코스트 = 기본값(영구 변경 포함) + 상시 보정값. 체력은 받은 피해량으로 관리하고, 상시 보정은 처리 단계마다 처음부터 다시 계산한다.
- **상태 해시** (`StateHasher`, FNV-1a 64): 리플레이·동기화 검증용. 시드 + 덱 + 명령 목록이 같으면 해시가 같다.

## 5. 이벤트와 연출 — 개요만 (상세는 구현 전 사용자와 논의)

- Core는 `GameEvent` 목록만 내보내고, 연출을 전혀 모른다.
- Client의 연출 큐가 이벤트를 하나씩 꺼내, `이벤트 종류 + 카드의 presentationStyle`로 연출기(`IEventPresenter` 같은 것)를 골라 `await` 재생.
- 연출 방식 추가 = 새 연출기 등록. 기존 코드 수정 없음.
- 이 구조 덕분에 연출 스킵, 빠르게 감기, 리플레이가 가능하다.
- 이전 프로젝트의 `TimelineDirector.PlayAsync`와 Binder 패턴(§8)을 Timeline 기반 연출기에 활용할 수 있다.

## 6. 네트워크

- **호스트 판정 P2P**: 방장 클라이언트가 `GameEngine`을 돌린다.
  - 상대(게스트)는 Command만 보내고, 호스트는 검증 → 적용 → **플레이어별 시야로 가린 이벤트**(상대 손패·덱 내용 숨김)를 보낸다.
  - 단순 lockstep(양쪽이 같은 시드로 계산)은 상대 손패·덱 순서가 내 컴퓨터에서 계산되므로 쓰지 않는다.
- **`ITransport` 인터페이스**로 통신을 분리:
  - 1차 구현: **Unity Relay + Lobby (UGS)** — NAT 문제 해결, Win/Mac/모바일 크로스플레이.
  - Steam 출시 시: Steam 네트워킹은 Steam 사용자끼리만 연결되어 모바일 크로스플레이가 끊긴다. 대전 연결은 UGS로 유지하고 Steam은 친구 초대·오버레이·업적에만 쓰는 것을 권장.
- 턴제라 메시지 단위 통신이면 충분. Netcode for GameObjects 같은 오브젝트 동기화 프레임워크는 쓰지 않는다.
- 대전 시작 핸드셰이크: 앱/스키마/점수표 버전 확인 → 덱 교환 및 `ScoreCalculator`로 방 규칙 검증 → 없는 에셋(해시 기준) 전송 → 시작.
- 나중에 전용 서버가 필요하면 Core를 그대로 서버에 올린다.

## 7. 유저 에셋 (이미지·사운드)

- `IFilePicker`: 데스크톱은 StandaloneFileBrowser 등, 모바일은 NativeFilePicker 등. 플랫폼별 구현 교체.
- `IUserAssetStore`: 파일을 SHA-256 해시로 `Application.persistentDataPath` 아래에 저장·조회. 카드 JSON은 해시만 참조.
- 런타임 로드: 이미지 → `Texture2D.LoadImage` 또는 `UnityWebRequestTexture`, 사운드 → `UnityWebRequestMultimedia.GetAudioClip` (OGG/WAV 권장).
- 가져올 때 크기·길이 제한 검사, 이미지 리사이즈 (제한값은 미정, GameDesign Q10).
- 나중에 서버 업로드를 붙여도 해시 참조 구조를 그대로 쓴다.

## 8. 이전 프로젝트(ProjectNeoulgyeol) 차용 목록

경로 기준: `/Users/dgsw26/GithubProject/ProjectNeoulgyeol/Assets/01_Scripts/`

| 파일 | 쓰임새 | 비고 |
|---|---|---|
| `Timeline/TimelineDirector.cs` | `PlayAsync`로 타임라인 끝까지 대기 | `CharacterHandler`·`SkillActData` 의존 제거 |
| `Interfacese/ITimelineBinder.cs`, `Timeline/Binder/SkillTimelineBinder.cs` | 공용 타임라인 템플릿에 카드 이미지·사운드를 실행 중 연결 | 바인딩 데이터 타입 교체 |
| `Timeline/Battle/MoveToTarget*.cs`, `Timeline/Binder/MoveToTargetBinder.cs`, `Runtime/Battles/Phase/Compete/MoveToTargetExecutor.cs` | 카드가 공격 대상에게 돌진하는 연출 | |
| `Runtime/Battles/AttackArrowController.cs`, `AttackArrowView.cs` | 공격 대상 지정 화살표 | 입력을 Input System 포인터로 |
| `Runtime/Battles/UI/DamageText.cs`, `DamageTextSpawner.cs` | 피해 숫자 표시 | QTE 결과 의존 제거 |
| `Runtime/Battles/CameraControlle/CameraHandler.cs` | 카메라 이동 | 필요 시 |
| `Runtime/Worlds/SceneLoader.cs`, `Runtime/Worlds/Transition/FadeTransition.cs` | 씬 전환 | |
| `Interfacese/Singleton.cs` | 싱글톤 유틸 | Client에서만 사용 |

차용하지 않는 것: `BattlePhaseCoordinator`와 페이즈 컨트롤러들(턴 구조가 다름), `CharacterData`/`CharacterSkill` ScriptableObject(유저 카드는 실행 중에 만드는 JSON 데이터), QTE(온라인 공정성 문제), 월드 탐험 관련 코드.
