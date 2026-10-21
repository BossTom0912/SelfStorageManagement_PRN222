using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/facilities")]
[AllowAnonymous]
public class CatalogController : BaseController
{
    private readonly IFacilityCatalogService _catalogService;

    public CatalogController(IFacilityCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <summary>
    /// Tra cứu danh mục các chi nhánh/cơ sở kho đang hoạt động (Public / Customer Catalog).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FacilityCatalogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFacilities(
        [FromQuery] GetFacilitiesCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetFacilitiesAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedResult<FacilityCatalogDto>>.Ok(result, "Danh sách cơ sở đã được tải thành công."));
    }

    /// <summary>
    /// Tra cứu thông tin chi tiết một cơ sở theo mã ID.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<FacilityCatalogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFacilityById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetFacilityByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<FacilityCatalogDto>.Ok(result, "Chi tiết cơ sở đã được tải thành công."));
    }

    /// <summary>
    /// Tra cứu danh mục loại kho, giá niêm yết và số ô khả dụng tham khảo tại cơ sở.
    /// Giá được lấy từ facility_rates; thông tin khả dụng được gắn nhãn trung thực theo BR-RSV-03.
    /// </summary>
    [HttpGet("{id:long}/unit-types")]
    [ProducesResponseType(typeof(ApiResponse<List<FacilityUnitTypeCatalogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFacilityUnitTypes(
        long id,
        [FromQuery] GetFacilityUnitTypesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetFacilityUnitTypesAsync(id, request, cancellationToken);
        return Ok(ApiResponse<List<FacilityUnitTypeCatalogDto>>.Ok(result, "Danh mục loại kho và giá niêm yết đã được tải thành công."));
    }

    /// <summary>
    /// Tra cứu danh sách các ô kho ứng viên đang khả dụng để đặt chỗ.
    /// Tuân thủ BR-OPS-02: loại trừ hoàn toàn các ô occupied, reserved, maintenance, out_of_service, v.v.
    /// </summary>
    [HttpGet("{id:long}/units/available")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AvailableStorageUnitDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableUnits(
        long id,
        [FromQuery] GetAvailableUnitsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetAvailableUnitsAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PagedResult<AvailableStorageUnitDto>>.Ok(result, "Danh sách ô kho khả dụng đã được tải thành công."));
    }

    /// <summary>
    /// Lấy dữ liệu sơ đồ mặt bằng và tọa độ hiển thị của các ô kho.
    /// Dữ liệu đã được giản lược trạng thái và loại trừ thông tin nhạy cảm của khách thuê.
    /// </summary>
    [HttpGet("{id:long}/floor-map")]
    [ProducesResponseType(typeof(ApiResponse<FacilityFloorMapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFloorMap(
        long id,
        [FromQuery] GetFacilityFloorMapRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetFacilityFloorMapAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FacilityFloorMapDto>.Ok(result, "Dữ liệu sơ đồ mặt bằng đã được tải thành công."));
    }
}
