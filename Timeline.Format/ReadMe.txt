
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

[ 역할 구분 ]
- 로그의 공통 데이터 모델과 포맷 상수를 정의합니다. UI 없이 참조할 수 있는 클래스 라이브러리입니다.
- Timeline.IO, Timeline.Tracing, TimelineViewer를 참조하지 않습니다.
- JSON 필드명 매핑, 직렬화·역직렬화, 파일 읽기·쓰기 및 레코드 검증은 Timeline.IO에서 담당합니다.
- originUtc와 originMono 생성, 상대 시간 측정, 이벤트 ID 발급 및 백그라운드 큐는 Timeline.Tracing에서 담당합니다.
- Begin/End 연결, 중복 이벤트, Track·Parent 참조 및 순환 검사는 TraceAssembler에서 담당합니다.
- 화면 배치, 시간대 변환 및 진단 표시는 TimelineViewer에서 담당합니다.

[ 데이터 의미 ]
- OriginUtc는 상대 시간 0에 대응하는 UTC 기준 시각입니다. 파일에 쓰는 순간의 시각이 아닙니다.
- TimeUs는 세션 기준점부터 흐른 상대 마이크로초입니다. 소요 시간은 End.TimeUs - Begin.TimeUs로 계산합니다.
- 이벤트 ID는 작업마다 유일하며, 같은 작업의 Begin과 End가 동일한 ID를 공유합니다.
- Track ID는 이벤트 ID와 별개의 식별 공간입니다. Track은 스레드, 작업 큐, 장치 등을 나타내는 논리적인 행입니다.
- ParentId는 상위 이벤트의 ID입니다. 실행 순서나 의존성을 뜻하지 않으며, 다른 Track의 부모도 허용합니다.
- 부모와 자식 사이에 시간 포함 관계를 강제하지 않습니다.
- 세션 종료 레코드는 로거의 종료 절차를 나타냅니다. 모든 작업의 완료나 성공을 의미하지 않습니다.
- DroppedRecordCount는 수집 과정에서 집계한 누락 레코드 수이며, 누락 작업 수나 Reader의 오류 줄 수가 아닙니다.

[ 모델 사용 시 주의사항 ]
- required는 생성 시 값 지정을 요구하지만, 빈 문자열이나 시간 범위 등의 유효성을 보장하지 않습니다.
- 헤더 속성의 기본값은 생성 편의를 위한 것입니다. Reader는 JSON 필수 필드 누락을 별도로 검사해야 합니다.
- init은 생성 이후 속성 재할당을 제한하지만, 참조하는 객체 내부까지 불변으로 만들지는 않습니다.
- Args의 IReadOnlyDictionary는 원본 Dictionary의 변경을 막지 않습니다. 큐에 전달할 데이터의 복사는 Tracer에서 처리합니다.
- record의 동등성 비교는 각 멤버의 비교 규칙을 따릅니다. Dictionary의 내부 항목까지 자동으로 비교하지 않습니다.
- 포맷 상수를 선언하는 것만으로 입력값이 검증되지는 않습니다. 실제 검증은 별도 코드에서 수행합니다.
