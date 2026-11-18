using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using RestaurantOrder.Data;
using RestaurantOrder.Models;

namespace RestaurantOrder.Pages.Admin;

/// <summary>Quản lý xem và in mã QR dán trên từng bàn (chính sách "AdminOnly" khai báo trong Program.cs).</summary>
public class QrCodesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IServer _server;

    public QrCodesModel(AppDbContext db, IServer server)
    {
        _db = db;
        _server = server;
    }

    public record TableQr(int Id, string Name, bool Occupied, string Url, string Svg);

    /// <summary>Địa chỉ web điện thoại mở được, ví dụ http://192.168.1.5:5090. Để trống thì tự đoán.</summary>
    [BindProperty(SupportsGet = true)] public string? BaseUrl { get; set; }
    public List<TableQr> Tables { get; private set; } = new();
    /// <summary>Web chỉ nghe trên localhost -> điện thoại quét mã sẽ không vào được.</summary>
    public bool LocalhostOnly { get; private set; }

    public async Task OnGetAsync()
    {
        var lanPort = LanPort();
        LocalhostOnly = lanPort is null;
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl(lanPort) : BaseUrl.Trim().TrimEnd('/');

        var tables = await _db.DiningTables.OrderBy(t => t.Id).ToListAsync();
        using var generator = new QRCodeGenerator();
        foreach (var t in tables)
        {
            var url = $"{BaseUrl}/order/t/{t.QrToken}";
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            // Dùng viewBox để ảnh co theo khung thẻ; width/height cố định sẽ bị cắt mép và máy quét không đọc được
            var svg = new SvgQRCode(data).GetGraphic(6, "#000000", "#FFFFFF", true, SvgQRCode.SizingMode.ViewBoxAttribute);
            Tables.Add(new TableQr(t.Id, t.Name, t.Status == TableStatus.Occupied, url, svg));
        }
    }

    [TempData] public string? StatusMessage { get; set; }

    /// <summary>Giải phóng bàn bị giữ (UC06): đóng phiên đang mở để điện thoại khác quét được.</summary>
    public async Task<IActionResult> OnPostReleaseAsync(int id)
    {
        var table = await _db.DiningTables.FindAsync(id);
        if (table is null) return NotFound();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var cancelled = await TableSessionActions.EndAsync(_db, table, userId,
            SessionCloseType.ForceReleased, "Giải phóng từ trang mã QR");

        StatusMessage = $"Đã giải phóng {table.Name}. Khách có thể quét mã lại."
                        + (cancelled > 0 ? $" {cancelled} đơn chưa thanh toán đã bị hủy." : "");
        return RedirectToPage(new { BaseUrl });
    }

    // Máy chạy web thường mở bằng localhost, nhưng điện thoại cần IP của máy trong mạng Wi-Fi.
    private string DefaultBaseUrl(int? lanPort)
    {
        var host = Request.Host.Host;
        var isLocal = host is "localhost" or "127.0.0.1" or "[::1]";
        if (!isLocal || lanPort is null) return $"{Request.Scheme}://{Request.Host}";
        return $"http://{LanIPv4() ?? host}:{lanPort}";
    }

    /// <summary>Cổng http đang nghe trên mọi địa chỉ mạng (0.0.0.0, [::], *).</summary>
    private int? LanPort()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        foreach (var a in addresses.Where(a => a.StartsWith("http://")))
        {
            var hostPort = a["http://".Length..].TrimEnd('/');
            var colon = hostPort.LastIndexOf(':');
            var host = hostPort[..colon];
            if (host is "0.0.0.0" or "[::]" or "*" or "+" && int.TryParse(hostPort[(colon + 1)..], out var port))
                return port;
        }
        return null;
    }

    /// <summary>
    /// IPv4 của card mạng có gateway: ưu tiên Wi-Fi, rồi card thật (không phải VPN/ảo như Radmin, TAP),
    /// rồi địa chỉ nội bộ (192.168.x, 10.x, 172.16–31.x). Điện thoại không vào được IP của card VPN.
    /// </summary>
    private static string? LanIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => (Nic: n, Bytes: u.Address.GetAddressBytes())))
            .OrderByDescending(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ThenByDescending(x => !IsVirtual(x.Nic))
            .ThenByDescending(x => x.Bytes[0] == 10 || x.Bytes[0] == 192 && x.Bytes[1] == 168
                                   || x.Bytes[0] == 172 && x.Bytes[1] is >= 16 and <= 31)
            .Select(x => string.Join('.', x.Bytes))
            .FirstOrDefault();

    private static bool IsVirtual(NetworkInterface n) =>
        new[] { "VPN", "Virtual", "TAP", "Hyper-V", "VMware", "VirtualBox", "Loopback" }
            .Any(k => n.Description.Contains(k, StringComparison.OrdinalIgnoreCase)
                      || n.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
