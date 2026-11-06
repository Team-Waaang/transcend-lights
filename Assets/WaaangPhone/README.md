# Waaang 휴대폰 컨트롤러

씬: `Assets/WaaangPhone/Scenes/WaaangPhoneController.unity`

프리팹: `Assets/WaaangPhone/Prefabs/WaaangPhoneController.prefab`

원본: `C:/Users/bsh07/workspace/Playground/phoneControler` (Unity 2022.3, Built-in)에서 이 게임 프로젝트(Unity 6, URP)로 옮겼습니다.

## 이 프로젝트로 옮기면서 바뀐 점

- **Android SDK**: `com.phonepointer.android`의 Runtime/Editor를 `Assets/PhonePointerSDK/Android`로 복사했습니다(원본 GUID 유지). 기존 `Assets/PhonePointerSDK/Core`와 내용이 같아 그대로 참조합니다.
- **AR 패키지**: `com.unity.xr.arcore` 6.7.1(AR Foundation 6.7.1 포함)을 추가했습니다. Android XR 로더는 ARCore만 활성화했고(`Assets/PhonePointer/Settings`), PC(Standalone)에는 XR 로더가 없습니다.
- **인식 이미지**: 폰은 룬 석판 이미지(`Resources/RuneSlabReference.png`)를 추적합니다. 이 프로젝트의 `Resources/ScreenMarker.png`는 PC 화면 마커이므로 `PhoneController`가 `RuneSlabReference`를 먼저 불러오도록 수정했습니다.
- **입력**: 이 프로젝트는 Input System 전용이라 EventSystem 모듈을 `InputSystemUIInputModule`로 바꿨습니다. SDK의 자이로(에어마우스) 모드는 레거시 입력이 필요하므로 이 설정에서는 비활성으로 표시됩니다. 필요하면 Player Settings의 Active Input Handling을 Both로 바꿉니다.

## 터치 이펙트 (VFX_Klaus)

기존 `SmallLightMuzzle` + 전용 파티클 셰이더(`PhoneSpell.shader`)는 삭제했습니다. 화면을 누르면 `Assets/VFX_Klaus`(Hyper Casual FX Vol.2) 프리팹을 URP 머티리얼 그대로 재생합니다.

- 지팡이 끝: `HCFX_Hit_04`(빛 폭발 → 링) + `HCFX_Flash_04`(광채와 반짝임)
- 지팡이 몸통: `HCFX_Flash_05`(별 반짝임) × `shaftPoints`
- 누르고 있는 동안 0.32초마다 다시 재생합니다. 이전 발사의 잔광이 끊기지 않도록 3세트를 번갈아 씁니다.
- 색상: 파티클 색조를 플레이어 색으로 이동합니다(아멜리아 금색, 녹스 파랑). 밝기는 유지하므로 흰 중심부는 흰색으로 남습니다. `playerTint` 0이면 VFX 원래 색입니다.

`WaaangPhoneControllerUI` 인스펙터의 `Touch VFX` 항목에서 이펙트 프리팹, 크기(`tipEffectScale`, `shaftEffectScale`), 개수, 색조를 바꿀 수 있습니다.

## 화면과 입력

- 인식 전: SDK의 AR 카메라 위에 `ParchmentTitleCard` 양피지로 "석판을 인식하라" 안내를 표시합니다.
- 인식 후: 지팡이(아멜리아/녹스)를 중앙에 표시합니다. 추적·보정이 끝난 상태에서 화면을 누르면 SDK의 `PressDown()`이 호출되고 터치 이펙트가 재생됩니다. 손을 떼거나 화면 밖으로 나가거나 포커스·추적을 잃거나 설정을 열면 `PressUp()`이 호출됩니다.
- 좌측 하단 중앙 버튼: 중앙 보정 (설정 버튼과 같은 크기).
- 우측 하단 설정: 감도, 석판 재등록, AR/자이로 전환, 카메라 보기, 진단 로그 저장.
- 지팡이는 레이어 30의 별도 RenderTexture에 렌더링하며, AR 카메라는 이 레이어를 렌더링하지 않습니다.

## 빌드

빌드 씬 목록은 PC 게임용 그대로 두었습니다. 폰 APK를 만들 때는 Android 플랫폼으로 전환하고, `Phone Pointer/Android/Apply ARCore build settings` 메뉴(세로 화면, ARM64, IL2CPP, GLES3)를 적용합니다. 빌드 씬은 이 씬 하나만 체크합니다.
