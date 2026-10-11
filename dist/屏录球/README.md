> **这是随程序一起分发的说明文件。**
> 源码、构建方式、自检命令与问题记录见仓库根目录的 `README.md`。
> 本目录里只有可执行文件与它们的依赖，`ffmpeg.exe` 需自备（见仓库 README 的「准备 ffmpeg」）。

---
# 灞忓綍鐞?路 Screen Recorder Floating Ball

> 涓€涓?Windows 妗岄潰褰曞睆灏忓伐鍏凤細妗岄潰涓婁竴涓崐閫忔槑**鎮诞鐞?*锛岀偣涓€涓嬪紑濮嬪綍鍒跺睆骞曪紙鍚郴缁熷０闊?/ 楹﹀厠椋庯級锛?> 鍐嶇偣涓€涓嬪仠姝㈠苟淇濆瓨涓烘爣鍑?**MP4锛圚.264 + AAC锛?*銆?> 鏃犻渶瀹夎锛岃嚜甯︾紪鐮佸櫒锛岀函 C# / WPF 缂栧啓锛屼笉渚濊禆浠讳綍 NuGet 鍖呫€?
[English summary](#english-summary) 路 [蹇€熷紑濮媇(#涓€蹇€熷紑濮? 路 [甯歌闂](#涓冨父瑙侀棶棰? 路 [瀹炵幇瑕佺偣](#涔濆疄鐜拌鐐圭粰鍚庣画缁存姢鑰?

![褰曞埗鏁堟灉棰勮](docs/images/棰勮.png)

*(涓婂浘鏄粠鏈伐鍏峰綍鍒剁殑瑙嗛閲屾娊鐨勪竴甯э紝鍙充笅瑙掕兘鐪嬪埌鎮诞鐞冩湰浣?*

---

## 鐗规€?
- **鎮诞鐞冧竴閿綍鍒?*锛氬崟鍑诲紑濮?鍋滄锛屽弻鍑绘墦寮€璁剧疆锛屽彸閿脊鍑哄畬鏁磋彍鍗曪紝鍙殢鎰忔嫋鍔ㄤ綅缃紙浼氳浣忥級
- **澹伴煶**锛氱郴缁熷０闊筹紙WASAPI 鍥炵幆锛? 楹﹀厠椋庯紝鍙垎鍒皟闊抽噺銆佸彲鍙敤鍏朵腑涓€涓€佷篃鍙互涓嶅綍
- **褰曞埗鍖哄煙**锛氫富鏄剧ず鍣?/ 鎵€鏈夋樉绀哄櫒锛堣櫄鎷熸闈㈡嫾鎺ワ級/ 鎸囧畾鏄剧ず鍣?/ **榧犳爣妗嗛€変换鎰忓尯鍩?*
- **杈撳嚭鍗?MP4**锛欻.264锛坙ibx264锛? AAC-LC 48 kHz 绔嬩綋澹帮紝甯?`+faststart`锛屽彲鐩存帴鎷栬繘娴忚鍣ㄣ€佸井淇°€佸壀杈戣蒋浠?- **闊崇敾鍚屾**锛氫互鍚屼竴鏉″崟璋冩椂閽熸帹杩涳紝鏆傚仠/缁х画銆佸崱椤块兘涓嶄細璁╁０闊崇敾闈㈤敊浣?- **鍏ㄥ眬蹇嵎閿?*锛歚Ctrl+Alt+R` 寮€濮?鍋滄锛宍Ctrl+Alt+P` 鏆傚仠/缁х画
- **鎵樼洏鍥炬爣** + 褰曞埗璁℃椂姘旀场 + 寮€濮?鍋滄鎻愮ず闊?- **鎶楀紓甯?*锛氫繚瀛樻祦绋嬪垎绾у洖閫€锛堟贩娴佸け璐ヤ篃浼氬瓨鍑虹敾闈級銆佸惎鍔ㄥ墠棰勬鐩綍涓庣紪鐮佸櫒銆佸仠姝㈠綍鍒朵笉鍗＄晫闈?
## 绯荤粺瑕佹眰

| 椤圭洰 | 瑕佹眰 |
| --- | --- |
| 鎿嶄綔绯荤粺 | Windows 10 1809锛坆uild 17763锛夋垨鏇撮珮 / Windows 11 |
| 杩愯鏃?| .NET 10 妗岄潰杩愯鏃讹紙浠呰繍琛屽彂甯冪増锛涜嚜琛岀紪璇戦渶瑕?.NET 10 SDK锛?|
| 缂栫爜鍣?| `ffmpeg.exe`锛?*鏈殢鏈粨搴撴彁渚?*锛岃涓嬫柟銆屽噯澶?ffmpeg銆嶏級 |
| 鏄惧崱 | 鏃犺姹傦紙鎶撳睆璧?GDI锛屼笉渚濊禆 GPU锛?|

---

## 涓€銆佸揩閫熷紑濮?
### 1. 鍑嗗 ffmpeg

鏈粨搴?*涓嶅寘鍚?ffmpeg.exe**锛堜綋绉?60+ MB锛屼笖鍒嗗彂闇€閬靛畧鍏惰鍙瘉锛夈€傝浠婚€変竴绉嶆柟寮忓噯澶囷細

```powershell
# 鏂瑰紡 A锛歸inget锛堟帹鑽愶級
winget install Gyan.FFmpeg

# 鏂瑰紡 B锛歴coop
scoop install ffmpeg

# 鏂瑰紡 C锛氭墜鍔ㄤ笅杞?https://www.gyan.dev/ffmpeg/builds/ 鐨勮В鍘嬪寘锛?#         鎶?bin\ffmpeg.exe 鏀惧埌涓嬮潰浠绘剰涓€涓綅缃細
#           <绋嬪簭鐩綍>\ffmpeg.exe
#           <绋嬪簭鐩綍>\ffmpeg\ffmpeg.exe
#           tools\ffmpeg\ffmpeg.exe      鈫?浠撳簱閲岄鐣欎簡杩欎釜鐩綍
```

绋嬪簭鍚姩鏃朵細渚濇鍦ㄨ繖浜涗綅缃互鍙婄郴缁?`PATH` 涓煡鎵?`ffmpeg.exe`锛屽苟鍦ㄦ棩蹇楅噷鍐欐槑鐢ㄧ殑鏄摢涓€涓€?**鎵句笉鍒版椂浼氱洿鎺ユ彁绀猴紝涓嶄細闈欓粯澶辫触銆?*

### 2. 杩愯

```powershell
# 鑷缂栬瘧锛堥渶瑕?.NET 10 SDK锛?git clone <浣犵殑浠撳簱鍦板潃>
cd ScreenRecorder

# 鎶?ffmpeg 鏀惧埌 tools\ffmpeg\ 涓嬶紙瑙佷笂锛夛紝鐒跺悗涓€閿墦鍖呭埌 dist\灞忓綍鐞僜
powershell -ExecutionPolicy Bypass -File tools\build.ps1
```

鎵撳寘浜х墿鍦?`dist\灞忓綍鐞僜`锛岀洿鎺ュ弻鍑?`灞忓綍鐞?exe` 鍗冲彲锛?*鏃犻渶瀹夎**銆?
> 鎵撳寘鑴氭湰浼氶『渚垮湪妗岄潰鍒涘缓蹇嵎鏂瑰紡銆傚鏋滄闈笉鍙啓锛屼篃鍙互鍦ㄧ▼搴忛噷鍙抽敭鎮诞鐞?鈫掋€屽垱寤烘闈㈠揩鎹锋柟寮忋€嶃€?
### 3. 鐢ㄦ硶

| 鎿嶄綔 | 鏁堟灉 |
| --- | --- |
| **鍗曞嚮**鎮诞鐞?| 寮€濮?/ 鍋滄褰曞埗锛堟殏鍋滅姸鎬佷笅涓虹户缁級 |
| **鍙屽嚮**鎮诞鐞?| 鎵撳紑璁剧疆 |
| **鍙抽敭**鎮诞鐞?| 寮€濮嬪綍鍒?/ 鏆傚仠缁х画 / 璁剧疆 / 鎵撳紑鏂囦欢澶?/ 鍒涘缓妗岄潰蹇嵎鏂瑰紡 / 閫€鍑?|
| **鎸変綇鎷栧姩** | 绉诲姩鎮诞鐞冧綅缃紙浼氳浣忥紝澶氭樉绀哄櫒涔熸敮鎸侊級 |
| `Ctrl + Alt + R` | 寮€濮?/ 鍋滄褰曞埗 |
| `Ctrl + Alt + P` | 鏆傚仠 / 缁х画 |

褰曞埗涓偓娴悆鍙樼孩骞舵湁鍛煎惛鍏夌幆锛屼笂鏂规樉绀?`00:12` 璁℃椂姘旀场锛涙殏鍋滄椂鍙橀粍骞舵樉绀烘殏鍋滅鍙枫€?
---

## 浜屻€佽缃」

鍙屽嚮鎮诞鐞冩墦寮€璁剧疆锛?
- **褰曞埗鍖哄煙**锛氫富鏄剧ず鍣?/ 鎵€鏈夋樉绀哄櫒 / 鎸囧畾鏄剧ず鍣?/ 鑷畾涔夊尯鍩燂紙鐐广€屾閫夊尯鍩熴€嶏紝灞忓箷鍙樻殫鍚庢嫋鎷芥閫夛級
- **澹伴煶**锛氱郴缁?楹﹀厠椋?/ 浠呯郴缁?/ 浠呴害鍏嬮 / 涓嶅綍闊筹紱涓よ矾闊抽噺 0鈥?50%锛涘彲鎸囧畾楹﹀厠椋庤澶?- **鐢昏川**锛氬抚鐜?15/20/24/25/30/45/60锛涙竻鏅板害 4 / 8 / 12 / 20 Mbps
- **淇濆瓨浣嶇疆**锛氶粯璁?`鎴戠殑瑙嗛\灞忓綍鐞僠锛屽彲鏀规垚浠绘剰鐩綍
- **鍏朵粬**锛氬綍瀹岃嚜鍔ㄦ墦寮€鏂囦欢澶癸紙骞堕€変腑璇ユ枃浠讹級銆佸紑濮?鍋滄鎻愮ず闊炽€佹墭鐩樺浘鏍囥€佹偓娴悆澶у皬

璁剧疆淇濆瓨鍦?`%LOCALAPPDATA%\ScreenRecorder\settings.json`锛堣鐩綍涓嶅彲鍐欐椂鑷姩閫€鍥炵▼搴忕洰褰曪級銆?鏃ュ織鍦ㄧ▼搴忕洰褰曚笅 `logs\app.log`銆?
---

## 涓夈€佽緭鍑烘枃浠?
- 鏂囦欢鍚嶏細`褰曞睆_骞存湀鏃鏃跺垎绉抇姣.mp4`
- 缂栫爜锛氳棰?**H.264 (libx264, yuv420p)**锛岄煶棰?**AAC-LC 48 kHz 绔嬩綋澹?*
- 宸插啓鍏?`+faststart`锛岄€傚悎鐩存帴鎾斁涓庝笂浼?
---

## 鍥涖€侀」鐩粨鏋?
```
ScreenRecorder/
鈹溾攢 src/ScreenRecorder/                 涓荤▼搴忥紙WPF + 鍘熺敓 Win32 浜掓搷浣滐紝鏃?NuGet 渚濊禆锛?鈹? 鈹溾攢 EntryPoint.cs                    鍏ュ彛锛氬懡浠よ鑷 / 鍗曞疄渚?/ 鍚姩鎮诞鐞?/ 鍒涘缓蹇嵎鏂瑰紡
鈹? 鈹溾攢 Capture/
鈹? 鈹? 鈹溾攢 GdiScreenCapture.cs           鎶撳睆锛欸etDC + CreateDIBSection + BitBlt
鈹? 鈹? 鈹溾攢 ScreenCapture.cs              鎶撳睆缁熶竴鍏ュ彛涓庡抚缁撴瀯
鈹? 鈹? 鈹溾攢 RecordingEngine.cs            褰曞埗寮曟搸锛氭椂闂磋酱銆侀煶鐢诲悓姝ャ€佹殏鍋溿€佹贩闊炽€佽惤鐩?鈹? 鈹? 鈹溾攢 ScreenHelper.cs               鏄剧ず鍣ㄦ灇涓?/ 铏氭嫙妗岄潰鑼冨洿
鈹? 鈹? 鈹溾攢 Audio/WasapiInterop.cs        WASAPI 鍘熺敓浜掓搷浣滃０鏄?鈹? 鈹? 鈹溾攢 Audio/WasapiAudioCapture.cs   绯荤粺鍥炵幆 + 楹﹀厠椋庨噰闆嗐€侀噸閲囨牱銆佹贩闊?鈹? 鈹? 鈹斺攢 Encoding/FFmpegRecorder.cs    ffmpeg 瀹炴椂缂栫爜 + 鍒嗙骇鍥為€€灏佽
鈹? 鈹溾攢 UI/                              鎮诞鐞?/ 璁剧疆 / 妗嗛€夊尯鍩?/ 鎵樼洏 / 鍏ㄥ眬蹇嵎閿?鈹? 鈹溾攢 Shell/                           璁剧疆鎸佷箙鍖栥€佹棩蹇椼€佸揩鎹锋柟寮?鈹? 鈹溾攢 SelfTest/                        鍛戒护琛岃嚜妫€锛堝綍鍒?/ 鎶撳睆 / 浜や簰 / 淇濆瓨 / 娓叉煋锛?鈹? 鈹斺攢 Assets/app.ico                   绋嬪簭鍥炬爣锛堝灏哄锛?鈹溾攢 tools/
鈹? 鈹溾攢 build.ps1                        涓€閿瀯寤?+ 鎵撳寘鍒?dist
鈹? 鈹溾攢 make-icon.ps1                    鐢ㄧ函 .NET 鐢熸垚澶氬昂瀵?ico
鈹? 鈹斺攢 ffmpeg/                          棰勭暀锛氭妸 ffmpeg.exe 鏀捐繖閲岋紙涓嶇撼鍏ョ増鏈鐞嗭級
鈹溾攢 docs/images/                        鏂囨。鐢ㄦ埅鍥?鈹溾攢 鏇存柊灞忓綍鐞?cmd                       鏃ュ父閲嶅缓鍚庢妸浜х墿鎷疯繘 dist
鈹斺攢 README.md
```

---

## 浜斻€佸紑鍙戣€咃細鏋勫缓涓庤嚜妫€

```powershell
# 鏋勫缓
dotnet build src\ScreenRecorder\ScreenRecorder.csproj -c Release

# 涓€閿墦鍖咃紙鍚鍒?ffmpeg銆佸垱寤烘闈㈠揩鎹锋柟寮忥級
powershell -ExecutionPolicy Bypass -File tools\build.ps1
```

绋嬪簭鍐呯疆浜?*鏃犵晫闈㈣嚜妫€**锛岀敤鏉ラ獙璇佸綍鍒堕摼璺€屼笉鏄?鐪嬭捣鏉ヨ兘璺?锛?
```powershell
$exe = "src\ScreenRecorder\bin\Release\net10.0-windows\灞忓綍鐞?exe"

# 鐪熷疄褰?6 绉掑苟鐢熸垚 MP4锛屾墦鍗版椂闀?/ 澶у皬 / 甯ф暟
& $exe --selftest --seconds=6 --audio=both --out=D:\tmp

# 鎶撳睆閾捐矾锛氭姄涓€甯у瓨鎴?BMP 骞剁粺璁′寒搴︼紙纭涓嶆槸榛戝睆锛?& $exe --capturetest

# 鎮诞鐞冧氦浜掞細浜嬩欢缁戝畾 / 鍗曞嚮寮€濮?/ 鎷栧姩浣嶇Щ / 鑳藉惁鍋滀笅鏉?& $exe --interacttest

# 鍋滄鍝嶅簲锛氭祴閲?Stop() 鑰楁椂锛岄槻"鐐瑰仠姝㈠悗鐣岄潰鍗℃"鍥炲綊
& $exe --stoptest --seconds=6

# 淇濆瓨閾捐矾锛氬惈闊宠建 / 鏃犻煶杞ㄤ袱鏉¤矾寰勶紝鏍￠獙鏂囦欢闈炵┖涓旀槸鍚堟硶 MP4
& $exe --savetest

# 鐣岄潰娓叉煋锛氭妸鎮诞鐞冧笁绉嶅瑙傛覆鏌撴垚 PNG
& $exe --uitest
```

鍏ㄩ儴鑷椤圭殑瀹屾暣鍙傛暟锛?
| 鍙傛暟 | 璇存槑 |
| --- | --- |
| `--selftest [绉掓暟]` | 鏃犵晫闈㈠綍鍒惰嚜妫€ |
| `--area=primary\|full` | 鑷褰曞埗鍖哄煙 |
| `--audio=both\|system\|mic\|none` | 鑷闊抽鏉ユ簮 |
| `--out=鐩綍` | 鑷杈撳嚭鐩綍 |
| `--capturetest` | 鎶撳睆閾捐矾鑷 |
| `--interacttest` | 鎮诞鐞冧氦浜掕嚜妫€ |
| `--stoptest [绉掓暟]` | 鍋滄鍝嶅簲鑷 |
| `--savetest` | 淇濆瓨閾捐矾鑷 |
| `--uitest` | 鎮诞鐞冪晫闈㈡覆鏌撹嚜妫€ |

### 瀹炴祴鏁版嵁锛?560脳1600 @150% 缂╂斁锛學indows 11锛?
```
--selftest    鏃堕暱=00:06  澶у皬=2.74 MB  甯ф暟=183        H.264 2560x1600@30fps + AAC 48kHz 绔嬩綋澹?--stoptest    Stop() 鐢ㄦ椂 203 ms锛屾枃浠?2.04 MB锛?53 甯?--savetest    [鍚煶杞╙ 1117807 瀛楄妭 / [鏃犻煶杞╙ 1060623 瀛楄妭锛屼复鏃舵畫鐣?0
--interacttest 璺敱浜嬩欢琚鐞?down=True up=True锛涙嫋鍔ㄤ綅绉?152,106 px锛堜笌榧犳爣 1:1锛?```

---

## 鍏€佹妧鏈€夊瀷璇存槑

| 鍐崇瓥 | 鍘熷洜 |
| --- | --- |
| **涓嶇敤 NuGet 鍖?* | WinRT 鎶曞奖鍖呭湪绂荤嚎鐜鏃犳硶杩樺師锛涙敼鐢ㄥ師鐢?COM 浜掓搷浣滅洿鎺ヨ皟鐢?WASAPI 涓庣郴缁熸帴鍙ｏ紝浜х墿鏇村皬銆佸惎鍔ㄦ洿蹇?|
| **鎶撳睆鐢?GDI BitBlt** | `Windows.Graphics.Capture` 鍦ㄨ繙绋嬫闈€侀儴鍒嗚櫄鎷熸樉绀洪┍鍔ㄣ€佸彈绛栫暐闄愬埗鐨勪細璇濅腑浼氳繑鍥?`E_ACCESSDENIED`锛汫DI 鍦ㄨ繖浜涘満鏅笅渚濈劧鍙敤銆傞厤鍚?`CreateDIBSection` 鐩存帴鎷垮埌鍍忕礌鍐呭瓨锛岄伩鍏?`GetDIBits` 鍦ㄦ煇浜涢┍鍔ㄤ笂杩斿洖鍏ㄩ粦 |
| **缂栫爜浜ょ粰 ffmpeg** | 鐪佹帀鑷繁缁存姢 MP4 灏佽 + 鐮佺巼鎺у埗锛沗libx264 -preset veryfast -tune zerolatency` 鍦?1080p/1440p 瀹炴椂褰曞睆涓?CPU 鍗犵敤鍙帶 |
| **涓ら樁娈典繚瀛?* | 褰曞埗涓彧鎶?BGRA 甯у啓杩?ffmpeg 鐨?stdin锛圚.264 鍏堢紪鐮佸埌涓存椂鏂囦欢锛夛紝闊抽 PCM 鏀掑湪鍐呭瓨锛涘仠姝㈡椂鍐嶅啓 WAV 骞剁敤鍚屼竴娆?ffmpeg 璋冪敤娣锋祦銆傚彧鐢ㄤ竴涓閮ㄨ繘绋嬶紝闊崇敾澶╃劧鍚屾锛屼篃閬垮厤鍙岀閬撲簤鐢?|
| **鐙珛鍐欑嚎绋?+ 鏈夌晫闃熷垪** | 绠￠亾鍐欎細鍦ㄧ紪鐮佸櫒璺熶笉涓婃椂闃诲锛涙斁鍒扮嫭绔嬬嚎绋嬪悗锛屽綍鍒朵富寰幆涓庡仠姝㈡祦绋嬮兘涓嶄細琚崱浣忥紙鏈€澶氫涪鏈€鏃х殑 3 甯э級 |

---

## 涓冦€佸父瑙侀棶棰?
**Q锛氭彁绀烘壘涓嶅埌 ffmpeg.exe锛?*
瑙併€屽揩閫熷紑濮?鈫?鍑嗗 ffmpeg銆嶃€傜▼搴忎細渚濇鍦ㄧ▼搴忕洰褰曘€乣ffmpeg\`銆乣tools\ffmpeg\`銆乣PATH` 閲屾壘銆?
**Q锛氬綍鍒舵枃浠跺緢澶?/ 寰堝皬锛?*
鍦ㄨ缃噷璋冦€屾竻鏅板害銆嶃€傚睆骞曞唴瀹瑰墽鐑堝彉鍖栵紙鐪嬭棰戙€佹粴鍔ㄩ〉闈級鏃剁爜鐜囦細鏄庢樉涓婂崌锛岃繖鏄?H.264 鐨勬甯歌涓恒€?
**Q锛氬疄闄呭抚鐜囦綆浜庤缃€硷紵**
鎶撳睆涓庣紪鐮侀兘鎸夎缃抚鐜囬┍鍔紱CPU 鍚冪揣鏃朵細涓㈠抚銆傚彲闄嶄綆甯х巼鎴栫缉灏忓綍鍒跺尯鍩熴€?
**Q锛氫负浠€涔堟姄灞忚蛋鐨勬槸 GDI 鑰屼笉鏄郴缁熷浘褰㈡崟鑾凤紵**
`Windows.Graphics.Capture` 鍦ㄥ綋鍓嶄細璇濊绯荤粺鎷掔粷鏃朵細鑷姩鍥為€€鍒?GDI BitBlt锛屽姛鑳戒笉鍙楀奖鍝嶏紝鏃ュ織閲屼細鍐欐槑璧颁簡鍝潯璺€?
**Q锛氫繚瀛樺嚭鏉ユ槸绌烘枃浠?/ 鎻愮ず"杈撳嚭鏂囦欢涓嶅畬鏁?锛?*
0.1.0 涔嬪悗鐨勭増鏈仛浜嗕笁绾у洖閫€锛氭贩娴佹垚鍔熺敤甯﹂煶杞ㄧ殑鏂囦欢 鈫?娣锋祦澶辫触鍒欒惤鐩樹负銆屼粎鐢婚潰銆峂P4 鈫?閮藉け璐ユ墠鎶ラ敊骞朵繚鐣欎复鏃舵枃浠躲€?鍙﹀**褰曞埗鏃堕暱涓嶈冻 1.5 绉?*鏃舵湰鏉ュ氨鍙兘鏉ヤ笉鍙婁骇鍑烘湁鏁堟枃浠讹紝璇峰綍涔呬竴鐐瑰啀鍋滄銆?鑻ヤ粛澶辫触锛屾妸 `logs\app.log` 閲岀殑銆岃棰戠紪鐮佺粨鏉熴€嶃€屾贩娴併€嶃€屼繚瀛樻垚鍔熴€嶄笁娈靛彂鍑烘潵鍗冲彲瀹氫綅銆?
**Q锛氭偓娴悆浼氬綍杩涚敾闈㈤噷鍚楋紵**
浼氥€傛兂閬垮厤鐨勮瘽锛屽紑濮嬪綍鍒跺墠鎶婂畠鎷栧埌涓嶆兂褰曠殑鍖哄煙涔嬪锛屾垨缂╁皬褰曞埗鍖哄煙缁曡繃瀹冦€?
**Q锛氭兂寮€鏈鸿嚜鍚紵**
鎶婃闈㈠揩鎹锋柟寮忓鍒跺埌 `shell:startup` 鐩綍鍗冲彲銆?
---

## 鍏€佸凡鐭ラ檺鍒?
- 鍙湁 Windows 鐗堟湰锛屼笖渚濊禆 .NET 10 妗岄潰杩愯鏃?- 鎮诞鐞冩湰韬細琚綍杩涚敾闈紙娌℃湁鍋氱獥鍙ｆ帓闄わ級
- 鎶撳睆璧?GDI锛屽抚鐜囦笂闄愬彈 CPU 褰卞搷锛?K 楂樺抚鐜囧満鏅缓璁缉灏忓尯鍩?- 涓嶅寘鍚?ffmpeg.exe锛岄渶瑕佽嚜琛屽噯澶?- 褰曞埗涓病鏈夊疄鏃堕瑙堢獥鍙?
---

## 涔濄€佸疄鐜拌鐐癸紙缁欏悗缁淮鎶よ€咃級

- **鎶撳睆**锛歚GetDC(NULL)` 鈫?`CreateCompatibleDC` 鈫?`CreateDIBSection`锛堣 GDI 鑷繁鍒嗛厤鍍忕礌鍐呭瓨锛夆啋 姣忓抚 `BitBlt` 鈫?鎷峰洖鎵樼鏁扮粍銆?  鐢?GDI 鑷繁鐨勫唴瀛樿€屼笉鏄?pin 鎵樼鏁扮粍锛屾槸涓轰簡閬垮紑 `CreateDIBSection` 瀵规墭绠℃暟缁勫湴鍧€鐨勮姹傦紙鏇惧嚭鐜扮敾闈㈠叏榛戯級銆?- **闊抽**锛歐ASAPI 浜嬩欢椹卞姩锛涚郴缁熷洖鐜敤 `AUDCLNT_STREAMFLAGS_LOOPBACK`锛岄害鍏嬮璧?capture 绔偣锛?  璁惧娣烽煶鏍煎紡澶氭牱锛堜緥濡?192 kHz float 绔嬩綋澹帮級锛岀粺涓€閲嶉噰鏍锋垚 16 bit / 48 kHz / 绔嬩綋澹板悗鎸夐噰鏍疯鏁版贩闊炽€?- **闊崇敾鍚屾**锛氫互 `Stopwatch` 涓哄敮涓€鏃堕棿杞达紱瑙嗛鎸?`1000/fps` 鍒嗘Ы锛岄煶棰戞寜鐩爣閲囨牱鏁版帹杩涳紱
  鏌愬埢缂烘暟鎹氨琛ラ潤闊?/ 閲嶅涓婁竴甯э紝鍥犳鏆傚仠銆佸崱椤块兘涓嶄細璁╁０鐢婚敊浣嶃€?- **鎷栧姩鎮诞鐞?*锛氱敤 `GetCursorPos` + `GetWindowRect` 璁板綍鎸変笅鏃剁殑鐗╃悊鍍忕礌浣嶇疆锛岀Щ鍔ㄦ椂 `SetWindowPos` 鐩存帴瀹氫綅锛?  闃堝€兼寜鐗╃悊鍍忕礌 脳 DPI 缂╂斁璁＄畻銆?*涓嶈**鐢?`PointToScreen` 鐨勫樊鍊煎幓鏀?`Window.Left/Top`锛圖IP 涓庣墿鐞嗗儚绱犳贩鐢紝楂?DPI 涓嬩細婕傜Щ锛屼笖浼氭妸鍗曞嚮璇垽鎴愭嫋鍔級銆?- **WPF 鍚姩**锛氫笉瑕佸悓鏃朵繚鐣?`App.xaml` 鐢熸垚鐨勫叆鍙ｇ偣鍜岃嚜瀹氫箟 `Main`锛?  鍚﹀垯浼氬湪 `InitializeComponent()` 閲屾姏鍑?鏃犳硶鍒涘缓绗簩涓?Application 瀹炰緥"銆傛湰椤圭洰鐩存帴鍒犳帀浜?`App.xaml`锛?  鍏ュ彛鍦?`EntryPoint.cs`锛岄渶瑕佷富棰樿祫婧愭椂鐢?pack URI 鍔犺浇銆?
### 韪╄繃鐨勫潙锛堥伩鍏嶉噸澶嶏級

1. **XAML 閲屽繀椤绘樉寮忕粦瀹氶紶鏍囦簨浠?*锛氬彧鍐欏鐞嗘柟娉曚笉浼氱敓鏁堬紝绐楀彛鐪嬭捣鏉ユ甯镐絾瀹屽叏娌℃湁浜や簰銆?   鐜板湪鏈?`--interacttest` 涓撻棬瀹堢潃杩欎竴鐐广€?2. **淇濆瓨鏀跺熬瑕佸垎绾у洖閫€**锛氭贩娴佸け璐ユ椂 ffmpeg 浼氱暀涓嬩竴涓?0 瀛楄妭鐨勭洰鏍囨枃浠讹紝
   鑻ユ鏃舵竻鐞嗘帀涓存椂瑙嗛锛屽氨浼?浠€涔堥兘娌＄暀涓?銆傝绗竷鑺傘€?3. **鍋滄褰曞埗涓嶈兘绛夌閬撳啓**锛氬綍鍒朵富寰幆鐩存帴鍐欑閬撲細鍦ㄧ紪鐮佸櫒璺熶笉涓婃椂闃诲锛?   `Stop()` 鍐?`Join` 灏变細鍗＄晫闈€傜幇鍦ㄥ厛 `AbortPipe()` 璁╅樆濉炵殑鍐欑珛鍒昏繑鍥炪€?
---

## 鍗併€佽鍙瘉

鏈粨搴撲唬鐮佷互 MIT 璁稿彲璇佸彂甯冿紝瑙?[LICENSE](LICENSE)銆?
**娉ㄦ剰**锛氱▼搴忚繍琛屼緷璧栫殑 `ffmpeg`锛堣嫢浣犳寜鍏惰鍙瘉鍒嗗彂锛夐渶閬靛畧 **LGPL / GPL**锛?甯歌鐨?`ffmpeg-gpl` 鏋勫缓鍥犲惎鐢?libx264 灞炰簬 **GPL**銆傛湰浠撳簱涓嶉檮甯?ffmpeg 浜岃繘鍒讹紝
浣跨敤鑰呰嚜琛岃幏鍙栵紝璇疯嚜琛岀‘璁よ鍙悎瑙勩€?
---

## English summary

**Screen Recorder Floating Ball** 鈥?a tiny Windows screen recorder with an on-screen translucent
floating ball. One click starts/stops recording of the screen plus system audio and/or microphone,
producing a standard MP4 (H.264 + AAC). Written in pure C#/WPF with native Win32 interop and
**no NuGet dependencies**; encoding is delegated to a `ffmpeg` binary that you supply yourself.

- Click the ball to start/stop, double-click for settings, right-click for the full menu, drag to move
- Region selection, multi-monitor, per-source volume, 15鈥?0 fps, 4鈥?0 Mbps
- Global hotkeys `Ctrl+Alt+R` (start/stop) and `Ctrl+Alt+P` (pause/resume)
- Built-in headless self-tests: `--selftest`, `--capturetest`, `--interacttest`, `--stoptest`, `--savetest`, `--uitest`

Build: `powershell -File tools\build.ps1` (requires .NET 10 SDK; put `ffmpeg.exe` in `tools\ffmpeg\` first).

