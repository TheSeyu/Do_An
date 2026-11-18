using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Customer;

/// <summary>
/// Trang khách mở khi quét QR trên bàn. Khách không đăng nhập: điện thoại quét đầu tiên mở
/// phiên bàn và giữ SessionToken trong cookie; máy khác quét lại sẽ thấy bàn đang bị khóa.
/// </summary>
public class TableModel : PageModel
{
    public const string CookieName = "TableSession";
    private const int MaxQuantity = 20;

    private readonly AppDbContext _db;

    public TableModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string QrToken { get; set; } = "";
    [BindProperty] public List<CartLine> Lines { get; set; } = new();
    [BindProperty, StringLength(300, ErrorMessage = "Ghi chú tối đa 300 ký tự")]
    public string? CustomerNote { get; set; }
    [TempData] public string? StatusMessage { get; set; }

    public DiningTable? Table { get; private set; }
    /// <summary>Bàn đang có phiên mở của điện thoại khác.</summary>
    public bool Locked { get; private set; }
    public List<Category> Menu { get; private set; } = new();
    public List<Models.Order> Orders { get; private set; } = new();

    public class CartLine
    {
        public int MenuItemId { get; set; }
        public int Quantity { get; set; }
    }

    public int QuantityOf(int menuItemId) =>
        Lines.Where(l => l.MenuItemId == menuItemId).Sum(l => l.Quantity);

    public async Task<IActionResult> OnGetAsync()
    {
        var session = await GetSessionAsync(openIfFree: true);
        if (Table is null) return NotFound();
        if (session is not null) await LoadAsync(session);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var session = await GetSessionAsync(openIfFree: false);
        if (Table is null) return NotFound();
        // Phiên đã bị thu ngân đóng/giải phóng hoặc cookie mất -> quay lại như vừa quét mã
        if (session is null) return RedirectToPage(new { qrToken = QrToken });

        var lines = Lines.Where(l => l.Quantity > 0)
            .GroupBy(l => l.MenuItemId)
            .Select(g => (MenuItemId: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();
        var ids = lines.Select(l => l.MenuItemId).ToList();
        var items = await _db.MenuItems.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        if (lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Chọn ít nhất 1 món trước khi gửi đơn.");
        if (lines.Any(l => l.Quantity > MaxQuantity))
            ModelState.AddModelError(string.Empty, $"Mỗi món tối đa {MaxQuantity} phần trong một đơn.");
        foreach (var l in lines.Where(l => !items.TryGetValue(l.MenuItemId, out var m) || !m.IsAvailable))
            ModelState.AddModelError(string.Empty,
                $"Món “{(items.TryGetValue(l.MenuItemId, out var m) ? m.Name : "không rõ")}” đã hết, vui lòng bỏ ra khỏi đơn.");

        if (!ModelState.IsValid)
        {
            await LoadAsync(session);
            return Page();
        }

        var order = new Models.Order
        {
            Code = await NextCodeAsync(),
            TableSessionId = session.Id,
            CustomerNote = string.IsNullOrWhiteSpace(CustomerNote) ? null : CustomerNote.Trim(),
            // Lấy giá từ CSDL, không tin giá gửi lên từ trình duyệt
            Items = lines.Select(l => new OrderItem
            {
                MenuItemId = l.MenuItemId,
                Quantity = l.Quantity,
                UnitPrice = items[l.MenuItemId].Price
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        StatusMessage = $"Đã gửi đơn {order.Code}. Thu ngân sẽ xác nhận trong ít phút.";
        return RedirectToPage(new { qrToken = QrToken }); // PRG: tránh gửi lại đơn khi F5
    }

    /// <summary>Trả về phiên bàn của điện thoại này; null nếu bàn bị máy khác giữ hoặc chưa mở phiên.</summary>
    private async Task<TableSession?> GetSessionAsync(bool openIfFree)
    {
        Table = await _db.DiningTables.FirstOrDefaultAsync(t => t.QrToken == QrToken);
        if (Table is null) return null;

        var open = await _db.TableSessions.FirstOrDefaultAsync(s => s.TableId == Table.Id && s.EndedAt == null);
        if (open is not null)
        {
            if (open.SessionToken == Request.Cookies[CookieName]) return open;
            Locked = true;
            return null;
        }
        if (!openIfFree) return null;

        open = new TableSession { TableId = Table.Id, SessionToken = Guid.NewGuid().ToString("N") };
        Table.Status = TableStatus.Occupied;
        _db.TableSessions.Add(open);
        await _db.SaveChangesAsync();

        Response.Cookies.Append(CookieName, open.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.Now.AddHours(12)
        });
        return open;
    }

    private async Task LoadAsync(TableSession session)
    {
        Menu = await _db.Categories.OrderBy(c => c.SortOrder)
            .Include(c => c.MenuItems.OrderBy(m => m.Id))
            .ToListAsync();
        Orders = await _db.Orders.Where(o => o.TableSessionId == session.Id)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Include(o => o.RejectReason)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    private async Task<string> NextCodeAsync()
    {
        var lastId = await _db.Orders.MaxAsync(o => (int?)o.Id) ?? 0;
        return $"DH{lastId + 1:D4}";
    }
}
