# 외부 자산과 라이선스

이 저장소에는 외부 자산 파일을 넣지 않는다. 모두 `.gitignore` 로 빠져 있고, 각 PC 에 따로 받아 둔다.
여기에는 어디서 받았고 어떤 조건으로 쓸 수 있는지만 적는다. 게임을 내보낼 때는 "표기 필요" 항목을 크레딧 화면에 넣는다.

## 고양이 모델과 동작

| 자산 | 출처 | 라이선스 | 표기 | 둔 곳 |
|---|---|---|---|---|
| Cartoon Cat (CAT) | Unity Asset Store, Zealous Interactive, 무료 | Standard Unity Asset Store EULA. 게임에 넣어 배포 가능, 파일 자체 재배포 금지 | 필요 없음 | `unity/Assets/Oddity/DevMotion/Cat/*.fbx`, `cartoon_cat_shared.glb` |
| Somali Cat Animated ver 1.2 (SOMALI) | [Sketchfab, DreamNoms](https://sketchfab.com/3d-models/somali-cat-animated-ver-12-e185c3fd92b64c32b4515a32b29252fc) | CC BY 4.0. 상업 이용과 변경 가능 | 필요: "Somali Cat Animated ver 1.2" by DreamNoms, CC BY 4.0 | `unity/Assets/Oddity/DevMotion/Cat/Somali/` |
| Medium poly Cat In Motion (cat4, 줄무늬) | [Sketchfab, rt699448](https://sketchfab.com/3d-models/medium-poly-cat-in-motion-3d-model-free-5c31c77904de4e458d434c167ea0f4bc), 2026-11-08 받음 | CC BY 4.0. 상업 이용과 변경 가능 | 필요: "Medium poly Cat In Motion 3d Model Free" by rt699448, CC BY 4.0 | 아직 프로젝트 밖 (`Downloads`, `test1/.../sketchfab/cat4/`) |
| 흰 고양이 (WHITE) | Tripo 로 만든 메시를 코덱스가 리깅 | Tripo 이용 약관을 따른다 (만든 계정의 요금제에 따라 상업 이용 조건이 다르니 배포 전에 확인) | 약관 확인 뒤 정함 | `unity/Assets/Models/test/whitecat-2-1*.glb` |
| 고양이 공통 동작 14개 | 이 저장소의 `tools/blender/whitecat_clips.py`, `retarget_cats.py` 로 만듦 | 자체 제작 | 필요 없음 | 위 glb 안 |

CC BY 로 받은 모델을 고쳐 쓴 것(눕기 동작 추가, 리타겟)도 원 저작자 표기가 필요하다. 고친 사실도 함께 적는다.

## 고양이 소리

| 자산 | 출처 | 라이선스 | 표기 |
|---|---|---|---|
| cat_mewfood 외 5개 | [OpenGameArt, Kerzoven "Cat Purr & Meow"](https://opengameart.org/content/cat-purr-meow) | CC0 | 필요 없음 |
| kitten_mew | [OpenGameArt, antumdeluge](https://opengameart.org/content/kitten-mew) | CC0 | 필요 없음 |
| meow | [OpenGameArt, IgnasD](https://opengameart.org/content/meow) | CC0 | 필요 없음 |
| cat_flap_ears | [OpenGameArt, bashar3a](https://opengameart.org/content/cat-flapping-ears-sound) | CC0 | 필요 없음 |
| cat_sfx_1 → 8 | [OpenGameArt, Macro "Cat SFX"](https://opengameart.org/content/cat-sfx) | CC BY 3.0 | 필요: "Cat SFX" by Macro, CC BY 3.0 |
| kr_* 10개 (코리안숏헤어 녹음) | [공유마당](https://gongu.copyright.or.kr), 저작권자 JICA, 출처 (재)전주정보문화산업진흥원 | 공공누리 제1유형 (출처표시, 상업 이용과 변경 가능) | 필요: "(재)전주정보문화산업진흥원, 공유마당" |
| kr_kitten_cute_sfx | 공유마당 wrtSn=13355382 | CC BY | 필요: 공유마당 저작물 정보에 적힌 저작자 |

소리 파일은 `unity/Assets/Oddity/DevMotion/Cat/Sfx/` 에 있고, 폴더마다 `CREDITS.txt` 에 원본 주소를 적어 두었다.
`kr_*` 는 `tools/cut_cat_sfx.py` 로 잘라 썼다(변경 허용 범위).

## 하린 동작

| 자산 | 출처 | 라이선스 |
|---|---|---|
| Mixamo 동작 (Mx*) | Adobe Mixamo | Adobe 이용 약관. 게임에 넣어 배포 가능, 동작 파일 자체 재배포 금지 |
| Anime Girl Idle Animations Free (AGIA_*) | Unity Asset Store, Clean Curve Studio | Standard Unity Asset Store EULA |
| KAWAII 동작 (Ka*) | 이미 있던 묶음 | 구입처의 약관을 따른다 (배포 전 확인) |
| Universal Animation Library (UAL1Standard) | [Quaternius](https://quaternius.itch.io/universal-animation-library) | CC0 |

## 개인용 (배포 금지)

- 제로투 춤 음악 `ZeroTwoTrack.mp3`: 사용자가 고른 영상에서 받은 음원. 저작권이 있어 시험용으로만 쓰고 빌드에 넣지 않는다.
- karina 음성: `build --demo-voice` 개인 빌드에만 쓴다.

## 참고만 한 자료

- 아파트 배치: LH 천안신방 행복주택 36A형 평면도(홍보물)의 치수만 참고했다. 그림은 저장소에 넣지 않았다. `shots/plans/apartment_36a_layout.md`
