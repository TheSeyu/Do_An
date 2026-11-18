using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Constants;
using RestaurantOrder.Models;

namespace RestaurantOrder.Data;

/// <summary>
/// CSDL được tạo bằng script RestaurantOrderDb.sql. Nếu chưa chạy script thì tự tạo bảng,
/// thêm 4 vai trò và 1 tài khoản quản lý đầu tiên.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Không dùng Migrate: script SQL tự tạo bảng và xóa __EFMigrationsHistory,
        // Migrate sẽ tạo lại bảng và báo lỗi "There is already an object named ...".
        // EnsureCreated không làm gì khi CSDL đã có bảng.
        await db.Database.EnsureCreatedAsync();

        foreach (var name in RoleNames.All)
            if (!await db.Roles.AnyAsync(r => r.Name == name))
                db.Roles.Add(new Role { Name = name });
        await db.SaveChangesAsync();

        // Đăng ký chỉ dành cho Quản lý, nên cần sẵn 1 tài khoản Quản lý để bắt đầu.
        if (!await db.Users.AnyAsync())
        {
            var adminRoleId = await db.Roles.Where(r => r.Name == RoleNames.Admin).Select(r => r.Id).FirstAsync();
            var admin = new User { Username = "admin", FullName = "Quản lý nhà hàng", RoleId = adminRoleId };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, "Admin@123");
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
    }
}
