using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Admin;

/// <summary>
/// Báo cáo doanh thu cho Quản lý (chính sách "AdminOnly" khai báo trong Program.cs).
/// Doanh thu tính theo thời điểm thu tiền (Payments.PaidAt), không theo lúc khách đặt đơn.
/// </summary>
public class RevenueModel : PageModel
{
    private const int MaxDays = 366;
    private readonly AppDbContext _db;

    public RevenueModel(AppDbContext db) => _db = db;

    public static readonly (string Key, string Label)[] Presets =
    {
        ("today", "Hôm nay"), ("7d", "7 ngày"), ("30d", "30 ngày"), ("month", "Tháng này"), ("lastmonth", "Tháng trước")
    };

    /// <summary>Khoảng có sẵn (Presets) hoặc "custom" khi chọn ngày bắt đầu / kết thúc.</summary>
    [BindProperty(SupportsGet = true)] public string? Range { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public record DayRow(DateTime Day, decimal Revenue, int Orders);
    public record MethodRow(PaymentMethod Method, int Orders, decimal Revenue);
    public record ItemRow(string Name, int Quantity, decimal Revenue);
    public record CashierRow(string Name, int Orders, decimal Revenue);

    public decimal Revenue { get; private set; }
    public int PaidOrders { get; private set; }
    public decimal AverageOrder { get; private set; }
    /// <summary>Doanh thu kỳ trước cùng độ dài, để so sánh tăng / giảm.</summary>
    public decimal PreviousRevenue { get; private set; }
    public int RejectedOrders { get; private set; }
    public int CancelledOrders { get; private set; }
    public List<DayRow> Days { get; private set; } = new();
    public List<MethodRow> Methods { get; private set; } = new();
    public List<ItemRow> TopItems { get; private set; } = new();
    public List<CashierRow> Cashiers { get; private set; } = new();
    public string? Notice { get; private set; }

    public DateTime Start => From!.Value;
    public DateTime End => To!.Value;
    public int DayCount => (End - Start).Days + 1;

    /// <summary>% thay đổi so với kỳ trước; null khi kỳ trước không có doanh thu.</summary>
    public decimal? ChangePercent =>
        PreviousRevenue > 0 ? Math.Round((Revenue - PreviousRevenue) / PreviousRevenue * 100, 1) : null;

    /// <summary>Đỉnh trục tung làm tròn lên (1, 1,5, 2, 3, 4, 5, 6, 8 × 10^n) để vạch chia dễ đọc.</summary>
    public decimal AxisMax
    {
        get
        {
            var max = Days.Count == 0 ? 0 : Days.Max(d => d.Revenue);
            if (max <= 0) return 1_000_000;
            var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
            foreach (var step in new[] { 1m, 1.5m, 2m, 3m, 4m, 5m, 6m, 8m, 10m })
                if (step * magnitude >= max) return step * magnitude;
            return 10 * magnitude;
        }
    }

    public async Task OnGetAsync()
    {
        ResolveRange();
        var end = End.AddDays(1);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= Start && p.PaidAt < end)
            .Include(p => p.Cashier)
            .Include(p => p.Order).ThenInclude(o => o.Items).ThenInclude(i => i.MenuItem)
            .ToListAsync();

        Revenue = payments.Sum(p => p.Amount);
        PaidOrders = payments.Count;
        AverageOrder = PaidOrders > 0 ? Math.Round(Revenue / PaidOrders) : 0;

        var previousStart = Start.AddDays(-DayCount);
        PreviousRevenue = await _db.Payments
            .Where(p => p.PaidAt >= previousStart && p.PaidAt < Start)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        // Đơn không mang lại doanh thu: tính theo lúc khách đặt
        RejectedOrders = await _db.Orders.CountAsync(o =>
            o.Status == OrderStatus.Rejected && o.CreatedAt >= Start && o.CreatedAt < end);
        CancelledOrders = await _db.Orders.CountAsync(o =>
            o.Status == OrderStatus.Cancelled && o.CreatedAt >= Start && o.CreatedAt < end);

        var byDay = payments.GroupBy(p => p.PaidAt.Date).ToDictionary(g => g.Key, g => g.ToList());
        Days = Enumerable.Range(0, DayCount).Select(i => Start.AddDays(i)).Select(d =>
            byDay.TryGetValue(d, out var ps) ? new DayRow(d, ps.Sum(p => p.Amount), ps.Count) : new DayRow(d, 0, 0))
            .ToList();

        Methods = Enum.GetValues<PaymentMethod>().Select(m =>
        {
            var ps = payments.Where(p => p.Method == m).ToList();
            return new MethodRow(m, ps.Count, ps.Sum(p => p.Amount));
        }).ToList();

        TopItems = payments.SelectMany(p => p.Order.Items)
            .GroupBy(i => i.MenuItemId)
            .Select(g => new ItemRow(g.First().MenuItem.Name, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
            .OrderByDescending(x => x.Revenue).ThenByDescending(x => x.Quantity)
            .Take(10)
            .ToList();

        Cashiers = payments.GroupBy(p => p.CashierId)
            .Select(g => new CashierRow(g.First().Cashier.FullName, g.Count(), g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Revenue)
            .ToList();
    }

    private void ResolveRange()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        if (From is not null || To is not null) Range = "custom";
        if (Range != "custom" && Presets.All(p => p.Key != Range)) Range = "30d";

        var (from, to) = Range switch
        {
            "today"     => (today, today),
            "7d"        => (today.AddDays(-6), today),
            "month"     => (monthStart, today),
            "lastmonth" => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            "custom"    => ((From ?? To)!.Value.Date, (To ?? From)!.Value.Date),
            _           => (today.AddDays(-29), today)
        };
        if (from > to) (from, to) = (to, from);
        if ((to - from).Days + 1 > MaxDays)
        {
            from = to.AddDays(-(MaxDays - 1));
            Notice = $"Mỗi lần xem tối đa {MaxDays} ngày, đã lấy {MaxDays} ngày gần nhất tính tới {to:dd/MM/yyyy}.";
        }
        From = from;
        To = to;
    }
}
