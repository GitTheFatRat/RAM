# Báo Cáo Thay Đổi & Nâng Cấp Hệ Thống (So Sánh Bản Mới Nhất vs. Bản Trước)

Tài liệu này tổng hợp chi tiết các điểm khác biệt giữa **phiên bản hiện tại (M39 – M42 + Recovery Launch)** và **phiên bản trước đó**.

---

## 1. Bảng Tóm Tắt So Sánh Nhanh

| Hạng mục | Phiên bản trước (Cũ) | Phiên bản hiện tại (Mới) |
| :--- | :--- | :--- |
| **Xử lý tài khoản bị Cảnh cáo / Moderated khi Launch** | Báo lỗi `Moderated` và **bỏ qua (skip)** luôn tài khoản trong hàng đợi. | **Recovery Launch tự động:** Mở cửa sổ trình duyệt ẩn danh `800x600` đã gắn sẵn cookie đến trang `/not-approved`, hiển thị trạng thái `"Đang chờ xử lý lỗi..."`, chờ người dùng bấm **"I Agree"** rồi **tự động mở lại game** và tiếp tục hàng đợi. |
| **Khóa kích thước cửa sổ Recovery (`800x600`)** | Chỉ có cấu hình độ phân giải chung trong `AppSettings`. | Ghi đè tạm thời `800x600` theo từng phiên/tiến trình trong `RobloxWindowSizer` **mà không làm thay đổi** `AppSettings` toàn cục. |
| **Xử lý Rate-Limit (`HTTP 429`) khi Launch hàng loạt** | Khi gặp 429 giữa chừng, hàng đợi dừng hẳn (`break`), các tài khoản phía sau bị hủy. | **Exponential Backoff thông minh:** Tự động tạm dừng đếm ngược (`60s -> 120s -> 240s`, tối đa `300s`) rồi **thử lại đúng tài khoản đó** và chạy tiếp toàn bộ danh sách còn lại. |
| **Kiểm tra trạng thái tài khoản (`Check` / `Check All`)** | Gửi request liên tục dễ chạm ngưỡng chặn IP (429) của Roblox; khi tài khoản đổi tên không tự cập nhật UI ngay. | Thêm nhịp nghỉ chống chặn IP (`1.5s–2.5s` giữa mỗi nick, tự nghỉ `15s` khi gặp 429) và **đồng bộ tức thì `Username` / `UserId` mới** lên bảng danh sách. |
| **Nhập tài khoản (Add Account) & Chống Arkose FunCaptcha** | Dùng trình duyệt tự động đính kèm CDP (`--remote-debugging-port`), dễ bị hệ thống Arkose Labs của Roblox phát hiện bot và khóa phiên. | **M42 Local Extension Bridge:** Tiện ích mở rộng Chrome/Edge/Brave (`Manifest V3`) lấy cookie từ trình duyệt thật 100%, gửi qua `127.0.0.1:48123/import`, sau đó **chỉ xóa `.ROBLOSECURITY` cục bộ** (giữ nguyên cookie chống bot và không làm die token) rồi chuyển ngay về trang `/login`. |
| **Giao diện người dùng (UI/UX - M41)** | Layout phẳng, thanh công cụ rời rạc, bảng dữ liệu hơi chật (`RowHeight=36-38`), trang Cài đặt không có thanh cuộn chuẩn. | **Windows 11 Fluent Dark (Mica):** Thiết kế lại toàn bộ `MainWindow`, `AccountsPage`, `ServersPage`, `SettingsPage`, `Tokens.xaml` với thẻ nổi (`CommandBarCardStyle`), huy hiệu icon (`SectionIconBadgeStyle`), bảng thoáng (`RowHeight=42`), và cuộn mượt. |

---

## 2. Chi Tiết Từng Tính Năng Mới

### 2.1. Recovery Launch — Tự Động Cứu Tài Khoản Bị Cảnh Cáo (`Warned` / `Moderated`)
* **Vấn đề trước đây:** Khi chạy `Launch Selected` hoặc `Rotation Group`, nếu một tài khoản bị Roblox cảnh cáo (yêu cầu bấm *"I Agree / Reactivate"* trên web), API trả về `AuthErrorCodes.Moderated` và ứng dụng bỏ qua tài khoản đó.
* **Cách hoạt động mới:**
  1. Khi phát hiện mã lỗi `Moderated` trong `ExecuteSequentialLaunchQueueAsync` hoặc `RotationManager`, hệ thống **không bỏ qua** tài khoản.
  2. Trạng thái của tài khoản trên giao diện lập tức chuyển thành **`"Đang chờ xử lý lỗi..."`**.
  3. Hệ thống kích hoạt chế độ ghi đè độ phân giải tạm thời **`800x600`** trong `RobloxWindowSizer` (chỉ áp dụng cho phiên khôi phục, hoàn toàn không ghi đè `AppSettings` của người dùng).
  4. Nếu lấy được `AuthTicket`, ứng dụng mở trực tiếp client; nếu `AuthTicket` bị từ chối do đang bị khóa cảnh cáo, `IncognitoBrowserLauncher.LaunchRecoveryAsync` tự động mở cửa sổ trình duyệt `800x600`, nạp sẵn `.ROBLOSECURITY` và đưa thẳng tới `https://www.roblox.com/not-approved`.
  5. Ngay khi người dùng tích chọn và bấm **"I Agree"** (API `/v1/users/authenticated` trả về `200 OK`), cửa sổ khôi phục tự đóng, tài khoản được cập nhật sang `Valid` và **tự động vào game ngay lập tức**.

---

### 2.2. Chống Rate-Limit (`HTTP 429`) & Tự Động Phục Hồi Hàng Đợi (M39 & M40)
* **Hàng đợi Khởi chạy (`AccountsViewModel` & `RotationManager`):**
  * Thay vì hủy ngang hàng đợi khi gặp lỗi `429 Too Many Requests`, hệ thống sử dụng `LaunchBackoffPolicy.ComputeDelay(consecutiveRateLimits)`:
    * Lần 1: chờ `60 giây`
    * Lần 2: chờ `120 giây`
    * Lần 3+: chờ `240 giây` (tối đa `300 giây`)
  * Hiển thị đồng hồ đếm ngược trực tiếp trên thanh tiến trình, sau đó thử lại chính tài khoản vừa bị chặn mà không bỏ sót bất kỳ nick nào trong nhóm.
* **Kiểm tra toàn bộ tài khoản (`Check All`):**
  * Thêm khoảng nghỉ ngẫu nhiên `1500ms – 2500ms` giữa các lần kiểm tra từng tài khoản.
  * Nếu gặp `429`, tự động tạm dừng `15 giây` rồi kiểm tra lại đúng tài khoản đó.
  * Bổ sung `AccountItemViewModel.UpdateAccount(Account)` để khi tài khoản đổi tên (`Username`) hoặc cập nhật trạng thái, dòng dữ liệu trên UI được làm mới ngay lập tức mà không cần khởi động lại ứng dụng.

---

### 2.3. Local Extension Bridge — Nhập Nick 1-Click Không Bị FunCaptcha (M42)
* **Vấn đề trước đây:** Khi đăng ký hoặc đăng nhập nhiều tài khoản trên cửa sổ trình duyệt điều khiển qua giao thức tự động (CDP), Arkose Labs FunCaptcha dễ nhận diện môi trường tự động và tăng độ khó captcha hoặc khóa phiên.
* **Giải pháp mới (`tools/BrowserExtension/` & `LocalExtensionBridgeService`):**
  1. **Tiện ích mở rộng trình duyệt (`Manifest V3`):**
     * Thư mục: `tools/BrowserExtension/` (đã được đóng gói kèm tại `C:\Users\Administrator\Pictures\Roblox Account Manager\BrowserExtension`).
     * Hoạt động trực tiếp trên trình duyệt Chrome / Edge / Brave thật của người dùng.
  2. **Cầu nối Loopback an toàn (`http://127.0.0.1:48123/import`):**
     * Chỉ mở cổng lắng nghe khi cửa sổ **Add Account (`AddAccountWindow`)** đang mở và tự động đóng cổng ngay khi tắt cửa sổ.
     * Kiểm tra nghiêm ngặt header `Origin` (chỉ chấp nhận `chrome-extension://`, `moz-extension://`, `extension://`; từ chối mọi trang web thường với mã `403 Forbidden`).
     * Tuyệt đối không ghi log chuỗi cookie thô ra file log.
  3. **Quy trình 1-Click mượt mà:**
     * Người dùng đăng nhập/đăng ký nick trên `roblox.com` -> Bấm nút **"Import & Next Account"** trên Extension.
     * Extension gửi `.ROBLOSECURITY` về `RbxManager` -> `RbxManager` xác thực tài khoản và lưu thẳng vào Vault mã hóa.
     * Ngay khi nhận phản hồi `200 OK`, Extension gọi `chrome.cookies.remove` **chỉ xóa duy nhất cookie `.ROBLOSECURITY`** trên trình duyệt (giữ nguyên toàn bộ cookie anti-bot/fingerprint khác và **không** gọi lệnh Logout của Roblox để tránh làm chết cookie vừa lưu), đồng thời tự chuyển tab về `https://www.roblox.com/login` để đăng nhập nick tiếp theo ngay lập tức.

---

### 2.4. Đại Tu Giao Diện Windows 11 Fluent Dark (M41)
* **Hệ thống màu sắc & Token (`Tokens.xaml`):**
  * Nền chính sâu hơn (`#090D16`), thẻ nổi phân lớp rõ ràng (`#111827` / `#161F33`), viền mảnh `1px` (`#1E293B`) và điểm nhấn màu Indigo (`#6366F1`).
* **Cửa sổ chính (`MainWindow.xaml`):**
  * Kích thước chuẩn `1200x760`, hiệu ứng nền `Mica`, bo góc `Round`, thanh điều hướng bên trái (`OpenPaneLength="216"`) với hiệu ứng chuyển trang `FadeInWithSlide`.
* **Trang Tài Khoản (`AccountsPage.xaml`):**
  * Cụm tiêu đề có biểu tượng badge nổi bật; thanh Tìm kiếm + Lọc nhóm + Khởi chạy hàng loạt được gom vào thẻ nổi `CommandBarCardStyle`.
  * Thanh tiến trình `Check All` và `Bulk Launch` được thiết kế dạng banner có viền nhấn màu Indigo.
  * Bảng dữ liệu tăng chiều cao dòng lên `42px` giúp các pill trạng thái (`Valid`, `Expired`, `Error`, `Running`) thoáng và dễ nhìn hơn.
* **Trang Máy Chủ (`ServersPage.xaml`):**
  * Tách thanh công cụ thành thẻ lệnh 2 tầng gọn gàng (Tầng 1: Place ID, Sắp xếp, Làm mới, Tải thêm; Tầng 2: Theo dõi & vào cùng máy chủ với người chơi bất kỳ).
  * Cột số lượng người chơi (`Playing / MaxPlayers`) được hiển thị dạng huy hiệu (pill badge) rõ nét.
* **Trang Cài Đặt (`SettingsPage.xaml`):**
  * Bổ sung `ScrollViewer` cuộn mượt cho toàn bộ 8 thẻ cấu hình (`MaxWidth="940"`).
  * Mỗi thẻ cài đặt đều có biểu tượng nhận diện riêng (`SectionIconBadgeStyle`) và bố cục căn lề đồng nhất.

---

## 3. Hướng Dẫn Cài Nhanh Tiện Ích Mở Rộng (Browser Extension)
1. Mở trình duyệt **Chrome**, **Edge**, hoặc **Brave**.
2. Truy cập trang quản lý tiện ích:
   * Chrome: `chrome://extensions`
   * Edge: `edge://extensions`
3. Bật chế độ **Developer mode (Chế độ dành cho nhà phát triển)** ở góc trên.
4. Nhấn **Load unpacked (Tải tiện ích đã giải nén)** và chọn thư mục:
   * `C:\Users\Administrator\Pictures\Roblox Account Manager\BrowserExtension` (hoặc `tools\BrowserExtension` trong thư mục mã nguồn).
5. Mở `RbxManager.App.exe` -> Bấm **Thêm qua Đăng nhập (Add via Login)** -> Đăng nhập trên trình duyệt và bấm nút **Import & Next Account** trên tiện ích mở rộng.
