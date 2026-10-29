# BÁO CÁO KIỂM TOÁN KỸ THUẬT ZERO-TRUST — RbxManager (M43)

**Ngày:** 28/10/2026  
**Phạm vi:** Working tree hiện tại (chưa commit, trên `4cacd17` M38) — `src/Core`, `src/Infrastructure`, `src/App.Wpf`, `tools/BrowserExtension`, toàn bộ test.  
**Phương pháp:** Đọc trực tiếp C#/XAML, đối chiếu DI container, đối chiếu binding với symbol trong DLL đã build, xem từng ảnh chụp UI thật. **Không** coi kết quả PASS của test là bằng chứng.

> [!IMPORTANT]
> Kết luận ngắn: app **build sạch, test xanh, nhưng không đáng tin như test gợi ý**. Có 6 lỗi CRITICAL thật, trong đó 3 lỗi về bảo mật/log (master password vô dụng, bản sao vault không mật khẩu nằm trên đĩa, log Infrastructure không bao giờ vào file và bỏ qua bộ lọc cookie). Không tìm thấy binding XAML nào bị gãy.

---

## 0. KẾT QUẢ CHẠY THỰC TẾ (Mục 6)

| Lệnh | Kết quả | Đánh giá zero-trust |
| :--- | :--- | :--- |
| `dotnet build "RAM CLONE.slnx" -c Debug` | **0 Warning, 0 Error** | Sạch thật. |
| `dotnet test` | **373/373 Passed** (Core 93, App.Wpf 56, Infrastructure 224) | Hầu hết là unit test với fake. Không test nào phát hiện được các lỗi CRITICAL dưới đây. |
| `tools/run-ui-tests.ps1` | **38/38 scenario PASS** (Dark + Light), artifacts: `artifacts/ui/20261028_143509` | **PASS giả một phần**: scenario `ServersPage (Canned List)` chụp ra màn hình *"Không tìm thấy máy chủ"* mà vẫn PASS. `AccountsContextMenu`/`AccountsAddCookieDialog`/`EditDialog`/`DeleteConfirmation` tìm menu item dưới root cửa sổ chính (ContextMenu nằm ở HWND popup riêng), mỗi bước bọc `if (... != null)` → không thực thi gì vẫn PASS (`UiHarnessTests.cs:132-196`). |

---

## 1. [CRITICAL - BROKEN]

### C1. Log của toàn bộ Infrastructure **không vào file log** và **bỏ qua bộ lọc cookie**
- `App.xaml.cs:55` dùng `Host.CreateDefaultBuilder()` nhưng **không** gọi `UseSerilog()`/`AddSerilog()`; `App.Wpf.csproj:29-30` chỉ có `Serilog` + `Serilog.Sinks.File` (thiếu `Serilog.Extensions.Hosting`). `services.AddSingleton<Serilog.ILogger>(Log.Logger)` (`App.xaml.cs:209`) không ai dùng.
- Hệ quả: mọi `ILogger<T>` (Vault, SessionTracker, RobloxLogWatcher, RamCleaner, Watchdog, DpiToolService, RobloxAuthService, LocalExtensionBridgeService) đi vào provider mặc định (Console/Debug/EventSource/**Windows Event Log**), **không** vào `logs/app-.log`.
  - Các log đó không đi qua `RobloxSecretRedactionEnricher`.
  - Log từ Warning trở lên kèm `Exception` được ghi vào Event Log hệ thống (ví dụ `DpiToolService.cs:394/397`, `LocalExtensionBridgeService.cs:612/618`).
- Khi debug thực tế, file log gần như trống phần Infrastructure, nên "Ghost Disconnect"/"Auto-Rejoin" không thể chẩn đoán từ log.

### C2. Master password **có thể bỏ qua chỉ bằng DPAPI**
- `EncryptedFileVault.cs:606-608`: khóa AES **chính là** đầu ra Argon2id.
- `EncryptedFileVault.cs:640`: chính khóa đó được `ProtectedData.Protect(dataKey, …, CurrentUser)` rồi **ghi vào file**.
- `EncryptedFileVault.cs:545`: khi load chỉ so `FixedTimeEquals(dpapiUnwrapped, argonKey)`, tức là blob đã wrap = khóa AES.
- Bất kỳ code nào chạy dưới user hiện tại chỉ cần `ProtectedData.Unprotect(wrappedKey)` là giải mã được vault, **không cần mật khẩu**. Master password chỉ còn là một cổng UI.

### C3. Bản vault **không mật khẩu** còn nằm lại trên đĩa
- `EncryptedFileVault.cs:689`: `File.Replace(tmpPath, _filePath, bakPath)` luôn giữ thế hệ trước trong `vault.dat.bak`.
  - Sau khi bật master password (`ReencryptAsync`), `vault.dat.bak` vẫn là bản chỉ có DPAPI.
  - Cookie của tài khoản đã xóa cũng sống thêm một thế hệ.
- `EncryptedFileVault.cs:711-729`: `vault.dat.v1.bak` / `vault.dat.v1.<timestamp>.bak` được giữ vĩnh viễn.
- Ngược lại, `.bak` **không bao giờ được đọc** để khôi phục khi vault hỏng (`LoadInternalAsync` ném `VaultCorruptedException`, không có fallback). Rủi ro bảo mật cao mà giá trị khôi phục bằng 0.

### C4. Một exception bất kỳ **giết toàn bộ hàng đợi launch**; Rotation dừng âm thầm
- `AccountsViewModel.ExecuteSequentialLaunchQueueAsync` (`AccountsViewModel.cs:1178-1273`) và `ServersViewModel.RunJoinQueueAsync` (`ServersViewModel.cs:~287-466`) chỉ có `catch (OperationCanceledException)`, **không có try/catch theo từng tài khoản**.
- Các lệnh có thể ném exception ngay trong vòng lặp:
  - `UpdateAccountStatusOnLaunchFailureAsync` → `_accountStore.UpdateAsync` (`:1222`)
  - `_accountStore.UpdateAsync` (`ServersViewModel.cs:334/360/409`)
  - `LaunchRecoveryAsync` → `cookie.Reveal()` trên cookie đã dispose
- Khi một lệnh ném, exception bay lên `OnDispatcherUnhandledException` (`App.xaml.cs:115-131`) → hộp thoại "fatal error", và **các tài khoản còn lại bị bỏ**.
- `RotationManager.RunAsync` (`RotationManager.cs:141-144`): `catch (Exception) { LogLoopFailed }`. Lỗi của một tài khoản (ví dụ `ModerationRecoveryHandler` → `UpdateAsync`) làm **cả rotation dừng về Idle** mà chỉ ghi log. Do C1, log đó cũng không vào file.

### C5. RunningPage: nhóm xoay tua **không chọn được** khi có hơn ~3–4 nhóm
- `RunningPage.xaml:61-73`: `WrapPanel` nằm trong `StackPanel Orientation="Horizontal"` nên nhận chiều rộng vô hạn và **không bao giờ xuống dòng**.
- Sau nhãn "Các nhóm xoay tua (theo thứ tự này)", chỉ khoảng 3–4 checkbox vừa trước nút "Đóng tất cả" ở 960px. Các nhóm còn lại bị cắt và không có cách nào tick được, nên tính năng Rotation bị chặn với người dùng nhiều nhóm.

### C6. Phục hồi tài khoản bị moderation **báo thành công giả** và lưu `Valid`
- `IncognitoBrowserLauncher.cs:466-481` (`IsCookieReadyForCaptureAsync`) chỉ coi 401/403 là "chưa xong"; **mọi status khác, kể cả 429/5xx, đều trả `true`**.
- Khi đó trình duyệt bị đóng và cookie vẫn đang bị moderation được trả về như đã phục hồi. Caller lưu `Status = AccountStatus.Valid` (`AccountsViewModel.cs:1477-1483, 1538-1544`; `ServersViewModel.cs:352-360`). Đây là dữ liệu sai được ghi vào vault.

---

## 2. [WARNING - FLAWED LOGIC]

### 2.1 XAML & UI (Zero-Scroll / M41)
`MainWindow`: Height 760, MinHeight 640, MinWidth 960.

| # | Vị trí | Vấn đề |
| :- | :- | :- |
| U1 | `AccountsPage.xaml:253-432` | Tổng MinWidth các cột ≈ 756px, trong khi vùng grid ở 960px chỉ ≈ 716px. Grid đặt `HorizontalScrollBarVisibility="Disabled"` + `ClipToBounds`, nên cột **"Thao tác" (nút Kill) bị cắt và không cuộn tới được**. Ở 1100px, cột "Ngày tạo" đã bị cắt chữ (`14/10/202…`, thấy trong ảnh chụp). |
| U2 | `SettingsPage.xaml:531-569` (card "Nâng cao") | 3 hàng `*` chia khoảng 38px. Toggle "Xuất log ra Desktop" (h=20) đã chạm mép ở 700px và bị cắt một phần ở 640px. |
| U3 | `SettingsPage.xaml:380-525` (Hiệu năng), `:249-334` (Tự động vào lại) | Mô tả dài của Auto-Clean cache / Auto-Tile / Auto-Reconnect vừa khít ở 1100×700 và bị cắt 15–25px ở 960×640. Ô "Chiều cao (px)" bị cắt mép phải (thấy trong ảnh chụp). |
| U4 | `ServersPage.xaml:30-146` | Khi `IsJoining`, pill trạng thái + nút Hủy ≈ 280px khiến toolbar vượt chiều rộng ở 960px, và **nút Hủy Join bị cắt**. |
| U5 | `AccountsPage.xaml:67-99` | Chip nhóm nằm trong ScrollViewer ngang có scrollbar Hidden; con lăn chuột chỉ cuộn dọc nên chip cuối và nút Rename Group không tới được khi tràn. Vẫn có lối thay thế trong menu "Khác". |
| U6 | `AddAccountWindow.xaml:14-17` | Ở MinHeight 440 khi đang hiện ErrorMessage, ô dán cookie (`*`) co gần về 0px. |
| U7 | `AccountsViewModel.cs:854-856` | `ApplyFilter()` gọi `FilteredAccounts.Clear()`, và SelectedItem đang binding TwoWay, nên **mỗi lần gõ tìm kiếm/sort/đổi nhóm/refresh đều mất lựa chọn**: inspector trống, các nút Kiểm tra/Sửa/Xóa bị disable. |
| U8 | `AccountsViewModel.cs:81-82,113`; `ServersViewModel.cs:60-61,73` | `IsLoading` thiếu `[NotifyPropertyChangedFor]` cho `IsNotBusy` / `IsEmptyStateVisible`, nên trạng thái nút và overlay "không có máy chủ" có thể hiển thị cũ. |
| U9 | `MainWindow.xaml:98-114` | Pill xanh **"Hợp lệ" được hard-code**, không binding gì. Luôn báo hợp lệ là UI chết và gây hiểu nhầm. |
| U10 | `SettingsViewModel.cs:707` | Theme "System" được map sang **`ApplicationTheme.HighContrast`** thay vì theo theme của OS. |
| U11 | `SettingsViewModel.cs:408,554` | Thành viên VM chết: `GhostDisconnectModeChangedCommand`, `EnableGhostDisconnectDetectionToggledCommand`; `IsMultiInstanceAvailable` không được bind. `AddAccountViewModel.IsExtensionBridgeListening` không có chỉ báo nào trên UI. |
| U12 | `AccountsPage.xaml.cs:90-98` | Sort đặt `e.Handled=true` nhưng không set `SortDirection`, nên không bao giờ hiện mũi tên sort. |
| U13 | `AccountPickerControl.xaml:9-11` | Click vào dòng chỉ highlight chứ không tick checkbox; chỉ checkbox mới được tính khi chọn. |

**[VERIFIED WORKING]:**
- Khoảng 150 tên binding, toàn bộ command (kể cả cách toolkit bỏ tiền tố `On…`), mọi `x:Static Strings.*` và mọi event handler trong XAML đều tồn tại trong DLL đã build sau lần sửa XAML cuối.
- Không có binding TwoWay nào trỏ vào property read-only.
- Không có command nào có CanExecute luôn false.

### 2.2 Local Extension Bridge (M42)
| # | Vị trí | Vấn đề |
| :- | :- | :- |
| B1 | `LocalExtensionBridgeService.cs:377-379` | Kiểm tra Origin chỉ dùng `StartsWith("chrome-extension://")` / `moz-extension://` / `extension://`, **không ghim extension ID**: bất kỳ extension nào, kể cả chuỗi trần `chrome-extension://`, đều qua. Trang web thường không giả được Origin nên vẫn chặn được drive-by, nhưng process local thì giả tùy ý. |
| B2 | `:98-107` + AddAccountWindow | Listener khởi động lỗi (trùng port, instance thứ hai, `HttpListenerException` access denied) bị **nuốt thành `return false`**. `IsExtensionBridgeListening` không hiện trên UI, nên extension chỉ báo "Cannot connect" và không có port dự phòng. |
| B3 | `background.js:72` | Không kiểm tra kết quả `chrome.cookies.remove` (có thể trả null), vậy mà popup vẫn báo thành công. |
| B4 | `background.js:21-24` | Không truyền `storeId` và manifest không có khóa `incognito`, nên chỉ đọc được cookie của profile thường. |
| B5 | `:462-464` | Khớp tài khoản fallback theo `Alias == user.Name`: một tài khoản không liên quan có alias trùng username sẽ **bị ghi đè cookie**. |
| B6 | `:497-499` | ID = `Max + 1`; `_importGate` chỉ khóa các lần import qua bridge, nên import thủ công chạy song song có thể trùng ID → 500. Không hỏng dữ liệu. |
| B7 | `:146-148, :207-209, :127-129` | `catch {}` rỗng ở các đường dọn dẹp (ảnh hưởng thấp). Body chunked vượt giới hạn trả 400 thay vì 413. Không kiểm tra Content-Type. |

**[VERIFIED WORKING]:**
- Manifest V3 hợp lệ.
- Listener **Start** trên `Loaded` và **Stop** trên `Closed`, kể cả khi đóng bằng nút X. Gọi lặp an toàn (có lock); cửa sổ modal nên không mở đôi.
- Chỉ nhận kết nối loopback; preflight CORS đúng; giới hạn body 16KB.
- `chrome.cookies.remove` **chỉ xóa `.ROBLOSECURITY`**, **chỉ sau khi** server trả `ok=true`, và server chỉ trả `ok=true` sau khi đã validate và lưu.
- Cookie không bao giờ vào log. Event được marshal về UI thread.
- Test bridge dùng HttpListener thật (nhưng không test OPTIONS/405/413/giải phóng port/xung đột port).

### 2.3 Launch Queue & Backoff (M39–M40)
| # | Vị trí | Vấn đề |
| :- | :- | :- |
| L1 | `AccountsViewModel.cs:1237-1239`, `ServersViewModel.cs:427-431` | Sau một lần 429, khoảng chờ là `ComputeBackoffSeconds(1)` = **30s, ngắn hơn** nhịp bình thường 60–120s. Backoff đang làm **tăng tốc** thay vì giãn ra. Cách sửa: `Math.Max(normalDelay, backoff)`. |
| L2 | 3 vòng lặp | Tài khoản bị rate-limit **bị bỏ luôn**, không đưa lại vào hàng. Countdown backoff dùng chung chuỗi `NextAccountCountdown` nên không phân biệt được. |
| L3 | `RobloxRetryHandler.cs:39-76` | Retry-After của server được dùng nguyên giá trị, không có trần, và chạy **trong khi `RobloxLauncher` đang giữ `_launchGate`** (`RobloxLauncher.cs:97`). Hệ quả: queue, Rotation và auto-rejoin cùng đứng mà không có countdown. |
| L4 | `RobloxLauncher.cs:100-127` | Cooldown/jitter ẩn bên trong launcher không có phản hồi UI: đúng triệu chứng "timer biến mất" của M39. |
| L5 | `AccountsViewModel.cs:1209, 1389, 1362, 1404` | `_ = TrackSpawnedRobloxProcessAsync(..., CancellationToken.None)` là fire-and-forget:<ul><li>Chỉ poll 60s và chỉ đếm process **đã có MainWindowHandle**, nên khởi động chậm hoặc Roblox tự update làm **mất watcher** (không có session, LogWatcher, watchdog hay auto-rejoin).</li><li>Sau `ConfigureAwait(false)` nó duyệt `_allAccounts` (một `List` thường) trên thread pool trong khi UI thread có thể `Clear()`, dẫn tới `InvalidOperationException` không ai quan sát.</li></ul> |
| L6 | `SessionTracker.cs:240-291` | `TrackLaunchAsync` lấy baseline **trước** cooldown của launcher (có thể tới 300s). Gặp `newProcesses.Count > 1` thì `return null` ngay; nếu đúng 1 process lạ thì **có thể gắn nhầm tài khoản**. `ServersViewModel.cs:384` vẫn đếm là thành công dù không gắn được watcher. |
| L7 | `ServersViewModel.cs:264` vs `:298-307` | Doc comment "Returns null to abort the remaining queue" sai với code. `continue` bỏ qua countdown giãn cách. Snackbar cuối `ErrorTitle, ErrorTitle` vô nghĩa. |
| L8 | `AccountsViewModel.cs:720, 1154`; `ServersViewModel.cs:274` | Mỗi lần launch đều gọi `_instanceGate.TryEnable()` **bất kể người dùng đã tắt Multi-instance** trong Settings. |
| L9 | `AccountsViewModel.cs:1320-1350` | `KillHeadlessRobloxProcesses` kill **mọi** RobloxPlayerBeta không được theo dõi, không có cửa sổ và đã chạy hơn 75s, kể cả instance người dùng tự mở ngoài app. `catch {}` rỗng. |
| L10 | `RunningViewModel.cs:123` | `Dispatcher.Invoke` đồng bộ từ thread poll của SessionTracker có rủi ro deadlock khi shutdown. |
| L11 | `AccountsViewModel.cs:1170`, `ServersViewModel.cs:280` | `Console.WriteLine` trong app WPF (vô dụng). |

**[VERIFIED WORKING]:**
- **Không còn `break`/`return` nào khi gặp RateLimited** trong cả 3 vòng lặp (lỗi "dead queue" M39 đã được sửa).
- Cờ `IsBulkLaunching` / `IsJoining` / `Idle` được reset trong `finally`.
- CTS cục bộ, không có lỗi dispose dùng chung. Toán `LaunchBackoffPolicy` đúng (30/60/120/240/300, bão hòa ở 5).
- Các vòng lặp duyệt snapshot nên không đụng collection đang bị sửa. `LaunchRecoveryAsync` có timeout 15 phút nên không treo vĩnh viễn.

### 2.4 Recovery Launch & Window Sizer (M41)
> Ghi chú: `docs/prompts/M41.md` thực chất là bản redesign XAML. Debounce trong `SettingsViewModel` là CTS tự viết, **không** dùng `DebounceDispatcher`.

| # | Vị trí | Vấn đề |
| :- | :- | :- |
| R1 | `RobloxWindowSizer.cs:29,37,46,66-70` | Bộ đếm `_pendingRecoveryOverrides` là **toàn cục**, không theo PID/tài khoản:<ul><li>PID của trình duyệt recovery "ăn" token dành cho Roblox (`IncognitoBrowserLauncher.cs:207`).</li><li>Nhánh `:68` cấp 800×600 cho **bất kỳ PID nào**, nên lần launch kế tiếp của tài khoản khác có thể bị mở 800×600.</li></ul> |
| R2 | `RobloxWindowSizer.cs:189-192` | Override theo PID **không bị xóa khi process thoát** (chỉ dọn khi đổi settings/khởi động), nên rò rỉ dictionary và có rủi ro dính PID bị Windows tái sử dụng. |
| R3 | `RobloxWindowSizer.cs:184-206` + `WindowSizeLock` | Tắt "Ép độ phân giải" **không nhả** các cửa sổ đang bị lock: hook vẫn ép kích thước cũ cho tới khi Roblox thoát. |
| R4 | `RobloxWindowSizer.cs:221/256` | Auto-Tile tính ô lưới bằng kích thước 800×600 riêng, trong khi cửa sổ khác dùng kích thước global, nên các cửa sổ chồng lên nhau. |
| R5 | `WindowSizeLock.cs:158-172` | Mỗi lần Attach tạo thêm `Process` + `WaitForExitAsync` không bị hủy khi lock cũ dispose, nên handle tích lũy mỗi lần đổi settings. |
| R6 | `SettingsViewModel.cs:523-524` | Debounce 500ms **chỉ áp dụng cho việc resize**. `SaveSettingsAsync` vẫn ghi đĩa **ở mỗi phím gõ**, và đọc `CurrentSettings` trước khi await, tạo **race lost-update** với toggle khác (ví dụ EnableAutoTile `:451`): thay đổi này có thể âm thầm ghi đè thay đổi kia. |
| R7 | `SettingsViewModel` / `RobloxWindowSizer` | `ApplyToRunningProcesses` chạy trên UI thread và gọi `SetWindowPos` đồng bộ xuyên process (không có `SWP_ASYNCWINDOWPOS`): một Roblox bị treo có thể **đóng băng UI**. |
| R8 | `IncognitoBrowserLauncher.cs:94` | Hủy (cancel) không đóng trình duyệt: token của caller không được link vào CTS 15 phút. |

**[VERIFIED WORKING]:**
- Override 800×600 **chỉ nằm trong bộ nhớ** (`ConcurrentDictionary`), **không** chạm `AppSettings` và không ghi đĩa.
- Bộ debounce thật sự reset mỗi lần thay đổi, chạy trên UI thread và áp đúng giá trị mới nhất.
- `WindowSizeLock` giữ delegate hook bằng static root, nên không có crash callback-on-collected-delegate. Hook/unhook đúng thread.
- Cookie **không** nằm trên command line: được inject qua CDP `Network.setCookie`, serialize an toàn.
- Profile tạm bị xóa trong `finally` và được quét lại khi khởi động (`App.xaml.cs:455`).

### 2.5 Background Services & Security (M1–M38)
| # | Vị trí | Vấn đề |
| :- | :- | :- |
| S1 | `RobloxHandleCloser.cs` | **Code chết**: không có caller nào trong `src/`, chỉ test gọi tới. |
| S2 | `NamedMutexInstanceGate.cs:311-331, :112, :224-225` | `TryCloseExistingSingletonHandles` là no-op (chỉ đóng handle vừa tự mở). Gate production bỏ qua kiểm tra "Roblox đang chạy" và bỏ qua `createdNew=false`, nên **báo Success dù Roblox đã giữ mutex**: Multi-instance thất bại âm thầm. |
| S3 | `NamedMutexInstanceGate.cs:132-135` | Timeout 5s trả "Timeout" trong khi worker có thể vẫn bật gate sau đó, nên trạng thái lệch. |
| S4 | `App.xaml.cs:560-582` | Bộ handle mutex thứ hai trong App (unowned) trùng lặp với gate; `GC.KeepAlive` trên static field vô nghĩa. |
| S5 | `App.xaml.cs:503` | `async void OnExit`: phần sau `await _host.StopAsync()` (nhả mutex, `Log.CloseAndFlushAsync`) không chắc được chạy. Đường thoát instance trùng (`:307`) lại **khởi tạo** cả graph VM + gate worker chỉ để hủy. |
| S6 | `App.xaml.cs:115-131` | `async void OnStartup`: lỗi ở `StartAsync`/`LoadSettingsAsync` bị `Handled=true`, nên process sống tiếp mà không có cửa sổ nào. |
| S7 | `EncryptedFileVault.cs:549` + `MasterPasswordWindow.xaml.cs:49-53` | Sai mật khẩu và vault hỏng ném **cùng một** `VaultCorruptedException`, nên file hỏng hiện ra như "sai mật khẩu". |
| S8 | `SettingsViewModel.cs:618/644` | Bật mật khẩu không nguyên tử: crash giữa `ReencryptAsync` và lưu cờ → vault Argon2 + cờ false → không còn đường nhập mật khẩu (**khóa ngoài vĩnh viễn**). Key được đổi trước khi save; nếu save lỗi thì provider và file lệch nhau. |
| S9 | `EncryptedFileVault.cs:533, 606-608` | Argon2id (64MiB×3) chạy **mỗi** thao tác, và 2 lần cho mỗi lần ghi, nên bulk check/rejoin/launch rất chậm. |
| S10 | `VaultKeyProvider.cs:24-57` | Trả về instance `SecretValue` dùng chung: `SetKey`/`Clear` song song dispose nó trong khi vault đang `Reveal()`, gây `ObjectDisposedException`. |
| S11 | `RobloxSecretRedactionEnricher.cs:16-26`, `App.xaml.cs:139-171` | Chỉ lọc *property*, không lọc `Exception.Message`/stack trace. `crash_log.txt` ghi `exception.Message` không che. Hiện chưa có đường nào đưa cookie vào exception (phòng thủ chiều sâu). |
| S12 | `SessionTracker.cs:528-554` | Chế độ NotifyOnly không có dedupe nên snackbar spam theo từng dòng log. KillAndRejoin **bỏ qua** keyword xuất hiện trong 60s đầu (Error 277 ngay khi vào game không được xử lý). |
| S13 | `SessionTracker.cs:111-113, 371` | `RejoinPolicy` cache snapshot settings theo tài khoản: đổi RejoinDelay/MaxAttempts không có hiệu lực tới khi bật/tắt lại auto-rejoin. `Reset()` không bao giờ được gọi. Dùng `List` không khóa. |
| S14 | `SessionTracker.cs:453-470` | Smart hop luôn thay JobId bằng server ít người nhất, **bỏ qua server gốc/private server**. |
| S15 | `RobloxLogWatcher.cs:146-186, 210, 106-109` | Gắn log không rõ ràng thì bỏ cuộc âm thầm (log Warning nhưng mất do C1). Dòng cuối chưa flush có thể làm sót keyword. `StopWatching` dùng `Wait(500)` đồng bộ có thể chạy trên UI thread. |
| S16 | `RamCleanerService.cs:57-62, 25` | Không kiểm tra lại `RamCleanerAcknowledged` sau delay. Trạng thái static toàn cục. |
| S17 | `DpiToolService.cs:140-147, 265-285` | **Lưu ý: đây KHÔNG phải GoodbyeDPI mà là Cloudflare WARP.** MSI tải về thư mục user-writable, **không kiểm tra hash/chữ ký**, rồi chạy `runas` (elevated). Có TOCTOU, tức là **đường leo thang đặc quyền local**. |
| S18 | `DpiToolService.cs:369-370, 350-351` | `WaitForExit(60000)` hết giờ vẫn coi là thành công. `ReadToEnd()` trước `WaitForExit(3000)` nên timeout vô nghĩa và có thể treo vô hạn. WARP không bao giờ được disconnect khi thoát app. |
| S19 | `PotassiumService.cs:124-148` | Chạy file exe theo đường dẫn trong settings.json, chỉ kiểm tra `File.Exists`. Rủi ro thấp. |

**[VERIFIED WORKING]:**
- `_host.StartAsync()` được gọi (`App.xaml.cs:313`).
- `SessionTracker` (singleton = hosted), `InstanceWatchdogService`, `BackgroundWindowThrottler`, `RamCleanerService` được đăng ký qua `AddHostedService` và **thực sự chạy**, không chặn startup.
- `IRobloxLogWatcher`, `IInstanceGate`, `IDpiToolService`, `IPotassiumService`, `ILocalExtensionBridge` được đăng ký và thực sự được resolve.
- Thông số AES-GCM đúng:
  - tag 16B, nonce 12B ngẫu nhiên kèm khóa/salt mới mỗi lần lưu (không thể trùng nonce);
  - AAD phủ header + tham số KDF;
  - kiểm tra biên tham số KDF chống DoS bộ nhớ;
  - ghi tmp → `Flush(true)` → `File.Replace`.
- Mutex Roblox được tạo `initiallyOwned: true` trên thread worker sống suốt đời app, được nhả đúng thread, tự phục hồi mỗi 3s.
- Cookie hygiene tại nguồn:
  - `SecretValue.ToString()` = `[REDACTED]`;
  - mọi điểm `Reveal()` đều đã kiểm tra, không có chỗ nào log giá trị;
  - URI launch chỉ chứa auth *ticket*;
  - `UatLogger` che `gameinfo:`.
- Auto-Rejoin: task rejoin được bọc an toàn; CTS theo tài khoản; RejoinPolicy cửa sổ 30 phút, tối đa 5 lần; Watchdog có 60s grace + 2 strike.

---

## 3. THỨ TỰ ƯU TIÊN SỬA ĐỀ XUẤT

1. **C1** – Thêm `Serilog.Extensions.Hosting` + `.UseSerilog()`; xóa đăng ký `Serilog.ILogger` thừa.
2. **C2 + C3** – Argon2 mode: wrap một *data key ngẫu nhiên* bằng khóa Argon2 (không DPAPI-wrap chính khóa Argon2); xóa an toàn `.bak`/`.v1.bak` sau khi re-encrypt; dùng `.bak` để khôi phục khi vault hỏng.
3. **C4** – try/catch theo từng tài khoản trong 3 vòng lặp (Accounts / Servers / Rotation).
4. **C6** – `IsCookieReadyForCaptureAsync` chỉ trả `true` khi `IsSuccessStatusCode`.
5. **C5 + U1/U4** – Đổi StackPanel bao WrapPanel sang Grid/DockPanel; xem lại MinWidth cột DataGrid và toolbar Servers.
6. **L1, R1, R6, S2, S8, S17** – nhóm lỗi logic có tác động cao tiếp theo.

> [!CAUTION]
> Chưa sửa bất kỳ dòng code nào. **Cần xác nhận của bạn trước khi tiến hành sửa** (sửa tất cả theo thứ tự trên, hay chỉ nhóm CRITICAL trước).
