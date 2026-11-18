# RestaurantOrder – Đăng nhập & đăng ký (Razor Pages)

ASP.NET Core 8 Razor Pages + EF Core + SQL Server, bám theo báo cáo giai đoạn 1.

## Chạy
1. Mở `RestaurantOrderDb.sql` trong SSMS, kết nối tới `.\SQLEXPRESS01`, bấm Execute (F5).
   Script tạo CSDL, bảng và dữ liệu mẫu; chạy lại sẽ xóa và tạo lại toàn bộ dữ liệu.
   Hoặc chạy bằng dòng lệnh:
   ```bash
   sqlcmd -S ".\SQLEXPRESS01" -E -C -f 65001 -i RestaurantOrderDb.sql
   ```
2. Chạy web (nghe trên mọi địa chỉ mạng ở cổng 5090 để điện thoại cùng Wi-Fi vào được):
   ```bash
   dotnet run
   ```
3. Đăng nhập `admin`, vào **Mã QR bàn** (`/Admin/QrCodes`) để in mã. Khách quét mã sẽ mở
   `/order/t/{QrToken}` để xem thực đơn và gửi đơn, không cần đăng nhập. Điện thoại quét đầu tiên
   giữ bàn; máy khác quét cùng bàn sẽ thấy "Bàn đang có khách".

Không dùng `dotnet ef migrations` vì bảng đã do script tạo. Nếu dùng SQL Server khác,
sửa `Server=` trong `appsettings.json`.

Hệ thống chỉ có 2 vai trò nhân viên: **Quản lý** và **Thu ngân** (không có bếp, phục vụ).
Đơn đi theo: Chờ xác nhận → Chờ thanh toán → Đã thanh toán (hoàn tất); hoặc Bị từ chối / Đã hủy.

Tài khoản mẫu: `admin` / `Admin@123` (Quản lý), `thungan`, `thungan2` / `123456` (Thu ngân),
`thungan3` / `123456` (đã khóa). Nếu không chạy script, lần chạy đầu tự tạo bảng, 2 vai trò và tài khoản `admin`.

## Trang
| Đường dẫn | Ai vào được |
|---|---|
| /Account/Login | Mọi người |
| /Account/Register | Chỉ Quản lý (Admin) |
| /Index | Nhân viên đã đăng nhập |
| /Admin/QrCodes | Chỉ Quản lý (Admin) |
| /Admin/Revenue | Chỉ Quản lý: báo cáo doanh thu theo ngày, hình thức thanh toán, thu ngân, món bán chạy |
| /Cashier | Thu ngân và Quản lý: sơ đồ bàn, xác nhận / từ chối đơn, thu tiền, đóng / giải phóng bàn |
| /order/t/{QrToken} | Khách quét QR (không đăng nhập) |

Đăng ký chỉ dành cho Quản lý vì theo báo cáo, tài khoản nhân viên do Quản lý cấp; khách hàng không có tài khoản mà dùng mã QR.
