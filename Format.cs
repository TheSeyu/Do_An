using System.Globalization;

namespace RestaurantOrder;

/// <summary>Định dạng hiển thị dùng chung cho các trang.</summary>
public static class Format
{
    private static readonly CultureInfo Vi = new("vi-VN");

    public static string Money(decimal v) => v.ToString("#,0", Vi) + " đ";

    /// <summary>Số tiền rút gọn cho trục biểu đồ: "2,5 tr", "800 k".</summary>
    public static string ShortMoney(decimal v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000).ToString("0.#", Vi) + " tr",
        >= 1_000     => (v / 1_000).ToString("0.#", Vi) + " k",
        _            => v.ToString("0", Vi)
    };

    /// <summary>"vừa xong", "12 phút trước", "3 giờ trước" hoặc ngày giờ nếu đã lâu.</summary>
    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "vừa xong";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} phút trước";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} giờ trước";
        return t.ToString("dd/MM HH:mm");
    }

    /// <summary>"15:22 · 3 phút trước" trong ngày, hoặc "25/10 09:16" nếu đã quá 24 giờ.</summary>
    public static string When(DateTime t) =>
        DateTime.Now - t < TimeSpan.FromHours(24) ? $"{t:HH:mm} · {Ago(t)}" : t.ToString("dd/MM HH:mm");
}
