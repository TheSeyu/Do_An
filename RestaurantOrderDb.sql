/* =====================================================================
   RestaurantOrderDb – tạo CSDL, bảng và dữ liệu mẫu
   Khớp với các lớp trong Models/ và AppDbContext của project.
   Chạy bằng SQL Server Management Studio (SSMS) hoặc Azure Data Studio:
   mở file -> Execute (F5). Chạy lại nhiều lần được: bảng cũ sẽ bị xóa và tạo lại.

   Tài khoản mẫu:
     admin    / Admin@123  (Quản lý)
     thungan  / 123456     (Thu ngân)
     thungan2 / 123456     (Thu ngân)
     thungan3 / 123456     (Thu ngân – tài khoản ĐÃ KHÓA, dùng để thử đăng nhập bị chặn)
   Hệ thống chỉ có 2 vai trò: Quản lý và Thu ngân (không có bếp, phục vụ).
   ===================================================================== */

IF DB_ID(N'RestaurantOrderDb') IS NULL
    CREATE DATABASE RestaurantOrderDb;
GO
USE RestaurantOrderDb;
GO

/* ---------- Xóa bảng cũ (theo thứ tự khóa ngoại) ---------- */
DROP TABLE IF EXISTS Payments, OrderItems, Orders, TableSessions, RejectReasons,
                     MenuItems, Categories, DiningTables, Users, Roles;
DROP TABLE IF EXISTS __EFMigrationsHistory;
GO

/* ---------- Tạo bảng ---------- */
CREATE TABLE Roles (
    Id   int IDENTITY(1,1) CONSTRAINT PK_Roles PRIMARY KEY,
    Name nvarchar(50) NOT NULL
);
CREATE UNIQUE INDEX IX_Roles_Name ON Roles(Name);

CREATE TABLE Users (
    Id           int IDENTITY(1,1) CONSTRAINT PK_Users PRIMARY KEY,
    Username     nvarchar(50)  NOT NULL,
    PasswordHash nvarchar(max) NOT NULL,
    FullName     nvarchar(100) NOT NULL,
    RoleId       int           NOT NULL CONSTRAINT FK_Users_Roles_RoleId REFERENCES Roles(Id),
    IsActive     bit           NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
    CreatedAt    datetime2     NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSDATETIME()
);
CREATE UNIQUE INDEX IX_Users_Username ON Users(Username);
CREATE INDEX IX_Users_RoleId ON Users(RoleId);

CREATE TABLE Categories (
    Id        int IDENTITY(1,1) CONSTRAINT PK_Categories PRIMARY KEY,
    Name      nvarchar(100) NOT NULL,
    SortOrder int NOT NULL
);

CREATE TABLE MenuItems (
    Id          int IDENTITY(1,1) CONSTRAINT PK_MenuItems PRIMARY KEY,
    CategoryId  int NOT NULL CONSTRAINT FK_MenuItems_Categories_CategoryId REFERENCES Categories(Id) ON DELETE CASCADE,
    Name        nvarchar(150) NOT NULL,
    Price       decimal(18,0) NOT NULL,
    ImageUrl    nvarchar(300) NULL,
    IsAvailable bit NOT NULL CONSTRAINT DF_MenuItems_IsAvailable DEFAULT 1,
    -- Số phần còn trong kho: trừ khi thu ngân xác nhận đơn, cộng khi nhập kho
    Stock       int NOT NULL CONSTRAINT DF_MenuItems_Stock DEFAULT 0
                CONSTRAINT CK_MenuItems_Stock CHECK (Stock >= 0)
);
CREATE INDEX IX_MenuItems_CategoryId ON MenuItems(CategoryId);

-- Status: 0 = Trống (trắng), 1 = Đang sử dụng (xanh, bàn bị khóa)
CREATE TABLE DiningTables (
    Id       int IDENTITY(1,1) CONSTRAINT PK_DiningTables PRIMARY KEY,
    Name     nvarchar(50) NOT NULL,
    Capacity int NOT NULL,
    QrToken  nvarchar(64) NOT NULL,
    Status   tinyint NOT NULL CONSTRAINT DF_DiningTables_Status DEFAULT 0
);
CREATE UNIQUE INDEX IX_DiningTables_QrToken ON DiningTables(QrToken);

-- CloseType: 0 = Đóng bàn, 1 = Giải phóng bàn; EndedAt NULL = phiên đang mở
CREATE TABLE TableSessions (
    Id           int IDENTITY(1,1) CONSTRAINT PK_TableSessions PRIMARY KEY,
    TableId      int NOT NULL CONSTRAINT FK_TableSessions_DiningTables_TableId REFERENCES DiningTables(Id) ON DELETE CASCADE,
    SessionToken nvarchar(64) NOT NULL,
    StartedAt    datetime2 NOT NULL,
    EndedAt      datetime2 NULL,
    ClosedById   int NULL CONSTRAINT FK_TableSessions_Users_ClosedById REFERENCES Users(Id),
    CloseType    tinyint NULL,
    CloseReason  nvarchar(200) NULL
);
CREATE UNIQUE INDEX IX_TableSessions_SessionToken ON TableSessions(SessionToken);
CREATE INDEX IX_TableSessions_TableId ON TableSessions(TableId);
CREATE INDEX IX_TableSessions_ClosedById ON TableSessions(ClosedById);

CREATE TABLE RejectReasons (
    Id       int IDENTITY(1,1) CONSTRAINT PK_RejectReasons PRIMARY KEY,
    Content  nvarchar(200) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_RejectReasons_IsActive DEFAULT 1
);

-- Status: 0 Chờ xác nhận, 1 Chờ thanh toán, 2 Đã thanh toán (đơn hoàn tất),
--         3 Bị từ chối, 4 Đã hủy
CREATE TABLE Orders (
    Id             int IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY,
    Code           nvarchar(20) NOT NULL,
    TableSessionId int NOT NULL CONSTRAINT FK_Orders_TableSessions_TableSessionId REFERENCES TableSessions(Id) ON DELETE CASCADE,
    Status         tinyint NOT NULL,
    TotalAmount    decimal(18,0) NOT NULL,
    CustomerNote   nvarchar(300) NULL,
    CreatedAt      datetime2 NOT NULL,
    HandledById    int NULL CONSTRAINT FK_Orders_Users_HandledById REFERENCES Users(Id),
    HandledAt      datetime2 NULL,
    RejectReasonId int NULL CONSTRAINT FK_Orders_RejectReasons_RejectReasonId REFERENCES RejectReasons(Id),
    RejectNote     nvarchar(300) NULL
);
CREATE UNIQUE INDEX IX_Orders_Code ON Orders(Code);
CREATE INDEX IX_Orders_Status ON Orders(Status);
CREATE INDEX IX_Orders_TableSessionId ON Orders(TableSessionId);
CREATE INDEX IX_Orders_HandledById ON Orders(HandledById);
CREATE INDEX IX_Orders_RejectReasonId ON Orders(RejectReasonId);

CREATE TABLE OrderItems (
    Id            int IDENTITY(1,1) CONSTRAINT PK_OrderItems PRIMARY KEY,
    OrderId       int NOT NULL CONSTRAINT FK_OrderItems_Orders_OrderId REFERENCES Orders(Id) ON DELETE CASCADE,
    MenuItemId    int NOT NULL CONSTRAINT FK_OrderItems_MenuItems_MenuItemId REFERENCES MenuItems(Id),
    Quantity      int NOT NULL,
    UnitPrice     decimal(18,0) NOT NULL,
    Note          nvarchar(200) NULL
);
CREATE INDEX IX_OrderItems_OrderId ON OrderItems(OrderId);
CREATE INDEX IX_OrderItems_MenuItemId ON OrderItems(MenuItemId);

-- Method: 0 Tiền mặt, 1 Chuyển khoản
CREATE TABLE Payments (
    Id        int IDENTITY(1,1) CONSTRAINT PK_Payments PRIMARY KEY,
    OrderId   int NOT NULL CONSTRAINT FK_Payments_Orders_OrderId REFERENCES Orders(Id) ON DELETE CASCADE,
    Method    tinyint NOT NULL,
    Amount    decimal(18,0) NOT NULL,
    PaidAt    datetime2 NOT NULL,
    CashierId int NOT NULL CONSTRAINT FK_Payments_Users_CashierId REFERENCES Users(Id)
);
CREATE UNIQUE INDEX IX_Payments_OrderId ON Payments(OrderId);
CREATE INDEX IX_Payments_CashierId ON Payments(CashierId);
GO

/* =====================================================================
   DỮ LIỆU MẪU
   Thời gian tính theo lúc chạy script: lịch sử 3 ngày trước + các bàn
   đang có khách ngay lúc này (khớp wireframe màn hình thu ngân).
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now   datetime2 = SYSDATETIME();
DECLARE @today datetime2 = CAST(CAST(@now AS date) AS datetime2);

-- Vai trò
SET IDENTITY_INSERT Roles ON;
INSERT INTO Roles (Id, Name) VALUES
    (1, N'Admin'),
    (2, N'Cashier');
SET IDENTITY_INSERT Roles OFF;

-- Nhân viên (mật khẩu đã băm theo PasswordHasher của ASP.NET Core)
SET IDENTITY_INSERT Users ON;
INSERT INTO Users (Id, Username, PasswordHash, FullName, RoleId, IsActive, CreatedAt) VALUES
    (1, N'admin', N'AQAAAAIAAYagAAAAEDWx4+Mvt6jUhoneVcppxKx7Ab5jyZfm3aiV1f+mjirGVt4u0HWVYmkDqRfEc6vbzA==', N'Quản lý nhà hàng', 1, 1, DATEADD(DAY, -30, @today)),
    (2, N'thungan', N'AQAAAAIAAYagAAAAEPkwviW9k5D7JX9n/JSzHmWXBlMgf1doHkPfLjC1dOdu+vmN+xHw2s6G6jKBSOjpLg==', N'Nguyễn Thị Thu', 2, 1, DATEADD(DAY, -30, @today)),
    (3, N'thungan2', N'AQAAAAIAAYagAAAAEHLTU/6OFypnfaB89gGxBSeNxJwLj3Pk0pO3YsopQuMbdBDVZwJnkL5X4C/vkjZSOA==', N'Trần Văn An', 2, 1, DATEADD(DAY, -30, @today)),
    (4, N'thungan3', N'AQAAAAIAAYagAAAAEMHqtylhSuJJXBHDsNq7ErGkBFCzzSd6asuS5N21xhbYWmCykVi81qAAdB0OI6jiLQ==', N'Hoàng Thị Vy', 2, 0, DATEADD(DAY, -30, @today));
SET IDENTITY_INSERT Users OFF;

-- Danh mục
SET IDENTITY_INSERT Categories ON;
INSERT INTO Categories (Id, Name, SortOrder) VALUES
    (1, N'Món chính', 1),
    (2, N'Món ăn kèm', 2),
    (3, N'Lẩu', 3),
    (4, N'Đồ uống', 4);
SET IDENTITY_INSERT Categories OFF;

-- Món ăn và tồn kho: Mì Quảng hết hàng (để thử từ chối đơn), Nem rán và Lẩu gà sắp hết
SET IDENTITY_INSERT MenuItems ON;
INSERT INTO MenuItems (Id, CategoryId, Name, Price, ImageUrl, IsAvailable, Stock) VALUES
    (1, 1, N'Phở bò tái', 45000, NULL, 1, 30),
    (2, 1, N'Bún chả Hà Nội', 40000, NULL, 1, 25),
    (3, 1, N'Cơm gà xối mỡ', 55000, NULL, 1, 20),
    (4, 1, N'Cơm tấm sườn bì', 50000, NULL, 1, 20),
    (5, 1, N'Bún bò Huế', 45000, NULL, 1, 25),
    (6, 1, N'Mì Quảng', 40000, NULL, 1, 0),
    (7, 2, N'Nem rán (4 cái)', 35000, NULL, 1, 3),
    (8, 2, N'Gỏi cuốn tôm thịt', 30000, NULL, 1, 15),
    (9, 2, N'Khoai tây chiên', 25000, NULL, 1, 30),
    (10, 3, N'Lẩu Thái hải sản', 250000, NULL, 1, 8),
    (11, 3, N'Lẩu gà lá é', 220000, NULL, 1, 2),
    (12, 4, N'Trà đá', 5000, NULL, 1, 200),
    (13, 4, N'Nước cam ép', 25000, NULL, 1, 40),
    (14, 4, N'Cà phê sữa đá', 20000, NULL, 1, 50),
    (15, 4, N'Nước suối', 10000, NULL, 1, 60),
    (16, 4, N'Coca-Cola', 15000, NULL, 1, 48);
SET IDENTITY_INSERT MenuItems OFF;

-- Bàn: QrToken là chuỗi in trong mã QR -> /order/t/{QrToken}. Bàn 02, 03, 06, 09 đang sử dụng.
SET IDENTITY_INSERT DiningTables ON;
INSERT INTO DiningTables (Id, Name, Capacity, QrToken, Status) VALUES
    (1, N'Bàn 01', 4, N'95b25ede626a851ff0b4d22d29bd7b89', 0),
    (2, N'Bàn 02', 4, N'a858629e5a33c22655ac3228b1b9bc76', 1),
    (3, N'Bàn 03', 4, N'd27d0d4a4598d308c17f800bc28b73e4', 1),
    (4, N'Bàn 04', 4, N'c92d07feb701732400ef5a89ec6fc271', 0),
    (5, N'Bàn 05', 4, N'875abfeffedf46e9039f69d23363fb0a', 0),
    (6, N'Bàn 06', 4, N'b36586a0ca8216a5f44d654a3504c8e1', 1),
    (7, N'Bàn 07', 4, N'5748bcce5deb2b681c5cebd29a450879', 0),
    (8, N'Bàn 08', 4, N'183bfbbc100e1d25dbda39a2c4f3c08b', 0),
    (9, N'Bàn 09', 6, N'357126e24f515389a7fe10ad4ad3f18a', 1),
    (10, N'Bàn 10', 6, N'5389c00cf732561734bf173f1505879f', 0),
    (11, N'Bàn 11', 6, N'a2acf0a73c98c49ae34793ac5ae33dc2', 0),
    (12, N'Bàn 12', 6, N'3eb960a3c01e5d796e2d296fff20f392', 0);
SET IDENTITY_INSERT DiningTables OFF;

-- Lý do từ chối
SET IDENTITY_INSERT RejectReasons ON;
INSERT INTO RejectReasons (Id, Content, IsActive) VALUES
    (1, N'Không thấy khách tại bàn', 1),
    (2, N'Món trong đơn đã hết', 1),
    (3, N'Đơn trùng / gửi nhiều lần', 1),
    (4, N'Ngoài giờ phục vụ', 1),
    (5, N'Khác (ghi rõ)', 1);
SET IDENTITY_INSERT RejectReasons OFF;

-- Phiên bàn (EndedAt NULL = bàn đang được giữ)
SET IDENTITY_INSERT TableSessions ON;
INSERT INTO TableSessions (Id, TableId, SessionToken, StartedAt, EndedAt, ClosedById, CloseType, CloseReason) VALUES
    (1, 10, N'a8d1c47d0e7144dd287f274d8d816f75', DATEADD(MINUTE, 664, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 759, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (2, 4, N'b773109a99cf699cc1c9c1ee1aafabcb', DATEADD(MINUTE, 722, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 786, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (3, 9, N'70f4489fb6171184e0f37add1dea250a', DATEADD(MINUTE, 740, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 827, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (4, 3, N'53e86a30edcb4ddcf846b2f1fb6b627f', DATEADD(MINUTE, 1081, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 1161, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (5, 1, N'76b23cf45ee40b94537078f9292a795d', DATEADD(MINUTE, 1125, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 1203, DATEADD(DAY, -3, @today)), 2, 0, NULL),
    (6, 8, N'4480181141f5be5812f07eab79fa9737', DATEADD(MINUTE, 1173, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 1256, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (7, 7, N'722af5a1990c92ebca0b53705a7e9a7e', DATEADD(MINUTE, 1200, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 1294, DATEADD(DAY, -3, @today)), 3, 0, NULL),
    (8, 5, N'1e60945b7b26bb2e10a589cbd480da6e', DATEADD(MINUTE, 780, DATEADD(DAY, -3, @today)), DATEADD(MINUTE, 820, DATEADD(DAY, -3, @today)), 2, 0, NULL),
    (9, 9, N'5f6af976a9785e57a88e3c405547735f', DATEADD(MINUTE, 681, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 746, DATEADD(DAY, -2, @today)), 3, 0, NULL),
    (10, 1, N'a750d32d3d1134a9ad5709d4ca33f42f', DATEADD(MINUTE, 719, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 792, DATEADD(DAY, -2, @today)), 2, 0, NULL),
    (11, 2, N'8e15d709bf858723791e4b7f192e8d81', DATEADD(MINUTE, 740, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 829, DATEADD(DAY, -2, @today)), 3, 0, NULL),
    (12, 3, N'54fafa55bed07d3763d7da9ddd71ca85', DATEADD(MINUTE, 1100, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 1163, DATEADD(DAY, -2, @today)), 2, 0, NULL),
    (13, 4, N'd8f63316e96344b54554a28886491d3a', DATEADD(MINUTE, 1125, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 1219, DATEADD(DAY, -2, @today)), 3, 0, NULL),
    (14, 11, N'e5c414cb519d5dad5b83a7763770337d', DATEADD(MINUTE, 1168, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 1223, DATEADD(DAY, -2, @today)), 3, 0, NULL),
    (15, 12, N'79196bb433def6e189661476c4a3355a', DATEADD(MINUTE, 1200, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 1269, DATEADD(DAY, -2, @today)), 2, 0, NULL),
    (16, 11, N'4c65e9e971e27ca7a0fe0012615ddd99', DATEADD(MINUTE, 1180, DATEADD(DAY, -2, @today)), DATEADD(MINUTE, 1260, DATEADD(DAY, -2, @today)), 3, 0, NULL),
    (17, 6, N'102ba570825ff0354feafd7e35813a2b', DATEADD(MINUTE, 678, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 739, DATEADD(DAY, -1, @today)), 2, 0, NULL),
    (18, 2, N'ff14a0bb6d24c4312abb79537eefbf8b', DATEADD(MINUTE, 722, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 798, DATEADD(DAY, -1, @today)), 2, 0, NULL),
    (19, 12, N'c24282df006d99c1afc99aca40a40e34', DATEADD(MINUTE, 740, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 814, DATEADD(DAY, -1, @today)), 3, 0, NULL),
    (20, 10, N'bcd9aba827989a02d5e6d1a2c822ade3', DATEADD(MINUTE, 1098, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 1172, DATEADD(DAY, -1, @today)), 2, 0, NULL),
    (21, 11, N'a6af3cdd3bf953cbcf313ea8df61ec0b', DATEADD(MINUTE, 1125, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 1203, DATEADD(DAY, -1, @today)), 3, 0, NULL),
    (22, 8, N'ad8f377e4848792e088d0733728b6054', DATEADD(MINUTE, 1166, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 1242, DATEADD(DAY, -1, @today)), 2, 0, NULL),
    (23, 4, N'9567c471600132506df5bb3809a3eeea', DATEADD(MINUTE, 1200, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 1277, DATEADD(DAY, -1, @today)), 2, 0, NULL),
    (24, 7, N'add0e378869a28f9f944d40fa3e3ba37', DATEADD(MINUTE, 850, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 875, DATEADD(DAY, -1, @today)), 2, 1, N'Không có người tại bàn'),
    (25, 4, N'81eadb5d1a758342f523e63d91cf5476', DATEADD(MINUTE, 900, DATEADD(DAY, -1, @today)), DATEADD(MINUTE, 940, DATEADD(DAY, -1, @today)), 2, 1, N'Khách đã rời đi nhưng không đóng bàn'),
    (26, 2, N'f2a1f2413be066626f12ab573c07c653', DATEADD(MINUTE, -50, @now), NULL, NULL, NULL, NULL),
    (27, 3, N'0f4c894e4bc1c8ac94c4a50d6992a27b', DATEADD(MINUTE, -12, @now), NULL, NULL, NULL, NULL),
    (28, 6, N'b7db3cfa4ecf8a50a9f3e3c8f900d2d7', DATEADD(MINUTE, -25, @now), NULL, NULL, NULL, NULL),
    (29, 9, N'ace31b2be5d2ed39b5cffff881492403', DATEADD(MINUTE, -40, @now), NULL, NULL, NULL, NULL);
SET IDENTITY_INSERT TableSessions OFF;

-- Đơn hàng
SET IDENTITY_INSERT Orders ON;
INSERT INTO Orders (Id, Code, TableSessionId, Status, TotalAmount, CustomerNote, CreatedAt, HandledById, HandledAt, RejectReasonId, RejectNote) VALUES
    (1, N'DH0001', 1, 2, 155000, N'Cho thêm đá', DATEADD(MINUTE, 667, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 668, DATEADD(DAY, -3, @today)), NULL, NULL),
    (2, N'DH0002', 2, 2, 90000, N'Ít cay', DATEADD(MINUTE, 725, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 726, DATEADD(DAY, -3, @today)), NULL, NULL),
    (3, N'DH0003', 3, 2, 255000, N'Không hành', DATEADD(MINUTE, 743, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 744, DATEADD(DAY, -3, @today)), NULL, NULL),
    (4, N'DH0004', 8, 4, 90000, N'Khách đổi ý', DATEADD(MINUTE, 782, DATEADD(DAY, -3, @today)), NULL, NULL, NULL, NULL),
    (5, N'DH0005', 4, 2, 185000, NULL, DATEADD(MINUTE, 1084, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 1085, DATEADD(DAY, -3, @today)), NULL, NULL),
    (6, N'DH0006', 5, 2, 495000, N'Mang ra cùng lúc', DATEADD(MINUTE, 1128, DATEADD(DAY, -3, @today)), 2, DATEADD(MINUTE, 1129, DATEADD(DAY, -3, @today)), NULL, NULL),
    (7, N'DH0007', 5, 2, 345000, NULL, DATEADD(MINUTE, 1148, DATEADD(DAY, -3, @today)), 2, DATEADD(MINUTE, 1149, DATEADD(DAY, -3, @today)), NULL, NULL),
    (8, N'DH0008', 6, 2, 470000, N'Mang ra cùng lúc', DATEADD(MINUTE, 1176, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 1177, DATEADD(DAY, -3, @today)), NULL, NULL),
    (9, N'DH0009', 6, 2, 200000, N'Mang ra cùng lúc', DATEADD(MINUTE, 1196, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 1197, DATEADD(DAY, -3, @today)), NULL, NULL),
    (10, N'DH0010', 7, 2, 245000, NULL, DATEADD(MINUTE, 1203, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 1204, DATEADD(DAY, -3, @today)), NULL, NULL),
    (11, N'DH0011', 7, 2, 265000, NULL, DATEADD(MINUTE, 1223, DATEADD(DAY, -3, @today)), 3, DATEADD(MINUTE, 1224, DATEADD(DAY, -3, @today)), NULL, NULL),
    (12, N'DH0012', 9, 2, 390000, N'Ít cay', DATEADD(MINUTE, 684, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 685, DATEADD(DAY, -2, @today)), NULL, NULL),
    (13, N'DH0013', 9, 2, 150000, NULL, DATEADD(MINUTE, 704, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 705, DATEADD(DAY, -2, @today)), NULL, NULL),
    (14, N'DH0014', 10, 2, 520000, N'Ít cay', DATEADD(MINUTE, 722, DATEADD(DAY, -2, @today)), 2, DATEADD(MINUTE, 723, DATEADD(DAY, -2, @today)), NULL, NULL),
    (15, N'DH0015', 11, 2, 295000, NULL, DATEADD(MINUTE, 743, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 744, DATEADD(DAY, -2, @today)), NULL, NULL),
    (16, N'DH0016', 12, 2, 335000, N'Không hành', DATEADD(MINUTE, 1103, DATEADD(DAY, -2, @today)), 2, DATEADD(MINUTE, 1104, DATEADD(DAY, -2, @today)), NULL, NULL),
    (17, N'DH0017', 12, 2, 120000, NULL, DATEADD(MINUTE, 1123, DATEADD(DAY, -2, @today)), 2, DATEADD(MINUTE, 1124, DATEADD(DAY, -2, @today)), NULL, NULL),
    (18, N'DH0018', 13, 2, 250000, NULL, DATEADD(MINUTE, 1128, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 1129, DATEADD(DAY, -2, @today)), NULL, NULL),
    (19, N'DH0019', 14, 2, 160000, NULL, DATEADD(MINUTE, 1171, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 1172, DATEADD(DAY, -2, @today)), NULL, NULL),
    (20, N'DH0020', 16, 3, 130000, NULL, DATEADD(MINUTE, 1182, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 1183, DATEADD(DAY, -2, @today)), 2, NULL),
    (21, N'DH0021', 16, 2, 140000, NULL, DATEADD(MINUTE, 1190, DATEADD(DAY, -2, @today)), 3, DATEADD(MINUTE, 1191, DATEADD(DAY, -2, @today)), NULL, NULL),
    (22, N'DH0022', 15, 2, 480000, N'Không hành', DATEADD(MINUTE, 1203, DATEADD(DAY, -2, @today)), 2, DATEADD(MINUTE, 1204, DATEADD(DAY, -2, @today)), NULL, NULL),
    (23, N'DH0023', 17, 2, 355000, NULL, DATEADD(MINUTE, 681, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 682, DATEADD(DAY, -1, @today)), NULL, NULL),
    (24, N'DH0024', 18, 2, 150000, N'Cho thêm đá', DATEADD(MINUTE, 725, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 726, DATEADD(DAY, -1, @today)), NULL, NULL),
    (25, N'DH0025', 19, 2, 255000, N'Mang ra cùng lúc', DATEADD(MINUTE, 743, DATEADD(DAY, -1, @today)), 3, DATEADD(MINUTE, 744, DATEADD(DAY, -1, @today)), NULL, NULL),
    (26, N'DH0026', 24, 3, 310000, NULL, DATEADD(MINUTE, 852, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 853, DATEADD(DAY, -1, @today)), 1, NULL),
    (27, N'DH0027', 25, 3, 40000, NULL, DATEADD(MINUTE, 901, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 902, DATEADD(DAY, -1, @today)), 3, NULL),
    (28, N'DH0028', 25, 4, 40000, NULL, DATEADD(MINUTE, 903, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 904, DATEADD(DAY, -1, @today)), NULL, NULL),
    (29, N'DH0029', 20, 2, 520000, N'Không hành', DATEADD(MINUTE, 1101, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 1102, DATEADD(DAY, -1, @today)), NULL, NULL),
    (30, N'DH0030', 21, 2, 200000, N'Cho thêm đá', DATEADD(MINUTE, 1128, DATEADD(DAY, -1, @today)), 3, DATEADD(MINUTE, 1129, DATEADD(DAY, -1, @today)), NULL, NULL),
    (31, N'DH0031', 22, 2, 125000, NULL, DATEADD(MINUTE, 1169, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 1170, DATEADD(DAY, -1, @today)), NULL, NULL),
    (32, N'DH0032', 23, 2, 265000, N'Không hành', DATEADD(MINUTE, 1203, DATEADD(DAY, -1, @today)), 2, DATEADD(MINUTE, 1204, DATEADD(DAY, -1, @today)), NULL, NULL),
    (33, N'DH0033', 26, 2, 45000, NULL, DATEADD(MINUTE, -46, @now), 2, DATEADD(MINUTE, -45, @now), NULL, NULL),
    (34, N'DH0034', 26, 2, 115000, NULL, DATEADD(MINUTE, -38, @now), 2, DATEADD(MINUTE, -37, @now), NULL, NULL),
    (35, N'DH0035', 29, 2, 10000, NULL, DATEADD(MINUTE, -36, @now), 2, DATEADD(MINUTE, -35, @now), NULL, NULL),
    (36, N'DH0036', 29, 1, 80000, NULL, DATEADD(MINUTE, -15, @now), 2, DATEADD(MINUTE, -14, @now), NULL, NULL),
    (37, N'DH0037', 27, 2, 80000, NULL, DATEADD(MINUTE, -11, @now), 2, DATEADD(MINUTE, -10, @now), NULL, NULL),
    (38, N'DH0038', 27, 3, 65000, NULL, DATEADD(MINUTE, -10, @now), 2, DATEADD(MINUTE, -9, @now), 2, NULL),
    (39, N'DH0039', 27, 0, 95000, N'Ít hành', DATEADD(MINUTE, -8, @now), NULL, NULL, NULL, NULL),
    (40, N'DH0040', 29, 0, 60000, NULL, DATEADD(MINUTE, -3, @now), NULL, NULL, NULL, NULL);
SET IDENTITY_INSERT Orders OFF;

-- Chi tiết đơn (UnitPrice = giá lúc đặt)
SET IDENTITY_INSERT OrderItems ON;
INSERT INTO OrderItems (Id, OrderId, MenuItemId, Quantity, UnitPrice, Note) VALUES
    (1, 1, 5, 3, 45000, NULL),
    (2, 1, 12, 4, 5000, NULL),
    (3, 2, 9, 3, 25000, NULL),
    (4, 2, 16, 1, 15000, NULL),
    (5, 3, 3, 3, 55000, NULL),
    (6, 3, 1, 2, 45000, NULL),
    (7, 4, 2, 2, 40000, NULL),
    (8, 4, 12, 2, 5000, NULL),
    (9, 5, 4, 1, 50000, NULL),
    (10, 5, 8, 3, 30000, NULL),
    (11, 5, 16, 3, 15000, NULL),
    (12, 6, 10, 1, 250000, NULL),
    (13, 6, 4, 1, 50000, NULL),
    (14, 6, 1, 3, 45000, NULL),
    (15, 6, 9, 2, 25000, NULL),
    (16, 6, 12, 2, 5000, NULL),
    (17, 7, 1, 3, 45000, NULL),
    (18, 7, 5, 3, 45000, NULL),
    (19, 7, 13, 3, 25000, NULL),
    (20, 8, 11, 1, 220000, NULL),
    (21, 8, 5, 2, 45000, NULL),
    (22, 8, 7, 2, 35000, NULL),
    (23, 8, 8, 3, 30000, NULL),
    (24, 9, 5, 2, 45000, NULL),
    (25, 9, 4, 1, 50000, NULL),
    (26, 9, 2, 1, 40000, NULL),
    (27, 9, 12, 4, 5000, NULL),
    (28, 10, 7, 1, 35000, NULL),
    (29, 10, 1, 2, 45000, NULL),
    (30, 10, 4, 2, 50000, NULL),
    (31, 10, 14, 1, 20000, NULL),
    (32, 11, 7, 2, 35000, NULL),
    (33, 11, 8, 2, 30000, NULL),
    (34, 11, 9, 3, 25000, NULL),
    (35, 11, 16, 4, 15000, NULL),
    (36, 12, 11, 1, 220000, NULL),
    (37, 12, 4, 3, 50000, NULL),
    (38, 12, 14, 1, 20000, NULL),
    (39, 13, 4, 1, 50000, NULL),
    (40, 13, 13, 4, 25000, NULL),
    (41, 14, 10, 1, 250000, NULL),
    (42, 14, 5, 2, 45000, NULL),
    (43, 14, 3, 3, 55000, NULL),
    (44, 14, 16, 1, 15000, NULL),
    (45, 15, 8, 2, 30000, NULL),
    (46, 15, 4, 3, 50000, NULL),
    (47, 15, 7, 2, 35000, NULL),
    (48, 15, 16, 1, 15000, NULL),
    (49, 16, 11, 1, 220000, NULL),
    (50, 16, 7, 3, 35000, NULL),
    (51, 16, 12, 2, 5000, NULL),
    (52, 17, 1, 1, 45000, NULL),
    (53, 17, 13, 3, 25000, NULL),
    (54, 18, 3, 2, 55000, NULL),
    (55, 18, 2, 2, 40000, NULL),
    (56, 18, 14, 3, 20000, NULL),
    (57, 19, 9, 1, 25000, NULL),
    (58, 19, 7, 2, 35000, NULL),
    (59, 19, 1, 1, 45000, NULL),
    (60, 19, 12, 4, 5000, NULL),
    (61, 20, 6, 2, 40000, NULL),
    (62, 20, 13, 2, 25000, NULL),
    (63, 21, 5, 2, 45000, NULL),
    (64, 21, 13, 2, 25000, NULL),
    (65, 22, 10, 1, 250000, NULL),
    (66, 22, 2, 1, 40000, NULL),
    (67, 22, 1, 2, 45000, NULL),
    (68, 22, 13, 4, 25000, NULL),
    (69, 23, 3, 3, 55000, NULL),
    (70, 23, 2, 2, 40000, NULL),
    (71, 23, 1, 2, 45000, NULL),
    (72, 23, 14, 1, 20000, NULL),
    (73, 24, 8, 3, 30000, NULL),
    (74, 24, 16, 4, 15000, NULL),
    (75, 25, 5, 2, 45000, NULL),
    (76, 25, 1, 2, 45000, NULL),
    (77, 25, 13, 3, 25000, NULL),
    (78, 26, 10, 1, 250000, NULL),
    (79, 26, 16, 4, 15000, NULL),
    (80, 27, 14, 2, 20000, NULL),
    (81, 28, 14, 2, 20000, NULL),
    (82, 29, 10, 1, 250000, NULL),
    (83, 29, 8, 3, 30000, NULL),
    (84, 29, 2, 3, 40000, NULL),
    (85, 29, 14, 3, 20000, NULL),
    (86, 30, 7, 1, 35000, NULL),
    (87, 30, 5, 3, 45000, NULL),
    (88, 30, 8, 1, 30000, NULL),
    (89, 31, 7, 3, 35000, NULL),
    (90, 31, 15, 2, 10000, NULL),
    (91, 32, 9, 2, 25000, NULL),
    (92, 32, 8, 1, 30000, NULL),
    (93, 32, 5, 3, 45000, NULL),
    (94, 32, 13, 2, 25000, NULL),
    (95, 33, 1, 1, 45000, NULL),
    (96, 34, 2, 2, 40000, NULL),
    (97, 34, 7, 1, 35000, NULL),
    (98, 35, 12, 2, 5000, NULL),
    (99, 36, 3, 1, 55000, NULL),
    (100, 36, 13, 1, 25000, NULL);
INSERT INTO OrderItems (Id, OrderId, MenuItemId, Quantity, UnitPrice, Note) VALUES
    (101, 37, 2, 2, 40000, NULL),
    (102, 38, 6, 1, 40000, NULL),
    (103, 38, 13, 1, 25000, NULL),
    (104, 39, 1, 2, 45000, NULL),
    (105, 39, 12, 1, 5000, NULL),
    (106, 40, 8, 1, 30000, NULL),
    (107, 40, 16, 2, 15000, NULL);
SET IDENTITY_INSERT OrderItems OFF;

-- Thanh toán (chỉ đơn từ Đã thanh toán trở đi)
SET IDENTITY_INSERT Payments ON;
INSERT INTO Payments (Id, OrderId, Method, Amount, PaidAt, CashierId) VALUES
    (1, 1, 0, 155000, DATEADD(MINUTE, 671, DATEADD(DAY, -3, @today)), 3),
    (2, 2, 0, 90000, DATEADD(MINUTE, 729, DATEADD(DAY, -3, @today)), 3),
    (3, 3, 0, 255000, DATEADD(MINUTE, 747, DATEADD(DAY, -3, @today)), 3),
    (4, 5, 0, 185000, DATEADD(MINUTE, 1088, DATEADD(DAY, -3, @today)), 3),
    (5, 6, 0, 495000, DATEADD(MINUTE, 1132, DATEADD(DAY, -3, @today)), 2),
    (6, 7, 0, 345000, DATEADD(MINUTE, 1152, DATEADD(DAY, -3, @today)), 2),
    (7, 8, 0, 470000, DATEADD(MINUTE, 1180, DATEADD(DAY, -3, @today)), 3),
    (8, 9, 0, 200000, DATEADD(MINUTE, 1200, DATEADD(DAY, -3, @today)), 3),
    (9, 10, 0, 245000, DATEADD(MINUTE, 1207, DATEADD(DAY, -3, @today)), 3),
    (10, 11, 1, 265000, DATEADD(MINUTE, 1227, DATEADD(DAY, -3, @today)), 3),
    (11, 12, 1, 390000, DATEADD(MINUTE, 688, DATEADD(DAY, -2, @today)), 3),
    (12, 13, 0, 150000, DATEADD(MINUTE, 708, DATEADD(DAY, -2, @today)), 3),
    (13, 14, 1, 520000, DATEADD(MINUTE, 726, DATEADD(DAY, -2, @today)), 2),
    (14, 15, 0, 295000, DATEADD(MINUTE, 747, DATEADD(DAY, -2, @today)), 3),
    (15, 16, 0, 335000, DATEADD(MINUTE, 1107, DATEADD(DAY, -2, @today)), 2),
    (16, 17, 0, 120000, DATEADD(MINUTE, 1127, DATEADD(DAY, -2, @today)), 2),
    (17, 18, 0, 250000, DATEADD(MINUTE, 1132, DATEADD(DAY, -2, @today)), 3),
    (18, 19, 0, 160000, DATEADD(MINUTE, 1175, DATEADD(DAY, -2, @today)), 3),
    (19, 21, 0, 140000, DATEADD(MINUTE, 1194, DATEADD(DAY, -2, @today)), 3),
    (20, 22, 1, 480000, DATEADD(MINUTE, 1207, DATEADD(DAY, -2, @today)), 2),
    (21, 23, 1, 355000, DATEADD(MINUTE, 685, DATEADD(DAY, -1, @today)), 2),
    (22, 24, 0, 150000, DATEADD(MINUTE, 729, DATEADD(DAY, -1, @today)), 2),
    (23, 25, 0, 255000, DATEADD(MINUTE, 747, DATEADD(DAY, -1, @today)), 3),
    (24, 29, 0, 520000, DATEADD(MINUTE, 1105, DATEADD(DAY, -1, @today)), 2),
    (25, 30, 1, 200000, DATEADD(MINUTE, 1132, DATEADD(DAY, -1, @today)), 3),
    (26, 31, 0, 125000, DATEADD(MINUTE, 1173, DATEADD(DAY, -1, @today)), 2),
    (27, 32, 0, 265000, DATEADD(MINUTE, 1207, DATEADD(DAY, -1, @today)), 2),
    (28, 33, 1, 45000, DATEADD(MINUTE, -42, @now), 2),
    (29, 34, 0, 115000, DATEADD(MINUTE, -34, @now), 2),
    (30, 35, 0, 10000, DATEADD(MINUTE, -32, @now), 2),
    (31, 37, 0, 80000, DATEADD(MINUTE, -7, @now), 2);
SET IDENTITY_INSERT Payments OFF;

GO

/* ---------- Kiểm tra nhanh ---------- */
SELECT t.Name AS Ban, CASE t.Status WHEN 1 THEN N'Đang sử dụng' ELSE N'Trống' END AS TrangThai
FROM DiningTables t ORDER BY t.Id;

SELECT CASE o.Status WHEN 0 THEN N'Chờ xác nhận' WHEN 1 THEN N'Chờ thanh toán' WHEN 2 THEN N'Đã thanh toán'
                     WHEN 3 THEN N'Bị từ chối' ELSE N'Đã hủy' END AS TrangThaiDon,
       COUNT(*) AS SoDon
FROM Orders o GROUP BY o.Status ORDER BY o.Status;
GO
