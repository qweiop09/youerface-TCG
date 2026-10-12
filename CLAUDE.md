# youerface-TCG

유저가 카드(이미지, 등장/퇴장 사운드, 효과·성능)를 직접 만들고, 그 카드로 다른 유저와 1대1 온라인 대전을 하는 턴제 카드게임.
하스스톤 기반 규칙 + 원신 "칠성소환"식 별도 영역(필드, 동료, 소환물).

## 문서 맵 (작업 전 반드시 읽을 것)

| 문서 | 내용 |
|---|---|
| [docs/GameDesign.md](docs/GameDesign.md) | 기획: 용어, 영역, 카드 종류, 점수, 효과, 멀티, **미정 사항 목록** |
| [docs/Architecture.md](docs/Architecture.md) | 기술 설계: Core/Client 분리, 결정론, 데이터 스키마, 네트워크, 유저 에셋 |
| [docs/Roadmap.md](docs/Roadmap.md) | 마일스톤과 작업 체크리스트 (진행 시 체크 갱신) |

## 저장소 구조

- Unity 프로젝트는 저장소 루트가 아니라 **`youerface-TCG/` 하위 폴더**에 있음 (`youerface-TCG/Assets`, `youerface-TCG/Packages` …).
- Unity **6000.4.1f1**, URP 17.4, Input System, Timeline, Test Framework 설치됨.
- 기획 전용 세션과 개발 세션이 분리되어 있음. 기획 변경은 docs/ 문서에 먼저 반영된 뒤 개발함.

## 작업 원칙

1. **용어를 정확히 쓴다.** 특히 `점수`(카드 가치)와 `코스트`(하스스톤 마나에 해당)는 다른 개념이다. 하스스톤의 "하수인"은 **`기물`**이라고 부른다(코드 이름은 `Minion`). → [GameDesign.md §2](docs/GameDesign.md#2-용어)
2. **Core/Client 분리를 지킨다.** 게임 규칙은 `TCG.Core` 어셈블리(Unity 참조 없음)에만, 화면·연출·입력·네트워크는 `TCG.Client`에. → [Architecture.md §2–3](docs/Architecture.md)
3. **Core는 결정론적으로 작성한다.** int만 사용, 자체 RNG, 컬렉션 순회 순서 고정. → [Architecture.md §3](docs/Architecture.md#3-core-작성-규칙)
4. **"미정"으로 표시된 사항은 임의로 확정하지 않는다.** 구현에 필요하면 사용자에게 먼저 물어본다. 당장 값이 필요하면 `RuleConfig`의 임시값으로 두고 코드에 박지 않는다.
5. **사용자와 상세 논의가 예정된 기능은 구현 전에 먼저 논의한다.**
   - 연출 시스템(이벤트 큐 + 연출 해석기)의 세부 구조
   - 점수 계산 세부 공식(가중치, 하드캡)
6. 숫자 규칙값(영웅 체력, 덱 장수, 최대 코스트 등)은 전부 `RuleConfig` 데이터로 둔다.
7. Roadmap의 작업을 끝내면 [docs/Roadmap.md](docs/Roadmap.md) 체크박스를 갱신한다.
8. Core 로직에는 EditMode 단위 테스트를 함께 작성한다.

## 코드/에셋 컨벤션

- 에셋 폴더는 번호 접두어를 쓴다: `00_Scenes`, `01_Scripts`, `02_Prefabs`, `03_Datas`, `04_TimeLines`, `05_Animations` …
- 네임스페이스: `TCG.Core.*`, `TCG.Client.*`, `TCG.Tests.*`
- 커밋은 사용자가 요청할 때만 한다. 메시지 형식: `feat :: 설명`, `fix :: 설명`, `chore :: 설명`, `docs :: 설명`, `art :: 설명`

## 참고: 이전 프로젝트에서 차용할 코드

`/Users/dgsw26/GithubProject/ProjectNeoulgyeol` (사용자의 턴제 RPG). 구조가 달라 통째로 재사용하지 않고, 아래 연출·유틸 코드만 필요할 때 옮겨와 Client 쪽에 맞게 고쳐 쓴다. 목록은 [Architecture.md §8](docs/Architecture.md#8-이전-프로젝트projectneoulgyeol-차용-목록).

## 알려진 이슈

- (해결됨) 루트 `.gitignore`의 Unity 패턴은 `/youerface-TCG/[Ll]ibrary/`처럼 하위 Unity 폴더 기준으로 작성한다. 새 패턴을 추가할 때도 같은 기준을 따를 것.
