using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Constants;
using RestaurantOrder.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/AccessDenied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(10); // khoảng 1 ca làm
        opt.SlidingExpiration = true;
        opt.Cookie.HttpOnly = true;
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("AdminOnly", p => p.RequireRole(RoleNames.Admin));
    opt.AddPolicy("CashierOnly", p => p.RequireRole(RoleNames.Cashier, RoleNames.Admin));
});

builder.Services.AddRazorPages(opt =>
{
    opt.Conventions.AuthorizeFolder("/");                        // mặc định: mọi trang phải đăng nhập
    opt.Conventions.AllowAnonymousToPage("/Account/Login");
    opt.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    opt.Conventions.AuthorizePage("/Account/Register", "AdminOnly"); // chỉ Quản lý tạo tài khoản
    opt.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    opt.Conventions.AuthorizeFolder("/Cashier", "CashierOnly");    // thu ngân và quản lý
    opt.Conventions.AllowAnonymousToFolder("/Customer");             // khách quét QR, không đăng nhập
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

await DbSeeder.SeedAsync(app.Services);
app.Run();
