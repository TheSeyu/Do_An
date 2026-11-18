using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Models;

namespace RestaurantOrder.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<RejectReason> RejectReasons => Set<RejectReason>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();
        b.Entity<DiningTable>().HasIndex(x => x.QrToken).IsUnique();
        b.Entity<TableSession>().HasIndex(x => x.SessionToken).IsUnique();
        b.Entity<Order>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Order>().HasIndex(x => x.Status); // thu ngân lọc theo trạng thái liên tục

        // 1 đơn - tối đa 1 lần thanh toán
        b.Entity<Payment>().HasIndex(x => x.OrderId).IsUnique();
        b.Entity<Order>().HasOne(o => o.Payment).WithOne(p => p.Order)
            .HasForeignKey<Payment>(p => p.OrderId);

        // Nhiều FK tới Users -> tắt cascade để SQL Server không báo "multiple cascade paths"
        b.Entity<User>().HasOne(u => u.Role).WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TableSession>().HasOne(s => s.ClosedBy).WithMany()
            .HasForeignKey(s => s.ClosedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne(o => o.HandledBy).WithMany()
            .HasForeignKey(o => o.HandledById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne(o => o.RejectReason).WithMany()
            .HasForeignKey(o => o.RejectReasonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasOne(p => p.Cashier).WithMany()
            .HasForeignKey(p => p.CashierId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderItem>().HasOne(i => i.MenuItem).WithMany()
            .HasForeignKey(i => i.MenuItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
