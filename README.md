# 투신도시 GIRLS GIFT RPG (3DS) 한국어 패치

일본판 「闘神都市」(닌텐도 3DS, 타이틀 ID `0004000000112000`)의 비공식 한국어 패치입니다.

게임 데이터는 들어 있지 않고, 원본과의 차이(xdelta)만 들어 있습니다. 설치 프로그램이 가지고 계신 원본에서 한국어 파일을 만들어 모드 폴더(LayeredFS)에 넣습니다. 롬 파일 자체는 고치지 않습니다.

**받기:** [Releases](../../releases/latest)에서 `ToushinToshi3DS_Korean_Patch_v1.0.1.zip`

## 한국어로 바뀌는 것

- 대사·서술·선택지
- 메뉴·아이템·스킬·전투·상점·카드·걸즈 기프트 등 화면 글자
- 글자가 그려진 그림(단추·제목·설명 그림)
- 오프닝 영상의 노래 가사와 제작진 자막
- 실행 파일 속 글(`code.ips`), 주인공 이름 한글 입력(완성형 한글 2,350자)

음성은 일본어 그대로입니다.

## 준비물: 원본 (둘 중 하나)

- 복호화된 롬 파일: `.3ds` / `.cci` / `.cxi` / 복호화된 `.cia`
- romfs 폴더: Azahar 게임 목록에서 투신도시를 오른쪽 클릭 → **Dump RomFS** 로 뽑은 폴더 등

암호화된 롬은 읽을 수 없습니다. 자기 3DS에서 복호화해 뽑은 롬을 쓰세요. Azahar도 암호화된 롬은 켜지 않습니다(암호화된 CIA는 설치 단계에서 거부됩니다). 이것은 패치와 상관없는 Azahar 정책입니다.

## 설치

1. 압축을 모두 풉니다(설치 프로그램 옆의 `patch`·`tools` 폴더가 있어야 합니다).
2. `투신도시3DS_한국어패치_설치.exe`를 실행합니다. 서명 없는 프로그램이라 「Windows의 PC 보호」 창이 뜨면 **추가 정보 → 실행**을 누르세요.
3. **1. 원본**에서 롬 파일이나 romfs 폴더를 고릅니다.
4. **2. 설치할 곳**을 고르고 **설치**를 누릅니다.
   - **Azahar**: 사용자 폴더(보통 `%APPDATA%\Azahar`)를 저절로 찾습니다. 포터블판은 「직접 고르기」로 `azahar.exe`가 있는 폴더를 고르세요. 결과: `...\load\mods\0004000000112000\romfs\` 와 `exefs\code.ips`
   - **3DS SD 카드**: 결과는 `SD:\luma\titles\0004000000112000\romfs\` 와 `code.ips`. 넣은 뒤 SELECT를 누른 채 3DS를 켜서 Luma 설정의 **Enable game patching**을 켜고 저장하세요.
   - **폴더로 내보내기**: 고른 폴더에 `0004000000112000\romfs`를 만듭니다.
5. 이미 투신도시 모드 폴더가 있으면 지우지 않고 `0004000000112000.backup-날짜`로 이름을 바꿔 둡니다.
6. 게임을 켜서 타이틀 로고가 한글 「투신도시」로 나오면 적용된 것입니다.

**지우기:** 설치 프로그램에서 같은 「설치할 곳」을 고르고 **제거**를 누르면 패치 폴더를 휴지통으로 옮기고, 백업이 있으면 되돌립니다.

## 알아 둘 점

- 3DS 실기에서 이름을 한글로 치려면 한글 자판이 있는 시스템 키보드가 있어야 합니다.
- 오프닝 노래는 일본어 가사가 공개되지 않아, 한국어 가사 자막은 노래에 맞춰 새로 지어 넣었습니다.
- 원본 파일이 패치가 기대한 것과 달라도 설치는 막지 않고 기록 창에 경고만 남깁니다. 이 경우 그 파일은 제대로 바뀌지 않았을 수 있습니다.
- 다른 투신도시 모드나 패치와 함께 쓰지 마세요.

## 설치 프로그램 없이 직접 적용하려면

`patch` 폴더의 `.xdelta`는 xdelta3로 만든 파일 245개입니다(romfs 안 경로 + `.xdelta`). `manifest.tsv`의 gzip 열이 1인 파일은 원본을 gzip으로 풀고 적용한 뒤 다시 gzip으로 묶어야 합니다.

```
xdelta3 -d -s 원본(푼 것) patch\sys_scene.bin.xdelta 결과
```

`patch\code.ips`는 원본 없이 그대로 씁니다. Azahar는 `0004000000112000\exefs\code.ips`, Luma는 `0004000000112000\code.ips`에 둡니다.

## 소스

설치 프로그램 소스는 [`installer/Installer.cs`](installer/Installer.cs)입니다. 윈도에 기본으로 있는 .NET Framework 4의 `csc.exe`로 빌드합니다.

```
csc /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.VisualBasic.dll Installer.cs
```

함께 배포하는 `xdelta3.exe`는 [xdelta3 3.2.0](https://github.com/jmacd/xdelta)(Apache License 2.0)입니다.
