using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Cashier;

/// <summary>
/// Màn hình thu ngân: sơ đồ bàn, đơn chờ xác nhận, xác nhận / từ chối, thu tiền,
/// đóng bàn và giải phóng bàn (chính sách "CashierOnly" khai báo trong Program.cs).
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public record TableTile(int Id, string Name, int Capacity, bool Occupied, int Pending, int Unpaid);

    [BindProperty(SupportsGet = true, Name = "table")] public int? SelectedId { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public List<TableTile> Tables { get; private set; } = new();
    /// <summary>Đơn chờ xác nhận của các bàn đang mở, cũ nhất lên trước.</summary>
    public List<Models.Order> Queue { get; private set; } = new();
    public DiningTable? Selected { get; private set; }
    public TableSession? Session { get; private set; }
    public List<Models.Order> Orders { get; private set; } = new();
    public List<RejectReason> Reasons { get; private set; } = new();
    public decimal RevenueToday { get; private set; }
    public int PaidToday { get; private set; }
    /// <summary>Dấu thay đổi dữ liệu; trình duyệt so với /Cashier?handler=State để biết có đơn mới.</summary>
    public string Version { get; private set; } = "";

    public int UnpaidCount => Orders.Count(o => o.Status is OrderStatus.Pending or OrderStatus.Confirmed);

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        var open = (await _db.TableSessions.Where(s => s.EndedAt == null)
                .Select(s => new
                {
                    s.TableId,
                    Pending = s.Orders.Count(o => o.Status == OrderStatus.Pending),
                    Unpaid = s.Orders.Count(o => o.Status == OrderStatus.Confirmed)
                })
                .ToListAsync())
            .GroupBy(s => s.TableId)
            .ToDictionary(g => g.Key, g => (Pending: g.Sum(x => x.Pending), Unpaid: g.Sum(x => x.Unpaid)));

        var tables = await _db.DiningTables.OrderBy(t => t.Id).ToListAsync();
        Tables = tables.Select(t =>
        {
            var hasSession = open.TryGetValue(t.Id, out var c);
            return new TableTile(t.Id, t.Name, t.Capacity,
                hasSession || t.Status == TableStatus.Occupied, c.Pending, c.Unpaid);
        }).ToList();

        Queue = await _db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.TableSession.EndedAt == null)
            .Include(o => o.TableSession).ThenInclude(s => s.Table)
            .Include(o => o.Items)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        // Chưa chọn bàn -> mở luôn bàn có đơn chờ lâu nhất
        SelectedId ??= Queue.FirstOrDefault()?.TableSession.TableId;
        Selected = tables.FirstOrDefault(t => t.Id == SelectedId);
        if (Selected is not null)
        {
            Session = await _db.TableSessions
                .Where(s => s.TableId == Selected.Id && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
            if (Session is not null)
                Orders = await _db.Orders.Where(o => o.TableSessionId == Session.Id)
                    .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                    .Include(o => o.Payment)
                    .Include(o => o.RejectReason)
                    .OrderBy(o => o.CreatedAt)
                    .ToListAsync();
        }

        Reasons = await _db.RejectReasons.Where(r => r.IsActive).OrderBy(r => r.Id).ToListAsync();

        var today = DateTime.Today;
        var paidToday = _db.Payments.Where(p => p.PaidAt >= today);
        RevenueToday = await paidToday.SumAsync(p => (decimal?)p.Amount) ?? 0;
        PaidToday = await paidToday.CountAsync();

        Version = await VersionAsync();
    }

    /// <summary>
    /// Trình duyệt hỏi mỗi 2 giây: dấu phiên bản để biết có cần vẽ lại, kèm các đơn đang chờ
    /// xác nhận để báo "Đơn mới" ngay cả khi thu ngân đang nhập dở.
    /// </summary>
    public async Task<IActionResult> OnGetStateAsync()
    {
        var pending = await _db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.TableSession.EndedAt == null)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                code = o.Code,
                tableId = o.TableSession.TableId,
                table = o.TableSession.Table.Name,
                total = Format.Money(o.TotalAmount)
            })
            .ToListAsync();
        return new JsonResult(new { version = await VersionAsync(), pending });
    }

    public async Task<IActionResult> OnPostConfirmAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return Back(error: "Không tìm thấy đơn.");
        if (order.Status != OrderStatus.Pending)
            return Back(error: $"Đơn {order.Code} đã được xử lý trước đó ({order.Status.ToText()}).");

        // Xác nhận = trừ kho. Thiếu bất kỳ món nào thì hoàn tác toàn bộ, đơn vẫn chờ xác nhận.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var shortages = await StockActions.TakeAsync(_db, order.Items);
        if (shortages.Count > 0)
        {
            await tx.RollbackAsync();
            return Back(error: $"Không đủ kho để xác nhận đơn {order.Code}: {string.Join(", ", shortages)}. "
                               + "Hãy từ chối đơn hoặc nhờ quản lý nhập thêm kho.");
        }

        order.Status = OrderStatus.Confirmed;
        order.HandledById = UserId;
        order.HandledAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return Back(ok: $"Đã xác nhận đơn {order.Code} và trừ kho. Đơn đang chờ thanh toán.");
    }

    public async Task<IActionResult> OnPostRejectAsync(int orderId, int reasonId, string? note)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return Back(error: "Không tìm thấy đơn.");
        if (order.Status != OrderStatus.Pending)
            return Back(error: $"Đơn {order.Code} đã được xử lý trước đó ({order.Status.ToText()}).");

        var reason = await _db.RejectReasons.FirstOrDefaultAsync(r => r.Id == reasonId && r.IsActive);
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reason is null) return Back(error: "Chọn lý do từ chối.");
        if (reason.Content.StartsWith("Khác") && note is null)
            return Back(error: "Lý do “Khác” cần ghi rõ ở ô ghi chú.");
        if (note?.Length > 300) return Back(error: "Ghi chú tối đa 300 ký tự.");

        order.Status = OrderStatus.Rejected;
        order.RejectReasonId = reason.Id;
        order.RejectNote = note;
        order.HandledById = UserId;
        order.HandledAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Back(ok: $"Đã từ chối đơn {order.Code}: {reason.Content}.");
    }

    public async Task<IActionResult> OnPostPayAsync(int orderId, PaymentMethod method)
    {
        var order = await _db.Orders.Include(o => o.Payment).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return Back(error: "Không tìm thấy đơn.");
        if (order.Status != OrderStatus.Confirmed || order.Payment is not null)
            return Back(error: $"Đơn {order.Code} không ở trạng thái chờ thanh toán ({order.Status.ToText()}).");
        if (!Enum.IsDefined(method)) return Back(error: "Chọn hình thức thanh toán.");

        order.Payment = new Payment
        {
            Method = method,
            Amount = order.TotalAmount,
            PaidAt = DateTime.Now,
            CashierId = UserId
        };
        order.Status = OrderStatus.Paid; // thu tiền xong là hoàn tất đơn
        await _db.SaveChangesAsync();
        return Back(ok: $"Đã thu {Format.Money(order.TotalAmount)} ({method.ToText()}) cho đơn {order.Code}.");
    }

    public async Task<IActionResult> OnPostCloseAsync()
    {
        var table = await _db.DiningTables.FindAsync(SelectedId);
        if (table is null) return Back(error: "Không tìm thấy bàn.");

        var unpaid = await _db.Orders.CountAsync(o => o.TableSession.TableId == table.Id
            && o.TableSession.EndedAt == null
            && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed));
        if (unpaid > 0)
            return Back(error: $"{table.Name} còn {unpaid} đơn chưa xác nhận hoặc chưa thanh toán, chưa đóng bàn được.");

        await TableSessionActions.EndAsync(_db, table, UserId, SessionCloseType.Closed, null);
        return Back(ok: $"Đã đóng {table.Name}. Bàn trống, khách mới có thể quét mã.");
    }

    public async Task<IActionResult> OnPostReleaseAsync(string? reason)
    {
        var table = await _db.DiningTables.FindAsync(SelectedId);
        if (table is null) return Back(error: "Không tìm thấy bàn.");
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return Back(error: "Nhập lý do giải phóng bàn.");
        if (reason.Length > 200) return Back(error: "Lý do tối đa 200 ký tự.");

        var cancelled = await TableSessionActions.EndAsync(_db, table, UserId, SessionCloseType.ForceReleased, reason);
        return Back(ok: $"Đã giải phóng {table.Name}."
                        + (cancelled > 0 ? $" {cancelled} đơn chưa thanh toán đã bị hủy." : ""));
    }

    /// <summary>PRG: quay về đúng bàn đang xem, tránh gửi lại thao tác khi F5.</summary>
    private RedirectToPageResult Back(string? ok = null, string? error = null)
    {
        StatusMessage = ok;
        ErrorMessage = error;
        return RedirectToPage(new { table = SelectedId });
    }

    private async Task<string> VersionAsync()
    {
        var lastOrder = await _db.Orders.MaxAsync(o => (int?)o.Id) ?? 0;
        var lastSession = await _db.TableSessions.MaxAsync(s => (int?)s.Id) ?? 0;
        var openSessions = await _db.TableSessions.CountAsync(s => s.EndedAt == null);
        var pending = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Pending);
        return $"{lastOrder}.{lastSession}.{openSessions}.{pending}";
    }
}
