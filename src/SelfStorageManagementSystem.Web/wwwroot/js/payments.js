/**
 * payments.js
 * Client-side Realtime listener cho mô-đun Thanh Toán (Payments Index & Details - Task T15 R3 Fix R2-F03).
 * Lắng nghe app:payment-changed và app:realtime-reconnected từ SignalR Hub.
 */

document.addEventListener('DOMContentLoaded', function () {
    const detailsContainer = document.getElementById('paymentDetailsContainer');
    const detailsMsgArea = document.getElementById('paymentDetailsMessageArea');
    const indexMsgArea = document.getElementById('paymentsMessageArea');

    function showNotice(container, message, allowReload) {
        if (!container) return;
        const alertDiv = document.createElement('div');
        alertDiv.className = 'alert alert-info alert-dismissible fade show shadow-sm d-flex justify-content-between align-items-center mb-3';
        alertDiv.setAttribute('role', 'alert');

        const contentDiv = document.createElement('div');
        contentDiv.className = 'd-flex align-items-center';

        const iconSpan = document.createElement('span');
        iconSpan.className = 'fs-5 me-2';
        iconSpan.innerHTML = '&#128260;';

        const textSpan = document.createElement('span');
        textSpan.textContent = message;

        contentDiv.appendChild(iconSpan);
        contentDiv.appendChild(textSpan);
        alertDiv.appendChild(contentDiv);

        const actionsDiv = document.createElement('div');
        if (allowReload) {
            const reloadBtn = document.createElement('button');
            reloadBtn.type = 'button';
            reloadBtn.className = 'btn btn-sm btn-info fw-semibold me-2';
            reloadBtn.textContent = 'Tải Lại';
            reloadBtn.addEventListener('click', function () {
                window.location.reload();
            });
            actionsDiv.appendChild(reloadBtn);
        }

        const closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'btn-close';
        closeBtn.setAttribute('data-bs-dismiss', 'alert');
        closeBtn.setAttribute('aria-label', 'Đóng');
        actionsDiv.appendChild(closeBtn);

        alertDiv.appendChild(actionsDiv);

        container.innerHTML = '';
        container.appendChild(alertDiv);
    }

    function updateBadgeClass(badgeElem, status) {
        if (!badgeElem) return;
        const lower = (status || '').toLowerCase();
        let cssClass = 'badge px-3 py-1 fs-6 ';
        if (lower === 'succeeded' || lower === 'completed') {
            cssClass += 'bg-success';
        } else if (lower === 'failed') {
            cssClass += 'bg-danger';
        } else if (lower === 'reconciliation_required') {
            cssClass += 'bg-warning text-dark';
        } else if (lower === 'cancelled') {
            cssClass += 'bg-secondary';
        } else if (lower === 'refunded') {
            cssClass += 'bg-info text-dark';
        } else {
            cssClass += 'bg-warning text-dark';
        }
        badgeElem.className = cssClass;
        badgeElem.textContent = status;
    }

    // 1. Lắng nghe sự kiện PaymentChanged
    document.addEventListener('app:payment-changed', function (e) {
        const data = e.detail;
        if (!data) return;

        // A. Xử lý trên trang Chi Tiết (Payments/Details)
        if (detailsContainer) {
            const curPaymentId = detailsContainer.getAttribute('data-payment-id');
            const curReservationId = detailsContainer.getAttribute('data-reservation-id');

            const isMatch = (data.paymentId && String(data.paymentId) === String(curPaymentId)) ||
                            (data.reservationId && curReservationId && String(data.reservationId) === String(curReservationId));

            if (isMatch) {
                const statusBadge = document.getElementById('paymentStatusBadge');
                if (statusBadge) {
                    updateBadgeClass(statusBadge, data.status);
                }

                if (detailsMsgArea) {
                    showNotice(detailsMsgArea, 'Giao dịch #' + data.paymentId + ' vừa được cập nhật trạng thái: ' + data.status + '. Bấm vào đây để tải lại trang đầy đủ.', true);
                }
            }
        }

        // B. Xử lý trên trang Danh Sách (Payments/Index)
        if (indexMsgArea) {
            let matchedRow = null;
            if (data.paymentId) {
                matchedRow = document.querySelector('tr[data-payment-id="' + data.paymentId + '"]');
            }
            if (!matchedRow && data.reservationId) {
                matchedRow = document.querySelector('tr[data-reservation-id="' + data.reservationId + '"]');
            }

            if (matchedRow) {
                const rowBadge = matchedRow.querySelector('[data-payment-status-badge]') || matchedRow.querySelector('.badge');
                if (rowBadge) {
                    rowBadge.textContent = data.status;
                    if (data.status === 'completed' || data.status === 'succeeded') {
                        rowBadge.className = 'badge bg-success';
                    } else if (data.status === 'failed') {
                        rowBadge.className = 'badge bg-danger';
                    } else if (data.status === 'reconciliation_required') {
                        rowBadge.className = 'badge bg-warning text-dark';
                    }
                }
                matchedRow.classList.add('table-info');
            }

            showNotice(indexMsgArea, 'Giao dịch #' + data.paymentId + ' vừa chuyển sang trạng thái: ' + data.status + '. Bấm vào đây để làm mới danh sách.', true);
        }
    });

    // 2. Lắng nghe phục hồi kết nối Realtime (app:realtime-reconnected)
    document.addEventListener('app:realtime-reconnected', function () {
        if (detailsMsgArea) {
            showNotice(detailsMsgArea, 'Kết nối mạng realtime đã được khôi phục. Bấm vào đây để cập nhật trạng thái thanh toán mới nhất.', true);
        } else if (indexMsgArea) {
            showNotice(indexMsgArea, 'Kết nối mạng realtime đã được khôi phục. Bấm vào đây để làm mới danh sách thanh toán.', true);
        }
    });
});
