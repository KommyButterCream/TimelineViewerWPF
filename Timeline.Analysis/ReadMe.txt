
작성자 : RYLYNN
날짜 : 2026.11.01

[ 프로젝트 구조 ]
Timeline.Analysis/
├─ TraceSpanStatus.cs
├─ TraceSpan.cs
├─ TraceTrack.cs
├─ TraceAnalysisDiagnostic.cs
├─ TraceAnalysisResult.cs
└─ TraceAssembler.cs

[ 클래스 및 타입 구현 내용 ]
====== 타입 ======              ================= 내용 =================
├─ TraceSpanStatus              Completed, MissingEnd, InvalidEnd로 구간의 종료 상태 표현
├─ TraceSpan                    연결된 작업 구간, 시간, 상태, 부모 참조, Args 및 원본 줄 번호
├─ TraceTrack                   Track 정의와 소속 구간 목록, 임시 Track 여부 및 정의 줄 번호
├─ TraceAnalysisDiagnostic      분석 문제의 코드·설명·원본/관련 줄 번호·작업/Track ID
├─ TraceAnalysisResult          Header, 선택적 SessionEnd, 전체 Track·구간·진단 목록
└─ TraceAssembler               입력 보관, 중복 처리, 구간 연결, 부모 검사, Track 구성 및 결과 생성

[ 분석 흐름 ]
Add(record, lineNumber)
    → 레코드와 원본 줄 번호 보관, Args 복사
Build()
    → CollectRecords: 종류별 분류 및 중복 진단
    → BuildSpans: Begin/End 연결과 누락·잘못된 시간 진단
    → ResolveParentCandidates: 부모 존재 여부 및 자기 참조 검사
    → RemoveParentCycles: 부모 순환 검사와 연결 해제
    → 전체 구간 정렬
    → BuildTracks: 정의된 Track 및 임시 Track 구성
    → 진단 정렬, 읽기 전용 결과 목록 반환

[ 역할 구분 ]
- Timeline.Format만 참조합니다. Timeline.IO, Timeline.Tracing 및 WPF에 의존하지 않습니다.
- JSON 파싱·직렬화, 파일 I/O, 필수 필드 및 레코드 값 검증은 Timeline.IO 등의 입력 계층에서 담당합니다.
- Analysis는 이미 값 검증을 통과한 레코드의 구간과 참조 관계를 분석합니다. 개별 레코드 값을 전부 다시 검증하지 않습니다.
- 헤더/세션 종료의 파일 내 위치, 종료 기록 누락 같은 파일 구조 진단은 Reader의 책임입니다.
- 작업의 화면 좌표, 겹치는 구간 배치, 실제 시각 표시, 검색·선택 기능은 Viewer의 책임입니다.
- Reader 진단과 분석 진단은 서로 다른 결과입니다. Reader 결과 중 Record가 있는 항목만 Add에 전달합니다.

[ 입력 및 수명 계약 ]
- Add는 null 레코드와 1 미만의 줄 번호를 거부합니다. 줄 번호는 실제 원본 위치를 전달합니다.
- 중복의 첫 번째 기준은 줄 번호나 시간 정렬이 아니라 Add 호출 순서입니다.
- End·Track·Parent가 뒤늦게 등장해도 전체 입력을 받은 Build에서 연결합니다.
- Header는 정확히 하나 필수입니다. 없거나 중복이면 Build에서 InvalidOperationException을 발생시킵니다.
- SessionEnd는 없거나 하나입니다. 중복이면 Build에서 InvalidOperationException을 발생시킵니다.
- 지원하지 않는 레코드 타입은 Build에서 ArgumentException으로 거부합니다.
- Args는 Dictionary와 각 JsonElement.Clone으로 원본 변경 및 JSON 문서 수명에서 분리합니다.
- Add 실행 중 원본을 변경·해제하지 않아야 합니다. Add가 반환된 뒤에는 원본을 변경·해제할 수 있습니다.
- Args가 없는 레코드는 현재 불변 속성 구성을 전제로 같은 객체 참조를 보관합니다.
- 같은 Assembler에서 Build를 반복할 수 있으며, 이전 결과와 진단은 다음 Build에 의해 변경되거나 누적되지 않습니다.
- 한 호출자가 순차적으로 사용합니다. Add와 Build의 동시 호출은 지원하지 않습니다.

[ 구간 및 중복 정책 ]
- 중복 Track·Begin·End는 첫 레코드를 유지하고 이후 레코드마다 진단을 남깁니다.
- Track ID와 작업 ID는 별도 식별 공간입니다. 모든 ID는 대소문자를 구분합니다.
- 정상 Begin/End는 Completed이며 종료 시각이 시작 시각과 같은 0 길이 구간도 허용합니다.
- Completed는 시간상 유효한 종료를 뜻하며 작업 성공 여부를 뜻하지 않습니다.
- End가 없는 Begin은 MissingEnd입니다. 종료 시각·종료 줄 번호·소요 시간은 null입니다.
- 종료 시각이 시작보다 빠르면 InvalidEnd입니다. 유효한 종료 시각·소요 시간은 null이지만 End의 원본 줄 번호는 유지합니다.
- InvalidEndTime 진단에는 잘못된 시각과 Begin/End 줄 번호를 남깁니다. MissingEnd를 중복 진단하지 않습니다.
- 첫 End가 잘못된 시간이어도 이후 중복 End로 자동 교체하지 않습니다.
- Begin 없는 End는 MissingBegin 진단만 남기며 구간을 추측해 만들지 않습니다.

[ 부모 관계 정책 ]
- OriginalParentId는 원본 참조, ParentId는 분석 후 유효한 참조입니다.
- 없는 부모 또는 자기 참조는 진단 후 ParentId를 null로 유지합니다. 원본 ID는 보존합니다.
- 순환은 재귀 없이 경로와 처리 완료 집합으로 검사합니다. 순환 구성원 각각의 부모 연결을 해제합니다.
- A → B → C → D → B인 경우 B/C/D의 연결만 해제하고 A → B는 유지합니다.
- 다른 Track의 부모와 부모 시간 범위 밖의 자식을 허용합니다. 부모의 종료 상태도 연결을 제한하지 않습니다.

[ Track 및 결과 목록 ]
- 정의된 Track은 작업이 없어도 유지합니다. 정의 줄 번호순, 동률이면 ID순으로 반환합니다.
- 정의가 없는 Track은 참조 ID당 하나의 임시 Track을 만듭니다. 표시 이름은 ID이고 정의 줄 번호는 null입니다.
- MissingTrack 진단은 참조한 각 작업의 Begin 위치에 남깁니다. 임시 Track은 정상 Track 뒤에 ID순으로 배치합니다.
- 전체 구간은 시작 시각 → Begin 줄 번호 → ID순으로 정렬합니다.
- Track별 구간은 시작 시각 → Begin 줄 번호순으로 정렬합니다. 동률이면 전체 구간의 ID순이 유지됩니다.
- 진단은 줄 번호 → 진단 코드 → 작업 ID → Track ID순으로 정렬합니다.
- 결과 목록은 읽기 전용이며 Track별 목록과 전체 목록은 같은 최종 TraceSpan 객체를 참조합니다.
- 공개 결과 모델은 init 속성으로 외부에서도 생성할 수 있습니다. 위 일관성은 Assembler가 생성한 결과에 대한 계약입니다.

[ 사용 예시 ]
var assembler = new TraceAssembler();
assembler.Add(headerRecord, 1);
assembler.Add(trackRecord, 2);
assembler.Add(beginRecord, 3);
assembler.Add(endRecord, 4);
TraceAnalysisResult result = assembler.Build();

[ 검증 및 범위 ]
- Timeline.Sandbox에서 analysis를 선택하면 TEST 26~32를 실행합니다.
- 정상·역순 연결, 중복 첫 입력 유지, 미완료·잘못된 시간, 원본 줄 번호를 검증합니다.
- 부모 누락·자기 참조·순환 진입 경로·서로 독립적인 순환·공통 부모·1만 단계 계층을 검증합니다.
- 정상/빈/임시 Track, 대소문자 구분, Args 수명, 결과 목록 변경 거부 및 반복 Build를 검증합니다.
- JSONL 저장 → Reader → Assembler 연결도 검증합니다.
- 현재 Build는 전체 입력을 메모리에 보관하고 동기적으로 분석합니다. 취소·진행률 및 대규모 로그 성능 개선은 후속 확장 범위입니다.
