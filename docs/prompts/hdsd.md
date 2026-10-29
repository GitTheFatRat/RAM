# HƯỚNG DẪN SỬ DỤNG VÀ VẬN HÀNH RBXMANAGER

Chào mừng bạn sử dụng RbxManager. Hệ thống hiện tại được trang bị bộ lõi bảo vệ chống CAPTCHA và tuân thủ an toàn (Smart Launch & Circuit Breakers). Để đảm bảo dàn tài khoản hoạt động mượt mà 24/7 và không bị hệ thống chống gian lận (Byfron/Hyperion) khóa IP, vui lòng tuân thủ nghiêm ngặt các trình tự dưới đây.

---

## 1. QUY TẮC MẠNG & BẢO VỆ IP (QUAN TRỌNG NHẤT)

* **Sử dụng mạng Việt Nam (Không VPN):** Hệ thống đã được lập trình để đăng nhập và vượt rào trực tiếp trên IP mạng nội bộ (Viettel, VNPT, FPT). IP nhà mạng có độ uy tín cực cao. **Tuyệt đối không bật/tắt VPN liên tục**, điều này sẽ khiến IP bị đánh dấu là Datacenter và rơi vào vòng lặp CAPTCHA vĩnh viễn.
* **Tốc độ tạo tài khoản mới:** Nếu bạn tạo tài khoản mới trực tiếp từ tool, chỉ tạo tối đa **1 đến 2 tài khoản mỗi 10-15 phút**. Tạo dồn dập sẽ làm tăng độ khó CAPTCHA cho toàn bộ mạng nhà bạn.
* **KHÔNG BẤM "LOG OUT" (ĐĂNG XUẤT):** Khi bạn mở trình duyệt ẩn danh của tool để đăng nhập, sau khi thấy trang chủ Roblox, hãy đóng cửa sổ trình duyệt. **Tuyệt đối không bấm nút "Log Out" trên web**, thao tác này sẽ báo lên máy chủ Roblox tiêu hủy vĩnh viễn Cookie bạn vừa lấy.

---

## 2. TRÌNH TỰ KHỞI CHẠY TÀI KHOẢN (LAUNCHING)

Để tránh bị kẹt mạng (Lỗi 429 - Quá nhiều yêu cầu), tool đã có cơ chế tự động xếp hàng (Smart Pacing). Hãy thực hiện theo các bước sau:

**Bước 1: Chọn nhóm khởi chạy**
* Không chọn toàn bộ danh sách nếu bạn có quá nhiều tài khoản. 
* Hãy tích chọn từng nhóm nhỏ (khoảng **5 đến 10 tài khoản**) cho mỗi đợt.

**Bước 2: Hạn chế dùng nút "Check All"**
* Tool tự động ghi nhớ trạng thái sống/chết của Cookie trong 5 phút. Đừng bấm nút "Kiểm tra tất cả" ngay trước khi khởi chạy để tránh gửi lệnh rác làm máy chủ Roblox chú ý.

**Bước 3: Bấm khởi chạy (Launch Selected)**
* Bấm nút mở game và **để tool tự làm việc**.
* Tool sẽ tự động chèn một khoảng nghỉ ngẫu nhiên (từ 1.5 giây đến 6 giây cộng thêm thời gian đệm) giữa mỗi lần mở game để mô phỏng thao tác của người thật.

**Lưu ý khi kẹt mạng:** 
Nếu mạng nhà bạn tạm thời bị Roblox chặn, **Cầu chì ngắt mạch (Circuit Breaker)** sẽ tự động kích hoạt. Tool sẽ dừng mở acc mới và hiển thị trạng thái chờ (30 giây -> 60 giây -> 120 giây...). Bạn không cần tắt tool, cứ để yên, hệ thống sẽ tự động tiếp tục khi hết thời gian phạt.

---

## 3. TRÌNH TỰ TREO GAME (IDLING) & TẮT GAME

**Khi game đang tải:**
* Khi Roblox vừa mở lên, **không dùng chuột kéo thả cửa sổ**. Hệ thống khóa kích thước (`WindowSizeLock`) sẽ tự động bắt lấy cửa sổ khi map tải xong, ép về độ phân giải siêu nhỏ và xếp gọn gàng vào các góc màn hình để tiết kiệm tài nguyên.

**Khi muốn tắt game (Rất quan trọng):**
* **Chỉ sử dụng nút trên giao diện Tool:** Để dừng 1 tài khoản hoặc tắt toàn bộ, hãy bấm nút **"Kill / Stop"** trực tiếp trên danh sách tài khoản của RbxManager.
* **Không dùng Task Manager:** Tuyệt đối KHÔNG dùng Task Manager (Ctrl + Shift + Esc) để ép đóng (End Task) `RobloxPlayerBeta.exe` hoặc bấm dấu X đỏ trên cửa sổ game. Nếu bạn tắt thủ công kiểu này, hệ thống theo dõi (`SessionTracker`) sẽ tưởng nhầm là game bị crash (văng) và sẽ tự động... khởi động lại game đó!

---

*Cảm ơn bạn đã đọc kỹ hướng dẫn. Việc tuân thủ đúng trình tự sẽ giúp bảo vệ tài nguyên máy tính và giữ cho dàn tài khoản của bạn luôn trong trạng thái an toàn tuyệt đối trước mọi đợt quét của hệ thống.*