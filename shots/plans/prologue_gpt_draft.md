[04:45] 전하, 프롤로그는 **5장면·32줄·선택 2회**로 구성하였사옵니다. 수수한 면 티와 긴 실내 바지 차림의 하린을 눈높이에서 찍고, 밥을 놓거나 웃음을 참는 동안 얼굴을 충분히 보여 주옵니다. 주인공은 손과 목소리만 나오옵니다.

장면별 약 40초씩, 선택과 되감기 전환을 합쳐 **3분 30초 안팎**을 목표로 하옵니다. 무언 동작은 건너뛸 수 있게 하고, 프롤로그에서는 숨김 수치와 날짜 진행을 멈추옵니다. 글리치는 쓰지 않았사옵니다.

아래 JSON의 `prologue`는 신규 대본이며, `day1_replacements`는 해당 장면을 교체할 조각이옵니다. 1일차는 각각 **4 → 3줄, 4 → 3줄, 4 → 2줄**로 줄였사옵니다. 원본 파일은 고치지 않았사옵니다.

```json
{
  "prologue": {
    "day": 12,
    "scenes": [
      {
        "id": "p_dinner",
        "slot": "night",
        "lines": [
          {"who": "harin", "text": "밥 다 됐어. 거기 책 좀 치워 봐.", "face": "relaxed", "beat": "sets_table"},
          {"who": "player", "text": "그 책 위에 놓으면 안 돼?"},
          {"who": "harin", "text": "전공 책에 김치 국물 묻혀도 돼?", "face": "neutral", "beat": "raises_brow"},
          {"who": "player", "text": "그건 좀 곤란한데."},
          {"who": "harin", "text": "그럼 치워. 네 밥그릇 들고 있잖아.", "face": "happy", "beat": "camera_closeup"},
          {"who": "player", "text": "내 건 왜 이렇게 많이 담았어?"},
          {"who": "harin", "text": "아까부터 배에서 대답하길래.", "face": "happy", "beat": "holds_back_laughter"}
        ],
        "choices": [
          {"text": "네 밥도 좀 더 담아.", "next": "p_first_bite"},
          {"text": "계란 반은 네가 먹어.", "next": "p_first_bite"}
        ]
      },
      {
        "id": "p_first_bite",
        "slot": "night",
        "lines": [
          {"who": "harin", "text": "알았어. 나도 같이 먹을 거야.", "face": "relaxed", "beat": "sits_across"},
          {"who": "harin", "text": "잠깐. 먹기 전에 하나만 물을게.", "face": "neutral", "beat": "leans_in"},
          {"who": "player", "text": "왜. 설거지까지 내가 하라고?"},
          {"who": "harin", "text": "아니. 계란 밑은 보지 말라고.", "face": "neutral", "beat": "avoids_eye_contact"},
          {"who": "player", "text": "이미 봤어. 꽤 바삭하네."},
          {"who": "harin", "text": "맞아. 바삭한 쪽이야. 탄 거 말고.", "face": "happy", "beat": "laughs"}
        ]
      },
      {
        "id": "p_washing_up",
        "slot": "night",
        "lines": [
          {"who": "player", "text": "잘 먹었어. 그릇은 내가 씻을게."},
          {"who": "harin", "text": "그 말 기다렸어. 수세기 여기.", "face": "happy", "beat": "offers_sponge"},
          {"who": "player", "text": "수세미."},
          {"who": "harin", "text": "알아. 네가 듣나 본 거야.", "face": "neutral", "beat": "keeps_straight_face"},
          {"who": "player", "text": "그럼 이건 무슨 세제인데?"},
          {"who": "harin", "text": "그건 나도 읽을 수 있거든.", "face": "happy", "beat": "laughs"},
          {"who": "harin", "text": "나는 뭐 할까? 옆에서 구경?", "face": "relaxed", "beat": "tilts_head"}
        ],
        "choices": [
          {"text": "물기 닦아 줘.", "next": "p_side_by_side"},
          {"text": "옆에서 말이나 걸어 줘.", "next": "p_side_by_side"}
        ]
      },
      {
        "id": "p_side_by_side",
        "slot": "night",
        "lines": [
          {"who": "harin", "text": "행주는 여기 둘게. 나도 여기 있고.", "face": "relaxed", "beat": "sets_towel_down"},
          {"who": "player", "text": "알았어. 물 튀니까 조금만 비켜."},
          {"who": "harin", "text": "조금만? 이만큼이면 돼?", "face": "happy", "beat": "small_step"},
          {"who": "player", "text": "응. 내일 민재랑 밥 먹고 올게."},
          {"who": "harin", "text": "응. 다 먹고 천천히 와.", "face": "relaxed", "beat": "camera_closeup"},
          {"who": "harin", "text": "내 밥은 남겨 둬. 오늘처럼 먹진 마.", "face": "happy", "beat": "playful_glance"}
        ]
      },
      {
        "id": "p_rewind",
        "slot": "night",
        "lines": [
          {"who": "player", "text": "내가 언제 네 밥까지 먹었어."},
          {"who": "harin", "text": "계란. 내 쪽에 있던 한 조각.", "face": "neutral", "beat": "points_at_plate"},
          {"who": "player", "text": "그 조그만 걸 아직 기억해?"},
          {"who": "harin", "text": "응. 이런 건 내가 더 잘 기억해.", "face": "happy", "beat": "soft_smile"},
          {"who": "harin", "text": "처음엔 네가 내 얘기를 들어 줬지", "face": "relaxed", "beat": "holds_eye_contact"},
          {"who": "system", "text": "되감는 중...", "beat": "rewind_to_day_one"}
        ]
      }
    ]
  },
  "day1_replacements": {
    "day": 1,
    "scenes": [
      {
        "id": "d1_evening_listen",
        "slot": "evening",
        "lines": [
          {"who": "player", "text": "밥 먹자니까, 친구로 지내자더라."},
          {"who": "harin", "text": "응. 서윤이는 분명히 말해 줬네.", "face": "neutral"},
          {"who": "harin", "text": "그래도 먼저 말 꺼내긴 어려웠겠다.", "face": "relaxed"}
        ],
        "choices": [
          {"text": "고마워.", "next": "d1_evening_promise"}
        ]
      },
      {
        "id": "d1_evening_quiet",
        "slot": "evening",
        "lines": [
          {"who": "harin", "text": "응. 말 안 해도 돼. 여기 있을게.", "face": "relaxed"},
          {"who": "player", "text": "...그냥, 친구로 지내자고 했어."},
          {"who": "harin", "text": "응. 그 말만으로도 아팠겠다.", "face": "sad"}
        ],
        "choices": [
          {"text": "응.", "next": "d1_evening_promise"}
        ]
      },
      {
        "id": "d1_night",
        "slot": "night",
        "lines": [
          {"who": "system", "text": "읽지 않은 메시지 12개: 과 동기 단톡방", "face": "neutral"},
          {"who": "harin", "text": "자기 전에, 대화방 좀 같이 볼래?", "face": "relaxed"}
        ],
        "choices": [
          {"text": "뭘 정리하는데.", "next": "d1_night_task"}
        ]
      }
    ]
  }
}
```

전하, 12일째 본편에서는 같은 장면의 **앞뒤와 손동작**을 더 보여 주어, 감췄던 반응 두 가지를 드러내면 좋겠사옵니다.

1. **“수세기” 뒤의 웃음은 안도였사옵니다.** 프롤로그에서는 말실수를 둘러대는 장난으로 보이옵니다. 본편에서는 그 직전, 익숙한 물건 이름이 떠오르지 않아 행주를 움켜쥐던 손을 보여 주옵니다. 주인공이 “수세미”라고 자연스럽게 이어 주자 손이 풀리고 웃음이 나옵니다. 주인공도 웃어 넘긴 뒤 필요한 물건을 하나씩 짚어 주면, 돌봄의 방향이 바뀌옵니다.

2. **“천천히 와”에는 서운함을 삼킨 순간이 있사옵니다.** 프롤로그에서는 다정한 배웅으로 들리옵니다. 본편에서는 민재 이야기에 하린의 시선이 빈 맞은편 자리로 잠깐 내려갔다가 돌아오게 하옵니다. 이어 “나랑도 내일 저녁은 먹자”라고 제 바람을 말하게 하옵니다. 친구와의 약속을 지키면서도 함께할 시간을 청하는 반응이 되옵니다.