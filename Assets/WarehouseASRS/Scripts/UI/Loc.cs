using System.Collections.Generic;
using UnityEngine;
using WarehouseASRS.Core;

namespace WarehouseASRS.UI
{
    /// <summary>Korean/English strings. English text is the key; the choice is stored in PlayerPrefs.</summary>
    public static class Loc
    {
        static bool? korean;
        public static bool Korean
        {
            get { if (korean == null) korean = PlayerPrefs.GetInt("WarehouseASRS.lang", 1) == 1; return korean.Value; }
            set { korean = value; PlayerPrefs.SetInt("WarehouseASRS.lang", value ? 1 : 0); }
        }

        static readonly Dictionary<string, string> ko = new Dictionary<string, string>
        {
            ["Automated warehouse digital twin"] = "자동창고 입출고 디지털 트윈",
            ["Warehouse"] = "창고 구성", ["Stackers"] = "스태커 크레인", ["Levels"] = "단수", ["Bays"] = "열(bay)", ["AMRs"] = "운송 AMR", ["Stations"] = "포장 스테이션",
            ["Slot policy"] = "적재 위치 정책", ["Random"] = "랜덤", ["Nearest"] = "입출고구 근접", ["ABC"] = "ABC (회전율)",
            ["Dual command"] = "듀얼 커맨드 (넣고 바로 꺼내기)", ["Inbound / hour"] = "시간당 입고", ["Orders / hour"] = "시간당 주문",
            ["Restart"] = "다시 시작", ["Run"] = "실행", ["Start"] = "시작", ["Pause"] = "일시정지", ["Sim time"] = "시뮬레이션 시각", ["running"] = "실행 중", ["paused"] = "일시정지",
            ["View"] = "보기", ["Overview"] = "전체", ["Top"] = "위에서", ["Racks"] = "랙", ["Inbound"] = "입고장", ["Aisle"] = "통로",
            ["Lanes"] = "AMR 경로망", ["Planned paths"] = "주행 경로", ["Docks"] = "도킹 위치", ["Labels"] = "라벨", ["Follow selection"] = "선택 대상 따라가기",
            ["Compare"] = "정책 비교", ["Compare policies"] = "적재 정책 × 듀얼 비교", ["Running comparison…"] = "비교 실행 중…",
            ["Same warehouse and demand, simulated headless."] = "같은 창고·수요로 화면 없이 시뮬레이션합니다.",
            ["policy"] = "정책", ["dual"] = "듀얼", ["orders/h"] = "주문/시간", ["lead"] = "리드타임", ["crane"] = "크레인", ["AMR"] = "AMR", ["fill"] = "충진", ["stored"] = "입고",
            ["Legend"] = "범례", ["Settings"] = "설정", ["Fast mover (A)"] = "A급 (회전 빠름)", ["Medium (B)"] = "B급", ["Slow (C)"] = "C급 (느림)", ["Empty box"] = "빈 박스",
            ["Reserved for order"] = "주문 예약", ["Order box"] = "주문(포장) 박스", ["Selected SKU"] = "선택 SKU",
            ["Warehouse KPIs"] = "창고 현황", ["Occupancy"] = "점유율", ["Boxes stored"] = "보관 박스", ["Empty boxes"] = "빈 박스", ["Units in stock"] = "재고 수량",
            ["Inbound gate"] = "입고 게이트", ["open"] = "열림", ["FULL STOP"] = "만재 – 입고 중지", ["Orders"] = "주문", ["waiting for stock"] = "재고 없음 – 입고 대기",
            ["Throughput"] = "출고 처리량", ["Lead time"] = "주문 리드타임", ["Crane utilisation"] = "크레인 가동률", ["Dual-command share"] = "듀얼 커맨드 비율",
            ["AMR utilisation"] = "AMR 가동률", ["Station utilisation"] = "스테이션 가동률", ["Traffic wait"] = "주행 대기",
            ["Stacker cranes"] = "스태커 크레인", ["AMR fleet"] = "AMR 현황", ["Picking stations"] = "포장 스테이션", ["Events"] = "이벤트", ["History"] = "추이",
            ["idle"] = "대기", ["store"] = "입고", ["retrieve"] = "출고", ["return"] = "복귀",
            ["Idle"] = "대기", ["ToPad"] = "충전소로", ["Charging"] = "충전/대기", ["ToStop"] = "이동", ["Transfer"] = "이송",
            ["WaitOrderBox"] = "주문박스 대기", ["MoveOrd"] = "주문박스 이동", ["WaitSource"] = "원본박스 대기", ["MoveSrc"] = "원본박스 이동", ["Picking"] = "피킹", ["MoveOut"] = "잔량 반출", ["Shipping"] = "출하",
            ["Deselect"] = "선택 해제", ["State"] = "상태", ["Battery"] = "배터리", ["Load"] = "적재", ["Job"] = "작업", ["Distance"] = "주행거리", ["Waiting"] = "대기",
            ["Box"] = "박스", ["RFID"] = "RFID", ["SKU"] = "SKU", ["Quantity"] = "수량", ["Location"] = "위치", ["Slot"] = "슬롯", ["Order"] = "주문", ["Class"] = "등급", ["none"] = "없음",
            ["Stacker"] = "스태커", ["Slots free"] = "빈 슬롯", ["Commands"] = "작업 수", ["Chained"] = "듀얼", ["In queue"] = "입고 큐", ["Out queue"] = "출고 큐", ["Retrievals"] = "출고 대기",
            ["Station"] = "스테이션", ["Queue"] = "대기 주문", ["Items picked"] = "피킹 수량", ["Orders done"] = "완료 주문", ["Active order"] = "진행 주문",
            ["Drag: rotate · Right-drag: pan · Wheel: zoom · Click: select box / AMR / stacker / station"] = "드래그: 회전 · 오른쪽 드래그: 이동 · 휠: 확대 · 클릭: 박스/AMR/스태커/스테이션 선택",
            ["Supplier"] = "공급처", ["InboundConv"] = "입고 컨베이어", ["OnAmr"] = "AMR 위", ["InQueue"] = "스태커 입고큐", ["OnCrane"] = "크레인 위", ["Stored"] = "랙 보관",
            ["OutQueue"] = "스태커 출고큐", ["StationBuf"] = "스테이션 입구", ["StationWork"] = "스테이션 작업대", ["StationOut"] = "스테이션 출구", ["Shipped"] = "출하",
            ["empty"] = "비어 있음"
        };

        public static string T(string en) => Korean && ko.TryGetValue(en, out var s) ? s : en;
        public static string Policy(SlotPolicy p) => T(p == SlotPolicy.Abc ? "ABC" : p.ToString());
        public static string Event(SimEvent e) => Korean ? e.ko : e.en;
        public static string SkuName(Sku s) => Korean ? s.nameKo : s.name;
    }
}
