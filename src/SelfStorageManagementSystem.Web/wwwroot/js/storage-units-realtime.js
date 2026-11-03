/**
 * SelfStorageManagementSystem.Web - Storage Units Realtime CRUD Script (Task T13)
 * Implements SignalR real-time CRUD operations adhering to TeacherCode pattern.
 * Safely encodes HTML to prevent XSS, upserts rows by ID to prevent duplicates,
 * respects filter scopes, serializes snapshot requests via SignalR Hub to prevent race conditions,
 * eliminates full-page HTML fetches, and maintains reliable pagination footers across page transitions.
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
     * Reads current filter context from DOM dataset safely.
     */
    RealtimeUtils.getCurrentFilter = function () {
        const configEl = document.getElementById('storageUnitsPageConfig');
        return {
            facilityId: configEl ? (configEl.dataset.facilityId || '') : '',
            unitTypeId: configEl ? (configEl.dataset.unitTypeId || '') : '',
            areaId: configEl ? (configEl.dataset.areaId || '') : '',
            physicalStatus: configEl ? (configEl.dataset.physicalStatus || '') : '',
            isListed: configEl ? (configEl.dataset.isListed || '') : '',
            searchTerm: configEl ? (configEl.dataset.searchTerm || '') : '',
            sortBy: configEl ? (configEl.dataset.sortBy || 'unit_code') : 'unit_code',
            sortDescending: configEl ? configEl.dataset.sortDescending === 'true' : false,
            pageNumber: configEl ? parseInt(configEl.dataset.pageNumber || '1', 10) : 1,
            pageSize: configEl ? parseInt(configEl.dataset.pageSize || '10', 10) : 10,
            totalCount: configEl ? parseInt(configEl.dataset.totalCount || '0', 10) : 0,
            canManage: configEl ? configEl.dataset.canManage === 'true' : false
        };
    };

    /**
     * Updates storageUnitsPageConfig dataset attributes with fresh snapshot data.
     */
    RealtimeUtils.updateConfigDataset = function (snapshot) {
        const configEl = document.getElementById('storageUnitsPageConfig');
        if (!configEl || !snapshot) return;

        configEl.dataset.pageNumber = String(snapshot.pageNumber);
        configEl.dataset.totalCount = String(snapshot.totalCount);
        if (snapshot.pageSize) {
            configEl.dataset.pageSize = String(snapshot.pageSize);
        }
    };

    /**
     * Generates table row inner HTML for a storage unit with safe XSS escaping and string ID safety.
     */
    RealtimeUtils.generateRowContent = function (item, canManage) {
        const rawStatus = item.physicalStatus || item.status;
        const badge = RealtimeUtils.getStatusBadge(rawStatus);
        const areaDisplay = item.areaName
            ? RealtimeUtils.escapeHtml(item.areaName)
            : 'Chưa gắn khu vực';

        let subArea = '';
        if (item.floorLabel || item.zoneLabel) {
            const floor = item.floorLabel ? RealtimeUtils.escapeHtml(item.floorLabel) : '';
            const zone = item.zoneLabel ? ' - ' + RealtimeUtils.escapeHtml(item.zoneLabel) : '';
            subArea = `<div class="small text-muted">${floor}${zone}</div>`;
        }

        const isListedBool = typeof item.isListed === 'boolean' ? item.isListed : String(item.isListed).toLowerCase() === 'true';
        const listingBadge = isListedBool
            ? '<span class="badge bg-info-subtle text-info border border-info-subtle">Niêm yết</span>'
            : '<span class="badge bg-secondary-subtle text-secondary border border-secondary-subtle">Tạm ẩn</span>';

        // Stringify ID to prevent 64-bit integer precision truncation in JavaScript
        const stringId = String(item.id || item.idNumeric || '');

        const actionButtons = `
            <div class="btn-group btn-group-sm" role="group">
                <a href="/StorageUnits/Details?id=${encodeURIComponent(stringId)}" class="btn btn-outline-secondary" title="Xem chi tiết">
                    Chi tiết
                </a>
                ${canManage ? `
                    <a href="/StorageUnits/Edit?id=${encodeURIComponent(stringId)}" class="btn btn-outline-primary" title="Chỉnh sửa ô kho">
                        Sửa
                    </a>
                    <a href="/StorageUnits/Delete?id=${encodeURIComponent(stringId)}" class="btn btn-outline-danger" title="Xóa ô kho">
                        Xóa
                    </a>
                ` : ''}
            </div>
        `;

        const typeNameDisplay = item.unitTypeName || item.typeName || '';
        const timeDisplay = item.updatedAtFormatted || RealtimeUtils.formatDateTime(item.updatedAt);

        return `
            <td class="ps-3 fw-bold text-primary font-monospace">
                ${RealtimeUtils.escapeHtml(item.unitCode)}
            </td>
            <td>
                <div class="fw-semibold text-dark">${RealtimeUtils.escapeHtml(item.facilityName)}</div>
                <div class="small text-muted font-monospace">${RealtimeUtils.escapeHtml(item.facilityCode)}</div>
            </td>
            <td>
                <div>${RealtimeUtils.escapeHtml(typeNameDisplay)}</div>
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
                ${RealtimeUtils.escapeHtml(timeDisplay)}
            </td>
            <td class="pe-3 text-end">
                ${actionButtons}
            </td>
        `;
    };

    /**
     * Generates valid pagination URL preserving active filter parameters.
     */
    RealtimeUtils.generatePaginationUrl = function (targetPage, filter) {
        const params = new URLSearchParams();
        params.set('pageNumber', targetPage);
        params.set('pageSize', filter.pageSize || 10);
        if (filter.facilityId) params.set('facilityId', filter.facilityId);
        if (filter.unitTypeId) params.set('unitTypeId', filter.unitTypeId);
        if (filter.areaId) params.set('areaId', filter.areaId);
        if (filter.physicalStatus) params.set('physicalStatus', filter.physicalStatus);
        if (filter.isListed !== undefined && filter.isListed !== null && filter.isListed !== '') {
            params.set('isListed', String(filter.isListed).toLowerCase());
        }
        if (filter.searchTerm) params.set('searchTerm', filter.searchTerm);
        if (filter.sortBy) params.set('sortBy', filter.sortBy);
        if (filter.sortDescending) params.set('sortDescending', 'true');
        return '/StorageUnits?' + params.toString();
    };

    /**
     * Generates inner HTML for storageUnitsFooterContainer from snapshot data.
     */
    RealtimeUtils.generateFooterContent = function (snapshot, filter) {
        if (!snapshot) return '';

        const totalCount = snapshot.totalCount || 0;
        const pageNumber = snapshot.pageNumber || 1;
        const pageSize = snapshot.pageSize || 10;
        const totalPages = snapshot.totalPages || 0;
        const hasPrev = !!snapshot.hasPreviousPage;
        const hasNext = !!snapshot.hasNextPage;

        if (totalPages > 1) {
            const from = (pageNumber - 1) * pageSize + 1;
            const to = Math.min(pageNumber * pageSize, totalCount);

            let pageItemsHtml = '';
            for (let p = 1; p <= totalPages; p++) {
                const isActive = p === pageNumber ? 'active' : '';
                const url = RealtimeUtils.generatePaginationUrl(p, filter);
                pageItemsHtml += `
                    <li class="page-item ${isActive}">
                        <a class="page-link" href="${url}">${p}</a>
                    </li>
                `;
            }

            const prevUrl = hasPrev ? RealtimeUtils.generatePaginationUrl(pageNumber - 1, filter) : '#';
            const nextUrl = hasNext ? RealtimeUtils.generatePaginationUrl(pageNumber + 1, filter) : '#';

            return `
                <div class="card-footer bg-white d-flex flex-wrap justify-content-between align-items-center py-3 border-top">
                    <div class="small text-muted mb-2 mb-md-0" id="storageUnitsDisplayRange">
                        Hiển thị <strong>${from}</strong>
                        đến <strong>${to}</strong>
                        trong tổng số <strong id="storageUnitsTotalCount">${totalCount}</strong> ô kho
                    </div>
                    <nav aria-label="Phân trang ô kho" id="storageUnitsPaginationContainer">
                        <ul class="pagination pagination-sm mb-0">
                            <li class="page-item ${hasPrev ? '' : 'disabled'}">
                                <a class="page-link" href="${prevUrl}" aria-label="Trang trước">&laquo;</a>
                            </li>
                            ${pageItemsHtml}
                            <li class="page-item ${hasNext ? '' : 'disabled'}">
                                <a class="page-link" href="${nextUrl}" aria-label="Trang sau">&raquo;</a>
                            </li>
                        </ul>
                    </nav>
                </div>
            `;
        } else if (totalCount > 0) {
            return `
                <div class="card-footer bg-white d-flex flex-wrap justify-content-between align-items-center py-3 border-top">
                    <div class="small text-muted mb-2 mb-md-0" id="storageUnitsDisplayRange">
                        Hiển thị <strong>1</strong> đến <strong>${totalCount}</strong>
                        trong tổng số <strong id="storageUnitsTotalCount">${totalCount}</strong> ô kho
                    </div>
                    <nav aria-label="Phân trang ô kho" id="storageUnitsPaginationContainer"></nav>
                </div>
            `;
        } else {
            return `
                <div class="card-footer bg-white d-flex flex-wrap justify-content-between align-items-center py-3 border-top">
                    <div class="small text-muted mb-2 mb-md-0" id="storageUnitsDisplayRange">
                        Hiển thị <strong>0</strong> trong tổng số <strong id="storageUnitsTotalCount">0</strong> ô kho
                    </div>
                    <nav aria-label="Phân trang ô kho" id="storageUnitsPaginationContainer"></nav>
                </div>
            `;
        }
    };

    /**
     * Fast-path client evaluation for categorical filters.
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

        const itemStatus = item.physicalStatus || item.status || '';
        if (filter.physicalStatus && itemStatus.toLowerCase() !== String(filter.physicalStatus).toLowerCase()) {
            return false;
        }

        if (filter.isListed !== undefined && filter.isListed !== null && filter.isListed !== '') {
            const expectedListed = String(filter.isListed).toLowerCase() === 'true';
            if (Boolean(item.isListed) !== expectedListed) {
                return false;
            }
        }

        return true;
    };

    /**
     * Synchronizes table content via read-only SignalR snapshot method (HubGet_StorageUnitsTableSnapshot).
     * Eliminates full-page HTML HTTP fetches, serializes requests with generation sequence numbers,
     * securely escapes user input, and updates table body and pagination atomically.
     */
    RealtimeUtils.syncTableViaHub = async function (tableBodyId, totalCountId, affectedItemId, eventType, filterOverride, hubConnection) {
        const generation = ++latestScheduledGeneration;
        const connection = hubConnection || window.connection;

        // If SignalR is not connected, keep existing DOM and show persistent retry warning
        if (!connection || connection.state !== 'Connected') {
            if (generation < latestScheduledGeneration) {
                return { status: 'superseded', generation: generation, success: false, applied: false };
            }
            RealtimeUtils.showSyncPendingWarning();
            return { status: 'failed', generation: generation, reason: 'connection_not_ready', success: false, applied: false };
        }

        const filter = filterOverride || RealtimeUtils.getCurrentFilter();

        const query = {
            facilityId: filter.facilityId || '',
            unitTypeId: filter.unitTypeId || '',
            areaId: filter.areaId || '',
            physicalStatus: filter.physicalStatus || '',
            isListed: (filter.isListed !== undefined && filter.isListed !== null && filter.isListed !== '') ? String(filter.isListed) : '',
            searchTerm: filter.searchTerm || '',
            sortBy: filter.sortBy || 'unit_code',
            sortDescending: !!filter.sortDescending,
            pageNumber: filter.pageNumber || 1,
            pageSize: filter.pageSize || 10,
            clientGeneration: generation
        };

        try {
            const snapshot = await connection.invoke('HubGet_StorageUnitsTableSnapshot', query);

            if (!snapshot || !Array.isArray(snapshot.items) ||
                !Number.isSafeInteger(snapshot.totalCount) || snapshot.totalCount < 0 ||
                !Number.isSafeInteger(snapshot.pageNumber) || snapshot.pageNumber < 1 ||
                !Number.isSafeInteger(snapshot.pageSize) || snapshot.pageSize < 1 ||
                snapshot.clientGeneration !== generation) {
                if (generation < latestScheduledGeneration) {
                    return { status: 'superseded', generation: generation, success: false, applied: false };
                }
                RealtimeUtils.showSyncPendingWarning();
                return { status: 'failed', generation: generation, reason: 'invalid_snapshot', success: false, applied: false };
            }

            // Generation sequence check: Discard out-of-order stale responses
            if (snapshot.clientGeneration < latestScheduledGeneration || snapshot.clientGeneration < latestAppliedGeneration) {
                return { status: 'superseded', generation: snapshot.clientGeneration, success: false, applied: false };
            }

            latestAppliedGeneration = snapshot.clientGeneration;

            // Update client filter state and DOM config
            filter.pageNumber = snapshot.pageNumber;
            filter.totalCount = snapshot.totalCount;
            RealtimeUtils.updateConfigDataset(snapshot);

            // 1. Render table body
            const tbody = document.getElementById(tableBodyId || 'storageUnitsTableBody');
            if (tbody) {
                if (!snapshot.items || snapshot.items.length === 0) {
                    tbody.innerHTML = `
                        <tr>
                            <td colspan="8" class="text-center py-5 text-muted">
                                <div class="mb-2 fs-3">&#128230;</div>
                                <div class="fw-semibold">Không tìm thấy ô kho nào phù hợp với điều kiện tìm kiếm.</div>
                                <div class="small">Vui lòng thử thay đổi bộ lọc hoặc thêm ô kho mới.</div>
                            </td>
                        </tr>
                    `;
                } else {
                    let rowsHtml = '';
                    for (const item of snapshot.items) {
                        const stringId = String(item.id || item.idNumeric || '');
                        const rowContent = RealtimeUtils.generateRowContent(item, snapshot.canManage);
                        rowsHtml += `<tr id="unit-row-${RealtimeUtils.escapeHtml(stringId)}" data-unit-id="${RealtimeUtils.escapeHtml(stringId)}">${rowContent}</tr>`;
                    }
                    tbody.innerHTML = rowsHtml;
                }
            }

            // 2. Render footer container
            const footerContainer = document.getElementById('storageUnitsFooterContainer');
            if (footerContainer) {
                footerContainer.innerHTML = RealtimeUtils.generateFooterContent(snapshot, filter);
            } else {
                const totalCountEl = document.getElementById(totalCountId || 'storageUnitsTotalCount');
                if (totalCountEl) {
                    totalCountEl.textContent = String(snapshot.totalCount);
                }
            }

            // 3. Highlight affected row if present
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

            // Clear active sync alert banner
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
            RealtimeUtils.showSyncPendingWarning();
            return { status: 'failed', generation: generation, error: e, success: false, applied: false };
        }
    };

    /**
     * Alias for backwards compatibility with tests and callers.
     * Delegates directly to syncTableViaHub without any full HTML fetch.
     */
    RealtimeUtils.refetchTable = async function (tableBodyId, totalCountId, affectedItemId, eventType) {
        return await RealtimeUtils.syncTableViaHub(tableBodyId, totalCountId, affectedItemId, eventType);
    };

    /**
     * Retry helper invoked manually or after connection restore.
     */
    RealtimeUtils.retryRefetch = async function () {
        const res = await RealtimeUtils.syncTableViaHub('storageUnitsTableBody', 'storageUnitsTotalCount');
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
