
작성자 : RYLYNN
날짜 : 2026.10.30

[ 프로젝트 구조 (구현 예정) ]
Timeline.IO/
├─ ITraceWriter.cs
├─ TraceJsonSerializer.cs
├─ JsonlTraceWriter.cs
├─ TraceRecordValidator.cs
├─ TraceReadOptions.cs
├─ TraceReadDiagnostic.cs
├─ TraceReadResult.cs
└─ JsonlTraceReader.cs

[ 클래스 및 인터페이스 구현 내용 ]
====== 타입 ======              ================= 내용 =================
├─ ITraceWriter                 레코드 쓰기, Flush, 비동기 자원 해제의 공통 계약
├─ TraceJsonSerializer          레코드 종류(type), JSON 필드명, UTC 표현, null 생략 및 직렬화·역직렬화 처리
├─ JsonlTraceWriter             헤더 우선 기록, UTF-8(BOM 없음) JSONL 저장, 순차 비동기 쓰기 및 Flush
├─ TraceRecordValidator         필수 필드, 빈 ID, 시간 범위, 포맷 버전 및 UTC 기준 시각 검증
├─ TraceReadOptions             엄격·복구 모드 및 최대 줄 길이 등 읽기 설정
├─ TraceReadDiagnostic          오류가 발생한 줄 번호, 진단 종류 및 원인
├─ TraceReadResult              한 줄의 읽기 결과: 유효한 레코드 또는 진단 정보
└─ JsonlTraceReader             비동기 순차 읽기, 헤더 검사, 레코드 검증, 오류 처리 및 취소 지원

[ 구현 순서 ]
1. ITraceWriter : Writer가 제공할 기능과 호출 규칙 정의
2. TraceJsonSerializer : Timeline.Format 모델과 JSONL 필드 간 변환 정의
3. TraceRecordValidator : Writer와 Reader에서 사용할 레코드 단위 검증 구현
4. JsonlTraceWriter : 레코드를 한 줄씩 저장하고 예제 파일로 검증
5. TraceReadOptions, TraceReadDiagnostic, TraceReadResult : Reader의 설정과 결과 정의
6. JsonlTraceReader : 정상 파일 및 잘못된 로그의 읽기 동작 구현

[ 역할 구분 ]
- Timeline.Format을 참조하여 공통 레코드와 포맷 상수를 사용합니다.
- JSON 필수 필드 누락은 역직렬화 시 확인합니다. 모델의 기본값으로 누락을 숨기지 않습니다.
- Writer는 한 호출자가 순차적으로 사용합니다. 병렬 호출을 직접 처리하는 큐는 포함하지 않습니다.
- originUtc와 originMono 생성, 이벤트 ID 발급, 백그라운드 큐는 Timeline.Tracing에서 담당합니다.
- Begin/End 연결, 중복 이벤트, Track·Parent 참조 및 순환 검사는 TraceAssembler에서 담당합니다.
- Flush는 Writer의 버퍼를 비우는 기능이며, Tracer 큐 처리 완료나 전원 장애 시 영속성을 보장하지 않습니다.
- Reader는 우선 완료된 파일을 대상으로 구현합니다. 기록 중인 파일을 따라 읽는 기능은 추후 확장합니다.
- 위 CS 파일들은 구현 계획이며, 현재 이 문서에서 구현 완료를 의미하지 않습니다.
