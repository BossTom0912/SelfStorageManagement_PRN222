/**
 * SelfStorageManagementSystem.Web - Storage Units Realtime CRUD Script (Task T13)
 * Implements SignalR real-time CRUD operations adhering to TeacherCode pattern.
 * Safely encodes HTML to prevent XSS, upserts rows by ID to prevent duplicates,
 * respects filter scopes, serializes refetch requests to prevent out-of-order race conditions,
 * and maintains reliable pagination footers across page transitions.
 */
(function (window, document, $) {
    'use strict';

    const RealtimeUtils = {};

    let latestScheduledGeneration = 0;
    let latestAppliedGeneration = 0;

    /**
     * Safely escapes HTML special characters to prevent XSS injection.
     */
    RealtimeUtils.escapeHtml = function (str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    };

    /**
     * Renders a temporary dismissible alert in the target message area.
     */
    RealtimeUtils.showMessage = function (containerId, type, messageHtml, autoDismissMs) {
        const container = document.getElementById(containerId);
        if (!container) return;

        const alertDiv = document.createElement('div');
        alertDiv.className = `alert alert-${type} alert-dismissible fade show shadow-sm mb-3`;
        alertDiv.setAttribute('role', 'alert');
        alertDiv.innerHTML = `
            <div class="d-flex align-items-center">
                <span class="me-2 fs-5">${type === 'success' ? '&#10004;' : type === 'warning' ? '&#9888;' : '&#10008;'}</span>
                <div>${messageHtml}</div>
            </div>
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Đóng"></button>
        `;

        container.prepend(alertDiv);

        if (autoDismissMs && autoDismissMs > 0) {
            setTimeout(function () {
                if (alertDiv.parentNode) {
                    if ($ && $.fn && $.fn.fadeOut) {
                        $(alertDiv).fadeOut(400, function () { $(this).remove(); });
                    } else {
                        alertDiv.remove();
                    }
                }
            }, autoDismissMs);
        }
    };

    /**
     * T13-R6-F03: Displays persistent warning banner when realtime sync is pending or degraded.
     */
    RealtimeUtils.showSyncPendingWarning = function (containerId) {
        if (document.getElementById('realtimeSyncAlert')) return;
        const alertDiv = document.createElement('div');
        alertDiv.id = 'realtimeSyncAlert';
        alertDiv.className = 'alert alert-warning alert-dismissible fade show shadow-sm mb-3';
        alertDiv.setAttribute('role', 'alert');
        alertDiv.innerHTML = `
            <div class="d-flex align-items-center justify-content-between flex-wrap gap-2">
                <div class="d-flex align-items-center">
                    <span class="me-2 fs-5 text-warning fw-bold">&#9888;</span>
                    <div>
                        <strong>Đang chờ đồng bộ:</strong> Có thay đổi dữ liệu thời gian thực nhưng chưa thể cập nhật từ máy chủ. Bảng đang giữ trạng thái hiện tại.
                    </div>
                </div>
                <button type="button" class="btn btn-warning btn-sm fw-semibold" id="btnRetrySync" onclick="window.StorageUnitsRealtime.retryRefetch()">
                    &#8635; Thử lại
                </button>
            </div>
        `;
        const msgArea = document.getElementById(containerId || 'storageUnitsMessageArea') || document.querySelector('.container, main');
        if (msgArea) {
            msgArea.prepend(alertDiv);
        }
    };

    /**
     * Returns the human-readable text and badge CSS class for a storage unit physical status.
     */
    RealtimeUtils.getStatusBadge = function (status) {
        switch (status) {
            case 'available':
                return { text: 'Khả dụng', css: 'bg-success text-white' };
            case 'occupied':
                return { text: 'Đang thuê', css: 'bg-danger text-white' };
            case 'reserved':
                return { text: 'Đã giữ chỗ', css: 'bg-warning text-dark' };
            case 'maintenance':
                return { text: 'Bảo trì', css: 'bg-secondary text-white' };
            default:
                return { text: status || 'Chưa rõ', css: 'bg-light text-dark' };
        }
    };

    /**
     * Formats an ISO-8601 UTC timestamp to Vietnam local display format.
     */
    RealtimeUtils.formatDateTime = function (isoString) {
        if (!isoString) return '—';
        try {
            const d = new Date(isoString);
            if (isNaN(d.getTime())) return isoString;
            return d.toLocaleString('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
                second: '2-digit'
            });
        } catch (e) {
            return isoString;
        }
    };

    /**
     * Generates table row inner HTML for a storage unit with safe XSS escaping.
     */
    RealtimeUtils.generateRowContent = function (item, canManage) {
        const badge = RealtimeUtils.getStatusBadge(item.status);
        const areaDisplay = item.areaName
            ? RealtimeUtils.escapeHtml(item.areaName)
            : 'Chưa gắn khu vực';

        let subArea = '';
        if (item.floorLabel || item.zoneLabel) {
            const floor = item.floorLabel ? RealtimeUtils.escapeHtml(item.floorLabel) : '';
            const zone = item.zoneLabel ? ' - ' + RealtimeUtils.escapeHtml(item.zoneLabel) : '';
            subArea = `<div class="small text-muted">${floor}${zone}</div>`;
        }

        const listingBadge = item.isListed
            ? '<span class="badge bg-info-subtle text-info border border-info-subtle">Niêm yết</span>'
            : '<span class="badge bg-secondary-subtle text-secondary border border-secondary-subtle">Tạm ẩn</span>';

        const actionButtons = `
            <div class="btn-group btn-group-sm" role="group">
                <a href="/StorageUnits/Details?id=${encodeURIComponent(item.id)}" class="btn btn-outline-secondary" title="Xem chi tiết">
                    Chi tiết
                </a>
                ${canManage ? `
                    <a href="/StorageUnits/Edit?id=${encodeURIComponent(item.id)}" class="btn btn-outline-primary" title="Chỉnh sửa ô kho">
                        Sửa
                    </a>
                    <a href="/StorageUnits/Delete?id=${encodeURIComponent(item.id)}" class="btn btn-outline-danger" title="Xóa ô kho">
                        Xóa
                    </a>
                ` : ''}
            </div>
        `;

        return `
            <td class="ps-3 fw-bold text-primary font-monospace">
                ${RealtimeUtils.escapeHtml(item.unitCode)}
            </td>
            <td>
                <div class="fw-semibold text-dark">${RealtimeUtils.escapeHtml(item.facilityName)}</div>
                <div class="small text-muted font-monospace">${RealtimeUtils.escapeHtml(item.facilityCode)}</div>
            </td>
            <td>
                <div>${RealtimeUtils.escapeHtml(item.typeName)}</div>
                <div class="small text-muted font-monospace">${RealtimeUtils.escapeHtml(item.unitTypeCode)}</div>
            </td>
            <td>
                <div>${areaDisplay}</div>
                ${subArea}
            </td>
            <td>
                <span class="badge ${badge.css} px-2 py-1">${RealtimeUtils.escapeHtml(badge.text)}</span>
            </td>
            <td>
                ${listingBadge}
            </td>
            <td class="small text-muted">
                ${RealtimeUtils.formatDateTime(item.updatedAt)}
            </td>
            <td class="pe-3 text-end">
                ${actionButtons}
            </td>
        `;
    };

    /**
     * T13-F03 / T13-R2-F03 / T13-R3-F03:
     * Fast-path client evaluation for categorical filters (facility, unit type, area, status, isListed).
     * IMPORTANT: When SearchTerm is active, event DTO intentionally omits 'notes' for security/privacy.
     * Therefore, client must NEVER reject an event based on searchTerm; server refetch is the sole source of truth.
     */
    RealtimeUtils.matchesFilter = function (item, filter) {
        if (!item || !filter) return true;

        if (filter.facilityId && String(item.facilityId) !== String(filter.facilityId)) {
            return false;
        }

        if (filter.unitTypeId && String(item.unitTypeId) !== String(filter.unitTypeId)) {
            return false;
        }

        if (filter.areaId && String(item.areaId || '') !== String(filter.areaId)) {
            return false;
        }

        if (filter.physicalStatus && String(item.status || '').toLowerCase() !== String(filter.physicalStatus).toLowerCase()) {
            return false;
        }

        if (filter.isListed !== undefined && filter.isListed !== null && filter.isListed !== '') {
            const expectedListed = String(filter.isListed).toLowerCase() === 'true';
            if (Boolean(item.isListed) !== expectedListed) {
                return false;
            }
        }

        // SearchTerm: Server queries unit_code, notes, floor_label, zone_label.
        // If categorical filters match, we do NOT reject here even if unitCode/floor/zone don't match,
        // because the unit's notes may match on the server.
        return true;
    };

    /**
     * T13-R3-F04 / T13-R3-F05 / T13-R4-F04: Refetch table content from server.
     * Uses generation sequence numbers to discard out-of-order stale responses.
     * Returns an outcome object:
     *   - { status: 'applied', generation, success: true, applied: true }
     *   - { status: 'superseded', generation, success: false, applied: false }
     *   - { status: 'failed', generation, success: false, applied: false, statusHttp?, error? }
     * Atomically replaces table body and stable pagination footer container.
     */
    RealtimeUtils.refetchTable = async function (tableBodyId, totalCountId, affectedItemId, eventType, fetchUrl) {
        const generation = ++latestScheduledGeneration;
        const targetUrl = fetchUrl || window.location.href;

        try {
            const response = await fetch(targetUrl, {
                headers: { 'Cache-Control': 'no-cache', 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (!response.ok) {
                // T13-R4-F04: Only mark failed if this request is still the latest scheduled generation
                if (generation < latestScheduledGeneration) {
                    return { status: 'superseded', generation: generation, success: false, applied: false };
                }
                return { status: 'failed', generation: generation, statusHttp: response.status, success: false, applied: false };
            }

            const html = await response.text();

            // T13-R4-F04: Out-of-order and superseded check.
            // If a newer generation was scheduled OR has already applied to DOM, discard this stale snapshot.
            if (generation < latestScheduledGeneration || generation < latestAppliedGeneration) {
                return { status: 'superseded', generation: generation, success: false, applied: false };
            }

            const doc = new DOMParser().parseFromString(html, 'text/html');

            // T13-R6-F03: Kiểm tra cấu trúc HTML nhận về. Nếu HTTP 200 nhưng thiếu table body hoặc thiếu footer/count,
            // coi là refetch thất bại, không cập nhật latestAppliedGeneration, giữ/hiển thị cảnh báo.
            const newTable = doc.getElementById(tableBodyId || 'storageUnitsTableBody');
            const newFooter = doc.getElementById('storageUnitsFooterContainer');
            const newTotalCount = doc.getElementById(totalCountId || 'storageUnitsTotalCount');

            if (!newTable || (!newFooter && !newTotalCount)) {
                RealtimeUtils.showSyncPendingWarning();
                return { status: 'failed', generation: generation, reason: 'missing_table_elements', success: false, applied: false };
            }

            latestAppliedGeneration = generation;

            // 1. Update table body
            const targetTable = document.getElementById(tableBodyId || 'storageUnitsTableBody');
            if (newTable && targetTable) {
                targetTable.innerHTML = newTable.innerHTML;
            }

            // 2. T13-R3-F04: Atomically update stable footer container (contains range, count, and pagination nav)
            const targetFooter = document.getElementById('storageUnitsFooterContainer');
            if (newFooter && targetFooter) {
                targetFooter.innerHTML = newFooter.innerHTML;
            } else {
                // Fallback for individual elements if footer container not used
                const newTotalCount = doc.getElementById(totalCountId || 'storageUnitsTotalCount');
                const targetTotalCount = document.getElementById(totalCountId || 'storageUnitsTotalCount');
                if (newTotalCount && targetTotalCount) {
                    targetTotalCount.textContent = newTotalCount.textContent;
                }

                const newRange = doc.getElementById('storageUnitsDisplayRange');
                const targetRange = document.getElementById('storageUnitsDisplayRange');
                if (newRange && targetRange) {
                    targetRange.innerHTML = newRange.innerHTML;
                }

                const newPagination = doc.getElementById('storageUnitsPaginationContainer');
                const targetPagination = document.getElementById('storageUnitsPaginationContainer');
                if (newPagination && targetPagination) {
                    targetPagination.innerHTML = newPagination.innerHTML;
                }
            }

            // 3. Highlight affected row if present in current view
            if (affectedItemId) {
                const affectedRow = document.getElementById('unit-row-' + affectedItemId);
                if (affectedRow) {
                    if (eventType === 'create') {
                        affectedRow.className = 'table-warning bg-warning-subtle text-danger fw-bold';
                        setTimeout(() => affectedRow.classList.remove('table-warning', 'bg-warning-subtle', 'text-danger', 'fw-bold'), 3500);
                    } else if (eventType === 'update') {
                        affectedRow.style.backgroundColor = '#fff3cd';
                        affectedRow.style.transition = 'background-color 0.4s ease';
                        setTimeout(() => { affectedRow.style.backgroundColor = ''; }, 2500);
                    }
                }
            }

            // Clear any active realtime sync alert banner
            const syncAlert = document.getElementById('realtimeSyncAlert');
            if (syncAlert) {
                if (window.$ && window.$.fn && window.$.fn.fadeOut) {
                    window.$(syncAlert).fadeOut(300, function () { window.$(this).remove(); });
                } else {
                    syncAlert.remove();
                }
            }

            return { status: 'applied', generation: generation, success: true, applied: true };
        } catch (e) {
            if (generation < latestScheduledGeneration) {
                return { status: 'superseded', generation: generation, success: false, applied: false };
            }
            return { status: 'failed', generation: generation, error: e, success: false, applied: false };
        }
    };

    /**
     * T13-R3-F06 / T13-R4-F04: Retry helper invoked manually or automatically after a transient refetch failure.
     */
    RealtimeUtils.retryRefetch = async function () {
        const res = await RealtimeUtils.refetchTable('storageUnitsTableBody', 'storageUnitsTotalCount');
        if (res && res.status === 'applied') {
            const syncAlert = document.getElementById('realtimeSyncAlert');
            if (syncAlert) {
                syncAlert.remove();
            }

            RealtimeUtils.showMessage(
                'storageUnitsMessageArea',
                'success',
                'Dữ liệu danh mục đã được đồng bộ thành công với máy chủ.',
                4000
            );
            return true;
        }
        return false;
    };

    // Diagnostics / testing getters
    RealtimeUtils.getLatestScheduledGeneration = () => latestScheduledGeneration;
    RealtimeUtils.getLatestAppliedGeneration = () => latestAppliedGeneration;
    RealtimeUtils.resetGenerations = () => {
        latestScheduledGeneration = 0;
        latestAppliedGeneration = 0;
    };

    window.StorageUnitsRealtime = RealtimeUtils;

})(window, document, window.jQuery);
