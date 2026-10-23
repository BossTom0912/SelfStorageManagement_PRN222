namespace SelfStorageManagementSystem.Web.Models;

/// <summary>
/// Mô hình hiển thị trạng thái rỗng (Empty State) cho danh sách, bảng dữ liệu hoặc kết quả tìm kiếm.
/// </summary>
public class EmptyStateViewModel
{
    public string Title { get; set; } = "Chưa có dữ liệu";
    public string Message { get; set; } = "Hiện tại chưa có bản ghi hoặc thông tin nào để hiển thị.";
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
    public string Icon { get; set; } = "&#128451;"; // Hộp lưu trữ / Thư mục rỗng
}
