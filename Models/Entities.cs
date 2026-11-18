using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantOrder.Models;

public class Role
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Username { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    [Required, MaxLength(100)] public string FullName { get; set; } = "";
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
}

public class MenuItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [Column(TypeName = "decimal(18,0)")] public decimal Price { get; set; }
    [MaxLength(300)] public string? ImageUrl { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class DiningTable
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    public int Capacity { get; set; } = 4;
    /// <summary>Chuỗi ngẫu nhiên in trong mã QR: /order/t/{QrToken}. Không dùng Id để tránh đoán URL bàn khác.</summary>
    [Required, MaxLength(64)] public string QrToken { get; set; } = "";
    public TableStatus Status { get; set; } = TableStatus.Available;
    public ICollection<TableSession> Sessions { get; set; } = new List<TableSession>();
}

/// <summary>Một lượt khách ngồi bàn: mở khi quét QR, đóng khi thu ngân bấm "Đóng bàn".</summary>
public class TableSession
{
    public int Id { get; set; }
    public int TableId { get; set; }
    public DiningTable Table { get; set; } = null!;
    [Required, MaxLength(64)] public string SessionToken { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }
    public int? ClosedById { get; set; }
    public User? ClosedBy { get; set; }
    /// <summary>Đóng bàn bình thường hay giải phóng bàn bị treo (UC06).</summary>
    public SessionCloseType? CloseType { get; set; }
    [MaxLength(200)] public string? CloseReason { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class RejectReason
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Content { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Order
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    public int TableSessionId { get; set; }
    public TableSession TableSession { get; set; } = null!;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    [Column(TypeName = "decimal(18,0)")] public decimal TotalAmount { get; set; }
    [MaxLength(300)] public string? CustomerNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Thu ngân xử lý (xác nhận hoặc từ chối)
    public int? HandledById { get; set; }
    public User? HandledBy { get; set; }
    public DateTime? HandledAt { get; set; }
    public int? RejectReasonId { get; set; }
    public RejectReason? RejectReason { get; set; }
    [MaxLength(300)] public string? RejectNote { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public Payment? Payment { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; }
    /// <summary>Giá tại thời điểm đặt (không đổi khi admin sửa giá món sau này).</summary>
    [Column(TypeName = "decimal(18,0)")] public decimal UnitPrice { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public PaymentMethod Method { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.Now;
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
}
