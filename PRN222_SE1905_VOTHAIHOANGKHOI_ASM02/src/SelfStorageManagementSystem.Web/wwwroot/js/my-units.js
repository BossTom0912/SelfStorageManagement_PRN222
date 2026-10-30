/**
 * my-units.js
 * JavaScript cho mô-đun Ô kho của tôi & Chi tiết bàn giao (Task T14)
 * Chuẩn bị listener invalidation cho T15 realtime SignalR.
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Lắng nghe sự kiện CheckInChanged từ SignalR qua DOM CustomEvent
    function setupRealtimeListener() {
        const msgArea = document.getElementById('myUnitsMessageArea');

        document.addEventListener('app:checkin-changed', function (e) {
            console.log('[MyUnits] CheckInChanged received:', e.detail);
            if (msgArea) {
                showInvalidationNotice(msgArea, 'Thông tin bàn giao ô kho vừa được cập nhật tại quầy tiếp nhận. Bấm vào đây để tải lại trang.');
            }
        });

        // T15 R2 Fix (F07): Refetch canonical state khi reconnect
        document.addEventListener('app:realtime-reconnected', function () {
            console.log('[MyUnits] Phục hồi kết nối realtime. Refetching trạng thái ô kho...');
            fetch(window.location.href, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function () {
                    if (msgArea) {
                        showInvalidationNotice(msgArea, 'Kết nối mạng đã được khôi phục. Bấm vào đây để cập nhật danh sách ô kho mới nhất.');
                    }
                })
                .catch(function () {});
        });
    }

    function showInvalidationNotice(container, message) {
        container.innerHTML = `
            <div class="alert alert-info alert-dismissible fade show shadow-sm d-flex justify-content-between align-items-center" role="alert">
                <div class="d-flex align-items-center">
                    <span class="fs-5 me-2">&#128260;</span>
                    <span>${message}</span>
                </div>
                <div>
                    <button type="button" class="btn btn-sm btn-info fw-semibold me-2" onclick="window.location.reload();">Tải Lại</button>
                    <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Đóng"></button>
                </div>
            </div>`;
    }

    setupRealtimeListener();
});
