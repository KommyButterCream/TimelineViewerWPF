
작성자 : RYLYNN
날짜 : 2026.10.31

[ 프로젝트 구조 ]
Timeline.Sandbox/
├─ Program.cs                  실행 진입점: SandboxChecks에 인수 전달
├─ SandboxChecks.cs            시나리오 목록, 메뉴, 선택 실행, 성공·실패 집계
├─ SampleTraceData.cs          독립 실행용 공통 예제 레코드와 입력 파일 생성
├─ SerializerChecks.cs         TEST 1, 11~13: 직렬화, 필드 순서, 필수 필드, 오류 구분
├─ ValidatorChecks.cs          TEST 2~5: 레코드 값 검증
├─ WriterChecks.cs             TEST 6~10: 저장과 기록 순서·종료 상태 검증
├─ ReaderChecks.cs             TEST 14~16: 정상 읽기, 헤더 및 세션 종료 정책
├─ ReaderBoundaryChecks.cs     TEST 17: 줄 길이·줄바꿈·복구·취소·자원 해제
├─ IoPolicyChecks.cs           TEST 18: 입력 규약 및 Writer 실패 처리
├─ TracerChecks.cs             TEST 19~21: 기본 기록, 명시적 Flush, 종료 경합
├─ TracerAutoFlushChecks.cs    TEST 22: 추가 이벤트 없는 자동 Flush 및 타이머 해제
├─ TraceScopeChecks.cs         TEST 23: Scope의 정상·예외·중첩·반복·동시 종료
├─ TraceArgsChecks.cs          TEST 24: Args 복사, 원본 변경·해제와 Scope 전달
└─ TracerFailureChecks.cs      TEST 25: 큐 포화 누락 집계, 저장 실패·대기자·핸들 검사

[ 실행 방법 ]
- 인수 없이 실행하면 목록과 입력 메뉴를 표시합니다. Enter만 입력하면 테스트 없이 종료합니다.
- 이름 또는 번호로 실행합니다. 예: tracing-flush 또는 20
- 여러 개는 공백·쉼표·세미콜론으로 구분합니다. 예: read tracing-flush 또는 14,20
- io: IO 관련 전체 / tracing: Tracing 관련 전체 / all: 전체 시나리오
- --list 또는 --help: 실행 없이 목록 표시
- 중복 선택은 한 번만 실행하며, 실행 순서는 등록된 번호 순서입니다.
- 선택한 이름이 하나라도 잘못되면 테스트를 실행하지 않고 오류를 표시합니다.

[ 터미널 예시 (솔루션 폴더 기준) ]
dotnet run --project Timeline.Sandbox -- tracing-flush
dotnet run --project Timeline.Sandbox -- tracing-auto-flush
dotnet run --project Timeline.Sandbox -- tracing-scope
dotnet run --project Timeline.Sandbox -- tracing-args
dotnet run --project Timeline.Sandbox -- tracing-failures
dotnet run --project Timeline.Sandbox -- read
dotnet run --project Timeline.Sandbox -- 14,20
dotnet run --project Timeline.Sandbox -- tracing
dotnet run --project Timeline.Sandbox -- all
dotnet run --project Timeline.Sandbox -- --list

[ Visual Studio ]
- Timeline.Sandbox를 시작 프로젝트로 설정하고 F5 또는 Ctrl+F5로 실행한 뒤 메뉴에서 선택합니다.
- 매번 같은 항목을 디버깅하려면 프로젝트 속성의 디버그 실행 프로필에서 명령줄 인수에 이름을 지정합니다.
- 예외 검증도 선택한 경우, 발생 시 중단 설정에 따라 처리 예정인 예외에도 디버거가 멈출 수 있습니다.
- 이번 구조 변경은 Visual Studio의 예외 설정을 변경하지 않습니다.

[ 결과 ]
- 로그 파일은 실행 파일 폴더 아래 logs에 생성됩니다.
- 각 시나리오는 다른 시나리오를 먼저 실행하지 않아도 동작하도록 입력을 준비합니다.
- 실패한 시나리오는 FAIL과 예외를 표시하고, 나머지 선택 시나리오는 계속 실행합니다.
- 종료 코드: 0=선택 항목 모두 성공 또는 메뉴 종료, 1=테스트 실패, 2=잘못된 선택
- 기존 TEST 19 묶음은 기본 기록(19), 명시적 Flush(20), 종료 경합(21)으로 나눴습니다.
- TEST 22는 실제 파일의 유휴 자동 Flush를 관찰하고, 추적용 TimeProvider로 타이머 생성·해제를 확인합니다.
- 1시간 주기의 다음 tick을 기다리지 않고 종료되는지도 제한 시간 내에 확인합니다.
