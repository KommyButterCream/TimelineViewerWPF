
작성자 : RYLYNN
날짜 : 2026.10.29 17:12

[ 프로젝트 구조 ]
Timeline.Format/
├─ TimelineFormat.cs
├─ TraceRecord.cs
├─ TraceHeaderRecord.cs
├─ TraceTrackRecord.cs
├─ TraceBeginRecord.cs
├─ TraceEndRecord.cs
└─ TraceSessionEndRecord.cs

[ 클래스 구현 내용 ]
====== 타입 ======			================= 내용 =================
├─ TimelineFormat			포맷 이름, 현재 버전, 시간 단위, 최대 시간 값
├─ TraceRecord				레코드들의 공통 기반 타입
├─ TraceHeaderRecord		포맷, 버전, 시간 단위, OriginUtc
├─ TraceTrackRecord			Track ID와 표시 이름
├─ TraceBeginRecord			이벤트 ID, Track ID, 이름, 상대 시간, 선택적 Parent와 Args
├─ TraceEndRecord			이벤트 ID, 상대 시간
├─ TraceSessionEndRecord	종료 상대 시간, 누락 레코드 수