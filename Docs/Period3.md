# 3교시 — 보스전

3교시 보스전의 흐름, 보스 컷씬 3종(비트·샷·VFX), 공명 QTE, 보스 아레나 배경, 실제 플레이 구현(5장)을 정리한 문서.

## 1. 흐름

```
3교시 입장 ─▶ 보스 등장 컷씬 ─▶ 전투 1페이즈 ─▶ 보스 2페이즈 컷씬 ─▶ 전투 2페이즈
   ─▶ (보스 HP가 아주 조금 남으면) 보스 패배 컷씬
         └ 레이저 겨루기 중 P1·P2 공명 QTE(가운데 충돌점 조준 + 폰 연타)
               ─ 성공 ─▶ 얼티메이트 빔! ─▶ 블라스트 → 정화 → 3교시 클리어
               └ 시간 초과 ─▶ 겨루기 구간 반복(게이지 절반 유지)
```

- 플레이어: Amelia(1P, 오른손잡이, 금색)와 Nox(2P, 왼손잡이, 파랑). 폰을 에어마우스로 써서 지팡이 레이저를 쏜다(마우스 테스트: 좌클릭 = 1P, 우클릭 = 2P).
- 보스: 저주받은 후드 쓴 아이 마법사. 패배 컷씬에서 후드가 날아가고(모델 교체) 정화되어 일어선다.
- 무대: 밤의 아카데미, 무너진 원형 결투장(아래 3장).

## 2. 보스 컷씬 3종

세 컷씬 모두 에디터 빌더가 씬과 Timeline을 **통째로 생성**한다. 씬을 직접 고치지 말고 빌더 코드를 고친 뒤 메뉴로 다시 빌드할 것.

| 컷씬 | 메뉴 | 빌더 | 결과 씬 | 길이 |
|---|---|---|---|---|
| 등장 | Tools/Cutscene/Build Boss Entrance Cutscene | `Assets/Cutscene/Editor/BossEntranceCutsceneBuilder.cs` | `Assets/Cutscene/BossEntrance/BossEntranceCutscene.unity` | 14.8s |
| 2페이즈 | Tools/Cutscene/Build Boss Phase 2 Cutscene | `BossPhase2CutsceneBuilder.cs` | `Assets/Cutscene/BossPhase2/BossPhase2Cutscene.unity` | 14.0s |
| 패배 | Tools/Cutscene/Build Boss Defeat Cutscene | `BossDefeatCutsceneBuilder.cs` | `Assets/Cutscene/BossDefeatCutscene.unity` | 37.0s (QTE 포함) |

공통 부품은 `CutsceneKit.cs`에 있다: 샷 카메라(`Shot`: From/To, LookAt, FOV 줌, 셰이크와 킥, `SnapIn`), VFX 생성·음소거·틴트·루프, 저주 위습(`CurseWisps`), 먼지(`Motes`), 볼륨(Base / Curse / Impact), 레터박스·플래시 UI, 호흡·떨림 오버라이드 레이어(`AddBreathing`, `AddTremble`), Timeline 조립(`AddShots`, `AddControlled`, `Place`).

### 좌표 약속 (중요)

- 보스는 **원점에 서서 -X를 본다** (`CutsceneKit.BossRotation` = Y -90°). 플레이어는 -X 쪽에 있다는 설정이고, 패배 컷씬의 플레이어 빔은 x = -3.6(`GoldSourceX`)에서 온다.
- **세 컷씬 어디에도 Amelia·Nox 모델은 없다.** 화면에 보스만 나온다.
- 보스 모델은 스케일 1(키 약 1.1m, 아이). 플레이어 캐릭터는 게임에서 1.5배(약 1.65m), **+Z를 본다**.
- 등장·2페이즈는 마지막에 레터박스가 닫히며 검게 끝나고(`TSlam`), 패배는 페이드아웃으로 끝난다. 전투로 넘어갈 때 컷이 숨겨진다.

### 2-1. 보스 등장 (`BossEntranceCutsceneBuilder`)

어둠 속 오멘 → 보랏빛 연기 속에서 웅크린 채 등장 → 차가운 응시 → 지팡이를 들어 저주를 부른다.

| 시간 | 비트 | 샷 |
|---|---|---|
| 0–2.45 | 오멘: 바닥 꽃잎 링, 맥동하는 Omen 라이트 | A_Establish (와이드) |
| 2.45 / 2.53 | 연기 폭발 속 웅크린 등장, 착지 쿵 | B_RiseLow (로우앵글, 착지 킥 + 썸프) |
| 4.6–6.2 | 일어서는 실루엣 | C_Silhouette |
| 6.2–10 | 차가운 응시, 8.4 얼굴 라이트 | D_ColdStare (머리 LookAt) |
| 10 / 10.9 | 소환: 지팡이 끝 번개 차지, 10.9 정점에 버스트 + 바닥 링 + 보라 빛기둥 3개, Dread 그레이드 | E_Summon |
| 13.5–14.05 | 얼굴로 펀치인, 셔터 닫힘 | Z_Snap |

애니메이션: model_2 "Young mage crouches" → "standing still" → model_1 "raises the wand"(정점에서 0.06배속 홀드).

### 2-2. 보스 2페이즈 (`BossPhase2CutsceneBuilder`)

플레이어 공격 명중 → 한쪽 무릎 → 심장박동처럼 저주가 맥동 → 지팡이 짚고 일어섬 → 팔을 벌리며 저주 폭발, 2형태.

| 시간 | 비트 | 샷 |
|---|---|---|
| 0.6 | 플레이어 공격이 가슴에 명중 (Hit_04 + Flash_05) | A_Hit |
| 1.7–5.4 | 무릎 꿇고 거친 호흡, 하트비트 3.0 / 3.9 / 4.6 / 5.15 (Pulse 볼륨, 카메라 킥) | B_Kneel |
| 5.4–8.0 | 지팡이 짚고 일어섬, 발밑 번개, 가슴에 에너지 모임 | C_Rise |
| 9.05 | 팔 활짝: 가슴 번개 폭발 + 마젠타 바닥 링, Phase2 그레이드(채도 있는 보라) | D_Erupt |
| 10.4–13.35 | 2형태 클로즈업 → 펀치인, 셔터 | E_Phase2, Z_Snap |

애니메이션: Idle → "gets hit" → model_2 "kneeling on one knee" → "throws both arms".

### 2-3. 보스 패배 (`BossDefeatCutsceneBuilder`) — 공명 QTE 포함, 37.0s

Amelia·Nox가 x ≈ -4.6에 서서(+X를 봄) 각자 지팡이 끝에서 빔을 쏜다(Amelia 금 Beam_03, Nox 파랑 Beam_02). 보스는 보라 Beam_04. 두 플레이어 빔은 지팡이 끝에서 충돌점까지 위치·회전·길이를 30fps로 구워 둔다.

| 시간 | 비트 | 샷 |
|---|---|---|
| 0.7 / 1.1 | 세 빔 발사, 중간에서 충돌 | A_ClashWide |
| 1.1–4.9 | 겨루기, 3.9에 보스가 밀어붙여 충돌점이 플레이어 쪽으로 | A → B_BossClose |
| **4.9–7.1** | **공명 인트로**: 두 사람에게 금·파랑 오라, 발밑 링, 지팡이 차지, "공명!" | R_Resonance |
| **7.1–15.1** | **QTE 라이브**: 충돌점이 화면 정중앙, 타깃 링·게이지 UI. 성공 전까지 이 8초 구간을 반복 | Q_Qte |
| **15.1** | **성공 → 얼티메이트 빔!!**: 두 빔이 굵어지고, 섬광과 충격, 충돌점이 보스로 밀려감 | U_Ultimate |
| 16.9– | 기존 패배 연출(+11.5s): 압도 → 블라스트(17.65) → 모델 교체 → 정화(28.7) → 피날레, 페이드아웃 | C … H |

QTE 규칙(`Assets/Cutscene/Runtime/ResonanceQte.cs`):
- 각 플레이어는 포인터를 타깃 링(충돌점) 안에 두고 버튼을 연타한다. 링 안에서 누른 순간마다 게이지 +0.085, 초당 0.12씩 빠짐(링 밖이면 1.6배).
- 두 게이지가 **동시에 95% 이상**이면 성공해서 15.1로 점프한다. 8초 안에 못 하면 "다시, 더 빠르게!"와 함께 게이지가 반으로 줄고 구간을 반복한다(실패 패널티 없음).
- 폰은 `CasterInput`으로 직접 읽는다(리그 없이 폰 세션의 `Pressed`와 포인터). 폰이 없고 마우스 테스트면 한 사람이 아무 버튼이나 연타해서 두 게이지를 같이 채운다.
- Space/Enter는 QTE 앞뒤만 건너뛰고 QTE 자체는 못 건너뛴다.
- 수치는 `ResonanceQte`의 `tapGain`, `drain`, `targetRadius`, `FullZone`.

## 3. 보스 아레나 배경 (`BossArena.cs`)

`Assets/Cutscene/Editor/BossArena.cs`가 만들고 `CutsceneKit.BuildEnvironment()`가 호출한다. 세 보스 컷씬이 같은 배경을 쓴다. 공유 에셋은 `Assets/Cutscene/BossArena/`(머티리얼, 절차 생성 텍스처, 첨탑 메시)에 있다.

- 반지름 3.4m 원형 판석 바닥(`FloorRadius`), 테두리 연석(몇 개는 빠지거나 비뚤어짐)
- 보스 발밑 봉인진: 이중 링, 룬 글리프 띠, 7각 별, 보라빛(가산 쿼드, 텍스처 절차 생성)
- 뒤와 양옆의 부서진 열주, 보스 정면 뒤에 문(인방 + 찢어진 학교 배너 남색/금색, 1교시 교실 배너와 같은 색)
- 바깥 폐허 벽, 안개 속 아카데미 첨탑(불 켜진 창 몇 개), 문 위로 낮게 뜬 저주받은 달과 헤일로
- 바닥을 뚫고 나온 저주 수정 6무더기(보라 발광 + 포인트 라이트), 공중에 떠서 천천히 흔들리는 잔해(`Levitate`, 런타임 전용)
- 보라 불꽃 화로 4개(파티클 + 라이트), 낮게 깔린 안개
- 플레이어 쪽(-X)은 열어 둠

각 컷씬이 비트에 맞춰 배경을 움직인다(`BossArena.AddTimelineClip`으로 `Environment`에 Arena 트랙 추가):
- 등장: 봉인진이 어둡다가 등장 때 켜지고, 소환 정점에서 봉인진·화로·수정이 확 밝아짐
- 2페이즈: 봉인진이 하트비트마다 맥동, 폭발 때 최대
- 패배: 빛이 내려친 뒤 봉인진·화로·수정 라이트가 사그라지고, 정화(17.2)에서 저주 불꽃이 꺼짐

애니메이션 가능한 경로: `Seal`(material `_BaseColor.a`, 기본 `SealAlpha` 0.3), `Brazier i/Fire Light`(i = 0..3), `Crystal Glow i`(i = 0..5).

플레이 씬에서 쓰려면 `BossArena.Build(parent, colliders: true)`를 호출한다. 레이저가 바닥·기둥·벽에 맞도록 콜라이더가 남는다. 반환값(화로 불꽃, 안개)은 플레이 모드에서 알아서 돈다.

## 4. 1교시에서 가져다 쓸 구조

| 부품 | 파일 | 3교시에서의 쓰임 |
|---|---|---|
| 같은 씬 컷씬 → 플레이 핸드오버 | `Period1CutsceneBuilder`, `GameManager` | 컷씬 애니메이터를 끄고 리그로 0.5초 블렌드. 마지막 샷이 플레이 카메라 위치에서 끝남 |
| 캐스터 생성 | `WaaangSceneSetup.CreateCaster(profile, cam, pos, slot)` | Amelia/Nox 전체(모델, 지팡이, 레이저, 히트 마커, IK 리그). +Z를 보고 섬 |
| 프로필 | `Assets/Game/Profiles/Amelia.asset`, `Nox.asset` (`CasterProfile`) | 색, 빔 프리팹, 손잡이 |
| 입력·조준 | `WandPointerRig`: `Firing`, `HasHit`, `HitPoint`, `HitCollider`, `SetPointer`, `ClearTrails` | 보스·투사체 피격 판정 |
| 스킵·타이틀 카드 | `PeriodIntro` (`skipTo`, `onFinished`, 양피지 카드) | 3교시 타이틀("보스" 등) |
| 룬 그리기 판정 | `RuneTrace`, `RuneStoneFx`, `RuneDefinition` | (제안) 보스 패턴 파훼에 1교시 룬 재활용 |
| 판정/진행 컴포넌트 | `RunePeriod` | `BossPeriod`를 같은 모양으로 (`game`, `Update`에서 `rig.Firing` 확인, `OnGUI` HUD, `onCleared`) |

## 5. 구현된 3교시 플레이 (`Assets/Game/Period3/Period3.unity`)

메뉴 **Tools/Period 3/Build Period 3 Scene**(`Assets/Game/Editor/Period3Builder.cs`)이 씬을 만들고, 이 씬과 보스 컷씬 3개를 Build Settings에 넣는다. 씬을 열고 Play 하면 된다(먼저 1교시 컷씬을 한 번 빌드해 둬야 `Assets/Game/Profiles`가 있다).

**흐름(`BossPeriod`).** 등장 컷씬 → 타이틀 카드("저주받은 아이") → 1페이즈 → HP 55에서 슬로모·페이드 → 2페이즈 컷씬 → 2페이즈(보라 그레이드, 보스 저주 오라) → HP 8에서 패배 컷씬(QTE) → "3교시 클리어!". 컷씬은 각자의 씬을 Additive로 불러 재생하고, 그동안 `Period3 Play` 루트(아레나, 카메라, 조명, 볼륨, 플레이어, 보스)를 끈다. 컷씬이 검게 시작하고 검게 끝나서 전환이 안 보인다.

**배치.** 컷씬 좌표 그대로다. 보스는 원점에서 -X를 보고 스케일 1.3, Amelia (-3.7, 0, 0.78)과 Nox (-3.78, 0, -0.78)는 +X를 본다. 카메라는 둘의 뒤(`CamBack`). 레이저 흔적은 끈다(`WandPointerRig.drawTrails = false`).

**전투 규칙.**
- 레이저가 보스에 닿는 동안 1.7 HP/s. 둘이 동시에 맞히면 "공명 ×1.5".
- 패턴(`BossController`), 1페이즈는 대기 → 저주 구슬 3개 → 대기 → 결계 반복:
  - 저주 구슬: 지팡이에서 플레이어 가슴으로 날아온다. 0.3초 쏘면 터진다. 도착하면 보스 +4 회복, 화면이 흔들리고 보라 플래시.
  - 결계: 보스를 감싼 구체. **둘이 동시에** 쏴야 줄어든다. 깨면 3초 약점(피해 ×1.8), 버티면 보스 +3.
- 2페이즈: 대기가 짧아지고 구슬 5개(더 빠름), 결계가 단단해지고, 하늘에서 떨어지는 저주의 비 7개가 추가된다.
- 회복은 현재 페이즈 시작 HP를 넘지 않는다.
- HUD: 보스 HP 바(55/8 표시), 결계 바, 공명/약점 표시, 상황 메시지, 마우스 테스트 안내.
- 개발 키: F1 = 2페이즈 컷씬으로, F2 = 다음 단계(패배 컷씬)로. 컷씬은 Space/Enter로 넘긴다.
- 수치는 `BossPeriod`(phase2At, defeatAt, laserDps, resonanceMultiplier, orbHeal)와 `BossController.phase1/phase2`(대기, 구슬 수·속도·HP, 결계 시간·HP)에 있다.

**연출.**
- 결계(`WardFx` + `Assets/Game/Shaders/Ward.shader`):
  - 셰이더 껍질: 프레넬 테두리, 육각 셀, 위로 훑는 스캔 밴드. 레이저가 맞은 곳에서 파문이 번지고, 약해질수록 셀이 꺼지면서 금이 간다.
  - 주변 장식: 기울어진 궤도로 도는 룬 고리 2개, 발밑에서 도는 링, 바닥 마법진(Klaus MagicCircle 텍스처), 번개와 에너지 파티클.
  - 등장할 때 탄성 있게 튀어나온다. 깨지면 셀 단위로 디졸브되면서 파편과 섬광이 터지고, 히트스톱이 0.15초 걸린다.
- 보스(`BossController`):
  - 떠서 위아래로 흔들리고(발밑에 도는 빛의 문양), 패턴 사이에 미끄러지듯 이동하거나 연기 속으로 순간이동한다.
  - 플레이어 쪽으로 몸과 머리를 돌리고, 이동 방향으로 기울고, 레이저에 맞으면 상체가 젖혀진다.
  - 시전할 때 지팡이 끝에 차지 이펙트가 생기고, 2페이즈는 포효로 시작한다. 저주 구슬에는 빛 꼬리가 붙는다.
- 플레이어(`CasterMotion`): 평소에는 체중을 옮기며 숨쉰다. 쏘는 동안 무릎을 굽히고 몸을 앞으로 기울이며 버틴다. 구슬에 맞으면 뒤로 밀린다. 다리는 IK로 바닥에 고정하고, 팔 조준은 리그가 그대로 맞춘다.
- HUD(`GameGui`):
  - 고운바탕 세리프 폰트, 문장 다이아와 PHASE 표기.
  - HP 바: 그라데이션 채움, 지연되어 따라오는 데미지 칩, 광택 스윕, 눈금, 페이즈 다이아. 약점 상태에서는 금색으로 깜박인다.
  - 결계 바, 배너 메시지, 페이즈 시작 배너.
- 패배 컷씬:
  - 플레이어 클립: `Amelia/Nox_Struggle`(버티며 미는 루프), `_Resonance`(서로 돌아보고 끄덕인 뒤 더 힘줌), `_Ultimate`(당겼다가 내지르는 반동). 메뉴는 `Tools/Cutscene/Build Battle Pose Clips`.
  - 보스: `Boss_PushStrain`(시전 자세에 떨림과 매초 밀어붙임을 구운 루프).
  - 빔 꼬리는 `BeamTailFollow`로 실제 지팡이 끝을 따라간다.
  - QTE 라이브 중에는 공명 게이지에 따라 충돌점과 세 빔이 보스 쪽으로 밀리고, 탭할 때마다 카메라가 흔들리고 빛이 번쩍인다. QTE 샷은 충돌점을 중심으로 천천히 돈다.

**남은 것.**
- 실제 폰 두 대로 연타 감각을 확인하고 `tapGain`·`drain`을 조정.
- 1교시(2교시) → 3교시 연결(`PeriodIntro.nextScene` 또는 씬 로드).
- 비캐릭터 효과음 적용 완료: 마법 발사·구슬·결계·소환·분신·사슬·공명 QTE·컷씬 및 환경음. 파일 목록과 재생·재생성 방법은 `Tools/Audio/README.md` 참고. 캐릭터 보이스는 별도.
- 보스 전투 애니메이션 다듬기(지금은 Idle·Cast·Summon·Guard·Hit 크로스페이드).
- Amelia 프로필 빔(Beam_04)이 보스 빔과 같은 프리팹이라 전투 중 색이 헷갈릴 수 있다. 컷씬에서는 Beam_03을 쓴다.

## 6. 작업 팁

- Play 모드 검증(에디터가 열려 있을 때): `PlayProbe.Play|Assets/Game/Period3/Period3.unity`, `PlayProbe.Status`, `PlayProbe.SetHp|55`, `PlayProbe.Seek|14.7`, `PlayProbe.MashQte|30`(합성 마우스 클릭), `PlayProbe.Shot|name`, `PlayProbe.Stop`(`Assets/Editor/PlayProbe.cs`, `ClaudeBridge` 명령).
- 컷씬 검토: `Tools/Cutscene/Render Preview Frames Of Open Scene`이 열린 컷씬 씬의 12프레임을 `Temp/CutscenePreview/`에 PNG로 저장한다(`CutscenePreview.cs`). 코드에서는 `CutscenePreview.RenderSceneCsv(scenePath, "1.5,3.3,...")`.
- 빌더는 `AssetDatabase.DeleteAsset` 후 다시 만들기 때문에 다시 빌드할 때마다 클립, 볼륨, 머티리얼 에셋과 `.meta`가 대량으로 바뀐다. 정상이다.
- 빌더의 `Build()`는 새 씬을 Single로 열어서, 저장 안 한 열린 씬을 버린다. 메뉴로 실행하면 저장 여부를 먼저 묻는다.
