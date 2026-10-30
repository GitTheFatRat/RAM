# M44 — Nhật ký vòng lặp Verify-Fix-Retest

> Quy ước phân loại: **BUG** = vi phạm trực tiếp yêu cầu của 7 Flow trong `docs/prompts/M44.md` hoặc lỗi chức năng/bảo mật mức High/Med có thể tái hiện. **Ghi chú (Low)** = hardening/tài liệu/khả năng test, được ghi nhận trung thực nhưng không tính vào `BUG_COUNT`.

## Iteration #1 — Phase A (2026-10-29 20:52)

Baseline: `dotnet build` 0 warning/0 error; unit tests 384/384 pass; `tools/run-ui-tests.ps1` 38/38 PASS (`artifacts/ui/20261029_135310`).

### BUG đã phát hiện (BUG_COUNT = 16)

| # | Flow | Mức | Vị trí | Mô tả |
|---|---|---|---|---|
| B1 | 4 | High | `WindowSizeLock.cs` EnforceSize / recreated-window / trailing / Attach | `EnforceSize` vẫn gọi `SW_RESTORE` khi cửa sổ đang minimize; các đường Attach, trailing enforcement, MOVESIZEEND, cửa sổ tái tạo vẫn khôi phục cửa sổ đang thu nhỏ. |
| B2 | 1 | High | `App.xaml.cs` file sink | Khối `{Exception}` trong log file không được che (enricher chỉ xử lý Properties) → cookie trong `ex.Message` lộ ra đĩa. |
| B3 | 1 | High | `App.xaml.cs` crash_log.txt | `crash_log.txt` ghi `exception.Message`/stack thô, không che cookie. |
| B4 | 2 | High | `EncryptedFileVault.ReencryptAsync` | Key provider bị thay đổi trước khi lưu thành công → lưu lỗi làm khoá vault trong phiên. |
| B5 | 3 | High | `RotationManager`, `AccountsViewModel`, `ServersViewModel` | Bộ lọc `ex is not OperationCanceledException` để `TaskCanceledException` (timeout, không phải người dùng huỷ) thoát khỏi try per-account → dừng cả hàng đợi / Rotation dừng im lặng. |
| B6 | 6 | Med | `MainWindow.xaml.cs` | Lưu UiState bằng snapshot `SaveSettingsAsync(current with …)` fire-and-forget khi đóng → ghi đè thay đổi độ phân giải/toggle đang lưu. |
| B7 | 6 | Med | `App.xaml.cs` OnExit | `async void OnExit`: phần sau `await _host.StopAsync()` (dispose host, flush log) có thể không chạy. |
| B8 | 1 | Med | `App.xaml.cs` | Đường dẫn log Release là tương đối theo CWD (autostart → System32) → không có file log. |
| B9 | 1 | Med | `RobloxSecretRedactionEnricher` | Regex bỏ sót dạng URL-encoded (`%7C`, `%3D`) và `.ROBLOSECURITY = …`/JSON. |
| B10 | 5 | Med | `LocalExtensionBridgeService` | Chấp nhận mọi `chrome-extension://`/`moz-extension://`/`extension://` origin. |
| B11 | 2 | Med | `EncryptedFileVault` | `.tmp` bị bỏ lại khi ghi thất bại. |
| B12 | 2 | Med | `EncryptedFileVault` | Sau khi nâng cấp vault legacy 0x01, file `.bak` vẫn giải mã được chỉ bằng DPAPI. |
| B13 | 2 | Med | `EncryptedFileVault.SecureDeleteFiles` | Xoá backup thất bại bị nuốt im lặng, UI vẫn báo "đã bật". |
| B14 | 2 | Med | `MasterPasswordWindow` | Settings và vault lệch nhau → mật khẩu bất kỳ đều mở khoá. |
| B15 | 7 | Med | `AccountsPage` @1100px | Cột Nhóm bị cắt cứng ("Farminç"), header "KIỂM TRA GẦN N…" bị cắt, cột "Thao tác" trống. |
| B16 | 3 | Low→tính | `ServersViewModel.cs:297` | `buildRequest` nằm ngoài try per-account → exception làm hỏng cả hàng đợi. |

### Ghi chú Low (không tính BUG_COUNT)
- F4-D11: TOCTOU giữa `IsIconic` và `User32.ForceWindowedMode` (ms). Không sửa ở `User32` vì test `M35_MoveResizeWindow…` khẳng định hành vi restore — đổi sẽ phá test đang pass.
- F4-D12/D6: thiếu seam TimeProvider (Throttler, ServersViewModel).
- F4-D13: gán PID heuristic cho recovery tab Accounts.
- F5-D2..D7: match theo Alias, 413 vs 400 khi chunked, log full exception, store failure không log, `chrome.cookies.remove` không kiểm tra kết quả, race Dispose.
- F6-D10..D12: thứ tự event SettingsChanged, async void handler không try/catch, Dispose semaphore.
- F2-V6: secure wipe chỉ best-effort trên SSD/VSS.
- F3-D4/D5: doc mismatch; Rotation không có gap 60–120s riêng.

### Phase B: chọn B1 (High, Flow 4, 1 file `WindowSizeLock.cs`).

### Phase C/D Iteration #1 — B1 ✅
- **Sửa** `WindowSizeLock.cs`: `EnforceSize` thoát sớm khi `IsIconic` (điểm chặn duy nhất cho Attach/trailing/MOVESIZEEND), bỏ nhánh `SW_RESTORE`; cửa sổ tái tạo đang minimize chỉ được "nhận" handle, việc đưa về ô tile hoãn đến `MINIMIZEEND` (`_pendingTileMove`).
- **Test real-data** (`WindowControllerTests`): tạo cửa sổ Win32 thật bằng `CreateWindowEx`, minimize bằng OS, gọi `Attach` + mọi WinEvent → vẫn `IsIconic`, restore rect giữ nguyên 640x480@(120,140); cửa sổ tái tạo minimize → không bị restore, khi user restore thì vào đúng tile (50,60,200,400).
- **Chứng minh tái hiện**: chạy 2 test trên mã `HEAD` cũ → **2/2 FAIL**; trên mã mới → PASS.
- Gate: build `-warnaserror` 0/0; tests 237 + 56 + 93 pass.

## Iteration #2 — B2 + B3 + B9 (cụm che cookie trong log) ✅
- **Sửa**: `RobloxSecretRedactionEnricher` — regex bao phủ dạng URL-encoded/khoảng trắng/JSON, thêm `Redact(string)`, che cả scalar không phải string (Uri). Thêm `RedactingTextFormatter` (che toàn bộ dòng đã render, gồm khối `{Exception}`), dùng cho file sink trong `App.xaml.cs`. `crash_log.txt` và MessageBox lỗi nghiêm trọng đi qua `Redact`.
- **Test real-data**: `LogFileRedactionRealDataTests` ghi qua Serilog file sink thật vào `logs/app-*.log` trên đĩa, đọc lại byte vật lý: không còn đuôi cookie trong property, exception, inner exception, URL-encoded, JSON. `CrashLogRedactionRealDataTests` gọi `App.WriteCrashLog` thật, đọc `crash_log.txt`.
- Gate: 0/0; tests 242 + 57 + 93 pass.

## Iteration #3 — B4 + B11 (vault: lưu thất bại phải nhất quán) ✅
- **Sửa** `EncryptedFileVault`: `ReencryptAsync` chụp lại khoá cũ, nếu `SaveInternalAsync` ném lỗi thì khôi phục khoá vào provider; `SaveInternalAsync` xoá `.tmp` khi ghi/replace thất bại.
- **Test real-data** `VaultSaveFailureRealDataTests`: vault thật trên đĩa + DPAPI + Argon2 thật, khoá `vault.dat.bak` bằng `FileShare.None` để `File.Replace` thất bại thật → không còn `.tmp`, khoá cũ vẫn giải mã được. Thêm test đổi mật khẩu (Old bị từ chối, New mở được, không còn `.bak`).
- Gate: 0/0; tests 244 + 57 + 93 pass.

## Iteration #4 — B5 + B16 (cô lập lỗi per-account) ✅
- **Sửa**: `RotationManager` (vòng per-account + `RunStepSafelyAsync`), `AccountsViewModel`, `ServersViewModel` lọc theo `!ct.IsCancellationRequested` thay vì theo kiểu exception; `buildRequest` của Servers được đưa vào trong try.
- **Test real-data**: `RotationManagerTests.LaunchGroup_WhenAccountHitsRealHttpTimeout…` — `HttpClient` thật timeout 200 ms vào socket loopback thật không phản hồi → Rotation vẫn launch account kế tiếp và vào Farming. `CheckAllPacingTests`: TaskCanceledException (không do user) không dừng hàng đợi; xác nhận `Math.Max(90s gap, 30s backoff)` = 90s.
- Gate: 0/0; tests 245 + 59 + 93 pass.
- **Phát hiện mới (B17, Med)**: còn 5 chỗ khác gọi `SaveSettingsAsync(CurrentSettings with …)` dạng snapshot (`AccountsViewModel:717/937/1132`, `ServersViewModel:53`, `RunningViewModel:234`) — cùng lớp lỗi lost-update với B6.

## Iteration #5 — B6 + B7 (lưu trạng thái khi tắt app) ✅
- **Sửa**: `MainWindow.OnWindowClosing` dùng `UpdateSettingsAsync(WithUiState(...))` (merge vào state mới nhất trong lock) và chờ có giới hạn 2s; `App.OnExit` chuyển sang đồng bộ: `StopAsync` chạy off-UI-thread có timeout 5–6s, `Log.CloseAndFlush()` luôn chạy.
- **Test real-data** `ShutdownSettingsPersistenceRealDataTests`: `SettingsService` thật + `settings.json` thật — chỉnh độ phân giải đang lưu rồi lưu UiState khi đóng → đọc lại file: cả hai đều còn.
- Gate: 0/0; tests 245 + 60 + 93; UI harness 38/38 PASS (`artifacts/ui/20261029_141221`).

## Iteration #6 — B17 (mọi chỗ lưu settings dạng snapshot) ✅
- **Sửa**: `AccountsViewModel` (LastPlaceId ×2, sort state), `ServersViewModel` (PlaceId), `RunningViewModel` (rotation), `PotassiumService`, `RamCleanerService` → `UpdateSettingsAsync(s => s with …)`. Không còn caller snapshot nào trong `src/`.
- Cập nhật `XamlSmokeTests.ServersViewModel_PlaceIdText…`: test cũ khẳng định API snapshot (chính là lỗi) → nay khẳng định kết quả ngữ nghĩa qua `UpdateSettingsAsync`.
- **Test real-data**: `ServersViewModel` thật + `SettingsService` thật, 40 lần sửa PlaceId xen kẽ 40 writer đồng thời → file đĩa đủ 40 increment.
- Gate: 0/0; tests 245 + 61 + 93.

## Iteration #7 — B8 (đường dẫn log phụ thuộc CWD) ✅
- **Sửa** `App.ResolveLogDirectory`: neo vào thư mục exe (`AppContext.BaseDirectory\logs`, cùng chỗ `crash_log.txt`), fallback `%LOCALAPPDATA%\RbxManager\logs` khi không ghi được.
- **Test real-data** `LogDirectoryResolutionRealDataTests`: thư mục thật ghi được / bị chặn bởi file thật tên `logs` → fallback đúng.
- Gate: 0/0; tests 245 + 64 + 93.

## Iteration #8 — B10 (bridge chấp nhận mọi extension) ✅
- **Sửa**: ghim khoá công khai vào `tools/BrowserExtension/manifest.json` (`"key"`, version 1.0.1) → ID cố định `omebbdjjpdniojlcceimbbmfndljgjdp` (Chrome & Edge). `IsAllowedExtensionOrigin` chỉ chấp nhận đúng `chrome-extension://<ID>`. Khoá riêng **không** được lưu.
- ⚠️ **Tác động người dùng**: phải **Reload** extension unpacked một lần (ID thay đổi theo key).
- **Test real-data**: listener loopback thật — origin extension lạ / moz / `null` → 403, không lưu gì; test suy ra ID từ `key` trong manifest khớp hằng số trong code.
- Gate: 0/0; tests 251 + 64 + 93.

## Iteration #9 — B12 (`.bak` yếu hơn primary) ✅
- **Sửa** `SaveInternalAsync`: nếu đang lưu ở chế độ mật khẩu mà primary cũ trên đĩa không phải 0x02 (DPAPI-only hoặc legacy 0x01) thì secure-delete `.bak` vừa được `File.Replace` xoay vào.
- **Test real-data**: vault DPAPI-only → lưu có mật khẩu → không còn `.bak`; lần lưu kế tiếp cùng mức bảo vệ vẫn tạo `.bak` 0x02 để khôi phục. Bổ sung assert vào test legacy 0x01 có sẵn.
- Gate: 0/0; tests 252 + 64 + 93.

## Iteration #10 — B13 (xoá backup thất bại bị bỏ quên) ✅
- **Sửa**: file xoá thất bại được đưa vào hàng đợi `_pendingSecureDeletes` và thử lại ở mỗi lần load vault.
- **Test real-data**: giữ handle OS thật trên `vault.dat.v1.bak` trong lúc bật mật khẩu → file còn; nhả handle → `ListAsync` kế tiếp xoá được.
- Giới hạn trung thực: hàng đợi nằm trong bộ nhớ, không sống qua lần khởi động lại (`.v1*.bak` vẫn được dọn khi khởi động, `.bak` thì không).
- Gate: 0/0; tests 253 + 64 + 93.

## Iteration #11 — B14 (settings/vault lệch → mật khẩu bất kỳ mở khoá) ✅
- **Sửa**: `EncryptedFileVault.IsPasswordProtectedOnDisk()` + `EnsurePasswordProtectionAsync(password)`; `MasterPasswordWindow` sau khi mở khoá sẽ mã hoá lại ngay bằng mật khẩu vừa nhập nếu vault đang DPAPI-only (lỗi I/O → chỉ log, vẫn mở khoá).
- Giới hạn trung thực: không thể "xác minh" mật khẩu chưa từng được áp dụng — chỉ có thể ràng buộc ngay lập tức.
- **Test real-data**: vault DPAPI-only → ràng buộc mật khẩu → mật khẩu khác bị từ chối, không còn `.bak`.
- Gate: 0/0; tests 254 + 64 + 93.

## Iteration #12 — B15 (AccountsPage cắt chữ) ✅
- Kiểm tra lại: cột "Thao tác" trống là **đúng thiết kế** (chỉ hiện khi tài khoản đang chạy) → loại khỏi danh sách lỗi.
- **Sửa** `AccountsPage.xaml`: style `TrimmedCellTextStyle` (ellipsis + tooltip giá trị đầy đủ) cho Alias/Nhóm/Tên người dùng, ellipsis cho "Kiểm tra gần nhất"; cân lại tỉ lệ cột (Alias 1.6*, Nhóm 1*, Username 1.5*, Kiểm tra 1.5*). MinWidth giữ nguyên.
- **Test**: `AccountsGridTrimmingTests` dựng `AccountsPage` thật.
- **Hồi quy tự phát hiện khi soi ảnh**: lần đầu style dựa trên `DataGridTextColumn.DefaultElementStyle` làm mất màu chữ/padding của WPF-UI (chữ xám, lệch trái) → đổi sang `DefaultDataGridTextElementStyle` của WPF-UI. Ảnh `artifacts/ui/20261029_143147/AccountsPage-*.png`: header "KIỂM TRA GẦN NHẤT" đầy đủ, "Farming" không bị cắt, giá trị dài kết thúc bằng "…".
- Gate: 0/0; tests 254 + 65 + 93; UI 38/38.

## Iteration #13 — Phase A quét lại (review độc lập toàn bộ diff)
Reviewer độc lập tìm thấy 7 vấn đề mới (do chính các bản sửa trước gây ra hoặc còn sót):

| # | Mức | Mô tả | Xử lý |
|---|---|---|---|
| R1 | Med | Bộ lọc `!ct.IsCancellationRequested` (iteration #4) làm lỗi IO xảy ra **sau** khi user bấm Huỷ thoát khỏi catch → dialog "fatal error" | Iteration #13 ✅ |
| R2 | Med | Mở khoá vault DPAPI-only gắn luôn mật khẩu gõ sai → khoá vĩnh viễn | Iteration #14 ✅ |
| R3 | Med/Low | `StageNormalPlacement` kéo restore rect hợp lệ ở màn hình phụ về màn hình chính | Iteration #15 ✅ |
| R4 | Low | Hàng đợi xoá `.bak` cũ có thể xoá nhầm `.bak` mới (bản khôi phục duy nhất) | Iteration #16 ✅ |
| R5 | Low | Đọc header vault với `FileShare.Read` có thể chặn `File.Replace` | Iteration #16 ✅ |
| R6 | Low | TOCTOU `IsIconic` → `ForceWindowedMode` | Ghi chú Low (không sửa `User32`, xem trên) |
| R7 | Low | Tooltip rỗng ở ô trống | Iteration #17 ✅ |

### Iteration #13 — R1 ✅
- Bộ lọc mới (4 chỗ): `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` — chỉ đúng lệnh huỷ của user được thoát.
- **Test**: user huỷ hàng đợi rồi account đang chạy ném `IOException` → `launchTask` kết thúc sạch, không exception lọt ra.
- Gate: 0/0; 254 + 66 + 93.

### Iteration #14 — R2 ✅
- `MasterPasswordWindow`: vault chưa được bảo vệ (hoặc chưa tồn tại) → bắt nhập **lại cùng mật khẩu** (`PasswordRebindConfirmation`, so sánh fixed-time) trước khi `SetKey`/mã hoá. Thêm chuỗi `MasterPasswordRebindConfirm` (vi + en, parity 281 khoá).
- Gate: 0/0; 254 + 68 + 93 (sửa CA1001 trong lần build đầu).

### Iteration #15 — R3 ✅
- `IsOffScreenRect` dùng chung: chỉ tile hoặc rect bị "park"/hỏng mới bị kẹp vào work area màn hình chính; rect nằm trên bất kỳ màn hình nào được giữ nguyên.
- **Test real-data** `OffScreenClassificationRealDataTests` dùng bố cục màn hình thật (`MonitorFromRect`, virtual-screen metrics).
- Gate: 0/0; 254 + 71 + 93.

### Iteration #16 — R4 + R5 ✅
- Thử xoá lại chỉ sau khi primary giải mã thành công; `.bak` vừa được `File.Replace` tạo mới (không yếu hơn) được gỡ khỏi hàng đợi; probe header dùng `FileShare.ReadWrite | FileShare.Delete`.
- **Test real-data**: `.bak` mới sau một lần lưu thành công vẫn còn dù hàng đợi cũ có `.bak`.
- Gate: 0/0; 255 + 71 + 93.

### Iteration #17 — R7 ✅
- Trigger `Text=""` → `ToolTip = null` trong `TrimmedCellTextStyle`.
- Gate: 0/0; 255 + 71 + 93; UI 38/38 (`artifacts/ui/20261029_144329`).

## Iteration #18 — Phase A quét lại lần 2 (verifier độc lập)
- R1, R3, R4/R5, R7 và các bất biến (WindowSizeLock, Throttler 15s/không SW_HIDE, không log cookie) xác nhận **OK**.
- **R2b (Low-Med)**: header vault bị khoá tạm thời (AV) → `SafeIsPasswordProtected` trả `true` → bỏ qua bước xác nhận. Kèm lỗi có sẵn: `IOException` từ `ListAsync` lọt khỏi handler `async void` và để lại key chưa xác minh.
- **Sửa**: trạng thái không xác định ⇒ coi là chưa bảo vệ (thêm một lần xác nhận); catch thêm `IOException`/`UnauthorizedAccessException` và `Clear()` key.
- **Test real-data**: khoá độc quyền `vault.dat` bằng handle OS thật → đi vào nhánh xác nhận.
- Gate: 0/0; 255 + 72 + 93.

## Iteration #19 — B18 (Flow 7: ảnh chụp không chứng minh được context menu)
- Phát hiện khi soi ảnh: `AccountsContextMenu-*.png` **không hề có menu** (kể cả baseline trước M44) — UIA xác nhận menu mở nhưng `PrintWindow` chỉ chụp HWND chính, menu WPF nằm ở popup HWND riêng.
- **Sửa harness** `tests/App.UiTests`: `NativeWindowCapture.CompositeProcessPopups` vẽ các popup (WS_POPUP, không caption) cùng process đè lên ảnh đúng vị trí; scenario context menu bắt buộc ≥ 1 popup được render vào ảnh. Assertion vẫn chạy trên ảnh gốc.
- Lần chạy đầu lộ 2 lỗi chụp (viền bóng trong suốt thành khối đen; menu tràn mép cửa sổ bị cắt) → đổi thành `ComposeWithProcessPopups`: ảnh mở rộng theo hợp các HWND, viền đen trong suốt được loại. Ảnh `artifacts/ui/20261029_145234/AccountsContextMenu-*.png` hiển thị đầy đủ menu "Khác" (Kiểm tra, Kiểm tra tất cả, Sửa, Xóa, Đổi tên nhóm, Sao lưu, Khôi phục) ở cả Dark/Light.
- UI 38/38 PASS.

## Iteration #20 — Phase A cuối cùng: BUG_COUNT = 0 ✅
Toàn bộ 7 Flow đạt; chỉ còn các ghi chú Low đã liệt kê (không vi phạm yêu cầu M44).

---

# 🚀 PHASE E — Tổng kết & phát hành

## Số vòng lặp
**20 iteration** (16 BUG ban đầu + B17, B18 phát sinh + R1–R7, R2b từ 2 vòng review độc lập; tất cả High/Med đã đóng).

## Lỗi đã sửa theo từng iteration
| Iteration | Lỗi | Flow | File chính |
|---|---|---|---|
| 1 | B1 — WindowSizeLock khôi phục cửa sổ đang minimize | 4 | `WindowSizeLock.cs` |
| 2 | B2/B3/B9 — cookie lộ trong exception, crash_log, dạng URL-encoded | 1 | `RobloxSecretRedactionEnricher.cs`, `RedactingTextFormatter.cs`, `App.xaml.cs` |
| 3 | B4/B11 — lưu vault thất bại làm lệch khoá, sót `.tmp` | 2 | `EncryptedFileVault.cs` |
| 4 | B5/B16 — timeout làm dừng cả hàng đợi/Rotation | 3 | `RotationManager.cs`, `AccountsViewModel.cs`, `ServersViewModel.cs` |
| 5 | B6/B7 — mất thay đổi khi đóng app, `async void OnExit` | 6 | `MainWindow.xaml.cs`, `App.xaml.cs` |
| 6 | B17 — 7 chỗ lưu settings dạng snapshot | 6 | 5 ViewModel/Service |
| 7 | B8 — thư mục log phụ thuộc CWD | 1 | `App.xaml.cs` |
| 8 | B10 — bridge chấp nhận mọi extension | 5 | `LocalExtensionBridgeService.cs`, `manifest.json` |
| 9 | B12 — `.bak` yếu hơn primary | 2 | `EncryptedFileVault.cs` |
| 10 | B13 — xoá backup thất bại bị bỏ quên | 2 | `EncryptedFileVault.cs` |
| 11 | B14 — settings/vault lệch | 2 | `EncryptedFileVault.cs`, `MasterPasswordWindow.xaml.cs` |
| 12 | B15 — AccountsPage cắt chữ | 7 | `AccountsPage.xaml` |
| 13 | R1 — IO sau khi Huỷ lọt ra "fatal error" | 3 | 3 file hàng đợi |
| 14 | R2 — gắn mật khẩu gõ sai | 2 | `MasterPasswordWindow.xaml.cs`, `Strings*.resx` |
| 15 | R3 — kéo cửa sổ màn hình phụ về màn hình chính | 4 | `RobloxWindowSizer.cs` |
| 16 | R4/R5 — xoá nhầm `.bak` mới, sharing mode | 2 | `EncryptedFileVault.cs` |
| 17 | R7 — tooltip rỗng | 7 | `AccountsPage.xaml` |
| 18 | R2b — khoá header bỏ qua xác nhận; IO lọt handler | 2 | `MasterPasswordWindow.xaml.cs` |
| 19 | B18 — ảnh chụp không chứa context menu | 7 | `tests/App.UiTests` |
| 20 | Quét cuối — 0 lỗi | — | — |

## Bằng chứng xác minh cuối
- `dotnet build -warnaserror`: **0 warning, 0 error**.
- `dotnet test`: **Infrastructure 255/255, App 72/72, Core 93/93 = 420 pass** (baseline 384 → +36 test mới, đa số real-data: cửa sổ Win32 thật, file log/vault/settings thật, socket loopback thật, HttpClient timeout thật, khoá file OS thật).
- `tools/run-ui-tests.ps1`: **38/38 PASS** — `artifacts/ui/20261029_145234/` (đã soi trực tiếp AccountsPage, SettingsPage, RunningPage, AccountsContextMenu ở Dark/Light).
- Binary phát hành: `C:\Users\Administrator\Pictures\Roblox Account Manager\RbxManager.App.exe` — 29/10/2026 21:57:22, **149.856.779 byte**, self-contained single-file win-x64, SHA-256 `88F905F2423D188098C9E1BA5DC9ED48037FF97158DD260FFACD9DAA62681924`. Bản trước lưu ở `RbxManager.App.exe.old` (20:51:09, 149.847.563 byte).

## Rủi ro còn lại (trung thực)
- ⚠️ Extension trình duyệt phải **Reload** một lần (ID mới `omebbdjjpdniojlcceimbbmfndljgjdp`), nếu không bridge trả 403.
- Các ghi chú Low ở Iteration #1/#13 (TOCTOU vài ms trong `User32.ForceWindowedMode`, hàng đợi xoá backup không sống qua restart, secure-wipe chỉ best-effort trên SSD) chưa sửa.
- Chưa kiểm thử với cửa sổ Roblox thật / tài khoản Roblox thật (live tests cần cookie thật, không chạy trong vòng lặp này).
