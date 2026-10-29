# Báo Cáo Rà Soát & Khắc Phục Hệ Thống Đợt 2 (`report-2.md`): Ổn Định Đa Phiên, Sửa Lỗi Hệ Thống & Bảo Vệ Tài Khoản Khỏi CAPTCHA / Rate Limit

**Ngày thực hiện:** 2026-10-26  
**Phạm vi kiểm tra & triển khai:** Toàn bộ `src/Core/`, `src/Infrastructure/`, `src/App.Wpf/` và bộ kiểm thử `tests/`  
**Trạng thái kiểm thử sau triển khai:**
- `RbxManager.Core.Tests`: **93 / 93 PASS**
- `RbxManager.App.Tests`: **54 / 54 PASS**
- `RbxManager.Infrastructure.Tests`: **203 / 203 PASS**
- **Tổng cộng:** **350 / 350 Unit Tests PASS (0 Warnings, 0 Errors)**

---

## Mục Lục
1. [Nhóm 1: Khắc Phục Lỗi CAPTCHA, `403 Forbidden` & Bảo Vệ Tài Khoản](#1-nhóm-1-khắc-phục-lỗi-captcha-403-forbidden--bảo-vệ-tài-khoản)
2. [Nhóm 2: Sửa Lỗi Quản Lý Phiên, Theo Dõi Log & Đa Tiến Trình](#2-nhóm-2-sửa-lỗi-quản-lý-phiên-theo-dõi-log--đa-tiến-trình)
3. [Nhóm 3: Sửa Lỗi Ép Độ Phân Giải Cửa Sổ (`WindowSizeLock` / `RobloxWindowSizer`) & Giao Diện Cài Đặt](#3-nhóm-3-sửa-lỗi-ép-độ-phân-giải-cửa-sổ-windowsizelock--robloxwindowsizer--giao-diện-cài-đặt)
4. [Nhóm 4: Sửa Lỗi Hạ Tầng Mạng, DI Lifecycle & An Toàn Dữ Liệu Vault](#4-nhóm-4-sửa-lỗi-hạ-tầng-mạng-di-lifecycle--an-toàn-dữ-liệu-vault)
5. [Nhóm 5: Hướng Dẫn Vận Hành Tối Ưu Để Bảo Vệ Tài Khoản](#5-nhóm-5-hướng-dẫn-vận-hành-tối-ưu-để-bảo-vệ-tài-khoản)

---

## 1. Nhóm 1: Khắc Phục Lỗi CAPTCHA, `403 Forbidden` & Bảo Vệ Tài Khoản

### Giải pháp 1.1: Khắc phục lỗi CAPTCHA khi Đăng nhập & Đăng ký trực tiếp trên IP Việt Nam (`NewLogin` / `NewLogin#signup`)
- **File & Vị trí:** [`src/App.Wpf/Services/IncognitoBrowserLauncher.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/Services/IncognitoBrowserLauncher.cs)
- **Nguyên nhân (Why):**
  1. Trên IP Việt Nam (`rbx-ip2=1`), các đường dẫn `/CreateAccount` và `/Login` bị máy chủ Roblox chuyển hướng `302` và treo kết nối (do phân vùng VNG Games), chỉ có duy nhất endpoint `https://www.roblox.com/NewLogin` trả về `HTTP 200 OK` trực tiếp trong 20ms.
  2. Tuy nhiên, khi tạo tài khoản trực tiếp trên `/NewLogin#signup`:
     - Đoạn mã cũ can thiệp vào prototype trình duyệt (`delete Object.getPrototypeOf(navigator).webdriver` và `--disable-blink-features=AutomationControlled`), bị hệ thống chống gian lận của Arkose Labs / PerimeterX phát hiện là trình duyệt bị chỉnh sửa, dẫn đến khóa vòng lặp CAPTCHA vô tận.
     - Ô nhập Username/Password gọi API `auth.roblox.com/v1/usernames/validate` và `v2/passwords/validate` liên tục mỗi `350ms` khi gõ phím, làm spam 10–15 request lên `auth.roblox.com` trước cả khi bấm nút Đăng ký.
- **Giải pháp đã triển khai:**
  - Giữ cơ chế Đăng nhập (`https://www.roblox.com/NewLogin`) và Đăng ký trực tiếp trên IP Việt Nam (`https://www.roblox.com/NewLogin#signup`) kết hợp `Page.setBypassCSP` trước khi điều hướng để cho phép Web Worker `blob:` của thử thách `"Press and hold"` hoạt động trên trang `/NewLogin` mà **không cần bật/đổi VPN** (tránh bị khóa tài khoản do dùng IP VPN dùng chung).
  - Loại bỏ hoàn toàn lệnh xóa `navigator.webdriver` và cờ `--disable-blink-features=AutomationControlled` để giữ nguyên bản chữ ký trình duyệt chuẩn.
  - Tăng thời gian trễ (debounce) khi kiểm tra Username/Password từ `350ms` lên `900ms` để giảm 80% số lượng request phụ gửi tới `auth.roblox.com` trước khi bấm Đăng ký.

---

### Giải pháp 1.2: Sử dụng Hồ sơ Trình duyệt Bền vững (`PersistentProfileDir`) kết hợp xóa chọn lọc `.ROBLOSECURITY`
- **File & Vị trí:** [`src/App.Wpf/Services/IncognitoBrowserLauncher.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/Services/IncognitoBrowserLauncher.cs#L30-L375)
- **Nguyên nhân (Why):**
  Việc tạo mới và xóa sạch thư mục tạm (`RbxManager_Browser_<guid>`) ở mỗi lần mở trình duyệt làm mất toàn bộ cookie định danh thiết bị hợp lệ của Roblox và Cloudflare/Arkose (`RBXEventTrackerV2`, `rbx-ip2`, `_cfuvid`, cache bundle JS), khiến mỗi lần đăng nhập đều bị hệ thống chống bot đánh giá là một thiết bị trắng hoàn toàn mới.
- **Giải pháp đã triển khai:**
  - Sử dụng thư mục hồ sơ cố định tại `%LOCALAPPDATA%\RbxManager\BrowserProfile` (chỉ tạo thư mục tạm nếu đang mở nhiều cửa sổ trình duyệt cùng lúc).
  - Bổ sung hàm `ClearRobloxSecurityCookieAsync` gọi lệnh CDP `Network.deleteCookies` chỉ xóa riêng cookie `.ROBLOSECURITY` ngay khi mở trình duyệt và ngay sau khi thu thập xong cookie:
    1. Trình duyệt luôn ở trạng thái đăng xuất sẵn sàng cho tài khoản tiếp theo.
    2. Cookie `.ROBLOSECURITY` nhạy cảm không bị lưu lại trên ổ cứng của trình duyệt.
    3. Không gọi API `/logout` của Roblox (tránh làm vô hiệu hóa cookie vừa lấy trên máy chủ Roblox).
    4. Giữ nguyên các token tin cậy thiết bị (`RBXEventTrackerV2`, `rbx-ip2`, cache tĩnh) giúp giảm thiểu tối đa tần suất xuất hiện CAPTCHA.

---

### Giải pháp 1.3: Боộ nhớ đệm xác thực Cookie 5 phút (`ValidationCacheTtl`) & Đồng bộ xóa Cache khi `401 Unauthorized`
- **File & Vị trí:**
  - [`src/Infrastructure/Auth/RobloxAuthService.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Auth/RobloxAuthService.cs#L27-L280)
  - [`src/Infrastructure/DependencyInjection.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/DependencyInjection.cs#L43)
- **Nguyên nhân (Why):**
  1. Mỗi lần kiểm tra tài khoản, mở game hoặc tự động vào lại đều gọi lại `GET https://users.roblox.com/v1/users/authenticated`, gây lãng phí hạn mức request trên cùng một IP.
  2. `IRobloxAuthService` trước đó được đăng ký là `AddTransient`, khiến `_validationCache` bị tạo mới ở từng dịch vụ thay vì dùng chung toàn ứng dụng.
- **Giải pháp đã triển khai:**
  - Chuyển đăng ký `IRobloxAuthService` sang `AddSingleton` để dùng chung bộ nhớ đệm `_validationCache` (lưu theo mã băm `SHA-256` của cookie trong 5 phút).
  - Tự động xóa mục cache ngay lập tức nếu `ValidateCookieAsync` hoặc `GetAuthTicketAsync` nhận phản hồi `401 Unauthorized`.
  - Ánh xạ cả mã `429 TooManyRequests` và `403 Forbidden` (sau khi đã thử CSRF) sang `AuthErrorCodes.RateLimited` ở cả hai hàm `ValidateCookieAsync` và `GetAuthTicketAsync`.

---

### Giải pháp 1.4: Giãn cách tự động (Jitter + Exponential Backoff) & Cầu chì ngắt mạch (`RateLimited` Circuit Breaker) trên mọi luồng
- **File & Vị trí:**
  - [`src/Infrastructure/Launch/RobloxLauncher.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Launch/RobloxLauncher.cs#L22-L214)
  - [`src/App.Wpf/ViewModels/AccountsViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/AccountsViewModel.cs#L562-L600)
  - [`src/App.Wpf/ViewModels/AddAccountViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/AddAccountViewModel.cs#L208-L270)
  - [`src/App.Wpf/ViewModels/ServersViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/ServersViewModel.cs#L290-L318)
  - [`src/Infrastructure/Rotation/RotationManager.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Rotation/RotationManager.cs#L154-L185)
- **Giải pháp đã triển khai:**
  1. **`RobloxLauncher`:** Áp dụng độ trễ tối thiểu 20 giây cộng thêm nhiễu ngẫu nhiên (`1.5s–6.0s` jitter) giữa các lần lấy ticket khởi chạy, và kích hoạt thời gian nghỉ lũy thừa (`30s -> 60s -> 120s -> 240s`, tối đa `300s`) khi gặp `AuthErrorCodes.RateLimited`.
  2. **`AddAccountViewModel.ImportAccountsAsync`:** Bổ sung nhịp nghỉ `500ms` giữa các lần xác thực cookie khi nhập hàng loạt và dừng ngay vòng lặp nếu gặp `AuthErrorCodes.RateLimited`.
  3. **`AccountsViewModel.CheckAllAsync`:** Tự động bỏ qua các tài khoản đang chạy trong game (`item.IsRunning == true`) để không tốn request kiểm tra lại phiên đang hoạt động, và dừng hàng đợi ngay khi gặp `RateLimited`.
  4. **`ServersViewModel.RunJoinQueueAsync` & `RotationManager.LaunchGroupAsync`:** Bổ sung cầu chì ngắt mạch dừng ngay việc mở các tài khoản tiếp theo trong nhóm/hàng đợi khi gặp `AuthErrorCodes.RateLimited`.

---

### Giải pháp 1.5: Loại bỏ can thiệp Handle chéo tiến trình & khóa độc quyền `RobloxCookies.dat`
- **File & Vị trí:** [`src/Infrastructure/Instance/NamedMutexInstanceGate.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Instance/NamedMutexInstanceGate.cs)
- **Nguyên nhân (Why):**
  Việc dùng `NtQuerySystemInformation` / `DuplicateHandle(DUPLICATE_CLOSE_SOURCE)` để đóng handle của tiến trình `RobloxPlayerBeta.exe` đang chạy và việc giữ `FileShare.None` trên file `RobloxCookies.dat` khiến client Roblox không thể đọc/ghi kho cookie cục bộ và dễ gây văng game hoặc buộc xác thực lại.
- **Giải pháp đã triển khai:**
  Chỉ giữ lại cơ chế tạo Named Mutex chuẩn (`ROBLOX_singletonMutex` và `ROBLOX_singletonEvent`) trong tiến trình quản lý, loại bỏ hoàn toàn việc quét/đóng handle chéo tiến trình và gỡ bỏ khóa file `RobloxCookies.dat`.

---

## 2. Nhóm 2: Sửa Lỗi Quản Lý Phiên, Theo Dõi Log & Đa Tiến Trình

### Giải pháp 2.1: Tách tác vụ `Auto-Rejoin` chạy nền không làm nghẽn vòng lặp quét tiến trình 2 giây
- **File & Vị trí:** [`src/Infrastructure/Sessions/SessionTracker.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Sessions/SessionTracker.cs#L281-L355)
- **Nguyên nhân (Why):**
  Trong `ExecuteAsync`, lệnh `await PollProcessesAsync` trước đó chờ `await Task.WhenAll(rejoinTasks)` hoàn tất. Vì mỗi tác vụ `HandleAutoRejoinAsync` phải chờ thời gian đếm ngược (`10s–80s`), chờ `_launchGate` (`20s+`) và dò PID (`tối đa 60s`), chỉ cần 1 tài khoản bị văng là vòng lặp quét tiến trình của toàn bộ các tài khoản khác bị đứng hình từ 1–2 phút.
- **Giải pháp đã triển khai:**
  Bổ sung tham số `waitForRejoins: false` khi gọi từ vòng lặp nền `ExecuteAsync` kèm hàm bọc an toàn `RunRejoinSafelyAsync`, giúp vòng lặp phát hiện tiến trình thoát vẫn duy trì đúng chu kỳ 2 giây trong khi các tác vụ Auto-Rejoin chạy nền độc lập (vẫn giữ mặc định `waitForRejoins = true` cho các bài kiểm thử đơn vị).

---

### Giải pháp 2.2: Bảo vệ tiến trình Roblox mới khởi chạy khỏi bị `KillHeadlessRobloxProcesses` tắt nhầm
- **File & Vị trí:** [`src/App.Wpf/ViewModels/AccountsViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/AccountsViewModel.cs#L1212-L1246)
- **Nguyên nhân (Why):**
  `KillHeadlessRobloxProcesses` tắt mọi tiến trình `RobloxPlayerBeta` chưa có trong `_activeSessions` nếu `MainWindowHandle == IntPtr.Zero`. Khi một tài khoản khác đang được `Auto-Rejoin` hoặc `RotationManager` khởi chạy đồng thời, trong 2–5 giây đầu tiên cửa sổ chính chưa kịp hiện (`MainWindowHandle == IntPtr.Zero`) và PID chưa kịp gán xong, tiến trình đó sẽ bị tắt nhầm.
- **Giải pháp đã triển khai:**
  Chỉ kết thúc các tiến trình `RobloxPlayerBeta` không có cửa sổ nếu thời gian sống của tiến trình đã vượt quá **45 giây** (`(DateTime.Now - proc.StartTime).TotalSeconds > 45`).

---

### Giải pháp 2.3: Đồng bộ nút `KillInstance` trên tab Tài khoản với `SessionTracker.CloseSessionAsync`
- **File & Vị trí:** [`src/App.Wpf/ViewModels/AccountsViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/AccountsViewModel.cs#L1200-L1210)
- **Nguyên nhân (Why):**
  Khi người dùng bấm tắt game trực tiếp từ danh sách Tài khoản (`KillInstance`), hàm chỉ gọi `target.KillInstance()` (`Process.Kill`) mà không báo cho `_sessionTracker.CloseSessionAsync(pid)`. Hệ quả là `SessionTracker` tưởng game bị crash ngoài ý muốn (`intentional == false`) và kích hoạt `Auto-Rejoin` mở lại game vừa bị người dùng đóng.
- **Giải pháp đã triển khai:**
  Gọi `_ = _sessionTracker.CloseSessionAsync(pid, graceful: false)` trước khi gọi `target?.KillInstance()` để đánh dấu đóng chủ đích và hủy mọi tác vụ Rejoin đang chờ.

---

### Giải pháp 2.4: Khắc phục lỗi `RobloxLogWatcher` ngừng đọc log vĩnh viễn sau từ khóa ngắt kết nối trong 60 giây đầu
- **File & Vị trí:** [`src/Infrastructure/Processes/RobloxLogWatcher.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Processes/RobloxLogWatcher.cs#L100-L210)
- **Nguyên nhân (Why):**
  1. Nếu `RobloxPlayerBeta.exe` tạo file log chậm hơn thời điểm phát hiện PID vài giây, `ResolveLogFile` trả về `null` và kết thúc luôn việc theo dõi log của phiên đó.
  2. Trong `TailLogAsync`, khi bắt gặp từ khóa lỗi mạng nhưng đang trong 60 giây ân hạn khởi động (`StartupGracePeriod`) hoặc đang ở chế độ `NotifyOnly`, câu lệnh `return;` làm thoát hoàn toàn khỏi hàm `TailLogAsync`, khiến phiên game đó không bao giờ được theo dõi log nữa trong suốt thời gian chơi còn lại.
- **Giải pháp đã triển khai:**
  - Cho phép `TailLogAsync` thử tìm lại file log định kỳ mỗi 2 giây (tối đa 10 lần) nếu tiến trình vừa chạy chưa kịp tạo file log trên đĩa.
  - Thay `return;` bằng `break;` khi phát hiện từ khóa để vòng lặp ngoài tiếp tục đọc các dòng log mới sau đó.

---

## 3. Nhóm 3: Sửa Lỗi Ép Độ Phân Giải Cửa Sổ (`WindowSizeLock` / `RobloxWindowSizer`) & Giao Diện Cài Đặt

### Giải pháp 3.1: Khắc phục lỗi rơi sự kiện ép kích thước khi người dùng kéo thả cửa sổ (`WindowSizeLock`) & khi Roblox tạo lại HWND
- **File & Vị trí:**
  - [`src/Infrastructure/Native/WindowSizeLock.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Native/WindowSizeLock.cs#L95-L215)
  - [`src/App.Wpf/Services/RobloxWindowSizer.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/Services/RobloxWindowSizer.cs#L40-L105)
- **Nguyên nhân (Why):**
  1. Khi người dùng kéo cạnh cửa sổ, Windows bắn hàng chục sự kiện `EVENT_OBJECT_LOCATIONCHANGE` mỗi giây. Sau 4 lần chỉnh đầu tiên trong 1 giây (`MaxBurstCorrections = 4`), các sự kiện tiếp theo bị bỏ qua mà không có lần chốt cuối cùng sau khi hết cửa sổ 1 giây, khiến cửa sổ giữ nguyên kích thước sai.
  2. Khi Roblox chuyển từ Splash sang cửa sổ D3D11 chính và đổi `HWND`, `OnWinEvent` trước đó chỉ nhận `HWND` mới nếu nhận đúng sự kiện `EVENT_OBJECT_SHOW`, bỏ lỡ trường hợp cửa sổ mới chỉ phát sự kiện `EVENT_OBJECT_LOCATIONCHANGE`.
- **Giải pháp đã triển khai:**
  - Bổ sung cơ chế `ScheduleTrailingEnforcement()` tự động áp lại kích thước chuẩn ngay khi hết thời gian chống dội 1000ms.
  - Tự động cập nhật sang `HWND` mới khi cửa sổ cũ không còn hiển thị (`!IsWindowVisible(_hWnd)`).
  - Tăng số chu kỳ kiểm tra sau khởi chạy trong `RobloxWindowSizer.ApplyIfEnabledAsync` lên 6 lần (15 giây) và luôn gọi `MoveResizeWindow` ở mỗi chu kỳ để chống lại việc Roblox tự phóng to cửa sổ sau khi tải xong place.

---

### Giải pháp 3.2: Chặn lưu trùng lặp và kiểm tra biên kích thước trên trang Cài đặt (`SettingsPage` / `SettingsViewModel`)
- **File & Vị trí:**
  - [`src/App.Wpf/Views/Pages/SettingsPage.xaml`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/Views/Pages/SettingsPage.xaml#L204-L212)
  - [`src/App.Wpf/ViewModels/SettingsViewModel.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/App.Wpf/ViewModels/SettingsViewModel.cs#L245-L310)
- **Giải pháp đã triển khai:**
  - Chuyển binding `IsChecked` của công tắc `Settings_CustomResolutionToggle` sang `Mode=OneWay` để tránh việc vừa kích hoạt `ToggleCustomResolutionCommand` vừa kích hoạt setter gây 2 lần ghi file cài đặt song song.
  - Bổ sung kiểm tra biên `[MinimumResolutionDimension..MaximumResolutionDimension]` trong `OnResolutionWidthChanged` và `OnResolutionHeightChanged`.

---

## 4. Nhóm 4: Sửa Lỗi Hạ Tầng Mạng, DI Lifecycle & An Toàn Dữ Liệu Vault

### Giải pháp 4.1: Tái sử dụng `SocketsHttpHandler` Pool trong `RobloxHttpClientFactory`
- **File & Vị trí:** [`src/Infrastructure/Http/RobloxHttpClientFactory.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Http/RobloxHttpClientFactory.cs)
- **Nguyên nhân (Why):**
  Mỗi lần gọi `CreateClient()` trước đây đều khởi tạo mới một `HttpClientHandler` riêng biệt và hủy ngay sau request, làm mất khả năng tái sử dụng kết nối TCP/TLS và gây nguy cơ cạn cổng mạng (`Socket Exhaustion`) khi chạy thời gian dài.
- **Giải pháp đã triển khai:**
  Sử dụng một pipeline `SocketsHttpHandler` dùng chung (`PooledConnectionLifetime = TimeSpan.FromMinutes(2)`, `UseCookies = false`, `disposeHandler: false`).

---

### Giải pháp 4.2: Kiểm tra cận trên (`Maximum...`) cho tham số Argon2id KDF khi đọc Vault & File Backup
- **File & Vị trí:**
  - [`src/Infrastructure/Storage/EncryptedFileVault.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Storage/EncryptedFileVault.cs#L320-L355)
  - [`src/Infrastructure/Backup/EncryptedBackupService.cs`](file:///C:/Users/Administrator/Desktop/RAM%20CLONE/src/Infrastructure/Backup/EncryptedBackupService.cs#L220-L255)
- **Nguyên nhân (Why):**
  Header KDF đọc các trường `uint` (`iterations`, `memorySizeKiB`, `parallelism`) và chỉ kiểm tra cận dưới (`< Minimum...`) trước khi ép kiểu sang `(int)`. Nếu file bị hỏng có giá trị `> int.MaxValue`, phép ép kiểu `(int)` sẽ bị tràn số âm hoặc cấp phát bộ nhớ quá mức.
- **Giải pháp đã triển khai:**
  Bổ sung hằng số giới hạn trên (`MaximumIterations = 64`, `MaximumMemorySizeKiB = 1048576` [1 GiB], `MaximumParallelism = 64`) và ném `VaultCorruptedException` / `InvalidDataException` rõ ràng trước khi ép kiểu.

---

## 5. Nhóm 5: Hướng Dẫn Vận Hành Tối Ưu Để Bảo Vệ Tài Khoản

1. **Không bao giờ bấm nút "Log Out" (Đăng xuất) trên web sau khi đã lấy Cookie:**
   - Khi bấm "Log Out" trên trang web Roblox, trình duyệt sẽ gửi lệnh hủy phiên lên máy chủ Roblox làm cookie `.ROBLOSECURITY` vừa lưu bị chết ngay lập tức (`401 Unauthorized`). Khi dùng trình duyệt tích hợp của phần mềm, chỉ cần đăng nhập xong và để phần mềm tự động thu thập cookie & đóng cửa sổ.
2. **Không bấm "Kiểm tra tất cả" (`Check All`) ngay trước khi bấm "Mở hàng loạt" (`Launch Selected`):**
   - Quá trình mở game đã tự động xác thực phiên và lưu cache 5 phút. Việc vừa kiểm tra toàn bộ danh sách xong lại mở hàng loạt ngay lập tức sẽ làm tăng gấp đôi số lượng request xác thực từ cùng một IP.
3. **Giữ nguyên IP/Quốc gia cố định cho mỗi nhóm tài khoản:**
   - Hệ thống bảo vệ phiên của Roblox (Session Protection) gắn chặt `.ROBLOSECURITY` với dải IP/quốc gia lúc đăng nhập. Việc bật/tắt VPN đổi quốc gia liên tục sẽ khiến Roblox buộc xác thực lại qua CAPTCHA hoặc khóa phiên tạm thời.
4. **Sử dụng quy mô nhóm hợp lý (5–10 tài khoản/nhóm):**
   - Kết hợp hàng đợi tuần tự (60–120 giây/tài khoản) và độ trễ khởi chạy (`>= 20 giây`) giúp lưu lượng mạng phân bổ tự nhiên như người dùng thật, tránh kích hoạt giới hạn `429 Too Many Requests`.
