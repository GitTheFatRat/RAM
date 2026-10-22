# RbxManager - Trình quản lý đa tài khoản Roblox trên Windows

> **Tuyên bố miễn trừ trách nhiệm quan trọng:**
> RbxManager là công cụ mã nguồn mở phục vụ nhu cầu cá nhân, **hoàn toàn không liên kết, không được ủy quyền hoặc chứng thực bởi Roblox Corporation**. Bạn tự chịu mọi trách nhiệm đối với tài khoản và rủi ro khi sử dụng phần mềm.

---

## 1. Giới thiệu tổng quan

**RbxManager** là ứng dụng Windows Desktop hiệu năng cao, giao diện hiện đại (Fluent Design, hỗ trợ Dark Mode mặc định) viết bằng C# 14 / .NET 10 LTS và WPF-UI, giúp người dùng quản lý an toàn nhiều tài khoản Roblox trên cùng một máy tính cá nhân.

### Các tính năng cốt lõi:
- **Lưu trữ bảo mật cục bộ**: Mã hóa tài khoản và cookie bằng chuẩn **AES-256-GCM** kết hợp với **Windows DPAPI** (`CurrentUser`). Cookie không bao giờ bị lộ dưới dạng plaintext hoặc ghi vào log.
- **Xác thực linh hoạt**: Đăng nhập qua WebView2 cách ly hoặc nhập cookie thủ công. Kiểm tra trạng thái cookie (Hợp lệ / Hết hạn) với 1 click.
- **Khởi chạy trò chơi nhanh chóng**: Khởi chạy trực tiếp tài khoản vào bất kỳ Place ID nào hoặc vào thẳng một Server Job cụ thể qua giao thức `roblox-player:`.
- **Duyệt máy chủ công khai (Server Browser)**: Xem danh sách server công khai, số người chơi hiện tại, ping, fps để chọn server phù hợp mà không cần mở trình duyệt web.
- **Theo dõi phiên & Tự động kết nối lại (Auto-rejoin)**: Theo dõi tiến trình `RobloxPlayerBeta.exe`, phát hiện ngắt kết nối và tự động vào lại phòng với thuật toán Exponential Backoff và Circuit Breaker chống lặp vô tận.
- **Sao lưu & Phục hồi mã hóa**: Xuất/nhập dữ liệu tài khoản với chuẩn mã hóa **PBKDF2-HMAC-SHA512 (600.000 vòng lặp) + AES-256-GCM**, bảo vệ bằng mật khẩu người dùng tự chọn.

---

## 2. Yêu cầu hệ thống

- **Hệ điều hành**: Windows 10 (bản 1809 trở lên) hoặc Windows 11 (kiến trúc x64).
- **Runtime yêu cầu** (đối với bản Framework-dependent):
  - [.NET 10.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
  - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (đã tích hợp sẵn trên hầu hết các bản Windows 10/11 hiện nay).
- **Roblox Client**: Đã cài đặt phiên bản chính thức của Roblox Windows Player.

---

## 3. Cài đặt & Khởi động lần đầu

1. Tải bản nén `RbxManager-win-x64.zip` từ trang phát hành hoặc Artifacts của GitHub Actions.
2. Giải nén vào một thư mục tùy chọn (ví dụ: `C:\Tools\RbxManager`).
3. Chạy tập tin `RbxManager.exe`.
4. Khi khởi chạy lần đầu, ứng dụng sẽ tự động khởi tạo kho lưu trữ mã hóa an toàn tại thư mục dữ liệu ứng dụng của tài khoản Windows hiện tại.

---

## 4. Hướng dẫn sử dụng chi tiết

### 4.1. Thêm tài khoản
Có 2 phương thức để thêm tài khoản vào RbxManager:
1. **Đăng nhập bằng WebView2 (Khuyên dùng)**:
   - Nhấn nút **Thêm tài khoản** và chọn chế độ đăng nhập WebView2.
   - Cửa sổ trình duyệt biệt lập sẽ hiển thị trang đăng nhập chính thức của Roblox (`roblox.com/login`).
   - Đăng nhập tài khoản thông thường (hỗ trợ đầy đủ 2FA, mã Authenticator, email). Sau khi đăng nhập thành công, RbxManager sẽ tự động trích xuất cookie phiên, mã hóa và lưu vào kho lưu trữ.
2. **Nhập Cookie thủ công**:
   - Nhập bí danh (Alias), nhóm (Group - tùy chọn), ghi chú (Note - tùy chọn).
   - Dán cookie `.ROBLOSECURITY` vào trường mật khẩu bảo vệ.
   - Nhấn **Lưu**.

### 4.2. Khởi chạy vào trò chơi
- Trong danh sách tài khoản, chọn tài khoản mong muốn và nhấn nút **Chơi** (hoặc phím tắt `Enter`).
- Nhập **Place ID** của trò chơi bạn muốn tham gia.
- Nếu muốn vào một server cụ thể, nhập thêm **Job ID** (Server GUID).
- Ứng dụng sẽ tự động yêu cầu Roblox cấp Auth Ticket một lần và kích hoạt tiến trình Roblox Player.

### 4.3. Duyệt danh sách máy chủ (Server Browser)
- Chuyển sang trang **Máy chủ**.
- Nhập **Place ID** và nhấn **Tìm kiếm**.
- Danh sách các server công khai đang chạy sẽ hiển thị kèm số lượng người chơi (Playing / Max Players), Ping và FPS.
- Chọn server mong muốn và nhấn **Tham gia** để mở ngay bằng tài khoản đã chọn.

### 4.4. Theo dõi phiên & Tự động kết nối lại (Auto-rejoin)
- Khi trò chơi được khởi chạy thông qua RbxManager, phiên chơi sẽ xuất hiện tại trang **Đang chạy**.
- Nếu bật tính năng **Tự động vào lại** cho phiên đó:
  - Khi tiến trình `RobloxPlayerBeta.exe` đột ngột đóng lại (do disconnect, crash, mạng chập chờn), ứng dụng sẽ tự động khởi động lại sau một khoảng chờ ngắn (backoff).
  - Để bảo vệ tài nguyên hệ thống, bộ ngắt mạch (**Circuit Breaker**) sẽ tự động kích hoạt sau tối đa 5 lần thử thất bại liên tiếp hoặc khi tài khoản bị khóa/hết hạn phiên.

### 4.5. Sao lưu và Khôi phục (Backup & Restore)
- Vào trang **Cài đặt** -> phần **Sao lưu & Khôi phục**.
- **Sao lưu**: Nhấn nút **Xuất sao lưu**, đặt mật khẩu mã hóa mạnh và chọn vị trí lưu tập tin `.rbxbackup`.
- **Khôi phục**: Nhấn nút **Nhập sao lưu**, chọn tập tin sao lưu và nhập đúng mật khẩu để giải mã. Dữ liệu tài khoản sẽ được đồng bộ an toàn vào kho hiện tại.

### 4.6. Chẩn đoán hệ thống (Diagnostics) & Thử nghiệm khô (Dry Run)
- **Thử nghiệm khô (Dry Run)**: Trong hộp thoại Khởi chạy trò chơi, nhấn nút **Thử nghiệm khô** để xem trước định dạng URI giao thức `roblox-player:` hoàn chỉnh với giả lập `<ticket>` mà không cần kết nối mạng hay kích hoạt tiến trình thực tế.
- **Trang Chẩn đoán**: Chọn tab **Chẩn đoán** từ thanh điều hướng bên trái:
  - Kiểm tra môi trường (phiên bản Windows, kiến trúc x64, runtime .NET, trạng thái quyền quản trị Administrator, WebView2 Runtime).
  - Kiểm tra Roblox Client (giao thức `roblox-player:`, đường dẫn thực thi, phát hiện bootstrapper Bloxstrap/Fishstrap).
  - Kiểm tra kết nối mạng không gửi cookie tới các endpoint cốt lõi của Roblox (`roblox.com`, `auth`, `games`, `presence`, `apis`).
  - Tùy chọn kiểm tra tài khoản cụ thể và xem báo cáo đã được khử nhạy cảm (redacted), an toàn 100% để chia sẻ khi gặp sự cố kỹ thuật.

### 4.7. Tùy biến mẫu khởi chạy nâng cao (Advanced Launch Templates)
Trong trường hợp Roblox thay đổi cú pháp tham số giao thức `roblox-player:`, bạn có thể can thiệp bằng cách chỉnh sửa tập tin `%LOCALAPPDATA%\RbxManager\settings.json` trong phần `Advanced`:
```json
{
  "Language": "vi-VN",
  "Advanced": {
    "LaunchUriTemplate": "roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTimeMs}+placelauncherurl:{placeLauncherUrl}",
    "PlaceLauncherUrlTemplate": "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request={request}&browserTrackerId={trackerId}&placeId={placeId}{gameIdParam}"
  }
}
```
*Ghi chú: Nếu giá trị là `null`, ứng dụng sẽ tự động dùng định dạng mặc định tích hợp sẵn. Các placeholder hợp lệ bao gồm `{ticket}`, `{launchTimeMs}`, `{placeLauncherUrl}`, `{trackerId}`, `{request}`, `{placeId}`, `{gameIdParam}`.*

### 4.8. Phím tắt thao tác nhanh (Keyboard Shortcuts)
Ứng dụng hỗ trợ đầy đủ điều hướng bằng bàn phím để tối ưu hóa năng suất làm việc:
- `Ctrl + N`: Thêm tài khoản nhanh qua giao diện đăng nhập WebView2.
- `Ctrl + F`: Nhảy nhanh đến ô tìm kiếm tài khoản.
- `F5`: Làm mới danh sách tài khoản hoặc dữ liệu trang hiện tại.
- `Enter`: Khởi chạy tài khoản đang chọn vào trò chơi.
- `Delete`: Xóa tài khoản đang chọn (có hộp thoại xác nhận).
- `Esc`: Đóng các hộp thoại (ContentDialog).

---

## 5. Lưu ý quan trọng về tính năng Đa phiên (Multi-Instance)

- **Cảnh báo rủi ro**: Roblox không chính thức hỗ trợ việc chạy đồng thời nhiều cửa sổ Roblox Player trên cùng một hệ điều hành. Các biện pháp can thiệp có thể vi phạm Điều khoản Dịch vụ (ToS) của Roblox hoặc kích hoạt hệ thống phát hiện hành vi bất thường.
- Trong phiên bản hiện tại, RbxManager cung cấp cấu trúc kiểm soát an toàn (`NamedMutexInstanceGate`) nhưng mặc định giữ tính năng này ở trạng thái cảnh báo/vô hiệu hóa cho tới khi có cơ chế an toàn tuyệt đối được kiểm chứng.

---

## 6. Dữ liệu lưu ở đâu & Cách gỡ cài đặt

### Vị trí lưu trữ dữ liệu
Toàn bộ dữ liệu của RbxManager được lưu trữ cục bộ tại máy tính của bạn:
- Kho tài khoản mã hóa: `%LOCALAPPDATA%\RbxManager\vault.dat`
- Cài đặt ứng dụng: `%LOCALAPPDATA%\RbxManager\settings.json`
- Nhật ký hoạt động: `%LOCALAPPDATA%\RbxManager\logs\app-YYYYMMDD.log` (tự động xóa sau 14 ngày)

### Cách gỡ cài đặt hoàn toàn
RbxManager là ứng dụng Portable (không can thiệp Registry hệ thống). Để gỡ cài đặt hoàn toàn:
1. Đóng ứng dụng RbxManager.
2. Xóa thư mục chứa tập tin `RbxManager.exe`.
3. Nhấn tổ hợp phím `Win + R`, nhập `%LOCALAPPDATA%` và xóa hoàn toàn thư mục `RbxManager`.

---

## 7. Các câu hỏi thường gặp (FAQ)

**Q: Cookie của tôi có bị gửi ra ngoài mạng internet không?**
> **A:** Tuyệt đối không. Cookie chỉ được giải mã tạm thời trong RAM khi gửi yêu cầu trực tiếp qua kết nối HTTPS tới các máy chủ chính thức của Roblox (`*.roblox.com`) để xin Auth Ticket. Ứng dụng không có cơ chế thu thập dữ liệu (telemetry), không có server trung gian và không có lưu trữ đám mây.

**Q: Người dùng khác cùng đăng nhập vào máy tính có xem được tài khoản của tôi không?**
> **A:** Không. Khóa bảo vệ dữ liệu được neo chặt vào tài khoản Windows của bạn thông qua Windows DPAPI (`DataProtectionScope.CurrentUser`). Các tài khoản Windows khác trên cùng máy tính không thể giải mã được tập tin `vault.dat`.

**Q: Tại sao tài khoản của tôi chuyển sang trạng thái "Hết hạn"?**
> **A:** Khi bạn đăng xuất khỏi trình duyệt hoặc đổi mật khẩu trên trang chủ Roblox, Roblox sẽ hủy phiên đăng nhập cũ và vô hiệu hóa cookie đó. Bạn chỉ cần cập nhật lại cookie mới hoặc đăng nhập lại qua WebView2.
