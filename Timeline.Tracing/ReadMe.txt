
작성자 : RYLYNN
날짜 : 2026.10.31

[ 프로젝트 구조 ]
Timeline.Tracing/
├─ ITracer.cs
├─ TimelineTracer.cs
├─ TimelineTracerOptions.cs
├─ TraceSessionClock.cs
├─ TraceTrackHandle.cs
├─ TraceSpanHandle.cs
├─ TraceScope.cs
├─ TraceExtensions.cs
└─ TraceCommand.cs

[ 클래스 및 인터페이스 구현 내용 ]
====== 타입 ======              ================= 내용 =================
├─ ITracer                      Track 생성, Begin 및 End의 기록용 공통 계약
├─ TimelineTracer               세션 생성, ID 발급, 핸들 검사, 큐 접수, 백그라운드 저장, 명시적 Flush 및 종료
├─ TimelineTracerOptions        큐 용량 및 자동 Flush 주기 설정
├─ TraceSessionClock            UTC 기준 시각과 단조 시계 기준값 보관, 상대 마이크로초 계산
├─ TraceTrackHandle             세션과 Track ID를 담는 값 타입. 기본값은 유효하지 않은 핸들
├─ TraceSpanHandle              세션과 작업 ID를 담는 값 타입. Begin 미접수 시 기본값 반환
├─ TraceScope                   using 범위 종료 시 End 요청, 반복·동시 Dispose의 중복 종료 방지
├─ TraceExtensions              ITracer.BeginScope 확장 메서드, Parent·Args 전달 및 Begin 핸들을 Scope에 연결
└─ TraceCommand                 큐 요청의 공통 타입. WriteTraceCommand와 FlushTraceCommand 포함

[ 기록 흐름 ]
CreateAsync → 옵션 검사 → Writer 생성 → 시계 초기화 → Header 기록 → 소비자 시작
CreateTrack / Begin / End → 상태·인수 검사 → 레코드 생성 → 제한된 Channel에 접수
소비자 하나 → 요청 순서대로 ITraceWriter 호출
DisposeAsync → 접수 중단 → 큐 입력 닫기 → 소비자 완료 대기 → SessionEnd 기록 → Writer 정리

[ 역할 구분 ]
- Timeline.Format의 레코드를 생성하고 Timeline.IO의 Writer로 저장합니다.
- JSON 필드 매핑, 직렬화 및 파일 입출력은 Timeline.IO에서 담당합니다.
- Begin/End 연결, 중복 End 및 Parent 관계 분석은 이후 TraceAssembler에서 담당합니다.
- 타임라인 배치와 표시 기능은 TimelineViewer에서 담당합니다.
- ITracer는 기록 기능만 제공합니다. 생성·Flush·종료는 소유자가 TimelineTracer를 통해 관리합니다.
- SessionId는 다른 Tracer의 핸들 사용을 검사하기 위한 내부 값이며 JSONL에는 기록하지 않습니다.

[ 큐 및 핸들 정책 ]
- 여러 호출자는 짧은 lock 안에서 상태 검사와 접수를 수행합니다. 파일 I/O와 비동기 대기는 lock 밖에서 수행합니다.
- Track은 접수 실패 시 예외를 전달하고 핸들을 반환하지 않습니다. 실패한 Track 등록은 누락 수에 포함하지 않습니다.
- Begin/End는 큐 포화 시 기다리지 않고 해당 레코드의 누락 수를 증가시킵니다.
- Begin 미접수 시 default 핸들을 반환합니다. End(default)는 기록과 누락 집계 없이 반환합니다.
- 유효하지 않은 부모 핸들은 부모 연결을 생략합니다. 다른 세션의 유효한 Track·작업 핸들은 거부합니다.
- 유효한 작업 핸들은 Begin 접수를 뜻하며 파일 저장 성공이나 End 기록을 보장하지 않습니다.
- 직접 End를 호출할 때 중복 종료는 호출자가 방지해야 합니다. TraceScope는 자신의 반복·동시 Dispose에서 End를 한 번만 호출합니다.
- Scope는 Tracer보다 먼저 종료해야 합니다. End의 오류를 숨기지 않으므로 작업 예외와 정리 오류가 겹치는 경우에 유의합니다.

[ 명시적 FlushAsync ]
- Flush 요청을 기록 요청과 같은 큐에 넣고, 소비자가 Writer.FlushAsync를 완료할 때까지 기다립니다.
- 큐가 가득 차면 비동기로 자리를 기다리며, 자동 폐기하거나 누락으로 집계하지 않습니다.
- 보장 범위는 Flush 요청보다 먼저 큐에 접수된 기록입니다. 동시에 시작한 다른 호출의 접수 순서까지 보장하지 않습니다.
- 취소가 접수 전에 발생하면 요청을 넣지 않습니다. 접수 후에는 호출자의 대기만 취소하며 소비자는 계속 처리합니다.
- 접수된 Flush는 종료 시 큐와 함께 처리됩니다. 종료 시작 이후 또는 저장 실패 이후의 신규 요청은 거부합니다.
- 저장 실패 시 현재 및 큐에 남은 Flush 대기자에게 오류를 전달합니다.
- Flush는 세션을 종료하지 않으며 전원 장애에 대한 디스크 영속성을 보장하지 않습니다.

[ Args 전달 및 복사 ]
- Begin과 BeginScope의 마지막 선택 인수로 IReadOnlyDictionary<string, JsonElement>를 전달합니다.
- Begin은 lock 밖에서 Dictionary를 복사하고 각 JsonElement.Clone으로 원본 JsonDocument의 수명과 분리합니다.
- 호출이 반환된 뒤 원본 Dictionary 수정·삭제와 JsonDocument 해제가 가능합니다. 복사 중에는 원본을 수정·해제하지 않아야 합니다.
- null Args는 생략하고 빈 Dictionary는 빈 객체로 기록합니다. 정상 JSON null 값도 허용합니다.
- default(JsonElement)의 Undefined는 호출 시점에 ArgumentException으로 거부합니다.
- CopyArgs는 Sandbox에서 복사 계약을 직접 검증할 수 있도록 internal로 제공하며 공개 API가 아닙니다.

[ 종료 및 오류 처리 ]
- 소비자 하나가 큐 요청과 PeriodicTimer를 함께 기다리며, 큐가 비어 있어도 주기적으로 Flush합니다.
- FlushInterval은 1ms 이상, uint.MaxValue-1 밀리초 이하입니다. 정확한 실행 시각이나 영속성은 보장하지 않습니다.
- 타이머는 소비자의 finally에서 해제합니다. 종료 시 대기 중인 큐·타이머 신호를 완료하고 소비자를 끝냅니다.
- 종료 시각은 신규 접수를 중단하는 시점의 상대 시간입니다. 파일 저장 완료 시각이 아닙니다.
- 동시·반복 DisposeAsync는 하나의 종료 Task를 공유합니다. 실패 결과도 동일하게 전달합니다.
- 소비자가 실패하면 신규 접수를 차단하고, 정상 종료 레코드 없이 Writer 정리를 시도합니다.
- 초기화 또는 종료 실패와 자원 정리 실패가 함께 발생하면 AggregateException으로 두 오류를 보존합니다.

[ 검증 및 남은 작업 ]
- Timeline.Sandbox의 TracerChecks에서 기본 세션, 부모·자식 작업, 동시 기록, 명시적 Flush 및 종료 경합을 검증합니다.
- TracerAutoFlushChecks에서 추가 이벤트 없는 파일 반영, 긴 타이머 대기 중 종료 및 타이머 해제를 검증합니다.
- 공개 생성은 TimeProvider.System을 사용합니다. Sandbox만 접근할 수 있는 내부 생성 경로에서 추적용 TimeProvider를 주입합니다.
- TraceScopeChecks에서 정상·예외·부모/자식·await 범위와 반복·동시 Dispose를 실제 파일로 검증합니다.
- Begin 미접수 핸들 전달은 테스트용 ITracer로 검증하며, 실제 큐 포화 검증과는 구분합니다.
- TraceArgsChecks에서 복사본 자체와 최종 JSONL을 각각 검증합니다. 원본 변경·문서 해제, Scope 전달과 Undefined 거부를 확인합니다.
- TracerFailureChecks에서 용량 1 큐를 제어해 Begin/End 누락 수, Track 거부 및 Flush 접수 대기 취소를 검증합니다.
- 제어 가능한 Writer를 주입해 쓰기·명시적 Flush 실패의 대기자 오류 전달, 정상 종료 생략, 자원 해제와 초기화·정리 이중 실패를 검증합니다.
- Writer 주입 경로는 internal이며 소유권을 Tracer에 넘깁니다. 공개 파일 경로 생성 API는 그대로 유지합니다.
- 다른 세션의 Track·Parent·End와 기본 Track 거부도 검증합니다. 자동 Flush 자체의 I/O 실패 주입과 장시간 부하 검증은 별도 확장 항목입니다.
