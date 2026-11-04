# Viewer rendering checks

Windows / .NET 9 WPF용 패키지 의존성 없는 실행형 검증입니다.

```powershell
dotnet build tests/Timeline.Viewer.Checks/Timeline.Viewer.Checks.csproj -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/Timeline.Viewer.Checks --no-build
```

마지막 명령에 `-- docs/verification`을 추가하면 Black/White 화면의 PNG를 저장합니다.
숨겨진 Window 자체가 아니라 분리한 콘텐츠 트리를 측정·배치·렌더링합니다.
따라서 네이티브 창 테두리, 실제 모니터 DPI 전환, 마우스 조작은 이 검증의 범위가 아닙니다.

겹침/인접 구간/빈 Track, 종료 누락, 0길이 경계 표식, 좌표 변환,
Track 색상, 900/1280/1800 DIP 배치, 테마별 렌더링,
Span 추가·삭제 및 Track 초기화 후 다시 그리기를 확인합니다.
