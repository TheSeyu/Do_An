using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Models;

namespace RestaurantOrder.Data;

/// <summary>
/// Tăng / giảm số tồn kho của món. Mỗi lần đổi là một câu UPDATE có điều kiện chạy thẳng
/// trên SQL Server ("chỉ trừ nếu còn đủ"), nên hai thu ngân bấm cùng lúc cũng không bán quá kho.
/// Gọi trong transaction để đơn nhiều món được trừ đủ hoặc không trừ món nào.
/// </summary>
public static class StockActions
{
    /// <summary>Ngưỡng báo "Sắp hết".</summary>
    public const int LowStock = 5;

    /// <summary>Trừ kho theo các món của đơn. Trả về danh sách món thiếu (rỗng = trừ thành công).</summary>
    public static async Task<List<string>> TakeAsync(AppDbContext db, IEnumerable<OrderItem> items)
    {
        var shortages = new List<string>();
        foreach (var (menuItemId, quantity) in Group(items))
        {
            var updated = await db.MenuItems
                .Where(m => m.Id == menuItemId && m.Stock >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock - quantity));
            if (updated == 0)
            {
                var m = await db.MenuItems.AsNoTracking().Where(m => m.Id == menuItemId)
                    .Select(m => new { m.Name, m.Stock }).FirstAsync();
                shortages.Add($"{m.Name} còn {m.Stock} (cần {quantity})");
            }
        }
        return shortages;
    }

    /// <summary>Trả lại kho khi đơn đã xác nhận bị hủy.</summary>
    public static async Task ReturnAsync(AppDbContext db, IEnumerable<OrderItem> items)
    {
        foreach (var (menuItemId, quantity) in Group(items))
            await db.MenuItems.Where(m => m.Id == menuItemId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock + quantity));
    }

    /// <summary>Nhập kho (số dương) hoặc điều chỉnh khi kiểm kê (số âm). False nếu kết quả bị âm.</summary>
    public static async Task<bool> AdjustAsync(AppDbContext db, int menuItemId, int change) =>
        await db.MenuItems
            .Where(m => m.Id == menuItemId && m.Stock + change >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock + change)) == 1;

    private static IEnumerable<(int MenuItemId, int Quantity)> Group(IEnumerable<OrderItem> items) =>
        items.GroupBy(i => i.MenuItemId).Select(g => (g.Key, g.Sum(i => i.Quantity)));
}
