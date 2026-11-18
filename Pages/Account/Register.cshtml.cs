using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Constants;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Account;

/// <summary>Quản lý tạo tài khoản nhân viên (chính sách "AdminOnly" khai báo trong Program.cs).</summary>
public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public RegisterModel(AppDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Role> Roles { get; private set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Nhập họ tên")]
        [StringLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự")]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Nhập tên đăng nhập")]
        [RegularExpression(@"^[a-zA-Z0-9_.]{4,50}$",
            ErrorMessage = "4–50 ký tự, chỉ gồm chữ không dấu, số, dấu chấm hoặc gạch dưới")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Nhập mật khẩu")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu từ 6 ký tự trở lên")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Nhập lại mật khẩu")]
        [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp")]
        [DataType(DataType.Password)]
        [Display(Name = "Nhập lại mật khẩu")]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessage = "Chọn vai trò")]
        [Display(Name = "Vai trò")]
        public int? RoleId { get; set; }
    }

    public async Task OnGetAsync() => await LoadRolesAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadRolesAsync();
        Input.Username = Input.Username?.Trim() ?? "";

        if (Input.RoleId is not null && Roles.All(r => r.Id != Input.RoleId))
            ModelState.AddModelError("Input.RoleId", "Vai trò không hợp lệ");

        if (ModelState.IsValid && await _db.Users.AnyAsync(u => u.Username == Input.Username))
            ModelState.AddModelError("Input.Username", "Tên đăng nhập này đã có người dùng");

        if (!ModelState.IsValid) return Page();

        var user = new User
        {
            FullName = Input.FullName.Trim(),
            Username = Input.Username,
            RoleId = Input.RoleId!.Value,
            IsActive = true
        };
        user.PasswordHash = _hasher.HashPassword(user, Input.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var roleName = RoleNames.Display(Roles.First(r => r.Id == user.RoleId).Name).Name;
        StatusMessage = $"Đã tạo tài khoản “{user.Username}” ({roleName}) cho {user.FullName}.";
        return RedirectToPage(); // PRG: tránh gửi lại form khi F5
    }

    private async Task LoadRolesAsync() =>
        Roles = await _db.Roles.OrderBy(r => r.Id).ToListAsync();
}
