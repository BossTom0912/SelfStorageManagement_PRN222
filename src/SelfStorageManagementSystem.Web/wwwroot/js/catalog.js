/**
 * SelfStorageManagementSystem.Web - Catalog & Floor Map Interactive Script
 * Quản lý tương tác sơ đồ mặt bằng SVG, chọn ô ứng viên, và chuyển hướng đặt chỗ (T07)
 */

(function (window, document, $) {
    'use strict';

    const Catalog = window.Catalog || {};

    /**
     * Khởi tạo tương tác sơ đồ mặt bằng SVG
     */
    Catalog.initFloorMap = function () {
        const $mapSvg = $('#facilityFloorMapSvg');
        if (!$mapSvg.length) return;

        // Xử lý sự kiện click trên từng ô kho SVG
        $mapSvg.on('click', '.unit-rect', function () {
            const $unit = $(this);
            Catalog.selectUnit($unit);
        });

        // Xử lý nút "Xem trên sơ đồ" từ danh sách bảng ô ứng viên
        $(document).on('click', '.btn-locate-on-map', function (e) {
            e.preventDefault();
            const unitCode = $(this).data('unit-code');
            const unitId = $(this).data('unit-id');

            // Chuyển sang tab Sơ đồ nếu đang ở tab khác
            const mapTabTrigger = document.querySelector('#tab-floor-map-nav');
            if (mapTabTrigger && window.bootstrap && window.bootstrap.Tab) {
                const tab = bootstrap.Tab.getOrCreateInstance(mapTabTrigger);
                tab.show();
            }

            // Đợi chuyển tab hoàn tất rồi highlight ô
            setTimeout(function () {
                Catalog.locateAndHighlightUnit(unitId, unitCode);
            }, 150);
        });

        // Xử lý thay đổi khu vực / tầng trên dropdown sơ đồ
        $('#floorMapAreaSelector').on('change', function () {
            const areaId = $(this).val();
            const url = new URL(window.location.href);
            if (areaId) {
                url.searchParams.set('selectedAreaId', areaId);
            } else {
                url.searchParams.delete('selectedAreaId');
            }
            url.searchParams.set('activeTab', 'floor-map');
            window.location.href = url.toString();
        });
    };

    /**
     * Chọn và hiển thị chi tiết ô kho trên sơ đồ
     * @param {jQuery} $unitRect Element <rect> của ô kho
     */
    Catalog.selectUnit = function ($unitRect) {
        // Gỡ bỏ trạng thái chọn cũ
        $('.unit-rect').removeClass('selected highlight-flash');
        $unitRect.addClass('selected');

        // Đọc dữ liệu từ data-* attributes
        const unitId = $unitRect.data('unit-id');
        const unitCode = $unitRect.data('unit-code');
        const unitTypeId = $unitRect.data('unit-type-id');
        const unitTypeName = $unitRect.data('unit-type-name');
        const areaName = $unitRect.data('area-name') || 'Chung';
        const areaM2 = $unitRect.data('area-m2') ? Number($unitRect.data('area-m2')).toFixed(1) + ' m²' : '—';
        const volumeM3 = $unitRect.data('volume-m3') ? Number($unitRect.data('volume-m3')).toFixed(1) + ' m³' : '—';
        const monthlyRate = $unitRect.data('monthly-rate') || 'Chưa có giá';
        const status = $unitRect.data('status') || 'available';
        const statusText = $unitRect.data('status-text') || status;
        const canProceed = $unitRect.data('can-proceed') === true || $unitRect.data('can-proceed') === 'true';

        // Cập nhật thông tin vào Panel chi tiết ô
        $('#selectedUnitCode').text(unitCode);
        $('#selectedUnitTypeName').text(unitTypeName);
        $('#selectedUnitAreaName').text(areaName);
        $('#selectedUnitDimensions').text(areaM2 + ' (' + volumeM3 + ')');
        $('#selectedUnitMonthlyRate').text(monthlyRate);

        const $statusBadge = $('#selectedUnitStatusBadge');
        $statusBadge.text(statusText);
        $statusBadge.removeClass('bg-success bg-secondary bg-warning bg-danger bg-info');

        switch (status) {
            case 'available':
                $statusBadge.addClass('bg-success');
                break;
            case 'occupied':
                $statusBadge.addClass('bg-secondary');
                break;
            case 'reserved':
                $statusBadge.addClass('bg-warning text-dark');
                break;
            case 'maintenance':
                $statusBadge.addClass('bg-warning text-dark');
                break;
            default:
                $statusBadge.addClass('bg-secondary');
                break;
        }

        // Cập nhật nút hành động đặt chỗ theo quy định BR-RSV-03
        const $actionBtnContainer = $('#selectedUnitActionContainer');
        $actionBtnContainer.empty();

        const reserveUrlTemplate = $('#mapConfigData').data('reserve-url-template') || '';
        const loginUrlTemplate = $('#mapConfigData').data('login-url-template') || '';
        const isAuthenticated = $('#mapConfigData').data('is-authenticated') === true || $('#mapConfigData').data('is-authenticated') === 'true';

        const numUnitTypeId = parseInt(unitTypeId, 10);
        if (canProceed && !isNaN(numUnitTypeId) && numUnitTypeId > 0) {
            let targetUrl = reserveUrlTemplate.replace('__TYPE_ID__', encodeURIComponent(numUnitTypeId.toString()));
            if (!isAuthenticated && loginUrlTemplate) {
                targetUrl = loginUrlTemplate.replace('__RETURN_URL__', encodeURIComponent(targetUrl));
            }

            const $link = $('<a></a>')
                .addClass('btn btn-primary btn-sm w-100 fw-semibold')
                .attr('href', targetUrl);

            const $checkSpan = $('<span></span>')
                .addClass('me-1')
                .text('\u2713');

            $link.append($checkSpan);
            $link.append(document.createTextNode(' Đặt chỗ loại kho "' + (unitTypeName || '') + '" \u2192'));

            const $helpText = $('<div></div>')
                .addClass('form-text small mt-1 text-muted')
                .text('Theo quy tắc BR-RSV-03: Đơn đặt chỗ giữ theo Loại kho; ô vật lý sẽ được nhân viên bàn giao tại quầy tiếp nhận.');

            $actionBtnContainer.append($link).append($helpText);
        } else {
            let note = 'Ô kho này hiện không khả dụng để đặt chỗ.';
            if (status === 'occupied') {
                note = 'Ô này hiện đang được khách hàng khác thuê hoạt động.';
            } else if (status === 'maintenance') {
                note = 'Ô này đang trong kế hoạch kiểm tra kỹ thuật / bảo trì.';
            } else if (status === 'available' && !canProceed) {
                note = 'Loại kho này đã hết sức chứa đặt chỗ khả dụng trong thời gian tra cứu.';
            }

            const $btn = $('<button></button>')
                .addClass('btn btn-secondary btn-sm w-100')
                .attr('disabled', 'disabled')
                .text('Không thể đặt chỗ');

            const $helpText = $('<div></div>')
                .addClass('form-text small mt-1 text-danger')
                .text(note);

            $actionBtnContainer.append($btn).append($helpText);
        }

        // Hiển thị khung chi tiết
        $('#unitDetailPanel').removeClass('d-none');
    };

    /**
     * Định vị và highlight ô trên sơ đồ từ danh sách ứng viên
     * @param {number|string} unitId ID ô kho
     * @param {string} unitCode Mã ô kho
     */
    Catalog.locateAndHighlightUnit = function (unitId, unitCode) {
        let $rect = $('.unit-rect[data-unit-id="' + unitId + '"]');
        if (!$rect.length && unitCode) {
            $rect = $('.unit-rect[data-unit-code="' + unitCode + '"]');
        }

        if ($rect.length) {
            Catalog.selectUnit($rect);
            $rect.addClass('highlight-flash');

            // Cuộn sơ đồ để căn giữa ô được chọn
            const container = document.getElementById('floorMapContainer');
            if (container) {
                const rectBox = $rect[0].getBoundingClientRect();
                const containerBox = container.getBoundingClientRect();
                container.scrollLeft += (rectBox.left - containerBox.left) - (containerBox.width / 2) + (rectBox.width / 2);
                container.scrollTop += (rectBox.top - containerBox.top) - (containerBox.height / 2) + (rectBox.height / 2);
            }
        } else {
            if (window.SelfStorage && window.SelfStorage.showNotification) {
                window.SelfStorage.showNotification('Ô kho này chưa được gán tọa độ trên sơ đồ khu vực hiện tại.', 'info');
            } else {
                alert('Ô kho này chưa được gán tọa độ trên sơ đồ khu vực hiện tại.');
            }
        }
    };

    /**
     * Giữ tab active khi chuyển trang hoặc đổi filter
     */
    Catalog.initTabSync = function () {
        $('a[data-bs-toggle="tab"]').on('shown.bs.tab', function (e) {
            const target = $(e.target).attr('href');
            if (target) {
                const tabKey = target.replace('#tab-', '');
                const url = new URL(window.location.href);
                url.searchParams.set('activeTab', tabKey);
                window.history.replaceState(null, '', url.toString());

                // Cập nhật các hidden input trong form lọc
                $('input[name="ActiveTab"]').val(tabKey);
            }
        });
    };

    // Khởi tạo khi DOM sẵn sàng
    $(document).ready(function () {
        Catalog.initFloorMap();
        Catalog.initTabSync();
    });

    window.Catalog = Catalog;

})(window, document, jQuery);
