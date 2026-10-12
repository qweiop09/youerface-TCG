# 개발 로드맵

> 작업을 마치면 체크박스를 갱신한다. 🗣 표시는 **구현 전 사용자와 논의가 필요한 작업**이다.
> 최종 갱신: 2026-10-12

## M0. 프로젝트 셋업
- [x] 루트 `.gitignore` 수정: Unity 패턴을 `/youerface-TCG/...` 기준으로 변경 (2026-10-12 완료)
- [x] 템플릿 잔여물 정리 (`Assets/TutorialInfo`, `Readme.asset`) — `Scenes`는 `00_Scenes`로 이동
- [x] 번호 접두어 에셋 폴더 생성 ([Architecture.md §2](Architecture.md#2-폴더--어셈블리-구조))
- [x] `com.unity.nuget.newtonsoft-json` 패키지 추가 (3.2.2)
- [x] asmdef 생성: `TCG.Core`(noEngineReferences), `TCG.Client`, `TCG.Core.Tests`(EditMode)
- [ ] 빈 테스트 1개가 Test Runner에서 통과하는지 확인 — dotnet 하네스로는 통과 확인, **Unity Test Runner 확인 대기**

**완료 기준**: Unity에서 컴파일 에러 없음, EditMode 테스트 실행됨, `git status`에 Library/Temp 등이 안 보임.

## M1. Core 데이터 모델
- [x] `RuleConfig` (규칙 수치, 임시값은 GameDesign §3.2)
- [x] `CardDefinition`, `EffectDefinition`(트리거/조건/대상/행동), 카드 종류·키워드 열거형 (+ `CardValidator`: §5.1 확정 규칙 검사)
- [x] JSON 직렬화/역직렬화 + `schemaVersion`
- [x] `GameState`, `PlayerState`, `Entity`, `Zone`(칸 수 제한: 하수인 3, 필드 1, 동료 4, 소환물 4)
- [x] `DeterministicRng` (시드·상태 직렬화 가능)
- [x] `ScoreCalculator` 뼈대 (static) — 🗣 세부 공식은 사용자와 논의 전까지 단순 임시 가중치
- [x] 테스트: JSON 왕복, Zone 칸 수 제한, RNG 재현성 (52개, dotnet 하네스 통과 / Unity Test Runner 확인 대기)

## M2. Core 규칙
- [ ] `GameEngine.Validate / Apply`, Command·GameEvent 정의
- [ ] 대전 시작(덱 셔플, 시작 손패), 턴 시작/종료, 코스트 증가, 드로우
- [ ] 카드 내기: 하수인 / 필드(교체 + 기존 필드 "파괴 시" 발동) / 동료 / 소환물
- [ ] 공격, 피해, 사망 처리, 승패 판정
- [ ] 효과 블록 레지스트리 + 초기 블록 10개 내외 — 🗣 최종 목록 확인 (GameDesign Q8)
- [ ] 소환물 사용 횟수·턴 종료 발동
- [ ] 동시 발동 효과 처리 순서 정의
- [ ] 상태 해시 함수
- [ ] 테스트: 각 규칙별 단위 테스트, 시드 + Command 목록 재실행 시 동일 해시

**완료 기준**: Unity 화면 없이 테스트만으로 1판을 처음부터 끝까지 진행 가능.

## M3. Client 로컬 대전 (핫시트)
- [ ] 🗣 연출 시스템 상세 구조 논의 (이벤트 큐 + 연출 해석기, GameDesign Q9)
- [ ] 보드 UI: 영역별 카드 배치, 손패, 코스트·영웅 체력 표시
- [ ] 입력: 드래그로 카드 내기, 공격 대상 지정(화살표 차용)
- [ ] 기본 연출: 카드 그대로 표시하는 등장/퇴장/공격/피해 + 사운드
- [ ] 한 컴퓨터에서 2인 번갈아 플레이

## M4. 카드 제작기
- [ ] `IFilePicker`, `IUserAssetStore`(해시 저장), 이미지·사운드 런타임 로드
- [ ] 카드 편집 UI: 종류, 코스트·공격력·체력, 효과 블록 조합, 미리보기
- [ ] 실시간 점수 표시 (`ScoreCalculator`)
- [ ] 로컬 저장/불러오기 (JSON)
- [ ] 🗣 에셋 제한값 확정 (GameDesign Q10)

## M5. 덱 빌더
- [ ] 덱 구성, 덱 규칙 검증(장수·매수), 점수 기준 필터
- [ ] 덱 저장/불러오기

## M6. 온라인 대전
- [ ] `ITransport` 인터페이스 + Unity Relay/Lobby 구현
- [ ] 방 만들기/참가, 방 규칙(점수 상한)
- [ ] 핸드셰이크: 버전 확인 → 덱 교환·검증 → 에셋 전송
- [ ] 호스트 판정 + 플레이어 시야 필터링
- [ ] 연결 끊김 처리, 턴 타이머

## M7. 출시 준비 (미정)
- [ ] Steam 연동 (친구 초대, 오버레이, 업적)
- [ ] 모바일 대응 (입력, UI 스케일, 파일 선택)
- [ ] 업로드/공유 서버 (미구상)
