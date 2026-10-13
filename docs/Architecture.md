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
├─ 00_Scenes/                   Title.unity (빌드 첫 장면, 시작 화면) → Battle.unity, CardMaker.unity
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
│  │  ├─ AI/                    컴퓨터 플레이어 (EasyAi, SearchAi), 시야 제한(AiView), 가능한 명령 목록, 판세 평가
│  │  └─ Serialization/         JSON 직렬화
│  ├─ Client/                   TCG.Client.asmdef    (Core, TextMeshPro 참조)
│  │  ├─ Battle/                BattleBootstrap(장면 시작점), BattleSession(명령 검증·적용·연출 등록·컴퓨터 차례), SampleData
│  │  ├─ Board/                 BoardLayout(칸 위치), BoardView(테이블·칸·카드 배치), HeroView, PileView(덱), HudView
│  │  ├─ Cards/                 CardView(보드 위 3D 카드), CardDetailView(크게 보는 uGUI 카드: 키워드 설명·효과 설명), CardPalette
│  │  ├─ Presentation/          PresentationPlayer(재생기), PresenterRegistry, DefaultPresenters, Tween, FloatingText, BigCardOverlay
│  │  ├─ Flow/                  BattleLaunch(장면 이동·난이도/시드 전달), TitleScreen(난이도 선택), ResultPanel(결과 화면)
│  │  ├─ Interaction/           BattleInput(포인터: 카드 끌어 내기·대상 지정·공격 화살표), TargetArrow, DecisionPanel(체인 선택 창)
│  │  ├─ CardMaker/             CardMakerScreen(카드 제작 화면), FormColumn(편집 줄 쌓기), LocalCardLibrary(내 카드 JSON)  ※ 폴더명 "Editor" 금지(Unity 특수 폴더)
│  │  ├─ DeckBuilder/
│  │  ├─ Net/                   ITransport 구현, 세션
│  │  ├─ UserAssets/            IFilePicker(StandaloneFilePicker), IUserAssetStore(LocalUserAssetStore), UserAssetImporter, UserAssetCache, ArtCropDialog, CardSoundPlayer, UserAssetService
│  │  └─ Common/                KoreanFont(OS 한글 폰트), MaterialCache, Shapes(도형·글자), KoreanTerms
│  ├─ EditorTools/              TCG.EditorTools.asmdef (에디터 전용) — 프로젝트 준비, Title·Battle 장면 생성, 화면 캡처
│  └─ Tests/
│     ├─ Core/                  TCG.Core.Tests.asmdef (EditMode, Core 참조)
│     └─ Client/                TCG.Client.Tests.asmdef (PlayMode) — Battle 장면 관전 모드로 연출·화면 일치 검사
├─ 02_Prefabs/
├─ 03_Datas/Resources/          샘플 카드(SampleCards/*.json)·덱(SampleDecks/*.json), 기본 머티리얼(Materials/BaseLit·BaseUnlit). Resources.Load로 읽음
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
  "schemaVersion": 2,
  "id": "c_7f3a...",
  "name": "불꽃 정령",
  "type": "Minion",
  "cost": 3,
  "attack": 3,
  "health": 2,
  "keywords": ["Taunt"],
  "art":    { "image": "sha256:ab12..." },
  "sounds": { "enter": "sha256:cd34...", "exit": "sha256:ef56..." },
  "presentation": { "enter": "default", "exit": "default" },
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
- **점수는 저장하지 않는다.** 항상 `ScoreCalculator`로 계산 (패치 시 자동 반영). 공식은 GameDesign §6.1, 가중치는 `ScoreTable`(Version 1, 블록별 사전 — 표에 없는 블록은 0점·배율 100). `Calculate`=실제 값(음수 가능, 편집 화면·방 규칙), `CalculateDisplay`=카드 표시(최소 0), `CalculateDetailed`=항목별 내역(`ScoreLine`: 기본·스탯·코스트·키워드·효과 순번·하드캡 환급). 새 블록을 추가하면 `ScoreTable`에도 값을 넣고 Version을 올린다.
- `presentation`(카드): 등장·퇴장 연출 종류(문자열 ID, 기본 `"default"`). 끌 수 없다. `effects[].presentation`: `enabled`(효과 연출 켜기/끄기), `activation`·`resolution`(발동·처리 연출 종류), `hitMode`(`Simultaneous` | `Sequential`, 광역 피격). GameDesign §7.3.
- 스키마 v1 → v2: `presentationStyle` 문자열을 `presentation.enter/exit`로 옮긴다(중첩 소환물 포함). 덱 JSON은 별도 형식 버전(1): `{ schemaVersion, id, name, cards: [카드 ID…] }`.
- 사운드는 일반화해 `enter`(등장: 소환/발동) / `exit`(퇴장: 사망/파괴/교체)로 둔다.
- 스키마 변경 시 `schemaVersion`을 올리고 마이그레이션 코드를 둔다.

## 4.1 규칙 엔진 처리 순서 (M2.5 체인 기준)

- **효과 블록 레지스트리** (`EffectRegistry`): 조건 `IConditionEvaluator`, 대상 `ITargetSelector`, 행동 `IActionHandler`(상시 가능하면 `IAuraActionHandler`), 키워드 `KeywordInfo`. 새 블록 = 열거형 항목 + 구현 클래스 + `CreateDefault()`에 등록 한 줄. 엔진과 기존 블록은 수정하지 않는다.
- **키워드**는 두 종류다. 효과형(선봉 = 등장 시 자신의 주인 1장 드로우, 보물상자 = 퇴장 시 1장 드로우)은 레지스트리에 암묵 효과로 등록한다. 판정형(도발·은신·일회성)은 엔진이 해당 시점에 검사한다.
- **체인** (GameDesign §7.2, 유희왕식): 체인 상태(`Chain`, `PendingTriggers`, `PriorityPlayer`, `PriorityPasses`, `Decision`, `PendingActions`)는 전부 `GameState`에 있다. 엔진은 처리 도중 플레이어 선택이 필요하면 멈추고 `Decision`(체인 기회: 올릴 수 있는 효과 목록 + 패스 가능 여부)을 남긴다. 클라이언트·AI는 `ChainActivateCommand`(효과 하나 올리기) / `PassChainCommand`로 답한다.
  1. 행동이 일으킨 발동은 `PendingTriggers`로 모인다 (조건은 이때 검사).
  2. 체인 기회 (**한 번에 하나**): 새 체인은 현재 턴 플레이어부터(그 플레이어에게 대기 효과가 없으면 상대부터). 기회를 가진 플레이어는 대기 효과 하나를 올리거나 패스한다.
     - 체인 1 다음은 발동자 → 상대, 체인 2 이후 다음은 상대 → 건 사람.
     - 올리지 않은 강제 효과가 있으면 패스 불가. 강제 1개뿐이면 자동으로 올림. 올릴 것이 없으면 자동 패스(대응 효과는 `HasResponses` 자리, 블록은 아직 없음).
  3. 둘이 연속 패스하면 체인이 닫힌다. 남은 대기 효과(선택 효과뿐)는 버린다.
  4. 마지막 링크부터 처리. 체력 0 이하는 피해 즉시 사망. 처리 중 생긴 발동은 다음 체인.
  5. 체인도 대기 효과도 없으면 `PendingActions`(전투 피해, 턴 넘기기)를 실행한다. 공격은 "공격 시" 체인 → 전투 피해, 턴 종료는 "턴 종료 시" 체인 → 상대 턴 시작.
  6. 매 단계마다 상시 효과 재계산 → 사망 → 승패 판정(`Settle`). 영웅 체력 0 이하면 즉시 대전 종료, 남은 체인은 버린다.
  7. 플레이어 행동 1개(선택 응답 포함)에서 처리한 체인 수가 `MaxTriggerResolutionsPerAction`을 넘으면 남은 체인·대기 효과를 버린다.
- **이벤트**: 체인에 올라감 `EffectActivatedEvent`(발동 연출) → 처리 시작 `EffectResolvingEvent`(처리 연출, 뒤따르는 이벤트가 결과) / 무효화 `EffectNegatedEvent` → `ChainEndedEvent`. 선택 대기 `DecisionRequestedEvent`.
- **카드 내기**: 코스트 지불 → 손패에서 제거 → 즉시 배치(필드는 기존 필드 퇴장 먼저) / 주문은 바로 묘지(일회성은 소멸) → 발동한 효과들(내 보드의 "카드 사용 시", 낸 카드의 "등장 시", 교체된 필드의 "퇴장 시")로 체인.
- **턴 시작**: 최대 코스트 증가·충전 → 기물 공격 가능 상태로 → 1장 드로우 → "턴 시작 시" 체인.
- **수치 모델**: 공격력·체력·코스트 = 기본값(영구 변경 포함) + 상시 보정값. 체력은 받은 피해량으로 관리하고, 상시 보정은 처리 단계마다 처음부터 다시 계산한다.
- **상태 해시** (`StateHasher`, FNV-1a 64): 리플레이·동기화 검증용. 시드 + 덱 + 명령 목록이 같으면 해시가 같다.

## 4.2 화면 (M3, 2.5D) — 구현 메모

- 화면 요소는 프리팹 없이 **코드로 생성**한다 (`Shapes`, `CardView` 등). 카드 그림·사운드가 붙기 전까지 종류별 색 + 이름 + 숫자로 그린다.
- 보드 좌표: 바닥이 XZ 평면, 화면을 보는 플레이어가 -Z 쪽. 배치는 `BoardLayout`에 모아 둔다 (동료 2x2 | 필드 | 기물 3 | 소환물 2x2 | 덱, 뒤에 영웅).
- `BoardView.Render(state, viewer)`는 상태를 **즉시** 그대로 배치한다 (연출 없음). 연출 재생기는 이 위에 얹는다.
- 한글 폰트: 지금은 OS 폰트(맥 Apple SD Gothic Neo, 윈도우 맑은 고딕)를 실행 중에 불러온다. **출시 전 라이선스 맞는 폰트 파일로 교체 필요.**
- 런타임 머티리얼은 `Resources/Materials`의 기본 머티리얼을 복제해 색만 바꾼다 (빌드에 URP 셰이더 포함 보장).
- TextMeshPro 기본 리소스는 `Assets/TextMesh Pro`에 있다 (TMP 표준 위치라 번호 접두어 예외).
- 개발용 명령 (에디터가 닫혀 있을 때):
  - 준비: `Unity -batchmode -quit -projectPath youerface-TCG -executeMethod TCG.EditorTools.ProjectSetup.SetupAll`
  - 화면 캡처: `Unity -batchmode -projectPath youerface-TCG -executeMethod TCG.EditorTools.BattleScreenshot.CaptureFromCommandLine -screenshot out.png [-seed N -steps N]` (`-nographics` 없이). 캡처 중에는 SRP Batcher를 잠시 끈다 (프레임 없이 바로 그리면 머티리얼 색이 반영되지 않음).
  - 에디터 메뉴: TCG/Setup/*, TCG/Capture Battle Screenshot

## 5. 이벤트와 연출 (M3 2단계 구현, 방향은 GameDesign §7.2·§7.3)

- Core는 `GameEvent` 목록만 내보내고, 연출을 전혀 모른다.
- **재생 단위**: 명령 1개의 이벤트 묶음. 클라이언트는 명령 직전 상태 복사본(`Before`)·직후 복사본(`After`)과 함께 `PresentationPlayer.Enqueue`로 넘긴다. 묶음은 차례로 재생된다.
- **연출기 고르기**: `PresenterRegistry`가 `(이벤트 종류, 연출 종류 ID)`로 `IEventPresenter`를 찾는다. 연출 종류는 카드의 `presentation.enter/exit`, 효과의 `presentation.activation/resolution`에서 오고, 없으면 `"default"`. **새 연출 = 새 항목 등록**, 기존 코드 수정 없음. 효과 연출이 꺼져 있으면(`enabled=false`) 발동·처리 연출만 건너뛰고 시스템 연출은 재생한다.
- **순서와 병렬**:
  - 구조 이벤트(카드 내기, 주문, 체인 발동·처리·무효, 공격, 턴, 승패)는 하나씩 순서대로.
  - 그 사이에 이어지는 결과 이벤트(피해, 회복, 사망·퇴장, 수치·사용 횟수 변화, 드로우·버리기·소멸, 소환물 등장)는 **대상별 줄**로 나눠 병렬 재생. 같은 대상은 한 줄에서 순서대로(예: 피격 → 사망).
  - 직전 처리 효과의 `hitMode`가 `Sequential`이면 줄마다 0.28초씩 늦게 시작한다(하나씩 차례로), `Simultaneous`면 동시에.
- **화면 숫자**: `CardView`·`HeroView`는 규칙 상태와 별개로 "보이는 숫자"를 들고, 이벤트대로 한 단계씩 바꾼다. 새로 그리는 카드는 `Before`의 값(이번 명령에서 생긴 소환물은 정의 값)에서 시작한다.
- **맞추기**: 묶음이 끝나면 `BoardView.SyncTo(After)`로 실제 상태와 맞추고, 달랐던 곳 수를 `TotalDrift`에 더해 경고를 남긴다(연출 버그 검출용, PlayMode 테스트가 0인지 검사).
- **시간**: 모든 연출은 `PresentationTime.Speed` 배속을 따른다(빨리 감기·스킵용). 연출 하나가 예외로 멈춰도 전체가 멈추지 않도록 `Tween.Safe`로 감싼다.
- **입력 제한**: `PresentationPlayer.IsPlaying` 동안 `BattleSession.TrySubmit`이 명령을 거절한다. 손패 카드를 집어 끄는 것은 되지만 내기·대상 선택·공격은 안 된다 (GameDesign §7.3).

## 5.0 컴퓨터 차례 표시 (M3 4단계)

- 상대가 카드를 내면 `BigCardOverlay`가 화면 가운데에 `CardDetailView`(이름·코스트·능력치·키워드 설명·효과 설명)를 크게 띄운다: 커지며 등장 → 1.1초 보여 줌(클릭하면 바로 넘김) → 사라짐. 그동안 3D 카드는 바로 칸으로 간다(주문은 옆에 떠서 효과 처리를 보여 줌). 내 카드는 크게 띄우지 않는다.
- 컴퓨터 행동 간격 `AiPacing`: 다음 행동을 먼저 고른 뒤 종류별로 쉰다 (카드 내기 0.75초, 공격 0.6초, 턴 종료 0.7초, 체인 선택 0.45초, 모두 연출 배속 적용). 그동안 HUD에 "상대가 생각 중…".

## 5.1 입력 (M3 3단계)

- 사람과 컴퓨터 모두 `BattleSession.TrySubmit(Command)`로 명령을 낸다 (검증 실패 시 이유 문자열 → 화면 아래 안내 문구). 컴퓨터 차례는 `BattleSession.Tick`이 연출이 끝난 뒤 진행한다.
- 포인터는 Input System의 `Pointer`(마우스·터치 공용)로 읽는다. 카드·영웅에 BoxCollider를 붙여 레이캐스트로 고른다.
  - 손패 카드 누르고 끌기 → `PlayLineZ`보다 위에 놓으면 내기 (기물은 놓은 X 위치로 칸 결정). 대상이 필요한 카드는 놓은 뒤 화살표로 대상 클릭 (오른쪽 클릭·Esc 취소).
  - 내 기물 누르고 끌기 → 공격 화살표, 대상 위에서 놓으면 공격.
  - 고를 수 있는 대상은 엔진 `Validate`로 미리 걸러 초록빛 표시.
  - 끌고 있는 카드는 `CardView.Held`로 표시해 배치 연출이 옮기지 않게 한다.
  - **카드 확인**: 카드를 끌지 않고 떼면(누른 시간 무관, 28px 안) `CardInspectOverlay`가 상세 카드(`CardDetailView`)를 화면 가운데에 크게 띄우고 뒤를 어둡게 한다. 앞면이 보이는 카드만(내 손패, 양쪽 보드의 기물·필드·동료·소환물 — 상대 손패 제외). 아무 데나 클릭·Esc로 닫고, 보는 동안에도 대전은 계속 진행된다(사용자 결정 2026-10-13). 손패 카드는 28px 넘게 움직이거나 0.45초 넘게 눌러야 들어 올리고, 내 기물은 28px 넘게 끌어야 공격 화살표가 나온다. 어두운 배경이 UI 클릭을 막고, 닫은 프레임의 클릭은 보드 입력으로 넘기지 않는다.
  - HUD 글자(`HudView`)는 `raycastTarget=false` — 글자 영역(가운데 알림 등)이 보드 클릭을 가로채지 않게. 새 UI 글자도 클릭 대상이 아니면 끈다.
- 체인 기회 창(`DecisionPanel`): "체인 n에 올릴 효과를 고르세요" — 하나를 누르면 바로 체인에 올라가고 다음 기회는 규칙대로 돌아온다. 한 창에 강제 효과(주황 얇은 선 상자 "무조건 사용")와 선택 효과(하늘색 상자 "체인 걸 수 있음")를 나눠 보여 준다(`UiFactory.OutlineBox`). 상자 영역이 `MaxListHeight`(약 5줄)를 넘으면 창 높이를 고정하고 `ScrollRect`로 스크롤(휠·끌기·스크롤바). 강제 효과가 남아 있으면 패스 버튼이 비활성.
- UI 클릭은 `EventSystem` + `InputSystemUIInputModule` (실행 시 생성).
- 이전 프로젝트의 `TimelineDirector.PlayAsync`와 Binder 패턴(§8)은 Timeline 기반 연출기를 만들 때 새 연출기로 등록해 쓴다.
- **나중에 연출 공유(GameDesign §7.4)를 위한 전제**: 유저 연출은 코드가 아닌 JSON 프리셋(기본 동작 단계의 순차/동시 조합)이고, 이를 해석하는 **데이터 기반 연출기 하나**를 레지스트리에 등록하는 방식으로 붙인다. 따라서 지금 만드는 기본 연출기들도 가능하면 "기본 동작 단계"(이동·크기·흔들기·페이드·사운드 등, 현재 `Tween`)를 조합하는 형태로 유지해, 나중에 같은 단계를 데이터로 옮기기 쉽게 한다. 연출 ID 자리(`presentation.enter` 등)는 기본 연출 ID 또는 프리셋 해시(`sha256:...`)를 받을 수 있게 둔다.

## 5.2 흐름: 시작 화면 → 대전 → 결과 (M3 5단계)

- 장면 두 개: `Title`(빌드 첫 장면) → `Battle`. 둘 다 `ProjectSetup`(TCG/Setup/Build Title Scene·Build Battle Scene)이 코드로 만들고 빌드 목록 순서(Title, Battle, 나머지)를 맞춘다.
- `TitleScreen`: 난이도 4개(쉬움·보통·어려움·챌린지) 중 하나 + "대전 시작". 덱은 양쪽 샘플 덱 고정(덱 선택은 나중에), 연출 배속 설정은 넣지 않음(사용자 결정 2026-10-13).
- `BattleLaunch`(static): 고른 난이도와 새 시드를 들고 Battle 장면을 연다. `BattleBootstrap.Start`가 요청을 한 번 읽어(`TryConsume`) 상대(위쪽) 컴퓨터 난이도·시드로 쓴다. 요청 없이 Battle 장면을 바로 열면(기존 PlayMode 테스트·에디터 Play) 장면에 저장된 설정 그대로.
- 결과: 대전이 끝나고 연출이 다 끝나면 `BattleBootstrap`이 HUD 위에 `ResultPanel`(승리/패배/무승부, 상대 난이도·턴 수, "다시 하기"=같은 난이도 새 시드로 Battle 다시 열기, "난이도 선택으로"=Title)을 덮는다. 반투명 배경이 아래 클릭을 막는다.

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

## 7. 유저 에셋 (이미지·사운드) — M4-2

- Core(`Core/Data`): `AssetLimits`(그림 512x427·6:5, 최대 확대 400%, JPG 품질 90, 사운드 5000ms, 확장자 목록), `AssetHash`("sha256:소문자hex"), `ArtCrop.Compute`(SNS식 영역 계산, 정수), `CardAssetRefs.Collect`(카드·포함 소환물의 에셋 해시, 대전 시 전송용).
- `IFilePicker` → `StandaloneFilePicker`: OS 기본 창. 패키지 `Packages/io.github.gkngkc.unity-standalone-file-browser`(OpenUPM 1.3.4, MIT, 맥 universal 바이너리)를 임베디드로 넣음. 원본 gkngkc 1.2는 맥 x86_64 전용이라 Apple Silicon에서 안 됨. 네이티브 실패 시 에디터는 `EditorUtility.OpenFilePanelWithFilters`로 대신. 모바일은 나중에(M7) 구현 교체.
- `IUserAssetStore` → `LocalUserAssetStore`: `persistentDataPath/UserAssets/<hex>.<확장자>`. 같은 내용은 다시 쓰지 않음(임시 파일 → 이동).
- `UserAssetImporter`: 이미지 `LoadSourceImage`(PNG·JPG, 크기 제한 없음, GPU 최대 텍스처 크기만) → `ArtCropDialog`(끌기·휠·+/−, 창에 보이는 그대로 저장) → `SaveArt`(Blit으로 잘라 줄이고 JPG). 사운드 `ImportSound`(OGG·WAV, `UnityWebRequestMultimedia`로 읽어 길이 검사, 원본 바이트 저장).
- `UserAssetCache`: 해시 → Texture2D(동기) / AudioClip(코루틴으로 미리 읽음). `UserAssetService`가 저장소·캐시·가져오기·파일 창을 하나씩 들고 있음.
- 대전 적용: `CardView`(그림 판 Quad, 위쪽 +Z), `CardDetailView`(그림 칸에 6:5 RawImage), `CardSoundPlayer`(BattleBootstrap이 덱 사운드를 미리 읽음, `CardEntered`·`SpellCast`에서 등장음, `CardExited`에서 퇴장음). 그림·사운드가 없거나 저장소에 없으면 기존 표시·무음.
- 나중에 서버 업로드를 붙여도 해시 참조 구조를 그대로 쓴다.

## 7.1 카드 제작기 (M4-3·M4-4)

- 장면 `CardMaker.unity`(에디터 메뉴 TCG/Setup/Build CardMaker Scene), 시작 화면 오른쪽 아래 "카드 제작" 버튼으로 들어감.
- `CardMakerScreen`: 왼쪽(기본 정보·키워드·그림·사운드) / 가운데(`CardDetailView` 미리보기, 점수, 규칙 검사 결과) / 오른쪽(효과 블록: 트리거·발동·조건·행동·대상·수치). 값이 바뀌면 양쪽 편집 칸을 다시 쌓는다(이름 입력은 미리보기만 갱신해 입력 칸이 유지됨).
- 고르기 조작은 ◀ 값 ▶(목록 순환), 숫자는 − 값 +, 키워드는 칩. 선택지는 전부 보여 주고 규칙 위반은 `CardValidator` 결과로 가운데에 표시(제작 중엔 저장 가능, 덱에 넣을 때 다시 검사).
- 소환물: 행동을 "소환물 생성"으로 고르면 기본 소환물이 생기고 "편집 →"으로 들어감. 위쪽 경로(카드 › 소환물)와 "돌아가기"로 나옴. 점수는 항상 최상위 카드 기준(소환물 점수 합산).
- 편집 규칙 도우미는 Core `CardEditing`(새 카드·효과 기본값, 종류·행동·대상·조건을 바꿀 때 맞지 않는 값 정리, JSON 왕복 복사). 편집기 입력 범위(스탯 0~20 등)는 규칙값이 아니라 화면 상수.
- 점수 표시: 실제 값(음수 가능), 표시 점수와 다르면 "(카드 표시 n)"도. 항목별 내역(기본·스탯·코스트·키워드 합·효과 합·패널티 상한).
- 저장: `LocalCardLibrary` → `persistentDataPath/Cards/<카드 ID>.json`(카드 JSON 그대로, 임시 파일→이동). 카드 ID는 Client가 `c_` + 12자리 16진수로 만든다(Core 로직 아님). "내 카드" 창에서 열기·삭제(두 번 눌러 확인), "복제"는 새 ID로 열기(저장은 따로), 바뀐 게 있으면 경로 옆에 "저장 안 됨".

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
