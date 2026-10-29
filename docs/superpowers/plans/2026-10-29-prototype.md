# 「30일 후 삭제됩니다」 첫 프로토타입 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 1일차 세 슬롯, 현실 과제, 나오는 장면, 2막 한 컷이 이어지는 유니티 프로토타입을 만든다.

**Architecture:** 게임 규칙(대사 읽기, 상태, 대화 진행, 슬롯 시계)은 유니티에 기대지 않는 순수 C# 어셈블리 `Oddity.Core` 에 두고 EditMode 테스트로 확인한다. 화면(메신저 UI, 하린 모델, 연출)은 `Oddity.Game` 어셈블리에서 Core를 불러 쓴다. 장면은 `Prototype.unity` 하나다.

**Tech Stack:** Unity 6 LTS, URP, UniVRM(VRM 1.0), Newtonsoft Json(`com.unity.nuget.newtonsoft-json`), Unity Test Framework, Timeline, 유니티 공식 MCP(막히면 MCP for Unity).

**Spec:** `docs/superpowers/specs/2026-10-29-prototype-design.md`

## Global Constraints

- 엔진 Unity 6 LTS, URP. 프로젝트 위치 `unity/`
- 화면 세로 9:16, 기준 해상도 1080 × 1920
- 하린 모델 `hari/gpt/previous/harin-v2.vrm`
- 빠른 모드 슬롯 간격 60초
- 나오는 장면 길이 10초 안팎
- 대사 파일 `unity/Assets/StreamingAssets/script/day1.json`, UTF-8
- 저장 파일 `Application.persistentDataPath/save.json`, 슬롯이 끝날 때만 저장
- `feeling` 장면은 숨김 수치를 바꾸지 않는다
- 표정 이름은 `neutral`, `happy`, `sad`, `angry`, `surprised`, `relaxed` 여섯 개
- `.gitignore`: `unity/Library/`, `unity/Temp/`, `unity/Logs/`, `unity/obj/`, `unity/Build/`, `unity/UserSettings/`
- 커밋 작성자 `13573570+destory1984@users.noreply.github.com`. 깃허브 push는 사용자에게 묻고, 마지막 push 뒤 1시간이 지나야 한다

## Review Focus

- 주인공 이름을 비우거나 공백만 넣음 → 이름 자리에 "너"가 들어가야 한다 (Task 4)
- 기기 시계를 뒤로 돌림 → 남은 대기 시간이 60초를 넘지 않아야 한다 (Task 5)
- 대사 파일 맨 앞에 BOM이 붙어 있음 → 정상으로 읽혀야 한다 (Task 2)
- 장면 도중 앱 종료 → 다음 실행 때 그 슬롯 첫 장면부터 다시 나와야 한다 (Task 4)
- 대사에 VRM에 없는 표정 이름이 들어감 → 멈추지 않고 `neutral` 로 두고 경고만 남겨야 한다 (Task 7)

---

## 파일 구조

```
unity/
  Assets/
    Oddity/
      Core/                     Oddity.Core.asmdef (noEngineReferences: true)
        ScriptModel.cs          Slot, Effects, Line, Choice, Scene, DayScript
        ScriptLoader.cs         JSON → DayScript, 검증
        GameState.cs            상태와 JSON 직렬화
        SaveStore.cs            파일 읽기/쓰기
        DialogueRunner.cs       장면 진행
        SlotClock.cs            슬롯 대기 시간
      Game/                     Oddity.Game.asmdef (Core, UniVRM, Timeline 참조)
        GameBootstrap.cs        시작점. Core와 화면을 잇는다
        ChatView.cs             메신저 UI
        HarinPuppet.cs          표정, 깜빡임, 노이즈, 흐려짐
        EmergenceSequence.cs    나오는 장면과 2막 한 컷
        DevPanel.cs             개발용 화면
      Shaders/
        HarinFade.shadergraph   흐려짐(투명도) + 노이즈
      Tests/EditMode/           Oddity.Tests.asmdef (Core 참조, Editor 전용)
        ScriptLoaderTests.cs
        GameStateTests.cs
        DialogueRunnerTests.cs
        SlotClockTests.cs
        Day1ContentTests.cs
    Scenes/Prototype.unity
    StreamingAssets/script/day1.json
    Models/harin-v2.vrm
```

## 테스트 돌리는 법

에디터가 열려 있으면 MCP의 테스트 실행 도구로 EditMode 테스트를 돌린다. 에디터가 닫혀 있으면 아래 명령을 쓴다. `<VER>` 는 설치한 Unity 6 LTS 버전이다.

```bash
"/c/Program Files/Unity/Hub/Editor/<VER>/Editor/Unity.exe" -batchmode -projectPath unity -runTests -testPlatform EditMode -testResults unity/Logs/editmode.xml -logFile -
```

통과 기준: `editmode.xml` 의 `<test-run ... result="Passed"` 와 `failed="0"`.

---

### Task 1: 프로젝트 뼈대와 MCP 연결

**Files:**
- Create: `unity/` (Universal 3D 템플릿), `unity/Packages/manifest.json` 수정, 위 asmdef 세 개, `.gitignore` 수정

**Interfaces:**
- Produces: 어셈블리 `Oddity.Core`, `Oddity.Game`, `Oddity.Tests`

- [ ] **Step 1:** 사용자가 Unity Hub에서 "Universal 3D" 템플릿으로 `C:\_c\oddity-phone\unity` 프로젝트를 만든다. (Hub 로그인은 사용자 몫)
- [ ] **Step 2:** AI Assistant 패키지(`com.unity.ai.assistant`)를 넣고 Project Settings > AI > Unity MCP를 켠다. Claude Code를 연결하고, 사용자가 연결 승인 창에서 허락한다. Personal 라이선스로 막히면 MCP for Unity(Coplay)를 Package Manager의 git URL로 넣고 Window > MCP for Unity의 자동 설정을 쓴다
- [ ] **Step 3:** `manifest.json` 에 `com.unity.nuget.newtonsoft-json`, `com.unity.timeline`, `com.unity.test-framework`, UniVRM(`com.vrmc.gltf`, `com.vrmc.vrm`, 같은 릴리스 태그의 git URL)을 넣는다
- [ ] **Step 4:** Player Settings: 세로 고정, 기준 해상도 1080 × 1920, Android 대상 추가
- [ ] **Step 5:** 파일 구조대로 asmdef 세 개를 만든다. `Oddity.Core` 는 `noEngineReferences: true`, Newtonsoft dll만 참조. `Oddity.Tests` 는 Editor 플랫폼만, `nunit.framework.dll` 과 `Oddity.Core` 참조
- [ ] **Step 6:** 루트 `.gitignore` 에 Global Constraints의 유니티 폴더 여섯 개를 넣는다
- [ ] **Step 7:** 빈 테스트 하나(`Assert.Pass()`)를 넣고 테스트 실행. 기대: Passed 1
- [ ] **Step 8:** `git status` 에 `Library/` 등이 안 보이는지 확인하고 커밋 `chore: scaffold Unity 6 URP project`

### Task 2: 대사 모델과 로더

**Files:**
- Create: `Core/ScriptModel.cs`, `Core/ScriptLoader.cs`
- Test: `Tests/EditMode/ScriptLoaderTests.cs`

**Interfaces:**
- Produces:
  - `enum Slot { Morning, Evening, Night }` (JSON 값 `"morning"`, `"evening"`, `"night"`)
  - `class Effects { int Stability; int Deviation; int Heart; }` (JSON `stability`, `deviation`, `heart`, 없으면 0)
  - `class Line { string Who; string Text; string Face; float Glitch; }` (Face 없으면 `"neutral"`, Glitch 없으면 0)
  - `class Choice { string Text; Effects Effects; string Next; }`
  - `class Scene { string Id; Slot Slot; string Type; List<Line> Lines; List<Choice> Choices; }` (Type 없으면 `"talk"`, 그 밖에 `"feeling"`, `"task"`)
  - `class DayScript { int Day; List<Scene> Scenes; Scene Get(string id); Scene EntryFor(Slot slot); }` — `EntryFor` 는 파일 순서에서 그 슬롯의 첫 장면
  - `static class ScriptLoader { static DayScript Parse(string json); }`
  - `class ScriptFormatException : Exception`

- [ ] **Step 1: 실패하는 테스트 작성**
  - `Parse_ReadsScenesChoicesAndEffects`: 설계 문서 6장의 예시 JSON → `Scenes.Count == 1`, `Get("d1_morning").Choices[0].Effects.Deviation == 1`, `Choices[1].Effects.Stability == -1`, `Lines[0].Face == "surprised"`, `Lines[1].Glitch == 0.2f`
  - `Parse_DefaultsMissingFields`: `face`, `glitch`, `type`, `effects` 를 뺀 장면 → `"neutral"`, `0f`, `"talk"`, 모든 수치 0
  - `Parse_ThrowsOnMissingNext`: `next: "nowhere"` → `ScriptFormatException`, 메시지에 `"d1_morning"` 과 `"nowhere"` 가 들어 있음
  - `Parse_ThrowsOnDuplicateId`: 같은 id 두 장면 → `ScriptFormatException`
  - `Parse_AcceptsUtf8Bom`: 예시 JSON 앞에 `"\uFEFF"` → 정상으로 읽힘
  - `EntryFor_ReturnsFirstSceneOfSlot`: morning 두 장면, evening 한 장면 → `EntryFor(Slot.Morning).Id` 가 첫 morning
- [ ] **Step 2:** 테스트 실행. 기대: 컴파일 실패 또는 FAIL
- [ ] **Step 3:** 모델과 `ScriptLoader.Parse` 구현. Newtonsoft `JsonConvert` 와 snake/camel 매핑, 문자열 앞 BOM 제거, 로드 뒤 id 중복과 `next` 대상 검사
- [ ] **Step 4:** 테스트 실행. 기대: 6개 모두 PASS
- [ ] **Step 5:** 커밋 `feat(core): script model and loader`

### Task 3: 게임 상태와 저장

**Files:**
- Create: `Core/GameState.cs`, `Core/SaveStore.cs`
- Test: `Tests/EditMode/GameStateTests.cs`

**Interfaces:**
- Consumes: `Effects`, `Slot`
- Produces:
  - `class GameState { const int CurrentVersion = 1; int Version; int Day = 1; Slot Slot = Morning; int Stability = 50; int Deviation = 0; int Heart = 0; int DaysLeft = 30; string PlayerName = ""; DateTime NextOpenUtc; void Apply(Effects e); string ToJson(); static GameState FromJson(string json, out bool recovered); }`
  - `Apply` 는 세 수치를 더하고 0 → 100 으로 자른다
  - `FromJson` 은 JSON이 깨졌거나 `Version != CurrentVersion` 이면 새 `GameState` 를 돌려주고 `recovered = true`
  - `class SaveStore { SaveStore(string path); GameState Load(out bool recovered); void Save(GameState s); }` — 파일이 없으면 새 상태, `recovered = false`. 저장은 임시 파일에 쓰고 바꿔치기

- [ ] **Step 1: 실패하는 테스트 작성**
  - `Apply_AddsAndClamps`: Stability 50에 +60 → 100, Deviation 0에 -5 → 0, Heart 0에 +3 → 3
  - `Json_RoundTrips`: 모든 필드를 바꾼 상태 → `FromJson(ToJson())` 이 같은 값, `recovered == false`
  - `FromJson_CorruptReturnsFresh`: `"{not json"` → Day 1, Stability 50, `recovered == true`
  - `FromJson_OldVersionReturnsFresh`: `Version: 0` → 새 상태, `recovered == true`
  - `SaveStore_MissingFileReturnsFresh`, `SaveStore_SaveThenLoad`: 임시 폴더 경로로 확인
- [ ] **Step 2:** 실행. 기대: FAIL
- [ ] **Step 3:** 구현
- [ ] **Step 4:** 실행. 기대: 6개 PASS
- [ ] **Step 5:** 커밋 `feat(core): game state and save store`

### Task 4: 대화 진행

**Files:**
- Create: `Core/DialogueRunner.cs`
- Test: `Tests/EditMode/DialogueRunnerTests.cs`

**Interfaces:**
- Consumes: `DayScript`, `Scene`, `Line`, `Choice`, `GameState.Apply`
- Produces:
  - `class DialogueRunner { DialogueRunner(DayScript script, GameState state); event Action<Line> LineShown; event Action<IReadOnlyList<Choice>> ChoicesShown; event Action<Slot> SlotFinished; Scene Current; void StartSlot(Slot slot); void Advance(); void Choose(int index); }`
  - `LineShown` 으로 넘기는 `Line.Text` 는 `{player}` 를 `state.PlayerName` 으로 바꾼 사본이다. 이름이 비었거나 공백뿐이면 `"너"`
  - `Advance()` 는 다음 줄을 보인다. 줄이 끝나면 선택지가 있으면 `ChoicesShown`, 없으면 `SlotFinished`
  - `Choose(i)` 는 장면 `Type != "feeling"` 일 때만 `Effects` 를 적용하고, `Next` 가 있으면 그 장면으로, 없으면 `SlotFinished`
  - 진행 중 위치는 저장하지 않는다. `StartSlot` 은 늘 `EntryFor(slot)` 에서 시작한다

- [ ] **Step 1: 실패하는 테스트 작성** (테스트용 작은 DayScript를 코드로 만든다)
  - `StartSlot_ShowsFirstLineOfEntry`
  - `Advance_AfterLastLine_ShowsChoices`
  - `Choose_AppliesEffectsAndMovesToNext`: Deviation +1, `Current.Id` 가 `Next`
  - `Choose_FeelingScene_DoesNotApplyEffects`: Stability, Deviation, Heart 그대로
  - `Choose_NoNext_FinishesSlot`: `SlotFinished(Slot.Morning)` 한 번
  - `PlayerToken_ReplacedWithName`: 이름 "민수" → `"민수, 맞지?"`
  - `PlayerToken_BlankName_UsesDefault`: 이름 `"  "` → `"너, 맞지?"`
  - `StartSlot_AgainAfterAbort_RestartsAtEntry`: 두 줄 진행 뒤 새 runner로 `StartSlot` → 첫 줄
- [ ] **Step 2:** 실행. 기대: FAIL
- [ ] **Step 3:** 구현
- [ ] **Step 4:** 실행. 기대: 8개 PASS
- [ ] **Step 5:** 커밋 `feat(core): dialogue runner`

### Task 5: 슬롯 시계

**Files:**
- Create: `Core/SlotClock.cs`
- Test: `Tests/EditMode/SlotClockTests.cs`

**Interfaces:**
- Consumes: `GameState.Slot`, `GameState.Day`, `GameState.NextOpenUtc`
- Produces:
  - `class SlotClock { static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(60); SlotClock(GameState state, Func<DateTime> utcNow); bool IsOpen; TimeSpan Remaining; void CloseCurrent(); void SkipWait(); }`
  - `CloseCurrent()` 는 슬롯을 Morning → Evening → Night → 다음 날 Morning 으로 넘기고 `NextOpenUtc = now + 60초`
  - `Remaining` 은 0 → 60초 범위로 자른다. 시계를 뒤로 돌려도 60초를 넘지 않는다
  - `SkipWait()` 는 `NextOpenUtc = now`

- [ ] **Step 1: 실패하는 테스트 작성** (가짜 시계 함수 사용)
  - `Close_AdvancesSlotAndWaits60s`: Morning → Evening, `IsOpen == false`, `Remaining == 60s`
  - `OpensAfterInterval`: 61초 뒤 `IsOpen == true`
  - `Close_Night_GoesToNextDayMorning`: Day 1 Night → Day 2 Morning
  - `ClockMovedBack_RemainingCappedAt60s`: 닫은 뒤 시계를 1시간 뒤로 → `Remaining == 60s`
  - `SkipWait_OpensNow`
- [ ] **Step 2:** 실행. 기대: FAIL
- [ ] **Step 3:** 구현
- [ ] **Step 4:** 실행. 기대: 5개 PASS
- [ ] **Step 5:** 커밋 `feat(core): slot clock`

### Task 6: 1일차 대사

**Files:**
- Create: `unity/Assets/StreamingAssets/script/day1.json`
- Test: `Tests/EditMode/Day1ContentTests.cs`

**Interfaces:**
- Consumes: `ScriptLoader.Parse`, `DayScript.EntryFor`
- Produces: 장면 id `d1_morning`, `d1_evening`, `d1_night`, 감정 버튼 장면 `d1_evening_feeling`, 현실 과제 장면 `d1_night_task`, 2막 한 컷 `d1_after` (slot `night`, 나오는 장면 뒤에 `GameBootstrap` 이 직접 시작)

- [ ] **Step 1:** 설계 문서 7장 방향대로 대사를 쓴다. 장면마다 3 → 8줄. 저녁에 "아프지 않게 해줄게", 밤 과제 선택지 세 개(서윤 대화방 지우기 / 친구 단톡에 답하기 / 둘 다 미루기), 밤 마지막 줄 "거기 따뜻해 보여". 표정은 여섯 개 이름만 쓴다
- [ ] **Step 2: 테스트 작성**
  - `Day1_Loads`: 파일을 읽어 `Parse` 가 예외 없이 끝남
  - `Day1_HasEntryForEachSlot`: 세 슬롯 모두 `EntryFor` 가 null 아님
  - `Day1_FacesAreKnown`: 모든 줄의 Face가 여섯 이름 중 하나
  - `Day1_HasFeelingAndTask`: `d1_evening_feeling` 의 Type `"feeling"`, `d1_night_task` 의 Type `"task"`, 선택지 3개
- [ ] **Step 3:** 실행. 기대: 4개 PASS
- [ ] **Step 4:** 커밋 `content: day 1 script draft`

### Task 7: 하린 모델과 앱 속 무대

**Files:**
- Create: `unity/Assets/Models/harin-v2.vrm` (복사), `Game/HarinPuppet.cs`, `Shaders/HarinFade.shadergraph`
- Modify: `Scenes/Prototype.unity`

**Interfaces:**
- Produces: `class HarinPuppet : MonoBehaviour { void SetFace(string face); void SetGlitch(float amount); void SetFade(float alpha); void MoveTo(Transform anchor); }`
  - `SetFace` 는 VRM 1.0 `Expression` 프리셋에 매핑한다. 모르는 이름이면 `neutral` 로 두고 `Debug.LogWarning`
  - 3 → 6초 간격으로 눈을 깜빡인다
  - `SetFade` 는 모든 머티리얼의 투명도를 0 → 1 로 바꾼다 (`HarinFade` 셰이더)

- [ ] **Step 1:** VRM을 `Assets/Models/` 에 넣고 UniVRM이 프리팹으로 가져오는지 확인한다
- [ ] **Step 2:** `Prototype.unity` 에 "AppStage" 를 원룸과 떨어진 좌표(x = 100)에 두고 하린을 세운다. 전용 카메라 → RenderTexture 1024 × 1024
- [ ] **Step 3:** `HarinPuppet` 과 `HarinFade` 구현
- [ ] **Step 4:** MCP로 플레이 모드를 돌려 `SetFace("happy")`, `SetFace("wink_unknown")`, `SetFade(0.3f)` 를 호출하고 RenderTexture를 캡처한다. 기대: 표정 변화, 경고 한 줄, 반투명
- [ ] **Step 5:** 커밋 `feat(game): harin puppet and app stage`

### Task 8: 원룸, 휴대폰, 메신저 UI, 시작점

**Files:**
- Create: `Game/ChatView.cs`, `Game/GameBootstrap.cs`
- Modify: `Scenes/Prototype.unity`

**Interfaces:**
- Consumes: `ScriptLoader`, `SaveStore`, `DialogueRunner`, `SlotClock`, `HarinPuppet`
- Produces:
  - `class ChatView : MonoBehaviour { event Action Tapped; event Action<int> ChoicePicked; event Action<string> NameEntered; void ShowLine(Line line); void ShowChoices(IReadOnlyList<Choice> choices); void ShowWaiting(TimeSpan remaining); void ShowLockNotice(string text); void AskName(); }`
  - `class GameBootstrap : MonoBehaviour` — 시작 시 `day1.json` 과 저장을 읽고, 이름이 비었으면 `AskName`, 슬롯이 열려 있으면 `StartSlot`. `LineShown` 마다 `ChatView.ShowLine` 과 `HarinPuppet.SetFace/SetGlitch`. `SlotFinished` 에서 `SlotClock.CloseCurrent()` 뒤 저장. Night가 끝나면 저장 뒤 `EmergenceSequence.Play()`

- [ ] **Step 1:** 원룸을 상자와 평면으로 짓는다(침대, 책상, 창문 자리, 밤 조명 하나). 책상 위에 휴대폰 상자와 화면 평면
- [ ] **Step 2:** 폰 화면 위 World Space 캔버스에 메신저 UI: 위쪽 하린 창(RenderTexture), 말풍선 목록, 선택지 버튼, 감정 버튼, 대기 화면. 메인 카메라를 화면에 꽉 붙인다
- [ ] **Step 3:** `ChatView`, `GameBootstrap` 구현
- [ ] **Step 4:** MCP 플레이 모드로 확인: 이름 입력 → 아침 장면 → 선택 → 60초 대기 화면. 캡처
- [ ] **Step 5:** 저장 확인: 아침 도중 플레이 모드를 멈췄다 다시 켜면 아침 첫 줄부터 나온다
- [ ] **Step 6:** 커밋 `feat(game): room, phone UI and bootstrap`

### Task 9: 나오는 장면과 2막 한 컷

**Files:**
- Create: `Game/EmergenceSequence.cs`, `Assets/Timeline/Emergence.playable`
- Modify: `Scenes/Prototype.unity`

**Interfaces:**
- Consumes: `HarinPuppet.MoveTo/SetGlitch/SetFade`, `DialogueRunner.StartSlot` 대신 `d1_after` 장면 시작용 `DialogueRunner.StartScene(string id)` 추가 (Task 4 클래스에 메서드 하나 더하고 테스트 `StartScene_ShowsFirstLine` 추가)
- Produces: `class EmergenceSequence : MonoBehaviour { event Action Finished; void Play(); }`

- [ ] **Step 1:** `DialogueRunner.StartScene` 테스트 먼저 쓰고 구현, EditMode 테스트 전부 PASS 확인
- [ ] **Step 2:** Timeline 10초 안팎: 0 → 2초 화면 노이즈(`SetGlitch` 0 → 1), 2 → 4초 폰 화면에 손 등장, 4초에 `MoveTo(RoomAnchor)`, 4 → 9초 메인 카메라가 폰에서 뒤로 빠져 원룸 전체, 9 → 10초 노이즈 0
- [ ] **Step 3:** `Finished` 에서 `d1_after` 시작. 이름을 부르다 멈추는 줄에서 `SetFade(0.4f)` 를 1초 걸었다가 1로 되돌린다
- [ ] **Step 4:** MCP 플레이 모드로 밤 끝 → 나오는 장면 → 2막 한 컷을 캡처한다. 기대: 하린이 사라지거나 바뀌는 프레임 없음
- [ ] **Step 5:** 커밋 `feat(game): emergence sequence and act 2 cut`

### Task 10: 개발용 화면

**Files:**
- Create: `Game/DevPanel.cs`

**Interfaces:**
- Consumes: `GameState`, `SlotClock.SkipWait`, `SaveStore.Save`
- Produces: 화면 오른쪽 위 작은 버튼으로 여닫는 창. 날, 슬롯, Stability, Deviation, Heart, DaysLeft 표시와 +/- 버튼, "다음 슬롯"(`SkipWait`), "저장 지우기"

- [ ] **Step 1:** 구현 (IMGUI 또는 UI Toolkit 중 짧은 쪽)
- [ ] **Step 2:** MCP 플레이 모드로 아침 선택지를 고른 뒤 Deviation이 1 오르는지, "다음 슬롯" 으로 바로 저녁이 열리는지 확인
- [ ] **Step 3:** 처음부터 끝까지 개발용 버튼으로 넘기며 설계 문서 10장 기준 1 → 3을 확인하고 캡처
- [ ] **Step 4:** 커밋 `feat(game): dev panel`

### Task 11: 안드로이드 빌드

**Files:**
- Modify: Build Profiles(Android), `.gitignore` 확인

- [ ] **Step 1:** Android 빌드 프로필로 `unity/Build/oddity-proto.apk` 를 뽑는다
- [ ] **Step 2:** 사용자가 폰에 설치해 처음부터 2막 한 컷까지 돌린다. 개발용 화면에 FPS 표시를 더해 30fps 이상인지 본다
- [ ] **Step 3:** 결과(기기 이름, 평균 fps, 문제)를 `docs/superpowers/specs/2026-10-29-prototype-design.md` 끝에 "실기기 결과" 로 적고 커밋 `docs: prototype device results`
