/**
 * app-realtime.js
 * Quản lý kết nối SignalR toàn hệ thống (Task T15)
 * Tích hợp 5 luồng hợp đồng (Contract Streams):
 *  1. CatalogInvalidated
 *  2. ReservationChanged
 *  3. PaymentChanged
 *  4. CheckInChanged
 *  5. NotificationsChanged
 * Và luồng bổ sung:
 *  6. AccountChanged
 * 
 * Nguyên tắc kỹ thuật:
 * - Chỉ kết nối khi người dùng đã xác thực (data-is-authenticated="true"), từ chối kết nối ẩn danh để tránh lỗi 401.
 * - Đăng ký tất cả listeners TRƯỚC KHI gọi connection.start().
 * - Chính sách tự động kết nối lại hữu hạn: [0, 2000, 5000, 10000].
 * - Khi phục hồi kết nối (onreconnected), phát sự kiện 'app:realtime-reconnected' và làm mới trạng thái chuẩn từ server.
 * - Không truyền hoặc lưu trữ bất kỳ mã PIN hay thông tin tài chính nhạy cảm nào.
 */
(function (window, document) {
    'use strict';

    const isAuthenticated = document.body && document.body.getAttribute('data-is-authenticated') === 'true';

    // 1. Kiểm tra trạng thái xác thực: Khách vãng lai (Anonymous) KHÔNG tạo kết nối SignalR
    if (!isAuthenticated) {
        window.AppRealtime = {
            isAuthenticated: false,
            connection: null,
            on: function () {},
            off: function () {}
        };
        return;
    }

    if (typeof signalR === 'undefined') {
        return;
    }

    // 2. Khởi tạo HubConnection với chính sách kết nối lại hữu hạn [0, 2000, 5000, 10000]
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/storageUnitHub').configureLogging(signalR.LogLevel.None)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

    window.appSignalRConnection = connection;

    let isReconnecting = false;

    // Helper hiển thị thông báo góc màn hình hoặc vùng thông báo
    function showToastAlert(title, message, type) {
        type = type || 'info';
        const alertArea = document.getElementById('globalRealtimeAlertArea') ||
                          document.getElementById('storageUnitsMessageArea') ||
                          document.getElementById('adminAccountsMessageArea') ||
                          document.getElementById('notificationsMessageArea') ||
                          document.getElementById('myUnitsMessageArea');

        if (alertArea) {
            const alertDiv = document.createElement('div');
            alertDiv.className = `alert alert-${type} alert-dismissible fade show shadow-sm mb-3`;
            alertDiv.setAttribute('role', 'alert');

            const contentDiv = document.createElement('div');
            contentDiv.className = 'd-flex align-items-center justify-content-between flex-wrap gap-2';

            const textDiv = document.createElement('div');
            textDiv.className = 'd-flex align-items-center';

            const iconSpan = document.createElement('span');
            iconSpan.className = 'me-2 fs-5';
            iconSpan.textContent = type === 'success' ? '✔' : type === 'warning' ? '⚠' : type === 'danger' ? '✖' : 'ℹ';

            const bodySpan = document.createElement('div');
            const strong = document.createElement('strong');
            strong.textContent = title + ': ';
            bodySpan.appendChild(strong);

            const msgText = document.createTextNode(message);
            bodySpan.appendChild(msgText);

            textDiv.appendChild(iconSpan);
            textDiv.appendChild(bodySpan);
            contentDiv.appendChild(textDiv);

            const closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.className = 'btn-close';
            closeBtn.setAttribute('data-bs-dismiss', 'alert');
            closeBtn.setAttribute('aria-label', 'Đóng');

            alertDiv.appendChild(contentDiv);
            alertDiv.appendChild(closeBtn);

            alertArea.prepend(alertDiv);

            setTimeout(function () {
                if (alertDiv.parentNode) {
                    alertDiv.remove();
                }
            }, 6000);
        }
    }

    // 3. Đăng ký TẤT CẢ listeners TRƯỚC KHI connection.start()

    // 3.1. CatalogInvalidated
    connection.on('CatalogInvalidated', function (data) {
        document.dispatchEvent(new CustomEvent('app:catalog-invalidated', { detail: data }));
    });

    // 3.2. ReservationChanged
    connection.on('ReservationChanged', function (data) {
        document.dispatchEvent(new CustomEvent('app:reservation-changed', { detail: data }));

        if (window.ReservationCountdown && typeof window.ReservationCountdown.applyStatusUpdateOnUi === 'function') {
            const currentResId = document.querySelector('[data-reservation-id]');
            if (currentResId && String(currentResId.getAttribute('data-reservation-id')) === String(data.reservationId)) {
                if (data.status === 'confirmed' || data.status === 'cancelled' || data.status === 'expired') {
                    window.ReservationCountdown.applyStatusUpdateOnUi(data.reservationId, data.status);
                }
            }
        }
    });

    // 3.3. PaymentChanged
    connection.on('PaymentChanged', function (data) {
        document.dispatchEvent(new CustomEvent('app:payment-changed', { detail: data }));

        if (window.ReservationCountdown && typeof window.ReservationCountdown.applyStatusUpdateOnUi === 'function') {
            const currentResId = document.querySelector('[data-reservation-id]');
            if (currentResId && String(currentResId.getAttribute('data-reservation-id')) === String(data.reservationId)) {
                if (data.status === 'completed') {
                    window.ReservationCountdown.applyStatusUpdateOnUi(data.reservationId, 'confirmed', 'Thanh toán thành công. Đơn đặt chỗ đã được xác nhận.');
                }
            }
        }
    });

    // 3.4. CheckInChanged
    connection.on('CheckInChanged', function (data) {
        document.dispatchEvent(new CustomEvent('app:checkin-changed', { detail: data }));
    });

    // 3.5. NotificationsChanged
    connection.on('NotificationsChanged', function (data) {
        document.dispatchEvent(new CustomEvent('app:notifications-changed', { detail: data }));

        showToastAlert('Thông báo mới', 'Bạn có thông báo mới từ hệ thống.', 'info');
    });

    // 3.6. AccountChanged
    connection.on('AccountChanged', function (data) {
        document.dispatchEvent(new CustomEvent('app:account-changed', { detail: data }));
    });

    // 4. Quản lý vòng đời kết nối (Connection Lifecycle)
    connection.onreconnecting(function (error) {
        isReconnecting = true;
    });

    connection.onreconnected(function (connectionId) {
        isReconnecting = false;
        document.dispatchEvent(new CustomEvent('app:realtime-reconnected', { detail: { connectionId: connectionId } }));

        // Refetch / đồng bộ lại dữ liệu sau khi reconnect
        const reloadBtn = document.getElementById('btnRetrySync');
        if (reloadBtn) {
            reloadBtn.click();
        }
    });

    connection.onclose(function (error) {
        isReconnecting = false;
        if (error) {
            showToastAlert('Mất kết nối thời gian thực', 'Không thể kết nối đến máy chủ thời gian thực. Bấm F5 để làm mới trang.', 'warning');
        }
    });

    // 5. Khởi chạy kết nối sau khi DOMContentLoaded và listeners đã sẵn sàng
    function startRealtime() {
        connection.start().then(function () {
            document.dispatchEvent(new CustomEvent('app:realtime-connected', { detail: { connection: connection } }));
        }).catch(function (err) {
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startRealtime);
    } else {
        startRealtime();
    }

    // 6. Expose API toàn cục
    window.AppRealtime = {
        isAuthenticated: true,
        connection: connection,
        isReconnecting: function () { return isReconnecting; },
        showToastAlert: showToastAlert,
        on: function (event, handler) { connection.on(event, handler); },
        off: function (event, handler) { connection.off(event, handler); }
    };

})(window, document);
