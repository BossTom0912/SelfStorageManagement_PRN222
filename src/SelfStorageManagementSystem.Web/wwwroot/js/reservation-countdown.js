/**
 * reservation-countdown.js
 * Quản lý đồng hồ đếm ngược giữ chỗ 15 phút (BR-RSV-01) cho hệ thống SelfStorage.
 * 
 * Nguyên tắc bảo đảm tính toàn vẹn (Integrity Principles):
 * 1. Server-based Clock: Không dùng giờ của trình duyệt máy khách (tránh bị chỉnh sửa giờ hệ điều hành).
 *    Đồng hồ tính toán dựa trên mốc hold_until và server_now do server trả về.
 * 2. Idempotent Reload: Tải lại trang (F5/Reload) chỉ đọc lại hold_until thực từ cơ sở dữ liệu,
 *    tuyệt đối không reset đồng hồ về 15:00 và không tạo đơn đặt chỗ mới.
 * 3. Server as Source of Truth: Khi đồng hồ đếm về 00:00, JS KHÔNG tự động quyết định trạng thái cuối.
 *    JS gửi yêu cầu lên máy chủ để tải trạng thái thật, kích hoạt dynamic check trên server
 *    và khóa toàn bộ các nút hành động không hợp lệ khi server xác nhận đơn đã hết hạn.
 */

(function () {
    'use strict';

    function initReservationCountdown() {
        const countdownElements = document.querySelectorAll('[data-reservation-countdown]');
        if (!countdownElements || countdownElements.length === 0) {
            return;
        }

        countdownElements.forEach(function (container) {
            const holdUntilStr = container.getAttribute('data-hold-until');
            const serverNowStr = container.getAttribute('data-server-now');
            const reservationId = container.getAttribute('data-reservation-id');
            const status = container.getAttribute('data-status');

            if (!holdUntilStr || !serverNowStr) {
                return;
            }

            // Chỉ đếm ngược khi trạng thái là pending hoặc awaiting_deposit
            if (status !== 'pending' && status !== 'awaiting_deposit') {
                return;
            }

            const holdUntilMs = new Date(holdUntilStr).getTime();
            const serverNowMs = new Date(serverNowStr).getTime();
            const clientSyncMs = Date.now();

            // Tính độ lệch giữa giờ client và giờ server tại thời điểm render
            const serverSkewMs = clientSyncMs - serverNowMs;

            const timerDisplay = container.querySelector('[data-countdown-display]');
            const progressBar = container.querySelector('[data-countdown-progress]');

            let isSyncing = false;
            let timerId = null;

            function updateClock() {
                const currentClientMs = Date.now();
                const estimatedServerMs = currentClientMs - serverSkewMs;
                const remainingMs = holdUntilMs - estimatedServerMs;
                const remainingSeconds = Math.max(0, Math.floor(remainingMs / 1000));

                const minutes = Math.floor(remainingSeconds / 60);
                const seconds = remainingSeconds % 60;
                const formattedTime = String(minutes).padStart(2, '0') + ':' + String(seconds).padStart(2, '0');

                if (timerDisplay) {
                    timerDisplay.textContent = formattedTime;
                    if (remainingSeconds <= 180) { // Dưới 3 phút đổi sang màu đỏ cảnh báo
                        timerDisplay.classList.add('text-danger');
                        timerDisplay.classList.remove('text-primary', 'text-warning');
                    } else if (remainingSeconds <= 300) { // Dưới 5 phút màu vàng cam
                        timerDisplay.classList.add('text-warning');
                        timerDisplay.classList.remove('text-primary', 'text-danger');
                    }
                }

                if (progressBar) {
                    // 15 phút = 900 giây
                    const percent = Math.min(100, Math.max(0, (remainingSeconds / 900) * 100));
                    progressBar.style.width = percent + '%';
                    if (percent <= 20) {
                        progressBar.className = 'progress-bar bg-danger';
                    } else if (percent <= 40) {
                        progressBar.className = 'progress-bar bg-warning';
                    } else {
                        progressBar.className = 'progress-bar bg-primary';
                    }
                }

                if (remainingSeconds <= 0) {
                    if (timerId) {
                        clearInterval(timerId);
                        timerId = null;
                    }

                    if (timerDisplay) {
                        timerDisplay.textContent = '00:00';
                        timerDisplay.classList.add('text-danger');
                    }

                    // Không tự coi JS timer là nguồn trạng thái cuối cùng!
                    // Gửi request lấy trạng thái thật từ máy chủ
                    if (!isSyncing && reservationId) {
                        isSyncing = true;
                        fetchRealServerStatus(reservationId, container);
                    }
                }
            }

            // Chạy ngay lần đầu tiên
            updateClock();

            // Lặp lại mỗi 1000ms
            timerId = setInterval(updateClock, 1000);
        });
    }

    /**
     * Tải trạng thái thật từ máy chủ khi đồng hồ chạm 00:00 và khóa các nút hành động
     */
    function fetchRealServerStatus(reservationId, container) {
        const statusUrl = '/Reservations/Details?id=' + encodeURIComponent(reservationId) + '&handler=Status';

        fetch(statusUrl, {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
        .then(function (response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
            return response.json();
        })
        .then(function (data) {
            // Chỉ coi là hết hạn khi server khẳng định status === 'expired'
            // Tuyệt đối không coi confirmed hoặc cancelled là expired dù isHoldActive === false
            if (data && data.status === 'expired') {
                applyExpiredStateOnUi(container, data, reservationId);
            } else if (data && (data.status === 'confirmed' || data.status === 'cancelled' || data.status)) {
                applyStatusUpdateOnUi(container, data, reservationId);
            }
        })
        .catch(function (error) {
            console.warn('Không thể đồng bộ trạng thái realtime với server:', error);
            // Thử reload lại trang để nạp giao diện cập nhật từ server
            setTimeout(function () {
                window.location.reload();
            }, 2500);
        });
    }

    function applyExpiredStateOnUi(container, data, reservationId) {
        // A. Cập nhật theo dòng cụ thể trên Index hoặc MyReservations (nhiều đơn cùng danh sách)
        const row = (container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]');
        if (row) {
            const rowBadge = row.querySelector('[data-status-badge]') || row.querySelector('td[data-status-cell] .badge');
            if (rowBadge) {
                rowBadge.className = 'badge bg-danger px-2 py-1';
                rowBadge.textContent = data.statusDisplayName || 'Hết hạn';
            }

            const countdownCell = row.querySelector('[data-countdown-cell]') || (container ? container.parentElement : null);
            if (countdownCell) {
                countdownCell.innerHTML = '<span class="badge bg-danger-subtle text-danger border border-danger-subtle">Hết hạn</span>';
            }

            const cancelAction = row.querySelector('[data-cancel-action]') || row.querySelector('a[href*="/Reservations/Cancel"]');
            if (cancelAction) {
                cancelAction.remove();
            }
        }

        // B. Cập nhật trên trang Chi Tiết (Details) nếu đang ở trang Details
        const statusBadge = document.getElementById('reservationStatusBadge');
        if (statusBadge) {
            statusBadge.className = 'badge bg-danger';
            statusBadge.textContent = data.statusDisplayName || 'Hết hạn (Expired)';
        }

        const countdownCard = document.getElementById('reservationCountdownPanel');
        if (countdownCard) {
            countdownCard.classList.add('border-danger', 'bg-danger-subtle');
            const desc = countdownCard.querySelector('[data-countdown-desc]');
            if (desc) {
                desc.textContent = 'Thời gian giữ chỗ 15 phút đã kết thúc. Sức chứa khả dụng đã được tự động hoàn lại cho hệ thống.';
                desc.classList.add('text-danger', 'fw-semibold');
            }
        }

        const expiredAlert = document.getElementById('reservationExpiredAlert');
        if (expiredAlert) {
            expiredAlert.classList.remove('d-none');
        }

        const paymentBtn = document.getElementById('btnProceedPayment');
        if (paymentBtn) {
            paymentBtn.disabled = true;
            paymentBtn.classList.add('disabled', 'd-none');
        }

        const cancelBtn = document.getElementById('btnCancelReservation');
        if (cancelBtn) {
            cancelBtn.disabled = true;
            cancelBtn.classList.add('disabled', 'd-none');
            cancelBtn.title = 'Đơn giữ chỗ đã hết hạn, không thể thực hiện hủy.';
        }

        const statusMsg = document.getElementById('reservationStatusAlertText');
        if (statusMsg) {
            statusMsg.textContent = 'Đơn đặt chỗ đã hết hạn. Vui lòng tạo đơn mới từ Danh mục kho.';
        }
    }

    function applyStatusUpdateOnUi(container, data, reservationId) {
        // A. Cập nhật theo dòng cụ thể trên Index hoặc MyReservations
        const row = (container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]');
        if (row) {
            const rowBadge = row.querySelector('[data-status-badge]') || row.querySelector('td[data-status-cell] .badge');
            if (rowBadge) {
                rowBadge.textContent = data.statusDisplayName || data.status;
                if (data.status === 'confirmed') {
                    rowBadge.className = 'badge bg-success px-2 py-1';
                } else if (data.status === 'cancelled') {
                    rowBadge.className = 'badge bg-secondary px-2 py-1';
                } else {
                    rowBadge.className = 'badge bg-info px-2 py-1';
                }
            }

            const countdownCell = row.querySelector('[data-countdown-cell]') || (container ? container.parentElement : null);
            if (countdownCell) {
                if (data.status === 'confirmed') {
                    countdownCell.innerHTML = '<span class="badge bg-success-subtle text-success border border-success-subtle">Đã xác nhận</span>';
                } else if (data.status === 'cancelled') {
                    countdownCell.innerHTML = '<span class="badge bg-secondary-subtle text-secondary border">Đã hủy</span>';
                } else {
                    countdownCell.innerHTML = '<span class="text-muted small">—</span>';
                }
            }

            if (!data.canCancel) {
                const cancelAction = row.querySelector('[data-cancel-action]') || row.querySelector('a[href*="/Reservations/Cancel"]');
                if (cancelAction) {
                    cancelAction.remove();
                }
            }
        }

        // B. Cập nhật trên trang Chi Tiết (Details)
        const statusBadge = document.getElementById('reservationStatusBadge');
        if (statusBadge) {
            statusBadge.textContent = data.statusDisplayName || data.status;
            if (data.status === 'confirmed') {
                statusBadge.className = 'badge bg-success';
            } else if (data.status === 'cancelled') {
                statusBadge.className = 'badge bg-secondary';
            }
        }

        // Tuyệt đối không hiển thị cảnh báo hết hạn cho đơn đã confirmed hoặc cancelled
        const expiredAlert = document.getElementById('reservationExpiredAlert');
        if (expiredAlert) {
            expiredAlert.classList.add('d-none');
        }

        const countdownCard = document.getElementById('reservationCountdownPanel');
        if (countdownCard) {
            countdownCard.classList.add('d-none');
        }

        if (!data.canCancel) {
            const cancelBtn = document.getElementById('btnCancelReservation');
            if (cancelBtn) {
                cancelBtn.disabled = true;
                cancelBtn.classList.add('disabled', 'd-none');
            }
        }

        if (!data.isHoldActive) {
            const paymentBtn = document.getElementById('btnProceedPayment');
            if (paymentBtn) {
                paymentBtn.disabled = true;
                paymentBtn.classList.add('disabled', 'd-none');
            }
        }
    }

    // Khởi chạy khi DOM đã sẵn sàng
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initReservationCountdown);
    } else {
        initReservationCountdown();
    }
})();
