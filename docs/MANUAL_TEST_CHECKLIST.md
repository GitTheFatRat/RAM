# Hướng dẫn Kiểm thử Thủ công (Manual Test Checklist)

> **CẢNH BÁO QUAN TRỌNG:**
> 1. Chỉ sử dụng **TÀI KHOẢN PHỤ (THROWAWAY ACCOUNT)** để kiểm thử. Tuyệt đối không dùng tài khoản chính (main account) có giá trị cao.
> 2. Tuyệt đối không dán cookie `.ROBLOSECURITY` vào bất kỳ trang web, ghi chú hoặc phần mềm nào khác ngoài RbxManager.

---

## Danh sách các bước kiểm tra thực tế

### 0. Chạy Chẩn đoán hệ thống (Diagnostics) & Báo cáo sự cố
- [ ] Mở ứng dụng RbxManager, chuyển sang trang **Chẩn đoán (Diagnostics)**.
- [ ] Chọn tài khoản thử nghiệm và nhấn **Chạy chẩn đoán (Run diagnostics)**.
- [ ] Kiểm tra báo cáo hoàn chỉnh hiển thị đủ các phần: Environment, Roblox Client, Network, Templates, Account Check.
- [ ] Nhấn nút **Sao chép báo cáo (Copy report)** và kiểm tra snackbar xác nhận.
- [ ] Báo cáo luôn bắt đầu bằng: `Safe to share: redacted. No cookies or tickets.` và tuyệt đối không chứa cookie hoặc auth ticket.
- [ ] **LƯU Ý QUAN TRỌNG KHI GẶP LỖI**: Khi bất kỳ bước kiểm thử nào (đặc biệt khi xác thực cookie Validate hoặc lấy vé Ticket thất bại) xảy ra sự cố, hãy vào ngay trang **Chẩn đoán (Diagnostics)**, chọn tài khoản đó và nhấn **Chạy chẩn đoán (Run diagnostics)**, sau đó nhấn **Sao chép báo cáo (Copy report)** và gửi toàn bộ báo cáo cho Tech Lead. Báo cáo chẩn đoán đã được che giấu toàn bộ bí mật, tự động che tên người dùng máy tính (`<user>`), hiển thị đầy đủ thông tin chuẩn đoán (Stage, HttpStatus, RobloxErrorCodes, RobloxMessage, CsrfTokenObtained, TicketHeaderPresent, ResponseHeaderNames) giúp Tech Lead xác định chính xác nguyên nhân mà không sợ lộ thông tin bảo mật.

### 1. Thêm tài khoản qua WebView2 & Xác thực trạng thái
- [ ] Mở ứng dụng RbxManager.
- [ ] Nhấn nút **Thêm tài khoản**, chọn phương thức đăng nhập WebView2.
- [ ] Tiến hành đăng nhập bằng tài khoản thử nghiệm trên giao diện web của Roblox.
- [ ] **Kết quả mong đợi**: Cửa sổ WebView2 tự động đóng sau khi đăng nhập thành công. Tài khoản xuất hiện trên danh sách với Tên người dùng (Username) chính xác và trạng thái **Hợp lệ (Valid)**.

---

### 2. Khởi động lại ứng dụng & Kiểm tra tính bảo mật của Vault
- [ ] Tắt hoàn toàn ứng dụng RbxManager.
- [ ] Mở lại ứng dụng: Tài khoản và nhóm đã lưu trước đó vẫn hiển thị đầy đủ, nguyên vẹn.
- [ ] Nhấn `Win + R`, mở đường dẫn `%LOCALAPPDATA%\RbxManager\vault.dat` bằng Notepad.
- [ ] **Kết quả mong đợi**: Tập tin ở định dạng nhị phân, 4 byte đầu tiên là chuỗi nhận diện `RBXV`, toàn bộ nội dung còn lại là dữ liệu mã hóa ngẫu nhiên không thể đọc được. Tuyệt đối không tìm thấy bất kỳ chuỗi văn bản nào chứa bí danh (Alias), tên người dùng hoặc cookie.

---

### 3. Kiểm tra tất cả tài khoản & Xử lý cookie hết hạn
- [ ] Trên trình duyệt web bên ngoài, truy cập tài khoản thử nghiệm trên roblox.com và chọn **Đăng xuất (Log out)** phiên đăng nhập đó.
- [ ] Trong RbxManager, nhấn nút **Kiểm tra tất cả** (Check all).
- [ ] **Kết quả mong đợi**: Trạng thái tài khoản vừa đăng xuất chuyển sang **Hết hạn (Expired)** hoặc **Không hợp lệ (Invalid)**. Thông báo snackbar hiển thị kết quả kiểm tra mà không gây treo ứng dụng.

---

### 4. Khởi chạy vào Place & Khởi chạy vào Server Job cụ thể
- [ ] Chọn tài khoản hợp lệ, nhấn nút **Chơi** (hoặc nhấn phím `Enter`).
- [ ] Nhập một Place ID công khai (ví dụ: `1818`). Nhấn **Khởi chạy**. Tiến trình Roblox Player mở lên và kết nối vào game thành công.
- [ ] Chuyển sang trang **Máy chủ (Servers)**, nhập Place ID `1818` và nhấn **Tìm kiếm**.
- [ ] Chọn một server trong danh sách và nhấn **Tham gia**.
- [ ] **Kết quả mong đợi**: Tiến trình Roblox khởi chạy thành công vào đúng phòng chơi (Job ID) tương ứng.

---

### 5. Theo dõi tiến trình & Cơ chế Tự động kết nối lại (Auto-rejoin)
- [ ] Khi Roblox đang chạy, chuyển sang trang **Đang chạy (Running)**.
- [ ] Kiểm tra thông tin phiên: Tên tài khoản, Place ID, PID của `RobloxPlayerBeta.exe`, thời gian bắt đầu.
- [ ] Bật tùy chọn **Tự động vào lại** cho phiên đó.
- [ ] Mở Task Manager trên Windows, tìm tiến trình `RobloxPlayerBeta.exe` và chọn **End task**.
- [ ] **Kết quả mong đợi**: RbxManager phát hiện tiến trình bị tắt, hiển thị trạng thái kết nối lại và tự động mở lại Roblox sau thời gian chờ (Exponential Backoff).
- [ ] Tiếp tục ép tắt tiến trình liên tục 5 lần.
- [ ] **Kết quả mong đợi**: Bộ ngắt mạch (**Circuit Breaker**) kích hoạt, dừng việc thử lại và thông báo cho người dùng, tránh vòng lặp khởi chạy vô tận.

---

### 6. Đa phiên (Multi-Instance)
- [ ] Đảm bảo toàn bộ tiến trình Roblox (`RobloxPlayerBeta.exe`) đã đóng hoàn toàn trước khi bắt đầu.
- [ ] Vào trang **Cài đặt**, bật tùy chọn **Bật chạy nhiều phiên Roblox** (Enable Multi-Instance Roblox).
- [ ] Hộp thoại cảnh báo rủi ro xuất hiện; đọc kỹ và nhấn **Đồng ý**. Trạng thái gạt chuyển sang Bật thành công.
- [ ] Thử mở một tiến trình Roblox bất kỳ bên ngoài rồi thử tắt/bật lại tùy chọn: Ứng dụng từ chối bật với thông báo yêu cầu đóng Roblox trước.
- [ ] Đóng Roblox, bật lại tùy chọn Đa phiên.
- [ ] Sử dụng **HAI TÀI KHOẢN PHỤ (THROWAWAY ACCOUNTS)**:
  - Khởi chạy tài khoản A vào một Place bất kỳ (ví dụ: `1818`).
  - Đợi cửa sổ game của tài khoản A xuất hiện và kết nối vào phòng.
  - Khởi chạy tiếp tài khoản B vào cùng Place hoặc Place khác.
- [ ] **Ghi nhận kết quả thực tế**:
  - Quan sát xem cửa sổ Roblox của tài khoản B có mở lên song song và cả hai tiến trình cùng duy trì trạng thái hoạt động (alive) hay không, hoặc Roblox có kích hoạt cơ chế phát hiện/đóng cửa sổ thứ hai không.
  - *Lưu ý*: Nếu client thứ 2 bị đóng bởi Roblox, ghi nhận lại kết quả để báo cáo (không can thiệp hoặc chèn mã vào bộ nhớ của tiến trình Roblox).

---

### 7. Xuất & Nhập sao lưu mã hóa (Backup Roundtrip)
- [ ] Vào trang **Cài đặt** -> chọn **Xuất sao lưu**.
- [ ] Nhập mật khẩu mã hóa (ví dụ: `TestPassw0rd!123`), lưu thành tập tin `backup_test.rbxbackup`.
- [ ] Xóa một tài khoản trong danh sách hiện tại.
- [ ] Nhấn **Nhập sao lưu**, chọn tập tin vừa xuất và nhập thử sai mật khẩu.
- [ ] **Kết quả mong đợi**: Ứng dụng báo lỗi sai mật khẩu hoặc lỗi xác thực xác nhận (Authentication Tag Failure), dữ liệu không bị thay đổi.
- [ ] Nhập lại đúng mật khẩu: Toàn bộ danh sách tài khoản được khôi phục đầy đủ.

---

### 8. Kiểm tra khả năng chống lỗi hỏng tập tin (Vault Corruption Detection)
- [ ] Lưu ý: Trường hợp kiểm thử hỏng tập tin đã được tự động hóa 100% trong `EncryptedFileVaultTests` (kiểm tra sai magic byte, sai version, sai HMAC tag, đổi ngẫu nhiên byte dữ liệu).
- [ ] Nếu thử thủ công: Đóng app, sao chép `vault.dat` sang bản backup, dùng Hex Editor sửa 1 byte ở giữa tập tin `vault.dat`, mở lại RbxManager.
- [ ] **Kết quả mong đợi**: Ứng dụng hiển thị hộp thoại cảnh báo tập tin bị hỏng (VaultCorruptedException), tuyệt đối không ghi đè trắng tập tin cũ. (Khôi phục lại bản backup sau khi kiểm tra xong).

---

### 9. Quét kiểm tra rò rỉ thông tin trong nhật ký (Log Leak Audit)
- [ ] Mở thư mục `%LOCALAPPDATA%\RbxManager\logs\`.
- [ ] Mở PowerShell tại thư mục logs và chạy lệnh:
  ```powershell
  Select-String -Path "*.log" -Pattern "_|WARNING:-DO-NOT-SHARE-THIS"
  Select-String -Path "*.log" -Pattern ".ROBLOSECURITY="
  ```
- [ ] **Kết quả mong đợi**: Lệnh không trả về bất kỳ kết quả nào. Không có bất kỳ token hoặc cookie nào bị lọt vào log.

---

## 10. Quan sát các mục chưa xác minh (UNVERIFIED Items from api-notes.md)

Theo dõi và ghi nhận phản hồi thực tế từ máy chủ Roblox đối với các hạng mục sau:

1. **Khởi chạy Server Riêng tư (Private Server Launch - BLOCKED / UNVERIFIED)**:
   - *Mục tiêu quan sát*: Cú pháp tham số URL của `PlaceLauncher.ashx` đối với Private Server (VIP Server) có sử dụng `accessCode` hay `linkCode` không. Hiện tại tính năng bị khóa trên UI để đảm bảo an toàn.
2. **Cơ chế xác thực Auth Ticket (`POST https://auth.roblox.com/v1/authentication-ticket`)**:
   - *Mục tiêu quan sát*: Header `rbx-authentication-ticket` trả về có hiệu lực trong bao lâu và có thể tái sử dụng lần thứ 2 không (giả thuyết: ticket chỉ dùng được 1 lần duy nhất trong vòng dưới 5 phút).
3. **Giới hạn tốc độ yêu cầu (Rate Limits)**:
   - *Mục tiêu quan sát*: Điểm cuối `GET https://users.roblox.com/v1/users/authenticated` khi người dùng nhấn "Kiểm tra tất cả" trên danh sách lớn (hơn 20 tài khoản) có gặp lỗi HTTP 429 Too Many Requests không.
