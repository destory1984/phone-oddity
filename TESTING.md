# 시험 규칙

모든 에이전트(감독, 일꾼)가 같이 따른다. 도구는 `python tools/dev.py` 하나다.

## 1. 시험 종류

| 종류 | 명령 | 걸리는 시간 | 무엇을 보나 |
|---|---|---|---|
| dotnet 규칙 시험 | `test --dotnet-only` | 약 5초 | `harness/Oddity.Core.Tests` 35개. 대본, 선택, 효과, 저장 |
| Unity EditMode | `test` | 약 1분 | `unity/Assets/Oddity/Tests/EditMode`. 대본 내용, 어셈블리 컴파일 |
| Unity PlayMode | `test` | 시험이 생기면 | 지금은 0개 |
| shot | `shot 장면` | 약 30초 | 장면을 800px 폭 png 로 찍고 baseline 과 차이를 % 로 본다 |
| check | `check` | 약 20초 | 컴파일만. 시험보다 먼저 돌린다 |

- 규칙은 먼저 dotnet 시험으로 만들고, 화면이 필요한 것만 Unity 시험으로 한다.
- 시험 이름은 `대상_기대` 꼴로 짓는다. 예: `Close_AdvancesSlotAndWaits60s`.

## 2. 결과 한 줄 형식

- 시험 한 개는 `TEST 이름 PASS` 또는 `TEST 이름 FAIL 첫 줄 메시지` 한 줄이다.
- 끝줄은 `TESTS 71 passed, 0 failed` 이고 종료 코드는 실패 수다.
- 화면 찍기는 `SHOT 장면 ok 800x800` 또는 `SHOT 장면 diff 3.2%` 다. 3% 넘게 달라지면 그림을 열어 확인한다.

## 3. 일하는 순서

- 숫자 먼저, 그림은 나중에 본다. `check` → `test` → 마지막에 `shot`.
- 버그를 고치면 같은 버그를 잡는 시험을 하나 더한다. 시험 없이 고친 것은 고친 것이 아니다.
- 화면 찍기는 한 번에 12장 이하, 폭은 800px 로 한다. 더 크게 찍지 않는다.
- 의도해서 모습이 바뀌면 `baseline 장면` 으로 기준 그림을 새로 저장한다. png 와 baseline 은 커밋하지 않는다.
- Unity 는 한 번에 하나만 돈다. `dev.py` 는 시작할 때 이 프로젝트의 에디터를 닫으니, 열어 둔 에디터의 저장 안 한 작업은 미리 저장한다.

## 4. 커밋과 push

- 커밋은 `ship` 으로만 한다. `check` 와 `test` 가 통과해야 하고, 작성자 주소가 noreply 인지, 개인 경로와 메일 주소가 없는지 본다.
- 묶음마다 `ship --quick 시험1,시험2`, push 전에만 전체 `test` 를 돌린다.
- push 는 사용자에게 먼저 묻고, 마지막 push 로부터 1시간(60분) 지난 뒤에만 한다. `git reflog show origin/<브랜치> --date=iso -1` 로 시각을 본다.
- 비밀값, 토큰, 개인 경로는 저장소에 넣지 않는다.
