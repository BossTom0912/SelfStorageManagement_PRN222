/**
 * notifications.js
 * JavaScript cho mô-đun Thông báo người dùng (Task T14)
 * Chuẩn bị listener invalidation cho T15 realtime SignalR (NotificationsChanged).
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Lắng nghe sự kiện NotificationsChanged từ SignalR qua DOM CustomEvent
    function setupRealtimeListener() {
        const msgArea = document.getElementById('notificationsMessageArea');

        document.addEventListener('app:notifications-changed', function (e) {
            console.log('[Notifications] NotificationsChanged received:', e.detail);
            if (msgArea) {
                showInvalidationNotice(msgArea, 'Bạn có thông báo mới từ hệ thống! Bấm vào đây để cập nhật danh sách.');
            }
        });

        // T15 R2 Fix (F07): Refetch canonical state khi reconnect
        document.addEventListener('app:realtime-reconnected', function () {
            console.log('[Notifications] Phục hồi kết nối realtime. Refetching trạng thái thông báo...');
            fetch(window.location.href, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function () {
                    if (msgArea) {
                        showInvalidationNotice(msgArea, 'Kết nối mạng đã được khôi phục. Bấm vào đây để làm mới danh sách thông báo.');
                    }
                })
                .catch(function () {});
        });
    }

    function showInvalidationNotice(container, message) {
        container.innerHTML = `
            <div class="alert alert-info alert-dismissible fade show shadow-sm d-flex justify-content-between align-items-center" role="alert">
                <div class="d-flex align-items-center">
                    <span class="fs-5 me-2">&#128276;</span>
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
