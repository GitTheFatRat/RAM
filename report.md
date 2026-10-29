# Báo Cáo Rà Soát Hệ Thống Toàn Diện (100% Chức Năng): Danh Sách Lỗi & Giải Pháp

**Ngày thực hiện:** 2026-10-25  
**Phạm vi kiểm tra:** Toàn bộ mã nguồn `src/Core/`, `src/Infrastructure/`, `src/App.Wpf/` và bộ kiểm thử `tests/`  
**Trạng thái sau khắc phục:**
- `dotnet build "RAM CLONE.slnx"`: **0 Warnings, 0 Errors**
- `RbxManager.Core.Tests`: **93 / 93 PASS**
- `RbxManager.Infrastructure.Tests`: **192 / 192 PASS**
- `RbxManager.App.Tests`: **53 / 53 PASS**
- `App.UiTests` (`tools/run-ui-tests.ps1`): **36 / 36 kịch bản UI (Dark & Light) PASS**

---

## Mục Lục
1. [Nhóm 1: Lỗi Nghiêm Trọng (Concurrency, Multi-Instance & Vòng Đời Phiên)](#1-nhóm-1-lỗi-nghiêm-trọng-concurrency-multi-instance--vòng-đời-phiên)
2. [Nhóm 2: Lỗi Nghiệp Vụ, Quản Lý Cửa Sổ & Dọn Dẹp Tài Nguyên](#2-nhóm-2-lỗi-nghiệp-vụ-quản-lý-cửa-sổ--dọn-dẹp-tài-nguyên)
3. [Nhóm 3: Lỗi Giao Diện (WPF), DI Lifecycle & Trải Nghiệm Người Dùng](#3-nhóm-3-lỗi-giao-diện-wpf-di-lifecycle--trải-nghiệm-người-dùng)
4. [Nhóm 4: Các Khuyến Nghị Cải Tiến Bổ Sung (Hardening & Tối Ưu Hóa)](#4-nhóm-4-các-khuyến-nghị-cải-tiến-bổ-sung-hardening--tối-ưu-hóa)

---

## 1. Nhóm 1: Lỗi Nghiêm Trọng (Concurrency, Multi-Instance & Vòng Đời Phiên)

### Lỗi 1.1: Race Condition chiếm quyền PID khi mở nhiều tài khoản / Auto-Rejoin (`TrackLaunchAsync`)
- **File & Vị trí:** `src/Infrastructure/Sessions/SessionTracker.cs` (`TrackLaunchAsync`)
- **Nguyên nhân (Why):**
  1. Danh sách `baselinePids` được chụp trước khi gọi `await launchAction()` (vốn phải chờ khóa `_launchGate` và thời gian giãn cách `LaunchDelaySeconds` trong `RobloxLauncher`).
  2. Bộ lọc `current.Where(p => !baselinePids.Contains(p.Pid))` không loại trừ các PID đã được gán vào `_activeSessions`.
- **Hệ quả (Impact):**
  Khi mở hàng loạt hoặc khi `RotationManager` / `Auto-Rejoin` chạy chồng lấp, Luồng 2 vừa thoát khỏi `launchAction()` sẽ nhìn thấy PID của Luồng 1 (được tạo trong lúc Luồng 2 đang chờ `_launchGate`) là tiến trình mới và ghi đè `_activeSessions[pid]` sang tài khoản của Luồng 2, còn tiến trình thực của Luồng 2 sinh ra sau đó bị bỏ sót hoàn toàn (hoặc gây lỗi `MultipleNewPids` làm hủy theo dõi).
- **Giải pháp (Đã áp dụng):**
  Loại trừ tất cả các PID đã nằm trong `_activeSessions` khi lọc tiến trình mới:
  ```csharp
  List<ProcessSnapshot> newProcesses = current
      .Where(p => !baselinePids.Contains(p.Pid) && !_activeSessions.ContainsKey(p.Pid))
      .ToList();
  ```
  *(Đã bổ sung unit test `TrackLaunchAsync_IgnoresAlreadyActiveSessionPids` trong `SessionTrackerTests.cs`).*

---

### Lỗi 1.2: `PollProcessesAsync` gọi `await HandleAutoRejoinAsync` tuần tự làm đóng băng vòng lặp theo dõi 30–160 giây
- **File & Vị trí:** `src/Infrastructure/Sessions/SessionTracker.cs` (`ExecuteAsync`, `PollProcessesAsync`, `HandleAutoRejoinAsync`)
- **Nguyên nhân (Why):**
  Vòng lặp quét tiến trình mỗi 2 giây gọi trực tiếp `await HandleAutoRejoinAsync(session, recovery)` tuần tự bên trong `foreach` và không truyền `stoppingToken`. Hàm này ngủ `Task.Delay(delay)` (10s–160s backoff hoặc 20s `RecoveryCooldown`), gọi mạng kiểm tra cookie/server, chờ `_launchGate` và dò PID (tối đa 60s).
- **Hệ quả (Impact):**
  - Khi 1 tài khoản bị văng, việc phát hiện thoát/văng của tất cả các phiên khác bị ngưng trệ trong 30–160 giây.
  - Nếu nhiều tài khoản bị văng cùng lúc, chúng phải chờ từng tài khoản hoàn tất toàn bộ chu trình dò PID 60s rồi mới tới lượt tài khoản tiếp theo.
  - Tắt ứng dụng trong lúc đang chờ `Task.Delay` không hủy được tác vụ ngay lập tức.
- **Giải pháp (Đã áp dụng):**
  Thu thập các tác vụ rejoin trong `PollProcessesAsync(CancellationToken ct)` và chạy song song với `Task.WhenAll(rejoinTasks)`, đồng thời truyền `CancellationToken` xuyên suốt vào `Task.Delay`, `ListAsync`, `ValidateCookieAsync`, `GetServersAsync` và `LaunchAsync`.

---

### Lỗi 1.3: Xung đột giữa `RotationManager` / `CloseAllSessionsAsync` và tác vụ `Auto-Rejoin` đang đếm ngược
- **File & Vị trí:** `src/Infrastructure/Sessions/SessionTracker.cs` (`CloseSessionAsync`, `CloseAllSessionsAsync`, `SetAutoRejoin`, `HandleAutoRejoinAsync`)
- **Nguyên nhân (Why):**
  Khi một tài khoản thuộc Nhóm 1 bị văng ngay trước thời điểm kết thúc chu kỳ xoay tua, `HandleAutoRejoinAsync` đang tạm dừng ở `await Task.Delay(delay)`. Khi `RotationManager` gọi `CloseAllSessionsAsync(graceful: false)` để đóng toàn bộ phiên của Nhóm 1 và chuyển sang Nhóm 2, chỉ các tiến trình đang sống trong `_activeSessions` bị đóng, còn tác vụ `HandleAutoRejoinAsync` đang ngủ không bị hủy. Vài giây sau, tài khoản của Nhóm 1 tự động mở lại chen ngang vào lượt của Nhóm 2.
- **Hệ quả (Impact):**
  Phá vỡ chu kỳ xoay tua nhóm của `RotationManager` hoặc tự động mở lại game dù người dùng đã bấm `"Đóng tất cả"` / tắt `Auto-Rejoin`.
- **Giải pháp (Đã áp dụng):**
  Quản lý `ConcurrentDictionary<long, CancellationTokenSource> _pendingRejoins` theo từng `AccountId`. Tự động gọi `CancelPendingRejoin(accountId)` khi thực thi `SetAutoRejoin(accountId, false)`, `CloseSessionAsync`, `CloseAllSessionsAsync`, hoặc `Dispose()`.  
  *(Đã bổ sung unit test `CloseAllSessionsAsync_CancelsInFlightAutoRejoin` trong `SessionTrackerTests.cs`).*

---

### Lỗi 1.4: `KillHeadlessRobloxProcesses` và `MonitorWindowLifecycleAsync` giết nhầm phiên Roblox hợp lệ đang khởi động hoặc teleport
- **File & Vị trí:**
  - `src/App.Wpf/ViewModels/AccountsViewModel.cs` (`KillHeadlessRobloxProcesses`, `TrackSpawnedRobloxProcessAsync`)
  - `src/App.Wpf/ViewModels/AccountItemViewModel.cs` (`MonitorWindowLifecycleAsync`)
- **Nguyên nhân (Why):**
  1. `KillHeadlessRobloxProcesses` giết mọi tiến trình `RobloxPlayerBeta` có `MainWindowHandle == IntPtr.Zero` nếu không khớp `_allAccounts.Any(a => a.ActiveProcessId == pid)`. Tuy nhiên, `AccountsViewModel` trước đó không lắng nghe `_sessionTracker.SessionStarted`/`SessionEnded`, nên các phiên mở từ tab **Máy chủ**, **Rotation**, hoặc **Auto-Rejoin** không được ghi nhận vào `ActiveProcessId`. Khi các phiên đó đang khởi động hoặc đang chuyển cảnh (`MainWindowHandle == IntPtr.Zero`), việc mở thêm tài khoản từ tab **Tài khoản** sẽ giết nhầm chúng.
  2. `MonitorWindowLifecycleAsync` giết tiến trình ngay ở chu kỳ 2 giây đầu tiên thấy `MainWindowHandle == IntPtr.Zero`, trong khi Roblox thường hủy và tạo lại cửa sổ mất 2–4 giây khi teleport giữa các place.
- **Hệ quả (Impact):**
  Văng game đột ngột khi đang teleport trong game hoặc khi mở kết hợp giữa tab Tài khoản và Rotation / Máy chủ.
- **Giải pháp (Đã áp dụng):**
  1. Đăng ký `AccountsViewModel` lắng nghe `_sessionTracker.SessionStarted` và `_sessionTracker.SessionEnded` để đồng bộ `ActiveProcessId`, đồng thời kiểm tra `_sessionTracker?.ActiveSessions` trong `KillHeadlessRobloxProcesses` và `TrackSpawnedRobloxProcessAsync`.
  2. Tăng ngưỡng kiểm tra `MainWindowHandle == IntPtr.Zero` trong `MonitorWindowLifecycleAsync` lên **3 chu kỳ liên tiếp (6 giây)** trước khi kết luận cửa sổ đã bị người dùng đóng.

---

## 2. Nhóm 2: Lỗi Nghiệp Vụ, Quản Lý Cửa Sổ & Dọn Dẹp Tài Nguyên

### Lỗi 2.1: `GhostDisconnectMode.KillAndRejoin` giết tiến trình nhưng không vào lại nếu `EnableAutoReconnect` tắt
- **File & Vị trí:** `src/Infrastructure/Sessions/SessionTracker.cs` (`OnGhostDisconnectDetected`)
- **Nguyên nhân (Why):**
  Trong `OnGhostDisconnectDetected`, khi `mode == GhostDisconnectMode.KillAndRejoin` nhưng cài đặt `EnableAutoReconnect == false`, code gọi trực tiếp `_processProvider.Kill(pid, graceful: false)` thay vì `RecoverSession(pid, keyword)`. Do `_recoveryKills` không được đánh dấu, `PollProcessesAsync` thấy `recovery == false` nên nếu công tắc Auto-Rejoin riêng của tài khoản đang tắt, phiên bị giết mà không được mở lại.
- **Hệ quả (Impact):**
  Chế độ `KillAndRejoin` chỉ giết game mà không tự vào lại như tên gọi.
- **Giải pháp (Đã áp dụng):**
  Luôn gọi `RecoverSession(pid, keyword)` khi `mode == GhostDisconnectMode.KillAndRejoin`.

---

### Lỗi 2.2: `SetAutoRejoin` không làm mới `RejoinPolicy` sau khi chạm ngưỡng Circuit Breaker
- **File & Vị trí:** `src/Infrastructure/Sessions/SessionTracker.cs` (`SetAutoRejoin`)
- **Nguyên nhân (Why):**
  Khi tài khoản vượt quá số lần thử lại tối đa (`RejoinMaxAttempts`), `_rejoinPolicies` vẫn giữ lịch sử các lần thử cũ. Khi người dùng tắt rồi bật lại `Auto-Rejoin`, lần văng kế tiếp trong vòng 30 phút sẽ lập tức kích hoạt `GaveUp` ngay ở lần thử đầu tiên mà không vào lại.
- **Giải pháp (Đã áp dụng):**
  Xóa trạng thái trong cả `_gaveUpAccounts` và `_rejoinPolicies` khi gọi `SetAutoRejoin`.

---

### Lỗi 2.3: `RobloxLogWatcher` báo lỗi `Ambiguous log mapping` với các file log của phiên vừa đóng
- **File & Vị trí:** `src/Infrastructure/Processes/RobloxLogWatcher.cs` (`ResolveLogFile`)
- **Nguyên nhân (Why):**
  `ResolveLogFile` dùng điều kiện `Math.Abs((f.CreationTimeUtc - startTime.UtcDateTime).TotalSeconds) <= 20` và chỉ loại trừ các file đang nằm trong `_activeWatchers`. Khi một phiên vừa thoát trong vòng 20 giây trước đó, `StopWatching` xóa phiên khỏi `_activeWatchers`, khiến file log cũ trở thành "chưa được gán" và nằm trong khoảng ±20 giây của phiên mới mở, dẫn đến `candidates.Count > 1` (`Ambiguous log mapping`) và bỏ qua việc theo dõi log của phiên mới.
- **Giải pháp (Đã áp dụng):**
  Lưu tập hợp `_claimedPaths` cho mọi file log đã từng được gán cho một PID, và thu hẹp cửa sổ thời gian tạo file về `[-5s, +20s]` so với `startTime`.

---

### Lỗi 2.4: `RobloxCacheCleanerService.RemoveEmptySubdirectories` có nguy cơ xóa thư mục `ContentProvider_<pid>` đang rỗng của tiến trình vừa chạy
- **File & Vị trí:** `src/Infrastructure/Maintenance/RobloxCacheCleanerService.cs` (`RemoveEmptySubdirectories`)
- **Nguyên nhân (Why):**
  Bước xóa file có kiểm tra `IsInsideLiveContentFolder`, nhưng bước `RemoveEmptySubdirectories(cacheRoot)` lại quét bằng `SearchOption.AllDirectories` (không có `IgnoreInaccessible = true` và không kiểm tra `liveContentFolders`). Nếu một tiến trình Roblox vừa tạo thư mục `ContentProvider_<pid>` nhưng chưa kịp ghi file vào đúng lúc dọn cache chạy, thư mục này sẽ bị xóa.
- **Giải pháp (Đã áp dụng):**
  Truyền `liveContentFolders` vào `RemoveEmptySubdirectories` để bỏ qua mọi thư mục thuộc PID đang chạy, đồng thời dùng `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }`.

---

### Lỗi 2.5: `User32.WaitForMainWindowAsync` thoát sớm khi cửa sổ Splash đóng để chuyển sang cửa sổ game chính
- **File & Vị trí:** `src/Infrastructure/Native/User32.cs` (`WaitForMainWindowAsync`)
- **Nguyên nhân (Why):**
  Khi `RobloxPlayerBeta.exe` khởi chạy, nó thường tạo một cửa sổ Splash ngắn hạn trước (`MainWindowHandle != IntPtr.Zero`), sau đó đóng Splash và tạo cửa sổ D3D11 chính. Nếu cửa sổ Splash vừa đóng ngay lúc kết thúc khoảng nghỉ `Task.Delay(1500, ct)`, `process.MainWindowHandle` tạm thời bằng `IntPtr.Zero` và hàm trả về `IntPtr.Zero` ngay lập tức thay vì tiếp tục chờ trong giới hạn 30 giây.
- **Hệ quả (Impact):**
  Tính năng **Ép độ phân giải (Custom Resolution)** và **Tự động xếp cửa sổ (Auto-Tile)** đôi khi không áp dụng được nếu Splash đóng đúng thời điểm kiểm tra.
- **Giải pháp (Đã áp dụng):**
  Đặt bước kiểm tra ổn định 1500ms bên trong vòng lặp `while (!process.HasExited)` để nếu `MainWindowHandle` về `IntPtr.Zero` sau khi Splash đóng, hàm tiếp tục chờ cửa sổ chính xuất hiện cho đến khi hết `maxWaitMs`.

---

### Lỗi 2.6: `WindowSizeLock` tự kích hoạt đệ quy khi đặt lại vị trí cửa sổ và thiếu bắt ngoại lệ khi tiến trình thoát sớm
- **File & Vị trí:** `src/Infrastructure/Native/WindowSizeLock.cs` (`DisposeWhenProcessExits`, `OnWinEvent`)
- **Nguyên nhân (Why):**
  1. Trong `OnWinEvent`, khi phát hiện cửa sổ mới tạo lại (`EVENT_OBJECT_SHOW`), lệnh `User32.MoveResizeWindow` được gọi mà không bật cờ `_isApplying = true`, làm phát sinh sự kiện `EVENT_OBJECT_LOCATIONCHANGE` đệ quy và làm hao hụt bộ đếm `_burstCorrections`.
  2. Trong `DisposeWhenProcessExits`, chỉ bắt `ArgumentException` khi gọi `WaitForExitAsync()`, có thể ném `InvalidOperationException` hoặc `Win32Exception` nếu tiến trình vừa thoát.
- **Giải pháp (Đã áp dụng):**
  Bọc `User32.MoveResizeWindow` trong `try { _isApplying = true; ... } finally { _isApplying = false; }` và bắt `Exception` tổng quát kèm `process?.Dispose()` trong `DisposeWhenProcessExits`.

---

### Lỗi 2.7: `RamCleanerService` không xóa cờ `LastDisabledDueToAccessDenied`, `PresenceService` chưa chuẩn hóa Cookie Header, và `SettingsService` ném `ObjectDisposedException` khi tắt app
- **File & Vị trí:**
  - `src/Infrastructure/Processes/RamCleanerService.cs`
  - `src/Infrastructure/Processes/BackgroundWindowThrottler.cs`
  - `src/Infrastructure/Servers/PresenceService.cs`
  - `src/Infrastructure/Settings/SettingsService.cs`
- **Nguyên nhân & Giải pháp (Đã áp dụng):**
  - **`RamCleanerService`:** Đặt lại `LastDisabledDueToAccessDenied = false` khi chu kỳ dọn RAM thành công (`_memoryOptimizer.LastTrimmedCount > 0`) để trang Chẩn đoán không báo trạng thái lỗi cũ.
  - **`BackgroundWindowThrottler`:** Đưa `await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken)` ra ngoài khối `try/catch` nghiệp vụ để tránh vòng lặp chiếm dụng CPU nếu có ngoại lệ phát sinh.
  - **`PresenceService`:** Chuẩn hóa cookie (`Trim` và cắt bỏ tiền tố `.ROBLOSECURITY=` nếu bị lặp) và dùng `request.Headers.TryAddWithoutValidation` kèm khối `catch (Exception)` an toàn.
  - **`SettingsService`:** Bọc `_semaphore.Release()` trong khối `try/catch (ObjectDisposedException)` để tránh lỗi khi lưu vị trí cửa sổ lúc đóng ứng dụng (`MainWindow.OnWindowClosing`) trùng với thời điểm `SettingsService.Dispose()`.

---

## 3. Nhóm 3: Lỗi Giao Diện (WPF), DI Lifecycle & Trải Nghiệm Người Dùng

### Lỗi 3.1: `RunningViewModel` và `RunningPage` đăng ký `Transient` gây rò rỉ `DispatcherTimer` và sự kiện mỗi lần chuyển tab
- **File & Vị trí:** `src/App.Wpf/App.xaml.cs` (`ConfigureServices`), `src/App.Wpf/ViewModels/RunningViewModel.cs`, `src/App.Wpf/Views/Pages/RunningPage.xaml.cs`
- **Nguyên nhân (Why):**
  `RunningViewModel` và `RunningPage` được đăng ký `AddTransient`, nhưng constructor của `RunningViewModel` đăng ký sự kiện vào các singleton (`_sessionTracker`, `_rotationManager`) và khởi chạy `_timer` 1 giây/lần. Mỗi lần người dùng chuyển sang tab khác rồi quay lại tab **Đang chạy**, một `RunningViewModel` mới được tạo ra trong khi các instance cũ vẫn bị giữ sống vĩnh viễn bởi singleton event và timer, khiến `GetRobloxProcesses()` bị gọi lặp bội số lần mỗi giây.
- **Giải pháp (Đã áp dụng):**
  - Chuyển `RunningViewModel` và `RunningPage` sang `AddSingleton` trong `App.xaml.cs`.
  - Cập nhật `RunningPage.xaml.cs` để làm mới danh sách phiên và nhóm xoay tua khi sự kiện `Loaded` kích hoạt.
  - Đổi `Dispatcher.Invoke(async () => ...)` trong `RunningViewModel.OnSessionStarted` sang `Dispatcher.InvokeAsync` có `try/catch`.

---

### Lỗi 3.2: Không áp dụng ngôn ngữ đã lưu (`AppSettings.Language`) khi khởi động ứng dụng
- **File & Vị trí:** `src/App.Wpf/App.xaml.cs` (`ConfigureCulture`, `OnStartup`)
- **Nguyên nhân (Why):**
  Constructor `App()` gán cứng `vi-VN` trước khi nạp `settings.json`. Trong `OnStartup`, sau khi gọi `await settingsService.LoadSettingsAsync()`, ứng dụng chỉ gọi `ApplyTheme(settings.Theme)` mà không cập nhật lại `CultureInfo` và `Strings.Culture` theo `settings.Language`.
- **Giải pháp (Đã áp dụng):**
  Bổ sung `ConfigureCulture(settings.Language)` ngay sau `await settingsService.LoadSettingsAsync()` trong `App.OnStartup`, cập nhật đồng bộ `Strings.Culture`, `CultureInfo.DefaultThreadCurrentCulture`, `CultureInfo.DefaultThreadCurrentUICulture` và `Thread.CurrentThread`.

---

### Lỗi 3.3: Chưa nối sự kiện `GhostDisconnectOccurred` và `RamCleanerAutoDisabled` lên Snackbar thông báo
- **File & Vị trí:** `src/App.Wpf/App.xaml.cs` (`OnStartup`)
- **Nguyên nhân (Why):**
  Sự kiện `ISessionTracker.GhostDisconnectOccurred` và `RamCleanerService.RamCleanerAutoDisabled` cùng các chuỗi thông báo локаlization đã được định nghĩa nhưng chưa có nơi nào đăng ký hiển thị lên giao diện.
- **Giải pháp (Đã áp dụng):**
  Đăng ký lắng nghe cả hai sự kiện trong `App.OnStartup` và hiển thị thông báo qua `ISnackbarService` trên luồng `Dispatcher`.

---

### Lỗi 3.4: Hộp thoại Thêm tài khoản / Sao lưu đóng im lặng không báo lỗi khi nhập dữ liệu không hợp lệ & Lưu thừa khi mở trang Cài đặt
- **File & Vị trí:**
  - `src/App.Wpf/ViewModels/AccountsViewModel.cs` (`AddAccountAsync`)
  - `src/App.Wpf/ViewModels/SettingsViewModel.cs` (`OnSelectedGhostDisconnectModeChanged`, `ExportBackupAsync`, `ImportBackupAsync`)
- **Nguyên nhân (Why):**
  1. Trong `AddAccountAsync`, khi `AccountValidator.Validate` thất bại (trùng tên gợi nhớ, để trống tên hoặc cookie), hàm `return` im lặng mà không hiển thị thông báo lỗi.
  2. Trong `ExportBackupAsync` và `ImportBackupAsync`, khi mật khẩu sao lưu < 12 ký tự hoặc mật khẩu xác nhận không khớp, `dialogControl.GetPassword()` trả về `null` sau khi hộp thoại đã đóng và hàm `return` im lặng không báo lỗi.
  3. Trong `OnSelectedGhostDisconnectModeChanged`, thiếu kiểm tra `if (_isLoadingSettings) return;`, dẫn đến việc gọi lưu file cài đặt không cần thiết khi nạp trang.
- **Giải pháp (Đã áp dụng):**
  - Hiển thị thông báo lỗi rõ ràng qua `_snackbarService.Show(..., ControlAppearance.Danger)` trong `AddAccountAsync`, `ExportBackupAsync` và `ImportBackupAsync`.
  - Thêm kiểm tra `if (_isLoadingSettings) return;` vào `OnSelectedGhostDisconnectModeChanged`.

---

## 4. Nhóm 4: Các Khuyến Nghị Cải Tiến Bổ Sung (Hardening & Tối Ưu Hóa)

Dưới đây là các điểm cải tiến kiến trúc (không gây lỗi luồng chạy hiện tại nhưng nên xem xét cho các bản nâng cấp tiếp theo):

1. **Tối ưu hóa hiệu năng nhập bản sao lưu lớn (`EncryptedBackupService.ImportAsync`):**
   - *Hiện trạng:* Gọi `_accountStore.AddAsync` tuần tự cho từng tài khoản trong vòng lặp; nếu bật Master Password (Argon2id 64 MB), nhập 50 tài khoản sẽ chạy Argon2id 100 lần.
   - *Đề xuất:* Bổ sung phương thức `AddRangeAsync(IEnumerable<Account>)` vào `IAccountStore` để giải mã và mã hóa lại `vault.dat` đúng 1 lần duy nhất cho toàn bộ lô tài khoản nhập vào.
2. **Giới hạn trần tham số KDF khi đọc header file (`EncryptedFileVault` & `EncryptedBackupService`):**
   - *Hiện trạng:* Đã kiểm tra ngưỡng tối thiểu (`MinimumMemorySizeKb`, `MinimumIterations`), nhưng chưa giới hạn ngưỡng tối đa trước khi ép kiểu `(int)` từ `uint`.
   - *Đề xuất:* Thêm kiểm tra giới hạn trên (ví dụ `memorySizeKb <= 4 * 1024 * 1024`, `iterations <= 10_000_000`) và ném `VaultCorruptedException` / `BackupException` nếu file bị chỉnh sửa ác ý.
3. **Tái sử dụng `SocketsHttpHandler` trong `RobloxHttpClientFactory`:**
   - *Hiện trạng:* `RobloxHttpClientFactory.CreateClient()` khởi tạo mới `SocketsHttpHandler` cho mỗi lệnh gọi `ValidateCookieAsync` / `GetAuthTicketAsync`.
   - *Đề xuất:* Chia sẻ một `SocketsHttpHandler` dùng chung (với `PooledConnectionLifetime = TimeSpan.FromMinutes(2)`) để tái sử dụng kết nối TLS khi kiểm tra hàng loạt tài khoản.
