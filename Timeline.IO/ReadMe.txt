
작성자 : RYLYNN
날짜 : 2026.10.30

[ 프로젝트 구조 ]
Timeline.IO/
├─ ITraceWriter.cs
├─ TraceJsonSerializer.cs
├─ JsonlTraceWriter.cs
├─ TraceRecordValidator.cs
├─ TraceReadOptions.cs
├─ TraceReadDiagnostic.cs
├─ TraceReadResult.cs
├─ BoundedLineReader.cs
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
├─ BoundedLineReader            길이 제한을 지키며 줄 읽기, 초과 줄 건너뛰기 및 CR/LF/CRLF 처리
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

[ 입력 규약 및 오류 정책 ]
- UTF-8 BOM 없는 파일을 사용합니다. BOM이 있는 입력은 첫 줄의 InvalidHeader로 거부합니다.
- 잘못된 UTF-8 바이트, 파일 I/O 실패 및 취소는 진단으로 변환하지 않고 예외로 전달합니다.
- JSON 속성 이름은 대소문자를 구분하며, type을 포함한 필드 순서는 자유입니다.
- 같은 객체 안의 중복 JSON 필드는 거부합니다. Args와 추가 필드의 중첩 객체·배열 내부도 검사합니다.
- 알 수 없는 추가 필드는 허용하지만 모델에 보존하지 않습니다. 추가 데이터 보존에는 Args를 사용합니다.
- Strict는 첫 오류 진단을 전달한 뒤 종료합니다. Recovery는 오류 줄을 제외하고 계속 읽습니다.
- 헤더 누락·잘못된 헤더는 모드와 무관하게 중단합니다. 중복 헤더는 모드별 오류 정책을 따릅니다.
- SessionEnd 이후 모든 추가 줄은 RecordAfterSessionEnd입니다. 종료 기록 없는 EOF는 MissingSessionEnd입니다.
- 최대 줄 길이는 줄바꿈을 제외한 UTF-16 코드 단위 수입니다. 초과 줄은 나머지를 버리고 다음 줄로 이동합니다.
- 마지막 줄바꿈 없는 파일도 읽습니다. EOF 진단은 마지막 줄 번호를 사용하므로 한 줄에 결과가 여럿 대응할 수 있습니다.

[ Writer 자원 관리 ]
- 경로 생성자는 CreateNew를 사용하여 기존 파일을 덮어쓰지 않습니다.
- Stream 생성자는 테스트 및 다른 저장 대상에 사용합니다. 기본적으로 스트림을 소유하며 leaveOpen=true이면 호출자가 정리합니다.
- 쓰기·Flush 시작 전 취소와 입력 검증 실패는 Writer를 실패 상태로 바꾸지 않습니다.
- 실제 쓰기·Flush 도중 오류 또는 취소가 발생하면 후속 쓰기·Flush를 거부합니다. DisposeAsync는 반드시 수행합니다.
- DisposeAsync도 버퍼 처리 중 오류를 전달할 수 있습니다. 실패한 로그에 정상 종료나 데이터 보존을 보장하지 않습니다.

[ 검증 ]
- Timeline.Sandbox의 TEST 1~18에서 정상 입출력, 레코드 검증, 헤더·세션 종료 정책을 실행합니다.
- ReaderBoundaryChecks에서 줄 길이·줄바꿈·오류 복구·취소·파일 해제를 검증합니다.
- IoPolicyChecks에서 중복 필드·BOM·추가 필드 및 테스트 스트림을 통한 Writer 쓰기·Flush 실패와 취소를 검증합니다.
