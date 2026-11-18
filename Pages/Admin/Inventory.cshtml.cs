using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Admin;

/// <summary>
/// Kho: xem số phần còn lại của từng món; mỗi dòng có ô nhập kho và nút ngừng bán / bán lại
/// (chính sách "AdminOnly" khai báo trong Program.cs).
/// </summary>
public class InventoryModel : PageModel
{
    private const int MaxChange = 100_000;
    private readonly AppDbContext _db;

    public InventoryModel(AppDbContext db) => _db = db;

    /// <summary>"low" = chỉ món sắp hết / hết hàng; trống = tất cả.</summary>
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }
    /// <summary>Món vừa sửa: thông báo hiện ngay trong dòng của món đó.</summary>
    [TempData] public int? EditedId { get; set; }

    public List<(string Category, List<MenuItem> Items)> Groups { get; private set; } = new();
    public int TotalItems { get; private set; }
    public int LowItems { get; private set; }
    public int OutItems { get; private set; }
    public int StoppedItems { get; private set; }

    public static (string Text, string Css) StatusOf(MenuItem m) =>
        !m.IsAvailable ? ("Ngừng bán", "off")
        : m.Stock == 0 ? ("Hết hàng", "out")
        : m.Stock <= StockActions.LowStock ? ("Sắp hết", "low")
        : ("Còn hàng", "ok");

    public async Task OnGetAsync()
    {
        var items = await _db.MenuItems.AsNoTracking()
            .Include(m => m.Category)
            .OrderBy(m => m.Category.SortOrder).ThenBy(m => m.Id)
            .ToListAsync();

        TotalItems = items.Count;
        StoppedItems = items.Count(m => !m.IsAvailable);
        OutItems = items.Count(m => m.IsAvailable && m.Stock == 0);
        LowItems = items.Count(m => m.IsAvailable && m.Stock is > 0 and <= StockActions.LowStock);

        if (Filter == "low") items = items.Where(m => m.Stock <= StockActions.LowStock).ToList();
        else Filter = null;

        Groups = items.GroupBy(m => m.Category.Name)
            .Select(g => (g.Key, g.ToList()))
            .ToList();
    }

    /// <summary>Nhập kho một món (số dương) hoặc trừ khi kiểm kê thấy thiếu (số âm).</summary>
    public async Task<IActionResult> OnPostAdjustAsync(int id, int add)
    {
        var item = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return BackTo(id, error: "Không tìm thấy món.");
        if (add == 0) return BackTo(id, error: $"Nhập số phần khác 0 cho {item.Name}.");
        if (Math.Abs(add) > MaxChange) return BackTo(id, error: $"Mỗi lần nhập tối đa {MaxChange:N0} phần.");

        if (!await StockActions.AdjustAsync(_db, id, add))
            return BackTo(id, error: $"{item.Name} chỉ còn {item.Stock} phần, không thể trừ {-add}.");

        var after = item.Stock + add;
        return BackTo(id, ok: add > 0
            ? $"Đã nhập {add} phần {item.Name}. Tồn kho: {after}."
            : $"Đã trừ {-add} phần {item.Name}. Tồn kho: {after}.");
    }

    /// <summary>Tạm ngừng bán / bán lại một món (không đổi số tồn).</summary>
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var item = await _db.MenuItems.FindAsync(id);
        if (item is null) return BackTo(id, error: "Không tìm thấy món.");
        item.IsAvailable = !item.IsAvailable;
        await _db.SaveChangesAsync();
        return BackTo(id, ok: item.IsAvailable ? $"{item.Name} đã được bán lại." : $"Đã ngừng bán {item.Name}.");
    }

    /// <summary>PRG: quay về đúng dòng món vừa sửa (#item-{id}), giữ bộ lọc đang chọn.</summary>
    private RedirectToPageResult BackTo(int id, string? ok = null, string? error = null)
    {
        StatusMessage = ok;
        ErrorMessage = error;
        EditedId = id;
        return RedirectToPage(null, null, new { filter = Filter }, $"item-{id}");
    }
}
