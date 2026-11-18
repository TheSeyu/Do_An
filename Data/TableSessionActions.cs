using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Models;

namespace RestaurantOrder.Data;

/// <summary>Kết thúc phiên bàn, dùng chung cho màn hình thu ngân và trang mã QR.</summary>
public static class TableSessionActions
{
    /// <summary>
    /// Đóng phiên đang mở của bàn và trả bàn về Trống. Khi giải phóng bàn (UC06), các đơn chưa
    /// thanh toán (chờ xác nhận / chờ thanh toán) bị hủy vì không còn khách để thu tiền,
    /// món của đơn đã xác nhận được trả lại kho.
    /// Trả về số đơn bị hủy.
    /// </summary>
    public static async Task<int> EndAsync(AppDbContext db, DiningTable table, int userId,
        SessionCloseType type, string? reason)
    {
        var now = DateTime.Now;
        var sessions = await db.TableSessions.Include(s => s.Orders).ThenInclude(o => o.Items)
            .Where(s => s.TableId == table.Id && s.EndedAt == null)
            .ToListAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        var cancelled = 0;
        foreach (var s in sessions)
        {
            s.EndedAt = now;
            s.ClosedById = userId;
            s.CloseType = type;
            s.CloseReason = reason;
            if (type != SessionCloseType.ForceReleased) continue;
            foreach (var o in s.Orders.Where(o => o.Status is OrderStatus.Pending or OrderStatus.Confirmed))
            {
                // Đơn đã xác nhận đã trừ kho -> trả lại; đơn chờ xác nhận chưa trừ nên bỏ qua
                if (o.Status == OrderStatus.Confirmed) await StockActions.ReturnAsync(db, o.Items);
                o.Status = OrderStatus.Cancelled;
                cancelled++;
            }
        }
        table.Status = TableStatus.Available;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return cancelled;
    }
}
