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
| `play` | Unity 에디터 재생으로 하린을 띄운다. 방 보기(ODDITY_ROOM=0 이면 옛 노을 배경): 앞 오른쪽 위에서 내려다보는 4x4m 방, WASD 는 화면 기준. L 주인공(카메라) 바라보기, H 식탁 의자에서 턱 괴기, Y 하품(MxYawn), K 해 드는 소파에서 자기(옆으로 누워 얼굴이 보임, Shift+K 는 웅크려 자기, 눈 감음). 왼쪽 벽 창으로 해가 들어 소파를 비춘다(천장과 앞벽은 그림자만). 원룸 가구(상자로 지음, 발자리는 걷기를 막음): 뒷벽 소파 옆에 싱크대·인덕션·세탁기 문·윗장과 구석 냉장고, 앞 오른쪽 구석에 현관 타일과 신발장(카메라 쪽이라 안 보임), 앞 왼쪽 구석에 슈퍼싱글 침대, 그 옆 앞벽에 책상, 오른쪽 벽에 낮은 서랍장과 작은 TV, 소파 앞 러그. 천장 형광등(그림자 없는 차가운 흰빛 점광 둘, 세기 0.8)은 기본으로 꺼져 있고 창 햇빛(1.6)과 주변광 0.40 만 쓴다. ODDITY_LAMP=1 이면 형광등을 켜고 주변광을 0.26 으로 낮춘다. C 는 가장 가까운 소파·의자 자리에 앉기(손 모으기, ODDITY_PLAYSIT 로 바꿈, 식탁 의자는 하린이 옆으로 걸어가는 동안 스스로 0.4m 빠져나오고, 하린이 앞에 서서 앉으면 하린을 태운 채 들어가며, 걷기나 다른 키로 일어날 때는 의자가 하린을 태우고 빠져나온 뒤 하린이 옆으로 비켜서면 스스로 들어간다. 손으로 의자를 잡지 않는다), V 는 무릎 끌어안고 웅크려 앉기, Shift+C 와 Shift+V 는 앉기 동작 바꾸기(V 쪽 Mixamo 엎드리기와 쪼그리기는 선 자리 바닥에서, Assets/Models/props 의 최신 sofa-n.glb, ODDITY_SOFA=0 이면 소파 없음), 1 → 9, 0, F 는 누르는 동안 동작하고 떼면 그 자세로 멈춤, Space 기본 자세(기본 자세는 무표정, 다른 동작은 0.3초에 걸쳐 미소), G 다음 걷기 클립(기본 KaWalk01, ODDITY_PLAYWALK 로 고름), WASD 걷기, Shift 누른 채 달리기(KaRun01), 걷기와 달리기도 키를 떼면 그 자세로 멈춤, ← → 누르는 동안 초당 100도로 돌고 떼면 멈춤, ↑ 줌 인(화각 46 → 5도, 가장 가까이에서는 얼굴), ↓ 줌 아웃(마우스 휠 위로 줌 인, 아래로 줌 아웃, 한 칸에 줌 범위의 8%), 줌 인 때 카메라는 하린이 화면 반높이의 30% 원 밖으로 나갈 때만 따라간다, N 다음 모델, Shift+N 이전 모델(처음엔 6T-N1 최신 판을 띄운다. I1 → O1 → O2 → … → N1 → I1 차례, 각 줄의 최신 판), R 현재 모델 다시 불러오기(그 줄의 최신 판을 새로 들여온다), 마우스 왼쪽 클릭: 소파를 누르면 누른 자리로 걸어가 앉고(C), 식탁이나 의자를 누르면 누른 곳에 가까운 의자를 빼서 앉으며, 바닥을 누르면 그곳으로 걸어가고, 창문을 누르면 침대와 소파 사이 창 앞으로 가서 창밖을 바라본다(걷기 키를 누르면 취소, 대본 키 clicksofa, clicktable, clickfloor, clickwindow). 창밖 풍경(하늘, 맞은편 아파트, 나무)은 창 뒤 판에 그린 그림이다. Esc 끝. `ODDITY_PLAYKEYS` 대본 시험, `ODDITY_PLAYSHOTS=1` 은 화면을 `shots/room_test/play/` 에 찍는다 |
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
