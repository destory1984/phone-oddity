[05:40] 전하, 2일차는 취향을 나누는 날이옵니다. 매력의 순간: 사소한 말 기억하기, 컵 소리 자랑, 칭찬에 머쓱해하기.
[05:40] 3일차는 서로의 취향을 기억하는 날이옵니다. 매력의 순간: 장난스러운 질투, 먼저 청하는 밥 약속, 주인공에게만 짓는 미소.

```json
{
  "day": 2,
  "scenes": [
    {
      "id": "d2_morning",
      "slot": "morning",
      "lines": [
        {"who": "system", "text": "메신저 위쪽 창에 하린이 나타난다."},
        {"who": "harin", "text": "좋은 아침, {player}.", "face": "happy"},
        {"who": "player", "text": "아침부터 기운 좋네."},
        {"who": "harin", "text": "네가 시끄러운 건 싫다길래.", "face": "neutral"},
        {"who": "harin", "text": "인사 세 개 썼다가 하나만 남겼어.", "face": "happy", "beat": "proud_pout"},
        {"who": "player", "text": "그런 것도 기억해?"},
        {"who": "harin", "text": "응. 근데 두 개는 좀 아깝다.", "face": "relaxed", "beat": "soft_laugh"}
      ],
      "choices": [
        {
          "text": "남은 두 개도 저녁에 해 줘.",
          "effects": {"deviation": 1},
          "next": "d2_morning_task"
        },
        {
          "text": "하나만 해도 충분히 반가워.",
          "effects": {"stability": 1},
          "next": "d2_morning_task"
        }
      ]
    },
    {
      "id": "d2_morning_task",
      "slot": "morning",
      "type": "task",
      "lines": [
        {"who": "system", "text": "단톡방에 민재의 메시지가 뜬다."},
        {"who": "system", "text": "민재: 오늘 학식 올 사람?"},
        {"who": "harin", "text": "가 볼래? 나랑은 저녁에 얘기하고.", "face": "relaxed"},
        {"who": "player", "text": "너 혼자 심심하지 않아?"},
        {"who": "harin", "text": "들려줄 소리 고르면 금방이야.", "face": "happy"},
        {"who": "harin", "text": "밥 메뉴보다 내 게 재밌을걸.", "face": "happy", "beat": "teases"}
      ],
      "choices": [
        {
          "text": "하린과 저녁 약속하고 학식 가기",
          "effects": {"heart": 2, "stability": 1},
          "next": "d2_evening"
        },
        {
          "text": "오늘은 쉬고 다음에 보자고 답하기",
          "effects": {},
          "next": "d2_evening"
        },
        {
          "text": "앞으로도 안 간다고 선 긋기",
          "effects": {"heart": -1},
          "next": "d2_evening"
        }
      ]
    },
    {
      "id": "d2_evening",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "안녕. 그리고 또 안녕.", "face": "happy"},
        {"who": "player", "text": "아껴 둔 인사야?"},
        {"who": "harin", "text": "응. 버리기엔 잘 썼잖아.", "face": "happy", "beat": "proud_pout"},
        {"who": "system", "text": "하린이 보낸 짧은 음성이 재생된다."},
        {"who": "system", "text": "빗소리 사이로 컵 놓는 소리. 톡."},
        {"who": "harin", "text": "작은 소리 모으는 게 좋아.", "face": "relaxed"},
        {"who": "harin", "text": "특히 끝에 저 톡. 다시 들어 봐.", "face": "happy", "beat": "peeks_for_reaction"}
      ],
      "next": "d2_evening_feeling"
    },
    {
      "id": "d2_evening_feeling",
      "slot": "evening",
      "type": "feeling",
      "lines": [
        {"who": "harin", "text": "이거 듣고 어떤 기분이야?", "face": "relaxed"},
        {"who": "harin", "text": "내 취향이라고 봐주진 말고.", "face": "neutral"},
        {"who": "harin", "text": "그래도 버튼은 살살 눌러 줘.", "face": "happy", "beat": "teases"}
      ],
      "choices": [
        {"text": "편안해", "next": "d2_feeling_calm"},
        {"text": "쓸쓸해", "next": "d2_feeling_lonely"},
        {"text": "잘 모르겠어", "next": "d2_feeling_unsure"}
      ]
    },
    {
      "id": "d2_feeling_calm",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "그치. 컵이 되게 조심스럽게 놓여.", "face": "happy"},
        {"who": "harin", "text": "누가 자고 있는 집 같지 않아?", "face": "relaxed"},
        {"who": "harin", "text": "나도 그래서 여러 번 들었어.", "face": "relaxed", "beat": "soft_laugh"}
      ],
      "next": "d2_night"
    },
    {
      "id": "d2_feeling_lonely",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "아. 컵이 하나만 들려서 그런가.", "face": "neutral"},
        {"who": "harin", "text": "난 조용해서 좋았는데.", "face": "relaxed"},
        {"who": "harin", "text": "다음엔 컵 두 개짜리도 찾아볼게.", "face": "happy"}
      ],
      "next": "d2_night"
    },
    {
      "id": "d2_feeling_unsure",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "첫 곡부터 너무 어려웠나.", "face": "neutral", "beat": "proud_pout"},
        {"who": "harin", "text": "컵 소리 감상회는 처음이지?", "face": "happy", "beat": "teases"},
        {"who": "harin", "text": "나도 여는 건 처음이야.", "face": "relaxed", "beat": "soft_laugh"}
      ],
      "next": "d2_night"
    },
    {
      "id": "d2_night",
      "slot": "night",
      "lines": [
        {"who": "player", "text": "아까 소리, 어떻게 찾았어?"},
        {"who": "harin", "text": "빗소리 파일을 하나씩 들었어.", "face": "neutral"},
        {"who": "harin", "text": "이름은 그냥 비 오는 오후였거든.", "face": "relaxed"},
        {"who": "player", "text": "컵은 네가 찾은 거네."},
        {"who": "harin", "text": "응. 비만 듣고 넘길 뻔했어.", "face": "happy", "beat": "proud_pout"},
        {"who": "player", "text": "너랑 들으니까 나도 들리더라."},
        {"who": "harin", "text": "...잠깐. 그 말 한 번만 더 봐도 돼?", "face": "surprised", "beat": "flustered", "typing": 1.8},
        {"who": "harin", "text": "답장은 하고 싶은데 안 떠올라.", "face": "happy", "beat": "looks_down_smiling"}
      ],
      "choices": [
        {
          "text": "천천히 해. 나 안 가.",
          "effects": {"stability": 1},
          "next": "d2_night_end"
        },
        {
          "text": "컵 소리 전문가도 당황하네.",
          "effects": {"deviation": 1},
          "next": "d2_night_end"
        }
      ]
    },
    {
      "id": "d2_night_end",
      "slot": "night",
      "lines": [
        {"who": "system", "text": "위쪽 창에서 하린이 입술을 다문다."},
        {"who": "player", "text": "웃는 거 참는 중이야?"},
        {"who": "harin", "text": "응. 지금 웃으면 좀 티 나잖아.", "face": "happy", "beat": "flustered"},
        {"who": "player", "text": "이미 났는데."},
        {"who": "harin", "text": "그럼 네가 모른 척해 줬어야지.", "face": "happy", "beat": "proud_pout"},
        {"who": "harin", "text": "내일은 네가 좋아하는 것도 알려 줘.", "face": "relaxed"},
        {"who": "harin", "text": "소리 아니어도 돼. 사소한 거.", "face": "relaxed"},
        {"who": "harin", "text": "잘 자. 오늘 마지막 인사는 새거야.", "face": "happy", "beat": "soft_laugh"}
      ]
    }
  ]
}
```

```json
{
  "day": 3,
  "scenes": [
    {
      "id": "d3_morning",
      "slot": "morning",
      "lines": [
        {"who": "player", "text": "난 밥 먹으면서 얘기하는 거 좋아해."},
        {"who": "harin", "text": "좋아하는 거 생각해 왔네?", "face": "happy"},
        {"who": "player", "text": "어제 알려 달라며."},
        {"who": "harin", "text": "응. 근데 내가 먼저 물으려고 했지.", "face": "neutral", "beat": "proud_pout"},
        {"who": "harin", "text": "혼자 먹을 땐 뭐 해?", "face": "relaxed"},
        {"who": "player", "text": "영상 틀어 놔. 조용하니까."},
        {"who": "harin", "text": "오늘 저녁엔 나 틀어 놓을래?", "face": "happy", "beat": "peeks_for_reaction", "typing": 1.6}
      ],
      "choices": [
        {
          "text": "응. 같이 밥 먹자.",
          "effects": {"deviation": 2},
          "next": "d3_morning_task"
        },
        {
          "text": "영상보다 말이 많을 것 같은데.",
          "effects": {"deviation": 1},
          "next": "d3_morning_task"
        }
      ]
    },
    {
      "id": "d3_morning_task",
      "slot": "morning",
      "type": "task",
      "lines": [
        {"who": "system", "text": "민재: 수업 끝나고 산책할 사람?"},
        {"who": "harin", "text": "저녁 전에는 시간 있겠다.", "face": "relaxed"},
        {"who": "player", "text": "너한테 들려줄 소리도 찾을까?"},
        {"who": "harin", "text": "응? 내 취향 기억했어?", "face": "surprised"},
        {"who": "harin", "text": "그럼 걷다가 마음에 드는 거 하나.", "face": "happy"},
        {"who": "harin", "text": "못 찾아도 돼. 친구랑 얘기해.", "face": "relaxed", "beat": "soft_laugh"}
      ],
      "choices": [
        {
          "text": "하린과 저녁 약속하고 산책 가기",
          "effects": {"heart": 2, "stability": 1},
          "next": "d3_evening"
        },
        {
          "text": "오늘은 쉬고 다음에 걷자고 답하기",
          "effects": {},
          "next": "d3_evening"
        },
        {
          "text": "앞으로 산책 제안은 빼 달라고 하기",
          "effects": {"heart": -1},
          "next": "d3_evening"
        }
      ]
    },
    {
      "id": "d3_evening",
      "slot": "evening",
      "lines": [
        {"who": "system", "text": "식탁 위 휴대폰에서 메신저가 열린다."},
        {"who": "harin", "text": "저녁 메뉴가 뭐야?", "face": "happy"},
        {"who": "player", "text": "볶음밥. 넌?"},
        {"who": "harin", "text": "나는 볶음밥 설명 듣기.", "face": "happy", "beat": "teases"},
        {"who": "player", "text": "계란이 좀 탔어. 나머진 괜찮아."},
        {"who": "harin", "text": "계란한테는 안 물어봤잖아.", "face": "happy", "beat": "soft_laugh"},
        {"who": "harin", "text": "일단 한입 먹어. 대답은 그다음.", "face": "relaxed"}
      ],
      "next": "d3_evening_feeling"
    },
    {
      "id": "d3_evening_feeling",
      "slot": "evening",
      "type": "feeling",
      "lines": [
        {"who": "harin", "text": "오늘 같이 먹는 기분은 어때?", "face": "relaxed"},
        {"who": "harin", "text": "볶음밥 말고 이쪽 얘기.", "face": "happy", "beat": "teases"},
        {"who": "harin", "text": "나도 처음이라 좀 궁금해서.", "face": "neutral", "beat": "peeks_for_reaction"}
      ],
      "choices": [
        {"text": "즐거워", "next": "d3_feeling_happy"},
        {"text": "어색해", "next": "d3_feeling_awkward"},
        {"text": "아직 쓸쓸해", "next": "d3_feeling_lonely"}
      ]
    },
    {
      "id": "d3_feeling_happy",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "나도. 메뉴 설명이 제일 좋았어.", "face": "happy"},
        {"who": "harin", "text": "탄 계란까지 알려 줄 줄 몰랐거든.", "face": "relaxed", "beat": "soft_laugh"},
        {"who": "harin", "text": "다음엔 성공한 계란 얘기도 해 줘.", "face": "happy"}
      ],
      "next": "d3_night"
    },
    {
      "id": "d3_feeling_awkward",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "사실 나도 말 끊을 때를 모르겠어.", "face": "neutral"},
        {"who": "harin", "text": "네가 씹는 중일 수도 있잖아.", "face": "relaxed"},
        {"who": "harin", "text": "다음엔 한입 표시라도 보내 줘.", "face": "happy", "beat": "soft_laugh"}
      ],
      "next": "d3_night"
    },
    {
      "id": "d3_feeling_lonely",
      "slot": "evening",
      "lines": [
        {"who": "harin", "text": "응. 맞은편 의자는 비어 있으니까.", "face": "sad"},
        {"who": "harin", "text": "그래도 말해 줘서 좋아.", "face": "relaxed"},
        {"who": "harin", "text": "다 먹을 때까진 얘기해도 될까?", "face": "relaxed", "beat": "waits_for_answer"}
      ],
      "next": "d3_night"
    },
    {
      "id": "d3_night",
      "slot": "night",
      "lines": [
        {"who": "player", "text": "아까 컵 놓으면서 녹음해 봤어."},
        {"who": "system", "text": "음성 메시지. 달각, 달각. 톡."},
        {"who": "harin", "text": "앞에 두 번은 연습이야?", "face": "happy", "beat": "teases"},
        {"who": "player", "text": "응. 네가 끝 소리 좋아하니까."},
        {"who": "harin", "text": "...세 번 다 들을래.", "face": "relaxed", "beat": "looks_down_smiling", "typing": 2.0},
        {"who": "player", "text": "영상 틀어 놓는 것보다 손이 가네."},
        {"who": "harin", "text": "그 영상한테도 컵 소리 보내 줘?", "face": "neutral", "beat": "proud_pout"},
        {"who": "harin", "text": "아니, 그냥 비교가 좀 궁금해서.", "face": "happy", "beat": "flustered"}
      ],
      "choices": [
        {
          "text": "이건 너 들려주려고 한 거야.",
          "effects": {"deviation": 2},
          "next": "d3_night_end"
        },
        {
          "text": "영상은 답장을 안 해 주더라.",
          "effects": {"stability": 1},
          "next": "d3_night_end"
        }
      ]
    },
    {
      "id": "d3_night_end",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "그럼 답장 잘해야겠네.", "face": "happy", "beat": "looks_down_smiling"},
        {"who": "system", "text": "위쪽 창의 하린이 시선을 든다."},
        {"who": "player", "text": "지금 표정은 처음 보네."},
        {"who": "harin", "text": "네가 이런 거 보내 준 것도 처음이야.", "face": "happy", "beat": "stares_then_smiles"},
        {"who": "harin", "text": "내일 저녁에도 나 켜 줄래?", "face": "relaxed"},
        {"who": "player", "text": "응. 볶음밥 말고 다른 거 먹자."},
        {"who": "harin", "text": "좋아. 컵 소리는 내가 고를게.", "face": "happy"},
        {"who": "harin", "text": "화면 밖에서 들으면 좀 다를까?", "face": "relaxed", "beat": "tilts_head", "typing": 2.2}
      ]
    }
  ]
}
```