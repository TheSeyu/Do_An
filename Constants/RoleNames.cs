namespace RestaurantOrder.Constants;

public static class RoleNames
{
    public const string Admin   = "Admin";
    public const string Cashier = "Cashier";

    public static readonly string[] All = { Admin, Cashier };

    /// <summary>Tên tiếng Việt và mô tả ngắn để hiển thị trên giao diện.</summary>
    public static (string Name, string Description) Display(string role) => role switch
    {
        Admin   => ("Quản lý",   "Tạo tài khoản, in mã QR bàn và dùng được màn hình thu ngân"),
        Cashier => ("Thu ngân",  "Xác nhận / từ chối đơn, thanh toán, đóng và giải phóng bàn"),
        _       => (role, "")
    };
}
