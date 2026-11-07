# Oddity Phone 작업 안내

> 시험 방법은 `TESTING.md` 를 따른다. 시험 도구는 `tools/dev.py` 하나다.

## 구성

| 폴더 | 책임 |
|---|---|
| `packages/com.oddity.core/` | 게임 규칙 (Unity에 기대지 않는 C#). 대본 읽기, 선택, 효과, 저장 |
| `harness/Oddity.Core` | 위 규칙을 dotnet으로 짓는 프로젝트 |
| `harness/Oddity.Core.Tests/` | 규칙 시험 70개. Unity 없이 몇 초에 돈다 |
| `unity/` | Unity 6000.3.27f1 프로젝트 (URP, 세로 1080x1920) |
| `unity/Assets/Oddity/Game/` | 화면 쪽 코드. `HarinPuppet` (표정, 눈 깜빡임, 깨짐, 투명도), `SceneConti` (3일차 밤 콘티와 강도 끔·약·중·강·근), `NightLook` (콘티 장면에서만 켜는 밤 조명) |
| `unity/Assets/Oddity/Editor/` | 에디터 도구. `Shots.cs` (화면 찍기), `ShotScenes.cs` 와 `ShotScenes.txt` (장면 표), `MotionImport.cs` (Mixamo Humanoid 임포트), `MotionSheet.cs` (동작을 넓은 화면으로 찍는 시트) |
| `unity/Assets/Oddity/DevMotion/` | Mixamo FBX (git 무시, 재배포 금지). `HarinMotion.cs` 가 상반신에만 입힌다 |
| `unity/Assets/Oddity/Tests/EditMode/` | Unity 안에서 도는 시험 (대본 내용 등) |
| `unity/Assets/Scenes/Prototype.unity` | 시제품 장면. `AppStageCamera` 가 하린을 1024x1024 로 그린다 |
| `hari/`, `harin/` | 하린 그림과 3D 원본 재료 (게임 코드 아님) |
| `shots/` | 화면 찍기 결과. png 와 baseline 은 커밋하지 않고, `shots/plans/` 는 커밋한다 |

## 명령 (`python tools/dev.py ...`)

| 명령 | 하는 일 |
|---|---|
| `check` | dotnet build, Unity batchmode 컴파일. `error CS` 가 있으면 실패 (약 20초) |
| `test [이름,이름]` | dotnet 시험, Unity EditMode, PlayMode. 한 줄씩 `TEST 이름 PASS/FAIL`, 끝에 `TESTS n passed, m failed` |
| `test --dotnet-only` | Unity 없이 규칙 시험만 |
| `shot [장면 ...]` | `shots/장면.png` (폭 800). baseline 이 있으면 `SHOT 장면 diff n.n%` |
| `scenes`, `baseline`, `kill` | 장면 목록, 기준 그림 저장, 이 프로젝트의 Unity 닫기 |
| `previs 장면 [--level 1,2,3,4] [--day]` | 장면을 사람처럼 자동으로 넘겨 음성과 효과음이 든 540x960 24fps mp4 로 뽑는다 → `shots/previs/장면_L2.mp4` (`--day` 는 밤 조명을 끄고 `장면_L2_day.mp4`). 지금은 `d3_night` 만 된다. 실시간으로 찍어서 한 벌에 약 50초, 셋이면 약 3분 걸린다. ffmpeg 가 PATH 에 있어야 한다 |
| `play` | Unity 에디터 재생으로 하린을 띄운다. 방 보기(ODDITY_ROOM=0 이면 옛 노을 배경): 앞 오른쪽 위에서 내려다보는 4x4m 방, WASD 는 화면 기준. L 주인공(카메라) 바라보기, H 식탁 의자에서 턱 괴기, Y 하품(MxYawn), K 해 드는 소파에서 자기(옆으로 누워 얼굴이 보임, Shift+K 는 웅크려 자기, 눈 감음). 왼쪽 벽 창으로 해가 들어 소파를 비춘다(천장과 앞벽은 그림자만). 거실 겸 부엌 가구(상자로 지음, 침대는 왼쪽 벽 앞쪽의 침실 문 너머에 있다고 친다, 발자리는 걷기를 막음): 뒷벽 소파 옆에 싱크대·인덕션·세탁기 문·윗장과 구석 냉장고, 앞 오른쪽 구석에 현관 타일과 신발장(카메라 쪽이라 안 보임), 왼쪽 벽 앞쪽에 닫힌 침실 문, 앞벽에 소파와 마주 보는 낮은 TV장과 TV(소파와 TV는 창과 직각인 두 벽에 마주 보게 두어 화면에 햇빛이 들지 않는다), 식탁은 현관과 거실 사이 소파 앞을 비운 자리(0.70, -0.45)에 두고 그 위에 노트북, 소파와 싱크대 사이 스탠드, 창과 침실 문 사이 구석 화분, 소파 앞 러그. 천장 형광등(그림자 없는 차가운 흰빛 점광 둘, 세기 0.8)은 기본으로 꺼져 있고 창 햇빛(1.6)과 주변광 0.40 만 쓴다. ODDITY_LAMP=1 이면 형광등을 켜고 주변광을 0.26 으로 낮춘다. C 는 가장 가까운 소파·의자 자리에 앉기(손 모으기, ODDITY_PLAYSIT 로 바꿈, 식탁 의자는 하린이 옆으로 걸어가는 동안 스스로 0.4m 빠져나오고, 하린이 앞에 서서 앉으면 하린을 태운 채 들어가며, 걷기나 다른 키로 일어날 때는 의자가 하린을 태우고 빠져나온 뒤 하린이 옆으로 비켜서면 스스로 들어간다. 손으로 의자를 잡지 않는다), V 는 무릎 끌어안고 웅크려 앉기, Shift+C 와 Shift+V 는 앉기 동작 바꾸기(V 쪽 Mixamo 엎드리기와 쪼그리기는 선 자리 바닥에서, Assets/Models/props 의 최신 sofa-n.glb, ODDITY_SOFA=0 이면 소파 없음), 1 → 9, 0, F 는 누르는 동안 동작하고 떼면 그 자세로 멈춤, Space 기본 자세(기본 자세는 무표정, 다른 동작은 0.3초에 걸쳐 미소), G 다음 걷기 클립(기본 KaWalk01, ODDITY_PLAYWALK 로 고름), WASD 걷기, Shift 누른 채 달리기(KaRun01), 걷기와 달리기도 키를 떼면 그 자세로 멈춤, ← → 누르는 동안 초당 100도로 돌고 떼면 멈춤, ↑ 줌 인(화각 46 → 5도, 가장 가까이에서는 얼굴), ↓ 줌 아웃(마우스 휠 위로 줌 인, 아래로 줌 아웃, 한 칸에 줌 범위의 8%), 줌 인 때 카메라는 하린이 화면 반높이의 30% 원 밖으로 나갈 때만 따라간다, N 다음 모델, Shift+N 이전 모델(처음엔 6T-N1 최신 판을 띄운다. I1 → O1 → O2 → … → N1 → I1 차례, 각 줄의 최신 판), R 현재 모델 다시 불러오기(그 줄의 최신 판을 새로 들여온다), 마우스 왼쪽 클릭: 소파를 누르면 누른 자리로 걸어가 앉고(C), 식탁이나 의자를 누르면 누른 곳에 가까운 의자를 빼서 앉으며, 바닥을 누르면 그곳으로 걸어가고, 창문을 누르면 침실 문과 소파 사이 창 앞으로 가서 창밖을 바라본다(걷기 키를 누르면 취소, 대본 키 clicksofa, clicktable, clickfloor, clickwindow). 창밖 풍경(하늘, 맞은편 아파트, 나무)은 창 뒤 판에 그린 그림이다. 고양이(Asset Store Cartoon Cat, `DevMotion/Cat/` 에 있을 때만, 소파 앞 러그 위): M 은 고양이 동작을 Idle → Walk → Jump → Sound → Eat 로 바꾸고, Tab 은 WASD(와 Shift)를 하린과 고양이 중 누가 받을지 바꾸며(고양이는 0.5 m/s, Shift 1.2 m/s, 카메라가 따라감), `ODDITY_CAT=0` 이면 고양이 없음. 화면 오른쪽 위 큰 글씨 HARIN/CAT 이 지금 조종하는 쪽을 알린다(Tab 때 1.3배에서 줄며 노랗게 번쩍). 고양이를 조종하면 왼쪽 키 목록도 고양이 것으로 바뀐다: 1 → 5 Idle/Walk/Jump/Sound/Eat(떼도 계속), Space 멈춤(Idle), WASD·Shift 걷기, ← → 제자리 돌기(100도/초), ↑ ↓·휠 줌, C·V·K 가장 가까운 소파 자리로 걸어가 뛰어오르기(0.5초 호), 그 밖의 키는 `고양이에게 없는 동작` 알림만 띄우고 하린은 움직이지 않는다. 고양이 마우스 클릭(하린은 무시): 바닥은 그곳으로 걷고, 소파는 앞에서 방석 위로 뛰어오르며(소파 위에서 걷거나 클릭하면 먼저 0.4초 호로 뛰어내림), 식탁·의자는 발자리 밖 가까운 바닥에서 가구를 보고 서며, 창은 창 아래에서 창을 본다(`clickfloor` 등 대본 키는 조종하는 쪽에 듣는다). 로그 `ROOMPLAY_CAT goto=… at=(x,z)`, `onsofa=1|0`. Esc 끝. 고양이 소리(git 제외 `DevMotion/Cat/Sfx`, 고양이 위치에서 나는 3D 소리): Sound 클립은 시작과 반복마다 무작위 울음, Eat 은 먹이 울음, Jump 는 반복마다 울음, 가만히 3초 뒤 조용한 가르랑 루프와 12 → 25초마다 작은 울음, 걷다 멈추면 귀 터는 소리, Tab 으로 고양이를 잡을 때 짧은 울음, `ROOMPLAY_CATSFX` 로그. 고양이는 둘이다(만화 고양이 CAT, 소말리 SOMALI, 둘 다 git 무시 자산이라 없으면 경고만 하고 빠진다). Tab 은 하린 → CAT → SOMALI → 하린 순으로 조종을 넘기고 오른쪽 위 이름표가 바뀐다. 소말리 키: 1 Idle, 2 걷기, 3 앉기, 4 일어서기, 5 눕기(스핑크스), 6 식빵 굽기(`somali_cat_lie.glb` 가 있을 때만, 없으면 알림), Space 서기 후 Idle(누웠으면 GetUp·LoafUp 먼저), C·V 소파에 올라 앉기, K 소파에 올라 눕기, 걸으면 먼저 일어선다, M 은 … → 서기 → 눕기 → 식빵. 스스로 쉴 때 앉기 40·눕기 35·식빵 25%(창턱은 눕기 50·식빵 30·앉기 20), 누우면 가르랑. 한국 고양이 소리(`DevMotion/Cat/Sfx/gongu/` 긴 녹음을 `python tools/cut_cat_sfx.py 입력폴더 출력폴더` 로 `gongu/cut/` 에 자른 조각): 울음은 무작위 울음 목록에 합류, 가르랑 루프는 시작할 때 무작위로 고르고, 만화 고양이 Eat 는 0.9초 뒤 사료 씹는 소리, 둘이 0.4 m 안에서 마주치면 늦게 온 쪽이 하악질(30초에 한 번, 로그 `SFX hiss`)하고 다른 곳으로 걸어간다. `ODDITY_CAT=0` 둘 다 뺌, `ODDITY_SOMALI=0` 소말리만 뺌, 크기 `ODDITY_CATSIZE`(만화 고양이 머리 높이 m)·`ODDITY_SOMALILENGTH`(소말리 몸길이 m), 로그 `ROOMPLAY_SOMALI`. 고양이 둘은 조종하지 않을 때 스스로 산다(배회, 소파, 창가, 하린 곁, 제자리 동작, 창턱·주방 조리대·식탁·냉장고 위 올라앉기, 0.35 m/s. Tab 으로 조종하면 바로 멈추고 놓으면 3초 뒤 다시 움직임. `ODDITY_CATAI=0` 이면 끔, `ODDITY_CATSEED=n` 으로 같은 결과 반복. 조종 중인 고양이는 조리대·식탁·냉장고(조리대를 거쳐 두 번 뛰기)·창턱을 클릭하면 올라가고, 대본 키 clickcounter, clickfridge, clicksill, clicktable. 앉은 고양이는 가장 낮은 정점을 바닥면에 매 프레임 맞춤). `ODDITY_PLAYKEYS` 대본 시험, `ODDITY_PLAYSHOTS=1` 은 화면을 `shots/room_test/play/` 에 찍는다 |
| `roomtest` | 그림 방 배경에 멀리 선 3D 하린(C48)을 얹은 약 23초 느낌 시험(작업 54 → 56, 버릴 시험, Mixamo FemaleIdle 위에 절차 동작, 끝에 WalkStandard 걷기(뼈대 포함 파일의 Avatar 공유), `ODDITY_WALKONLY=1` 이면 걷기만). 1920x1080 30fps → `shots/room_test/room_test.mp4` 와 `sheet.png`(3x2). 그림 3장은 `shots/room_test/` 에 둔다. 약 2분, ffmpeg 필요 |
| `ship -m "메시지" [--quick a,b]` | check, test 통과 뒤 커밋. push 는 절대 안 한다 |
| `build [--pc] [--demo-voice]` | 안드로이드 개발 APK → `unity/Build/oddity-proto.apk` (IL2CPP 라 길다), `--pc` 이면 Windows 개발 exe → `unity/Build/pc/oddity-proto.exe` (Mono, 540x960 창, 약 5분). 크기와 시간을 찍고 활성 대상을 Windows 로 되돌린다. karina 음성 파일이 있으면 멈추고, `--demo-voice` 일 때만 개인용으로 짓는다 (`oddity-proto-DEMO-private.apk`, `pc-DEMO-private/`) |

Unity 경로는 환경변수 `UNITY_EXE`, dotnet 은 `DOTNET_EXE` 로 바꾼다.
Unity MCP(unity-mcp)는 감독이 열린 에디터를 잠깐 살필 때만 쓴다. MCP로 TestRunnerApi 를 부르지 않는다(2026-10-30 에디터가 꺼졌다).

### 제로투 춤 시험

- 뷰어에서 Z를 누르면 양팔을 머리 뒤로 올리고 골반을 좌우로 흔드는 제로투풍 춤을 춘다. 음악의 첫 7초는 느리게 흔들고, 7초부터 두 배 속도(한 마디 4박에 좌우 왕복 2회)로 흔든다. 음악 속도는 바꾸지 않는다. Z를 떼면 동작과 음악이 함께 멈추고, 다시 누르면 그 자리부터 이어진다. Space는 기본 자세로 돌아간다.
- 음악은 사용자가 지정한 영상에서 받은 75초 음원이다. `Audio/Resources/Sfx/ZeroTwoTrack.mp3`에 두며 Git에서 제외한다. 음원에서 측정한 128 BPM에 맞춰 춤은 8박(3.75초)마다 반복하고 음악은 곡 전체를 이어서 재생한다. 음원이 없으면 창작 비트를 쓴다.
- `python tools/dev.py test ZeroTwoDance`로 동작과 반복 경계를 검사한다.

## 일하는 짜임: 감독과 일꾼 (2026-10-30)

1. 메인 세션(Opus)은 감독만 한다. 코드는 Sonnet 일꾼(백그라운드)이 짓는다.
2. 일꾼에게는 scratchpad 지시서 파일 하나를 준다: 할 일, 수치, 규칙, 쓸 시험 이름, 40줄 이하 보고.
3. 일꾼은 커밋, `ship`, push 를 하지 않는다. 감독이 diff를 읽고, 필요하면 `shot` 으로 보고, `ship` 한다.
4. Unity는 한 번에 하나만 연다. `dev.py` 는 시작할 때 이 프로젝트의 에디터를 닫는다. 일꾼이 시험 중이면 감독은 에디터나 MCP를 쓰지 않는다.
5. `ship` 은 폴더 전체를 커밋한다. 다음 일꾼은 `ship` 이 끝난 뒤에 보낸다.
6. 묶음마다 `ship -m "..." --quick 시험1,시험2` 로 점검과 그 묶음의 시험만 돌린다.
7. push 전에만 전체 `test` 를 돌린다. push 는 1시간 간격으로 한다.
8. 이야기, 기획, 2D 그림은 GPT(`codex exec --sandbox read-only -C .`, 그림은 workspace-write 로 image_gen)와 먼저 의논하고 계획을 `shots/plans/` 에 둔다.
9. 큰 일은 열 개 남짓 묶음으로 나눠 차례로 짓는다.

## 규칙

- 비밀값과 개인 경로를 저장소에 넣지 않는다. 커밋 작성자는 `13573570+destory1984@users.noreply.github.com` 만 쓴다.
- 새 장면을 찍으려면 `ShotScenes.cs` 에 한 줄 더하고 `ShotScenes.txt` 에 이름을 적는다.
