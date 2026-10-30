# Báo cáo quét dự án — 30/10/2026

Commit khảo sát: `846dfb4`. Working tree sạch trước khi quét. Không sửa mã sản phẩm trong lần khảo sát này.

## Phạm vi và cấu trúc

Đã lập danh mục toàn repository, đọc cấu hình build/CI, tài liệu kiến trúc, khảo sát các luồng chính và chạy kiểm tra tự động. Đây là khảo sát mã nguồn và khả năng build/test; không phải chứng nhận mọi dòng mã đều đúng hoặc quét malware các binary bên ngoài.

- 394 tệp được Git theo dõi; 209 tệp C# trong `src`/`tests`, tổng 33.543 dòng; 16 tệp XAML.
- 8 project: 3 project sản phẩm và 5 project kiểm thử.
- `Core`: model, validation, hợp đồng dịch vụ, chính sách rejoin; không có PackageReference.
- `Infrastructure`: vault/backup mã hóa, HTTP/auth, launcher, session/watchdog, Win32/mutex, cache/RAM, rotation, extension bridge và proxy.
- `App.Wpf`: giao diện MVVM, cấu hình host/DI, ViewModel, browser launcher, bản địa hóa Việt/Anh.
- `tools/BrowserExtension`: extension Manifest V3, nhập cookie, giao diện signup và PAC proxy.
- `.github/workflows/build.yml`: restore → format → Release build → test → publish.
- Hai thư mục `*_example` chứa binary/ZIP tham khảo, không thuộc solution; không chạy hoặc giải mã ngược trong lần quét.

Môi trường xác minh: Windows x64, .NET SDK 10.0.402, runtime 10.0.13, Node.js 24.19.0.

## Kết quả kiểm tra

| Kiểm tra | Kết quả |
|---|---|
| Build solution Release | Thành công, 0 warning, 0 error |
| Core.Tests | 93/93 đạt |
| App.Wpf.Tests | 72/72 đạt |
| Infrastructure.Tests | 263/264 đạt; 1 lỗi Potassium, xác nhận lại trong lần chạy riêng |
| `dotnet format --verify-no-changes --no-restore` | Không đạt, exit code 2; 625 chẩn đoán trên 14 tệp |
| Cú pháp 3 tệp JavaScript extension | Đạt `node --check` |
| Tái hiện race khi nhập cookie | Xác nhận bằng browser/network giả |
| Tái hiện rejoin dừng sau lỗi mạng | Xác nhận bằng dịch vụ giả |

Tổng hợp ba project kiểm thử: **428 đạt / 429 bài**, 1 thất bại. Lần chạy solution ban đầu không kết thúc sau hơn 6 phút và đã được dừng. Chạy riêng Infrastructure với log chi tiết và giới hạn hang 45 giây hoàn tất sau khoảng 22 giây, vẫn tái hiện đúng lỗi Potassium. Nguyên nhân lần chạy đầu bị treo chưa xác định; không khẳng định đã sửa hay đã loại trừ lỗi không ổn định của test runner/test suite.

Không chạy Live tests, UI automation E2E, publish/obfuscation hoặc Roblox thật. Bộ WPF unit/smoke tests đã chạy nhưng không thay thế kiểm tra giao diện trực quan. Không mở kho tài khoản thật; riêng test Potassium phát hiện đường dẫn tiến trình đang chạy trên máy do lỗi cô lập nêu bên dưới.

## Các vấn đề cần ưu tiên

### 1. [P1] Extension có thể xóa cookie mới chưa được lưu

Vị trí: `tools/BrowserExtension/background.js:84`, `:93`, `:135`, `:146`.

Luồng nhập đọc cookie A, chờ bridge xác thực/lưu rồi xóa cookie theo URL/tên/store. Nếu cookie thay thành B trong thời gian chờ, sự kiện mới bị bỏ qua bởi `importInFlight`; thao tác xóa sau đó xóa B, dù bridge mới chỉ lưu A. Người dùng có thể mất phiên đăng nhập mới trong trình duyệt mà vault chưa nhận được phiên đó.

Đã tái hiện offline: kết quả `imported=[FAKE_A]`, `removed=[FAKE_B]`. Không dùng cookie thật.

Hướng sửa: giữ sự kiện thay đổi trong hàng đợi; đối chiếu lại cookie hiện tại sau khi lưu trước khi quyết định xóa; xử lý cookie mới như một công việc riêng. Cần lưu ý thao tác đọc/xóa của browser không phải giao dịch nguyên tử.

### 2. [P1] Auto-rejoin mất công việc sau lỗi mạng tạm thời

Vị trí: `src/Infrastructure/Sessions/SessionTracker.cs:327`, `:421`, `:496`, `:505`.

Polling xóa session khi PID biến mất rồi khởi động một lần rejoin. Nếu xác thực trả lỗi mạng/rate limit, hàm return và xóa pending rejoin. Không còn session để vòng polling sau thử lại. Kết quả launcher thất bại ở cuối luồng cũng không được đưa lại vào hàng đợi. Cơ chế backoff/circuit breaker hiện không bảo đảm thử lại sau lần thất bại này.

Đã tái hiện offline: một session thoát bất ngờ → xác thực lỗi mạng → giả lập mạng hồi phục → polling thêm hai lần. Kết quả vẫn chỉ 1 lần xác thực, 0 lần launch, 0 active session và `gaveUp=False`.

Hướng sửa: quản lý công việc reconnect độc lập với danh sách PID đang sống; retry lỗi tạm thời theo backoff/giới hạn, chỉ dừng khi người dùng hủy hoặc gặp lỗi vĩnh viễn.

### 3. [P2] Proxy làm mất PAC loopback đã có của người dùng

Vị trí: `src/Infrastructure/Bridge/LocalSniDpiBypassProxy.cs:149`, `:155`, `:158`, `:191`.

Mã chỉ ghi nhớ PAC cũ nếu URL không chứa `127.0.0.1:`. Với cấu hình hợp lệ như `http://127.0.0.1:7890/proxy.pac` của công cụ khác, ứng dụng đặt giá trị cũ thành null, ghi PAC của mình, rồi xóa registry value khi dừng. Như vậy cấu hình cũ bị mất ngay cả khi thoát bình thường.

Bridge tự khởi động trong `App.xaml.cs:442`; không cần người dùng chủ động mở extension để đi vào luồng này. Ngoài ra, bản PAC cũ chỉ nằm trong bộ nhớ nên không có cơ chế khôi phục bền vững nếu process bị kết thúc đột ngột.

Xác nhận bằng truy vết mã; không thay đổi registry thật để thử. Hướng sửa: lưu chính xác trạng thái trước đó, nhận diện URL của chính ứng dụng bằng so sánh đầy đủ và có cơ chế khôi phục khi khởi động lại. Cũng cần xem lại extension đang áp dụng PAC ở phạm vi `regular`, trả DIRECT cho mọi host ngoài Roblox.

### 4. [P2] Test Potassium không được cô lập khỏi tiến trình thật

Vị trí: `src/Infrastructure/Integrations/PotassiumService.cs:68`, `:70`; `tests/Infrastructure.Tests/Integrations/PotassiumServiceTests.cs:107`, `:114`.

Test truyền các thư mục tạm nhưng `ScanCore()` vẫn gọi `InspectRunningProcesses()` trước và ưu tiên đường dẫn executable đang chạy. Trên máy này test `ScanAsync_DiscoversRandomizedFolderDataAndMcpToken_AndCachesExecutablePath` thất bại vì nhận đường dẫn Documents thật thay cho thư mục Temp của fixture.

Đây là lỗi về khả năng kiểm thử lặp lại, không phải bằng chứng chức năng tìm ứng dụng ngoài thực tế bị hỏng. Hướng sửa: inject bộ cung cấp tiến trình và sử dụng fake trong test, thay vì chỉ thay đường dẫn filesystem.

### 5. [P2] Kiểm tra format đang chặn CI

Vị trí: `.github/workflows/build.yml`, `.editorconfig`, 14 tệp được liệt kê trong log.

Lệnh giống bước kiểm tra format của CI trả exit code 2: 625 chẩn đoán gồm newline, whitespace và IDE0008. Vì bước này đứng trước build/test, build Release thành công trên máy không đồng nghĩa pipeline hiện sẽ xanh. Các chẩn đoán newline được ghi nhận trên checkout Windows hiện tại; kết quả checkout khác còn phụ thuộc cấu hình Git về line ending. Những lỗi whitespace/IDE0008 cũng cần xử lý riêng.

Log đầy đủ: `artifacts/project-scan/format-check.log`. Không tự format trong lần quét để giữ nguyên mã đang khảo sát.

### 6. [P3] Tài liệu trạng thái chưa phản ánh triển khai hiện tại

- `docs/STATE.md` dừng ở M28.7 trong khi lịch sử Git và tài liệu khác đã đến M44.
- `PROJECT_CONTEXT.md`/`ARCHITECTURE.md` còn mô tả GoodbyeDPI; `DpiToolService.cs:23` hiện dùng Cloudflare WARP.
- `docs/BEFORE_PUBLIC_RELEASE.md` còn ghi master password là hạng mục cần triển khai, nhưng vault hiện đã có Argon2id và key wrapping.
- Các tuyên bố không tải executable từ xa/không kết nối ngoài Roblox cần đối chiếu lại với dịch vụ tải WARP hiện có.

Hướng sửa: xác định một tài liệu trạng thái hiện hành; đánh dấu rõ các mô tả milestone lịch sử và cập nhật quy tắc/phạm vi theo quyết định sản phẩm hiện tại.

## Điểm tích cực đã quan sát

- Các lớp phụ thuộc đúng chiều Core ← Infrastructure ← App; Core độc lập dependency bên ngoài.
- Build bật nullable và warnings-as-errors.
- Vault có lưu thay thế, backup/recovery, mã hóa AES-GCM, DPAPI và cơ chế password key wrapping; bộ test có kiểm tra dữ liệu hỏng, lỗi ghi, bảo vệ mật khẩu và vệ sinh bộ nhớ.
- HTTP pipeline tắt redirect tự động và cookie jar; guard kiểm tra HTTPS/host trước khi gửi cookie.
- Settings cập nhật trên snapshot mới dưới semaphore, giảm nguy cơ các thao tác lưu đồng thời ghi đè nhau.
- Có test kiểm tra kiến trúc, bản địa hóa, log redaction, release hooks, XAML và Win32; kết quả riêng của các nhóm nằm trong TRX.

## Lệnh và bằng chứng tái chạy

```powershell
dotnet build 'RAM CLONE.slnx' -c Release --nologo -v minimal
dotnet test 'RAM CLONE.slnx' -c Release --no-build --filter 'Category!=Live&Category!=UI' --logger 'trx;LogFilePrefix=project-scan' --results-directory artifacts/project-scan --nologo -v minimal
dotnet test tests/Infrastructure.Tests/Infrastructure.Tests.csproj -c Release --no-build --blame-hang --blame-hang-timeout 45s --blame-hang-dump-type none --logger 'console;verbosity=normal' --logger 'trx;LogFilePrefix=infra-diagnostic' --results-directory artifacts/project-scan/diagnostic --nologo
dotnet format 'RAM CLONE.slnx' --verify-no-changes --no-restore --verbosity quiet
node artifacts/project-scan/extension-race-repro.cjs
dotnet run --project artifacts/project-scan/rejoin-repro/rejoin-repro.csproj -c Release --nologo
```

Các chương trình tái hiện nằm trong thư mục artifacts bị Git ignore; không được thêm vào solution hoặc mã sản phẩm. Ưu tiên tiếp theo: sửa mất cookie và mất công việc rejoin, bảo toàn PAC, cô lập test rồi làm sạch format/đồng bộ tài liệu.

Log kiểm thử Infrastructure: `artifacts/project-scan/infra-diagnostic.log`; kết quả máy đọc được: `artifacts/project-scan/diagnostic/infra-diagnostic_net10.0_20261030210511.trx`. TRX Core/WPF nằm trực tiếp trong `artifacts/project-scan`.
