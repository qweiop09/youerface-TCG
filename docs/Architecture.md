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
│  │  ├─ State/                 GameState, PlayerState, Entity, Zone
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
  "summon": { "name": "불씨", "type": "Summon", "usage": 2, "art": { "image": "sha256:..." },
              "effects": [ { "trigger": { "type": "OnTurnEnd" }, "conditions": [],
                             "target": { "type": "EnemyHero" }, "action": { "type": "Damage", "amount": 1 } } ] } } }
```
- **점수는 저장하지 않는다.** 항상 `ScoreCalculator.Calculate(card, scoreTable)`로 계산 (패치 시 자동 반영).
- `presentationStyle`: 연출 방식 확장용 자리. 현재 `"default"`(카드 그대로 표시)만.
- 사운드는 일반화해 `enter`(등장: 소환/발동) / `exit`(퇴장: 사망/파괴/교체)로 둔다.
- 스키마 변경 시 `schemaVersion`을 올리고 마이그레이션 코드를 둔다.

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
