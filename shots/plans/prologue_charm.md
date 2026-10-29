[04:48] 전하, 초안은 함께 사는 편안함은 있으나, 하린을 더 보고 싶게 하는 순간은 부족하옵니다.
집안일이 대화를 끌고 가므로, 클로즈업과 음성이 하린만의 매력을 드러낼 기회가 적사옵니다.
놀림 뒤의 머쓱함과 기다리는 눈빛을 늘리되, 수세기 말실수는 가볍게 덮어 복선을 살리겠사옵니다.

```json
{
  "day": 12,
  "scenes": [
    {
      "id": "p_dinner",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "책 좀 치워. 네 밥 들고 있잖아.", "face": "relaxed", "beat": "sets_table"},
        {"who": "player", "text": "내 밥은 왜 이렇게 많아?"},
        {"who": "harin", "text": "네 배가 나한테 계속 부탁하던데.", "face": "happy", "beat": "teases"},
        {"who": "player", "text": "그걸 다 듣고 있었어?"},
        {"who": "harin", "text": "응. 너보다 말이 많더라.", "face": "happy", "beat": "soft_laugh"},
        {"who": "harin", "text": "잠깐. 계란 밑은 보지 마.", "face": "neutral", "beat": "covers_plate"},
        {"who": "harin", "text": "왜 웃어. 아직 먹지도 않았잖아.", "face": "neutral", "beat": "proud_pout"}
      ],
      "choices": [
        {"text": "나 먹이려고 애쓴 게 좋아서.", "next": "p_praised"},
        {"text": "계란보다 네 표정이 더 볼만해서.", "next": "p_teased"}
      ]
    },
    {
      "id": "p_praised",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "그런 말을 그렇게 바로 해?", "face": "neutral", "beat": "flustered"},
        {"who": "player", "text": "그럼 언제 해?"},
        {"who": "harin", "text": "몰라. 일단 나 말고 밥 좀 봐.", "face": "happy", "beat": "looks_down_smiling"},
        {"who": "harin", "text": "맛있으면 그것도 바로 말해 줘.", "face": "happy", "beat": "peeks_for_reaction"}
      ],
      "next": "p_washing_up"
    },
    {
      "id": "p_teased",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "그래? 그럼 어디 한번 잘 봐.", "face": "neutral", "beat": "leans_close_then_back"},
        {"who": "player", "text": "왜 네가 먼저 눈을 피해?"},
        {"who": "harin", "text": "네가 진짜 볼 줄은 몰랐지.", "face": "happy", "beat": "flustered"},
        {"who": "harin", "text": "계란 식겠다. 나중에 마저 놀려.", "face": "happy", "beat": "soft_laugh"}
      ],
      "next": "p_washing_up"
    },
    {
      "id": "p_washing_up",
      "slot": "night",
      "lines": [
        {"who": "player", "text": "잘 먹었어. 그릇은 내가 씻을게.", "beat": "dinner_finished"},
        {"who": "harin", "text": "그 말 기다렸어. 수세기 여기.", "face": "happy", "beat": "offers_sponge"},
        {"who": "player", "text": "수세미."},
        {"who": "harin", "text": "아. 응. 수세미.", "face": "neutral", "beat": "brief_pause"},
        {"who": "harin", "text": "너 이런 건 꼭 잘 듣더라.", "face": "happy", "beat": "teases"},
        {"who": "player", "text": "다른 말도 듣고 있어."},
        {"who": "harin", "text": "그럼 내가 뭐 해 주면 좋겠어?", "face": "relaxed", "beat": "tilts_head"},
        {"who": "harin", "text": "구경은 잘해. 방금도 해 봤고.", "face": "happy", "beat": "stares_then_smiles"}
      ],
      "choices": [
        {"text": "물기 닦으면서 말 걸어 줘.", "next": "p_side_by_side"},
        {"text": "말 걸면서 물기 닦아 줘.", "next": "p_side_by_side"}
      ]
    },
    {
      "id": "p_side_by_side",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "어느 쪽이든 둘 다 하라는 거네.", "face": "happy", "beat": "mock_indignation"},
        {"who": "harin", "text": "그 컵 줘. 손잡이 쪽으로.", "face": "relaxed", "beat": "fingertips_brush"},
        {"who": "player", "text": "왜 그렇게 빤히 봐?"},
        {"who": "harin", "text": "아까 웃던 얼굴이랑 좀 달라서.", "face": "relaxed", "beat": "stares_then_smiles"},
        {"who": "player", "text": "설거지가 그렇게 즐겁진 않지."},
        {"who": "harin", "text": "난 좀 재밌는데. 너 옆에 있으니까.", "face": "happy", "beat": "quiet_confession"},
        {"who": "harin", "text": "아, 설거지 말고 네 표정이.", "face": "happy", "beat": "flustered"},
        {"who": "harin", "text": "내일도 같이 먹을래?", "face": "relaxed", "beat": "waits_for_answer"}
      ],
      "choices": [
        {"text": "응. 내일은 내가 해 줄게.", "next": "p_rewind"},
        {"text": "응. 계란 밑은 안 볼게.", "next": "p_rewind"}
      ]
    },
    {
      "id": "p_rewind",
      "slot": "night",
      "lines": [
        {"who": "harin", "text": "약속했다. 나 이런 건 기억해.", "face": "happy", "beat": "holds_eye_contact"},
        {"who": "player", "text": "밥 약속을 그렇게 오래 기억해?"},
        {"who": "harin", "text": "응. 네가 먼저 같이 먹자던 날도.", "face": "relaxed", "beat": "soft_smile"},
        {"who": "harin", "text": "그땐 밥 식는 줄도 모르고 떠들었네.", "face": "happy", "beat": "soft_laugh"},
        {"who": "system", "text": "되감기: 19일 후 삭제됩니다", "beat": "deletion_notice"},
        {"who": "harin", "text": "처음엔 네가 내 얘기를 들어 줬지", "face": "relaxed", "beat": "holds_eye_contact"},
        {"who": "system", "text": "되감는 중...", "beat": "rewind_to_day_one"}
      ]
    }
  ]
}
```

[04:48] 전하, 분기용 장면을 포함하여 6장면·38줄이며, 실제 한 회차는 5장면·34줄이옵니다. 선택은 3번이옵고, 파일은 고치지 않았사옵니다. 녹음할 8줄은 아래와 같사오며, 줄 번호는 각 장면의 `lines` 첫 항목을 1로 센 것이옵니다.

- `p_dinner` 3번: “네 배가 나한테 계속 부탁하던데.”
- `p_praised` 1번: “그런 말을 그렇게 바로 해?”
- `p_teased` 3번: “네가 진짜 볼 줄은 몰랐지.”
- `p_washing_up` 2번: “그 말 기다렸어. 수세기 여기.”
- `p_side_by_side` 6번: “난 좀 재밌는데. 너 옆에 있으니까.”
- `p_side_by_side` 7번: “아, 설거지 말고 네 표정이.”
- `p_side_by_side` 8번: “내일도 같이 먹을래?”
- `p_rewind` 6번: “처음엔 네가 내 얘기를 들어 줬지”