namespace RestaurantOrder.Models;

public enum TableStatus : byte
{
    Available = 0, // Trống  -> hiển thị màu TRẮNG trên máy thu ngân
    Occupied  = 1  // Đang sử dụng (đã có điện thoại giữ bàn) -> màu XANH, bàn bị khóa
}

/// <summary>Vòng đời đơn: khách gửi -> thu ngân xác nhận/từ chối -> thu tiền là xong đơn.</summary>
public enum OrderStatus : byte
{
    Pending   = 0, // Chờ thu ngân xác nhận
    Confirmed = 1, // Đã xác nhận, chờ thanh toán
    Paid      = 2, // Đã thanh toán: đơn hoàn tất
    Rejected  = 3, // Thu ngân từ chối (bắt buộc có lý do)
    Cancelled = 4  // Hủy khi giải phóng bàn lúc chưa thanh toán
}

public enum PaymentMethod : byte { Cash = 0, BankTransfer = 1 }

public enum SessionCloseType : byte { Closed = 0, ForceReleased = 1 } // Đóng bàn / Giải phóng bàn

public static class EnumText
{
    public static string ToText(this OrderStatus s) => s switch
    {
        OrderStatus.Pending   => "Chờ xác nhận",
        OrderStatus.Confirmed => "Chờ thanh toán",
        OrderStatus.Paid      => "Đã thanh toán",
        OrderStatus.Rejected  => "Bị từ chối",
        OrderStatus.Cancelled => "Đã hủy",
        _ => s.ToString()
    };

    public static string ToText(this PaymentMethod m) => m switch
    {
        PaymentMethod.Cash         => "Tiền mặt",
        PaymentMethod.BankTransfer => "Chuyển khoản",
        _ => m.ToString()
    };
}
