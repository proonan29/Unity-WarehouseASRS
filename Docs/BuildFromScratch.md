# WarehouseASRS — 자동창고 입출고 디지털 트윈 (처음부터 만드는 순서)

입고 컨베이어로 들어온 **RFID 박스**를 로더가 **AMR**에 올리고, AMR이 최적 경로로 지정된 **스태커 크레인(미니로드 ASRS)**의
입고 큐에 내려놓으면 크레인이 빈 슬롯에 넣고 기록합니다. 주문이 생기면 크레인이 꺼내 출고 큐에 놓고, AMR이 **포장 스테이션**으로
가져가면 **6축 로봇(진공 흡착)**이 주문 박스에 물건을 담아 출하하고 남은 물건은 다시 입고합니다. 빈 박스는 입고장에 가까운
스태커에 보관하고, 만재가 되면 입고를 멈추고, 재고가 없으면 주문이 다음 입고까지 기다립니다.
CellFactory·CleanerBot·WeldingBot·ThemePark·WeatherScape·RadarNet과 같은 방식(Unity CLI + Pipeline 패키지, `wa_` 명령,
명령 하나로 씬 재생성)으로 만들었습니다.

- 환경: Windows, Unity 6000.6.3f1 (URP, `com.unity.template.urp-blank`), Unity CLI 1.0.0-beta.11
- 경로: `D:\UnityAIProjs\DigitalTwin\WarehouseASRS`, GitHub: https://github.com/proonan29/Unity-WarehouseASRS (공개)
- 패키지 추가: `com.unity.pipeline` 0.8.0-exp.1 (`[CliCommand]` → `unity command wa_*`)
- 작업 순서: C# 작성 → `AgentTools\build.ps1`(dryrun 컴파일 → Refresh → 어셈블리 갱신 대기) → `AgentTools\war.ps1 wa_*` →
  `wa_capture`/`snap.ps1` 스크린샷 → git 커밋
- 처음 만들기
  1. `unity projects create WarehouseASRS --path D:\UnityAIProjs\DigitalTwin --editor-version 6000.6.3f1 --template com.unity.template.urp-blank --no-cloud`
  2. `Packages/manifest.json`에 `com.unity.pipeline` 추가(**BOM 없는 UTF-8**로 저장, BOM이 있으면 UPM이 500 오류),
     CellFactory의 Noto Sans KR 폰트·테마(.tss)·`.gitignore`·`Overlay.shader`·`AgentTools` 복사(이름만 WarehouseASRS로)
  3. `unity open` → 스크립트 작성 → `wa_setup_scene`
- 모델은 모두 **절차적 로우폴리**(박스·원기둥 조합, DontSave, 실행 중 생성). 치수·시간은 일반적인 예시 값입니다.

## 1. 사용자가 정한 내용 (2026-10-26)
1. 입고 로더 = 롤러 이송 + 단순 포크(푸셔) 애니메이션, 6축 로봇은 출고 피킹에만 사용
2. AMR 1대가 박스 1개 운반
3. 박스 1개 = SKU 1종 × 수량
4. 이름 WarehouseASRS, 공개 저장소 Unity-WarehouseASRS
5. 절차적 모델 먼저, Asset Store는 나중

## 2. 구조

| 폴더/파일 | 내용 |
|---|---|
| `Core/Tf.cs`, `Core/RobotKin.cs` | 변환·난수(xorshift) / 6축 기구·FK·해석적 IK (CellFactory와 동일) |
| `Core/PathGraph.cs`, `Core/Traffic.cs` | 방향 경로망(스퍼 + `Node.spur`), 구간 예약 교통 제어, 스퍼 일괄 진입, `Retarget`, 순환 감지 강제 통과 |
| `Core/Config.cs` | `WarehouseConfig`(스태커 4·bay 30·단 16·AMR 10·스테이션 2·정책 ABC·듀얼·입고 100/h·주문 60/h·초기 충진 0.4·만재 0.92/0.85) |
| `Core/Entities.cs` | Box(RFID)·Sku·Dock·Stacker·InboundLine·Order·Amr·Station, 슬롯/박스/주문/AMR/스테이션 상태 열거형 |
| `Core/Layout.cs` | 랙·AMR 링·도크·컨베이어·스테이션 좌표, 슬롯 인코딩, 크레인 시간 기반 슬롯 순위 |
| `Core/Crane.cs` | 미니로드 크레인: 사다리꼴 속도 이동(x·y 동시), 포크 2.2 s, 듀얼 커맨드, I/O 큐 |
| `Core/Warehouse.cs` | 0.1 s 고정 간격 결정론 시뮬레이션: WMS 슬롯 상태기계, 슬롯 정책, 입고 생성(만재 히스테리시스), KPI, 이력 |
| `Core/WarehouseOrders.cs` | 주문 생성·할당(best-fit), 출고 요청 순서, 스테이션 상태기계(주문박스 → 원본박스 → 피킹 → 잔량 반출 → 출하) |
| `Core/WarehouseAmr.cs` | AMR 배차·상태기계·충전, 같은 도크에서 연속 작업(TryChain) |
| `Core/PickRobot.cs` | 피킹 로봇 궤적(IK 웨이포인트, 흡착 3.8 s/개) |
| `World/Prims.cs`, `World/Shapes.cs` | 재질·도형·병합 메시(BoxMerger) / 크레인·AMR·컨베이어·로더·6축 로봇 |
| `World/WarehouseView.cs` | 홀·랙(병합 메시)·컨베이어·도크 생성, 매 프레임 크레인·AMR·로봇·박스(GPU 인스턴싱 7색) 표시, 경로 오버레이, 3D 라벨, 클릭 선택 |
| `App/AppController.cs`, `CameraRig.cs` | 시뮬레이션 소유·속도 진행·재구성·카메라 프리셋·백그라운드 정책 비교 / 궤도 카메라 |
| `UI/WarehouseUI.cs`, `UI/Loc.cs` | UI Toolkit(코드 생성) 한/영: 왼쪽 구성·실행·보기·비교·범례, 오른쪽 KPI·추이 그래프·스태커·스테이션·AMR·선택 상세, 아래 이벤트 |
| `Editor/WarehouseCommands.cs` | `wa_setup_scene`, `wa_setup_font`, `wa_status`, `wa_load`, `wa_advance`, `wa_set`, `wa_camera`, `wa_capture` |
| `Editor/SimCommands.cs`, `CheckCommands.cs` | `wa_sim_run`, `wa_compare`, `wa_trace`, `wa_ik_test`, `wa_layout`, `wa_check`(불변식 검사) |

## 3. 배치 (4 스태커·2 스테이션 기준, 단위 m, 홀 z = 0 … 36)
- 랙: x = 6 … 27 (bay 30 × 0.7), 단 16 × 0.45 → 높이 8.0 m, 통로 피치 2.5, 통로마다 양쪽 랙 줄(슬롯 960 × 4 = **3,840**)
- 스태커 크레인: 통로 안 레일 주행, 입출고(I/O) 지점은 x = 5.65(랙 앞), 입고 큐 z − 0.925 / 출고 큐 z + 0.925 (각 2칸)
- AMR 링(일방통행): 차선 A x = −1 북행(스태커 도크 x = 2.4), 차선 B x = −10 남행(입고 IN1·IN2, 스테이션 PK1·PK2 도크 x = −14),
  충전 패드 10개는 링 안쪽. 도크는 막다른 스퍼(들어갈 때 전진, 나올 때 후진)
- 입고 컨베이어: x = −32 → −16, 로더(포털 + 푸셔 바)가 AMR 데크(x = −14)로 밀어 올림
- 스테이션: 입구 버퍼(6칸) → 원본 작업대·주문 작업대, 6축 로봇(x = −20.8), 잔량 출구 버퍼(4칸), 출하 컨베이어
- 경로망: 노드 83, 간선 134, 교차점 22 (`wa_layout --stations 2`), 도크 간 도달 불가 쌍 0
- 크레인 시간(I/O 기준): 가장 가까운 슬롯 1.7 s, 가장 먼 슬롯 9.8 s

## 4. 시뮬레이션 규칙
- **슬롯 상태**: Free → ReservedIn → Stored → ReservedOut → Free. 슬롯 예약·해제는 `Reserve/Release` 한 곳에서만 처리.
- **슬롯 정책**: Random / Nearest(크레인 시간 최단) / **ABC**(A급은 I/O 가까이, B는 약 35 % 지점, C는 먼 끝). SKU 40종, Zipf 인기도로 A·B·C 분류
- **듀얼 커맨드**: 넣고 나서 곧바로 꺼낼 박스가 있으면 이어서 처리(이동 감소). 크레인 최고 속도·가속도 x 3.2 m/s·1.0 m/s², y 1.2 m/s·0.9 m/s²
- **입고**: 시간당 입고율(지수 분포) → 컨베이어 → 로더 → AMR → 스태커. 충진율 ≥ 0.92면 **만재 중지**, < 0.85에서 재개
- **주문**: 1~3개 품목, 주문 생성 때 best-fit으로 박스 할당. 재고 20개 미만이면 주문 생성을 멈추고 "다음 입고까지 대기"
  (이미 출고 완료된 주문은 끝, 대기 주문은 재고가 생기면 재개). 대기열 최대 12
- **출고 순서**: 스테이션 대기열 맨 앞 주문의 박스부터 크레인이 꺼내고, AMR도 그 박스를 먼저 운반(출고 큐가 뒤 주문으로 막히지 않게)
- **포장**: 스테이션이 주문 박스(빈 박스)와 원본 박스를 입구에서 작업대로 → 6축 로봇이 흡착으로 개당 3.8 s → 잔량이 있으면 원본 박스를
  출구 버퍼로 → AMR이 스태커로 반송(재입고). 주문 박스는 출하 컨베이어로 나가며 재고에서 빠짐
- **빈 박스**: 빈 박스로 표시, 입고장에 가장 가까운 스태커에 보관. 부족하면 공급처에서 빈 박스 투입(입고 중지 중에도 빈 슬롯 12개 이상이면)
- **AMR 배차**: 후보 = 스테이션 반송(+5)·출고 큐(+3, 활성 주문 +20)·입고 라인 → 대기 시간 순, 가장 가까운 가용 AMR. 도크 동시 작업 제한
  (스태커 2, 스테이션 4). 배터리 0.012 %/s 소모, 충전 0.1 %/s, 22 % 미만이면 70 %까지 충전. 작업이 끝나면 충전/대기 패드로
- **교통**: 노드 예약 + 교차 구역 일괄 예약, 스퍼는 통째로 한 번에 진입(정면 교착 방지), 대기 그래프 순환 감지 → 강제 통과(최후 수단)

## 5. CLI 명령 (`unity command <cmd> --no-banner --result-only`, `AgentTools\war.ps1 <cmd> ...`)
| 명령 | 내용 |
|---|---|
| `wa_setup_scene [--advance 1800]` | 재질(WA_Lit·WA_Overlay)·조명·카메라·App·UI·폰트 포함 씬 전체 재생성 후 저장 |
| `wa_setup_font` | Noto Sans KR 폰트 에셋 생성 |
| `wa_sim_run --hours 24 [--stackers --bays --levels --amrs --stations --policy --dual --in_per_hour --orders_per_hour --fill --seed --events]` | 화면 없이 실행, KPI·스태커·스테이션·AMR 보고 |
| `wa_compare --hours 12` | 슬롯 정책 × 듀얼 비교표 |
| `wa_check --hours 24 [...]` | 20분마다 불변식 검사(슬롯 장부·박스 컨테이너·주문·AMR 겹침) |
| `wa_trace`, `wa_ik_test`, `wa_layout` | 교통 진단 / 피킹 로봇 도달성 / 배치·경로망 정보 |
| `wa_status`, `wa_load`, `wa_advance --seconds`, `wa_set`, `wa_camera`, `wa_capture` | 실행 중 앱 제어 |
| `wa_set --select box:123 / amr:A3 / stacker:2 / station:1 / none`, `--toggles lanes=1,paths=1,zones=1,labels=1`, `--lang ko` | 선택·표시·언어 |
| `wa_camera --preset overview/top/racks/inbound/station1/aisle1` | 카메라 프리셋 |
- 플레이 모드 UI 화면: `editor_play` → `focus_unity.ps1`(**포커스가 없으면 에디터가 거의 갱신되지 않음**) → `snap.ps1 <이름> 1600 900 screen`
- 에디터 모드 `wa_capture`는 `cam.Render()` 사용(인스턴싱 박스 포함). 도메인 리로드 때 시뮬레이션이 t = 0으로 돌아가므로 캡처 전 `wa_advance`

## 6. 검증 결과 (seed 1, 헤드리스, 4 스태커 30×16, AMR 10, 스테이션 2)
- 24 h 기본(입고 100/h, 주문 60/h): 출고 1,402건 **57.9/h**, 리드타임 7.0분, 충진 40 → 69 %, 크레인 32 %, 듀얼 69 %, AMR 74 %, 교착 강제 통과 18
- 만재 시험(2 스태커 12×8, 입고 200/h, 주문 30/h): 충진 95 %까지 올라가 **만재 중지 7회**, 출고 29.4/h — 불변식 모두 통과
- 슬롯 정책 × 듀얼 (12 h, 주문 59~61/h로 거의 같음 — **병목은 크레인이 아니라 AMR·스테이션 흐름**)

| 정책 | 듀얼 | 출고/h | 리드타임(분) | 크레인 | 듀얼 비율 |
|---|---|---|---|---|---|
| Random | 아니오 / 예 | 61.1 / 59.3 | 6.6 / 6.4 | 31 / 32 % | 55 / 69 % |
| Nearest | 아니오 / 예 | 59.0 / 59.0 | 6.1 / 5.7 | 31 / 31 % | 56 / 69 % |
| ABC | 아니오 / 예 | 59.6 / 58.7 | 6.8 / 6.1 | 33 / 33 % | 56 / 68 % |
- `wa_check`(24 h, 두 구성): **INVARIANTS OK** — 슬롯 장부·박스 한 곳에만 존재·주문 박스 존재·AMR 간격 0.55 m 이상
- IK: 두 스테이션 모두 집기·놓기 도달(`wa_ik_test`), 컴파일 경고 0, 헤드리스 약 25,000배속

## 7. 문제와 해결 기록
- 매니페스트 BOM → UPM 500 오류 → BOM 없는 UTF-8로 저장
- `Cp` 별칭 충돌, `Crane.cs` 쓰레기 코드 재작성
- 스퍼 정면 교착(223회/2 h) → 스퍼 노드 표시 + 스퍼 일괄 진입 + `Retarget` → 2회/2 h
- 빈 박스 수 누수(`empties--` 누락) 수정
- 처리량 41/h 정체 → 원인: 스테이션 도크 동시 작업 2·입구 버퍼 4가 병목 → 도크 4·버퍼 6/4, 출고 순서(활성 주문 우선), 스테이션 2개 + AMR 10대 → 57.9/h
- 만재 중지 때 빈 박스 공급이 막혀 주문 정체(7/h) → 빈 박스는 입고 게이트와 무관하게 빈 슬롯이 12개 이상이면 투입
- `build.ps1`가 가끔 "assembly unchanged"로 끝남 → 포커스 + 다시 실행(Editor는 배경에서 임포트를 미룸)
- UI Toolkit `Box`가 `WarehouseASRS.Core.Box`와 모호 → 완전한 이름 사용
- ↺ 글리프가 폰트에 없음 → 문자 제거

## 8. 다음 작업 후보
1. Asset Store 모델(스태커 크레인·AMR·로봇)로 교체
2. 고장·정전 시나리오(크레인 정지·AMR 고장), 우선순위 주문(긴급 출고)
3. CSV 내보내기·시간대별 그래프 확대, 재고 보충(ROP) 정책
4. 다중 통로 간 박스 이동(리밸런싱)·크레인 통로 전환
