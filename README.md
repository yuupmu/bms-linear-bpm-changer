# BMS Linear BPM Changer

기존 BMS 파일에 선형 변속을 근사한 확장 BPM 이벤트(`#BPMxx`, `#xxx08`)를 삽입하는 Windows 데스크톱 프로그램입니다.

## 현재 데모에 포함된 기능

- BMS/BME/BML/PMS 파일 드래그 앤 드롭
- 여러 개의 `시작 마디 / 끝 마디 / 시작 BPM / 끝 BPM / 배치 간격` 구간
- 각 구간마다 4분음표당 또는 16분음표당 근사 선택
- 시간 등가 평균(기본값) 또는 단순 산술평균
- BPM 소수점 0~6자리 반올림
- 기존/신규/전체 확장 BPM ID 사용량과 선택 구간의 4분/16분 예상량
- 변환 구간 안의 기존 `#xxx03`, `#xxx08` BPM 이벤트 충돌 검사
- BPM 표, 그래프, 구간별 시간과 누적 오차 미리보기
- 원본을 유지하고 같은 폴더에 `파일명_linear_bpm.bms` 출력
- UTF-8(BOM 포함), Shift-JIS, EUC-KR/CP949 및 CRLF/LF/CR 줄바꿈 보존

파일 전체를 다른 인코딩으로 다시 쓰지 않습니다. 원본 바이트는 유지하고 ASCII로 표현되는 BPM 정의와 채널만 삽입하므로, 제목·아티스트·파일명에 들어 있는 일본어와 한국어 바이트가 그대로 남습니다.

## Windows에서 EXE 만들기

빌드에만 **.NET 10 SDK**가 필요합니다. 완성된 EXE는 .NET 설치 없이 실행되는 독립 실행형입니다.

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)를 설치합니다.
2. 저장소 루트의 `build-win-x64.bat`를 실행합니다.
3. `dist\win-x64\BmsLinearBpmChanger.exe`가 생성됩니다.

또는 명령 프롬프트에서 다음 명령을 실행할 수 있습니다.

```bat
dotnet publish src\BmsLinearBpmChanger.WinForms\BmsLinearBpmChanger.WinForms.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -o dist\win-x64
```

첫 빌드에서는 Microsoft 런타임 파일을 내려받기 위한 인터넷 연결이 필요할 수 있습니다.

## 사용법

1. BMS 파일을 창에 놓습니다.
2. 변속 구간을 입력합니다. 끝 마디는 목표 BPM에 도달하는 경계이며 구간은 `[시작 마디, 끝 마디)`입니다.
3. 각 구간의 배치 간격을 4분음표당 또는 16분음표당으로 선택하고 공통 평균 방식을 지정합니다.
4. 충돌 검사와 누적 오차를 확인합니다.
5. **변환 파일 저장**을 누르면 원본 옆에 `_linear_bpm.bms` 파일이 만들어집니다.

`samples\demo_4_4.bms`는 `#xxx02`를 생략한 기본 4/4박, `samples\demo_3_4.bms`는 041~048마디를 `#xxx02:0.75`로 지정한 3/4박 확인용 파일입니다. 기본 입력값 `041 → 049, 120 → 180`으로 4분음표와 16분음표 배치를 비교할 수 있습니다.

변속 진행률은 선택 구간의 누적 4분음표 길이를 기준으로 계산합니다. 따라서 3/4와 5/4처럼 길이가 다른 마디가 한 구간에 섞여도 BPM 변화량이 실제 음악 길이에 비례합니다.

## 시간 등가 평균

BPM이 `a`에서 `b`까지 선형으로 변하는 한 구간의 시간 등가 BPM은 로그 평균입니다.

```text
equivalent BPM = (b - a) / ln(b / a)
```

반올림하지 않은 시간 등가 BPM은 그 구간의 실제 통과시간을 정확히 보존합니다. 프로그램의 누적 오차는 선택한 소수점 자리로 반올림하면서 생기는 차이를 보여줍니다.

## 안전 동작과 현재 제한

- 기존 BPM 이벤트를 임의로 덮어쓰지 않습니다. 선택 구간에 기존 BPM 이벤트가 있으면 저장 버튼을 비활성화합니다.
- 확장 BPM ID는 `01`~`ZZ`의 1,295개가 한계입니다. 현재 구성과 선택 구간의 4분/16분 예상 사용량을 미리보기에 표시합니다.
- 인접한 구간은 지원합니다. 앞 구간의 끝 BPM과 다음 구간의 시작 BPM이 다르면 즉시 전환 경고를 표시합니다.
- `#RANDOM`/`#IF` 분기의 의미를 따로 실행하지 않고 파일에 적힌 BPM 이벤트를 모두 검사합니다.
- 인코딩 이름 감지는 휴리스틱일 수 있지만, 출력 보존은 감지 결과와 관계없이 원본 바이트 기반으로 수행합니다.

## 테스트

Windows에서는 `run-core-tests.bat`를 실행합니다. 계산 코어 테스트는 다음을 확인합니다.

- 시간 등가 평균과 산술평균
- 3/4박의 4분음표 이벤트 배치
- 4/4박의 4분음표/16분음표 이벤트 배치
- 구간별 근사 단위 혼합과 변박 구간 보간
- 기존 정의 재사용을 포함한 BPM ID 사용량 예상
- 기존 BPM 이벤트 충돌
- CP949, Shift-JIS, UTF-8 BOM 바이트 보존
- CRLF 보존
- 인접 구간과 출력 파일명

GitHub Actions도 `main` 브랜치의 코어 테스트, Windows 컴파일과 단일 EXE 생성을 자동으로 확인합니다. Actions 실행 결과의 `BmsLinearBpmChanger-win-x64` artifact에서도 최신 EXE를 받을 수 있습니다.

## 라이선스

MIT
