/**
 * reservation-countdown.js
 * Quản lý đồng hồ đếm ngược giữ chỗ 15 phút (BR-RSV-01) cho hệ thống SelfStorage.
 * 
 * Nguyên tắc bảo đảm tính toàn vẹn (Integrity Principles):
 * 1. Server-based Clock: Không dùng giờ của trình duyệt máy khách (tránh bị chỉnh sửa giờ hệ điều hành).
 *    Đồng hồ tính toán dựa trên mốc hold_until và server_now do server trả về.
 * 2. Idempotent Reload: Tải lại trang (F5/Reload) chỉ đọc lại hold_until thực từ cơ sở dữ liệu,
 *    tuyệt đối không reset đồng hồ về 15:00 và không tạo đơn đặt chỗ mới.
 * 3. Server as Source of Truth: Khi đồng hồ đếm về 00:00 (remainingMs <= 0), JS KHÔNG tự động quyết định trạng thái cuối.
 *    JS gửi yêu cầu lên máy chủ để tải trạng thái thật, kích hoạt dynamic check trên server.
 *    - Nếu server trả expired: cập nhật giao diện hết hạn và khóa toàn bộ nút hành động không hợp lệ.
 *    - Nếu server trả confirmed/cancelled: cập nhật trạng thái kết thúc tương ứng, không báo hết hạn.
 *    - Nếu server trả pending/awaiting_deposit (vẫn còn active do chênh lệch ms): tiếp tục giữ countdown và lập lịch đồng bộ tiếp tục cho đến khi server xác nhận hết hạn.
 * 4. Precise Expiration Boundary: Tách số giây hiển thị khỏi điều kiện đến hạn.
 *    Chỉ kích hoạt đồng bộ hết hạn khi remainingMs <= 0.
 * 5. Single In-Flight Request & Unified Backoff Scheduler:
 *    - Quản lý cờ isSyncing đảm bảo tối đa 1 request in-flight cho mỗi đơn.
 *    - Sử dụng nextSyncAtMs để timer 1 giây không chen ngang lịch backoff 3000ms khi gặp lỗi mạng.
 * 6. Authentication Resilience (Finding 1):
 *    - Phân biệt rõ HTTP 401 (hết hạn phiên cookie 5 phút) và HTTP 403 (từ chối quyền).
 *    - Dừng vĩnh viễn timer/polling, hiển thị thông báo hướng dẫn đăng nhập lại và returnUrl nội bộ, không đổi sang expired giả.
 */

(function () {
    'use strict';

    function initReservationCountdown(force) {
        const countdownElements = document.querySelectorAll('[data-reservation-countdown]');
        if (!countdownElements || countdownElements.length === 0) {
            return;
        }

        countdownElements.forEach(function (container) {
            // Tránh khởi tạo trùng lặp nếu script được gọi nhiều lần trên cùng một phần tử
            if (!force && container.getAttribute('data-countdown-initialized') === 'true') {
                return;
            }
            container.setAttribute('data-countdown-initialized', 'true');

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
            let serverNowMs = new Date(serverNowStr).getTime();
            const clientSyncMs = Date.now();

            if (isNaN(holdUntilMs) || isNaN(serverNowMs)) {
                return;
            }

            // Tính độ lệch giữa giờ client và giờ server tại thời điểm render
            let serverSkewMs = clientSyncMs - serverNowMs;

            const timerDisplay = container.querySelector('[data-countdown-display]');
            const progressBar = container.querySelector('[data-countdown-progress]');

            let isSyncing = false;
            let isTerminated = false;
            let nextSyncAtMs = 0; // Timestamp ms sớm nhất được phép gửi request tiếp theo
            let timerId = null;
            let retryTimeoutId = null;

            function stopAllTimers() {
                isTerminated = true;
                nextSyncAtMs = Infinity;
                if (timerId) {
                    clearInterval(timerId);
                    timerId = null;
                }
                if (retryTimeoutId) {
                    clearTimeout(retryTimeoutId);
                    retryTimeoutId = null;
                }
            }

            function syncServerStatus() {
                // Kiểm tra điều kiện: chưa kết thúc, không có request in-flight, có reservationId và ĐÃ ĐỦ THỜI GIAN nextSyncAtMs
                if (isTerminated || isSyncing || !reservationId || Date.now() < nextSyncAtMs) {
                    return;
                }

                isSyncing = true;
                if (retryTimeoutId) {
                    clearTimeout(retryTimeoutId);
                    retryTimeoutId = null;
                }

                const statusUrl = '/Reservations/Details?id=' + encodeURIComponent(reservationId) + '&handler=Status';

                fetch(statusUrl, {
                    method: 'GET',
                    headers: {
                        'Accept': 'application/json',
                        'X-Requested-With': 'XMLHttpRequest'
                    }
                })
                .then(function (response) {
                    // Finding 1: Xử lý rõ ràng HTTP 401 (hết phiên cookie) và HTTP 403 (từ chối quyền)
                    if (response.status === 401) {
                        stopAllTimers();
                        applyAuthErrorOnUi(container, 401, reservationId);
                        return null;
                    }
                    if (response.status === 403) {
                        stopAllTimers();
                        applyAuthErrorOnUi(container, 403, reservationId);
                        return null;
                    }

                    if (!response.ok) {
                        throw new Error('HTTP ' + response.status);
                    }
                    return response.json();
                })
                .then(function (data) {
                    if (!data) {
                        return; // Đã dừng và xử lý 401/403 ở trên
                    }

                    if (!data.success) {
                        throw new Error('Dữ liệu server không hợp lệ');
                    }

                    // Cập nhật lại server skew nếu server trả về serverNow
                    if (data.serverNow) {
                        const updatedServerMs = new Date(data.serverNow).getTime();
                        if (!isNaN(updatedServerMs)) {
                            serverSkewMs = Date.now() - updatedServerMs;
                        }
                    }

                    // 1. Phân nhánh EXPIRED: Server xác nhận đơn đã hết hạn
                    if (data.status === 'expired') {
                        stopAllTimers();
                        applyExpiredStateOnUi(container, data, reservationId);
                        return;
                    }

                    // 2. Phân nhánh TERMINAL (confirmed / cancelled / checked_in / completed / converted)
                    if (data.status === 'confirmed' || data.status === 'cancelled' ||
                        data.status === 'checked_in' || data.status === 'completed' || data.status === 'converted') {
                        stopAllTimers();
                        applyStatusUpdateOnUi(container, data, reservationId);
                        return;
                    }

                    // 3. Phân nhánh ACTIVE HOLD (pending / awaiting_deposit còn active):
                    // Server vẫn ghi nhận hold active (do lệch mili-giây sát deadline hoặc delay xử lý).
                    // TUYỆT ĐỐI KHÔNG xóa/ẩn countdown, KHÔNG khóa nút Hủy khi server chưa expire.
                    // Tiếp tục giữ countdown và lập lịch đồng bộ lại sau 1000ms.
                    if (data.status === 'pending' || data.status === 'awaiting_deposit') {
                        if (timerDisplay) {
                            timerDisplay.textContent = '00:00';
                            timerDisplay.classList.add('text-danger');
                        }
                        if (progressBar) {
                            progressBar.style.width = '0%';
                            progressBar.className = 'progress-bar bg-danger';
                        }

                        // Finding 2: Lập lịch thử lại sau 1000ms, đặt mốc nextSyncAtMs
                        if (!isTerminated) {
                            nextSyncAtMs = Date.now() + 1000;
                            retryTimeoutId = setTimeout(function () {
                                syncServerStatus();
                            }, 1000);
                        }
                    }
                })
                .catch(function (error) {
                    // Finding 2: Lỗi mạng có kiểm soát: lùi lịch 3000ms (backoff),
                    // cập nhật nextSyncAtMs để updateClock() không chen ngang gửi request sớm
                    if (!isTerminated) {
                        nextSyncAtMs = Date.now() + 3000;
                        if (retryTimeoutId) clearTimeout(retryTimeoutId);
                        retryTimeoutId = setTimeout(function () {
                            syncServerStatus();
                        }, 3000);
                    }
                })
                .finally(function () {
                    isSyncing = false;
                });
            }

            function updateClock() {
                if (isTerminated) {
                    return;
                }

                const currentClientMs = Date.now();
                const estimatedServerMs = currentClientMs - serverSkewMs;
                const remainingMs = holdUntilMs - estimatedServerMs;

                // 1. TÁCH SỐ GIÂY HIỂN THỊ KHỎI ĐIỀU KIỆN ĐẾN HẠN
                // Số giây hiển thị: Khi còn > 0ms, làm tròn lên (Math.ceil) để 500ms vẫn hiển thị 00:01
                const displaySeconds = remainingMs > 0 ? Math.ceil(remainingMs / 1000) : 0;

                const minutes = Math.floor(displaySeconds / 60);
                const seconds = displaySeconds % 60;
                const formattedTime = String(minutes).padStart(2, '0') + ':' + String(seconds).padStart(2, '0');

                if (timerDisplay) {
                    timerDisplay.textContent = formattedTime;
                    if (displaySeconds <= 180) { // Dưới 3 phút đổi sang màu đỏ cảnh báo
                        timerDisplay.classList.add('text-danger');
                        timerDisplay.classList.remove('text-primary', 'text-warning');
                    } else if (displaySeconds <= 300) { // Dưới 5 phút màu vàng cam
                        timerDisplay.classList.add('text-warning');
                        timerDisplay.classList.remove('text-primary', 'text-danger');
                    } else {
                        timerDisplay.classList.remove('text-danger', 'text-warning');
                        timerDisplay.classList.add('text-primary');
                    }
                }

                if (progressBar) {
                    // 15 phút = 900 giây
                    const percent = Math.min(100, Math.max(0, (displaySeconds / 900) * 100));
                    progressBar.style.width = percent + '%';
                    if (percent <= 20) {
                        progressBar.className = 'progress-bar bg-danger';
                    } else if (percent <= 40) {
                        progressBar.className = 'progress-bar bg-warning';
                    } else {
                        progressBar.className = 'progress-bar bg-primary';
                    }
                }

                // 2. CHỈ BẮT ĐẦU ĐỒNG BỘ HẾT HẠN KHI remainingMs <= 0 THỰC SỰ
                // VÀ ĐÃ ĐẾN HẠN nextSyncAtMs (Finding 2: Tuyệt đối không vượt qua lịch retry 3000ms)
                if (remainingMs <= 0) {
                    if (timerDisplay) {
                        timerDisplay.textContent = '00:00';
                        timerDisplay.classList.add('text-danger');
                    }
                    if (progressBar) {
                        progressBar.style.width = '0%';
                        progressBar.className = 'progress-bar bg-danger';
                    }

                    // Không tự coi JS timer là nguồn trạng thái cuối cùng!
                    // Gửi request lấy trạng thái thật từ máy chủ (chỉ gửi nếu chưa có request in-flight và đã qua mốc nextSyncAtMs)
                    if (!isSyncing && !isTerminated && Date.now() >= nextSyncAtMs) {
                        syncServerStatus();
                    }
                }
            }

            // Chạy ngay lần đầu tiên
            updateClock();

            // Lặp lại mỗi 1000ms cho đến khi terminated
            timerId = setInterval(updateClock, 1000);
        });
    }

    /**
     * Áp dụng giao diện hết hạn cho đơn đặt chỗ khi server khẳng định status === 'expired'
     */
    function applyExpiredStateOnUi(container, data, reservationId) {
        // A. Cập nhật theo dòng cụ thể trên Index hoặc MyReservations (nhiều đơn cùng danh sách)
        const row = (container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]');
        if (row) {
            const rowBadge = row.querySelector('[data-status-badge="' + reservationId + '"]') || row.querySelector('[data-status-badge]') || row.querySelector('td[data-status-cell] .badge');
            if (rowBadge) {
                rowBadge.className = 'badge bg-danger px-2 py-1';
                rowBadge.textContent = data.statusDisplayName || 'Hết hạn';
            }

            const countdownCell = row.querySelector('[data-countdown-cell="' + reservationId + '"]') || row.querySelector('[data-countdown-cell]') || (container ? container.parentElement : null);
            if (countdownCell) {
                countdownCell.innerHTML = '<span class="badge bg-danger-subtle text-danger border border-danger-subtle">Hết hạn</span>';
            }

            const cancelAction = row.querySelector('[data-cancel-action="' + reservationId + '"]') || row.querySelector('[data-cancel-action]') || row.querySelector('a[href*="/Reservations/Cancel"]');
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
            countdownCard.classList.remove('d-none');
            countdownCard.classList.add('border-danger', 'bg-danger-subtle');
            const desc = countdownCard.querySelector('[data-countdown-desc]');
            if (desc) {
                desc.textContent = 'Thời gian giữ chỗ 15 phút đã kết thúc. Sức chứa khả dụng đã được tự động hoàn lại cho hệ thống.';
                desc.classList.add('text-danger', 'fw-semibold');
            }
        }

        // Ẩn khối thông báo xác thực (nếu có) khi chuyển sang trạng thái hết hạn thực sự
        const authAlert = document.getElementById('reservationAuthAlert');
        if (authAlert) {
            authAlert.classList.add('d-none');
        }

        const expiredAlert = document.getElementById('reservationExpiredAlert');
        if (expiredAlert) {
            expiredAlert.className = 'alert alert-danger shadow-sm mb-4';
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

    /**
     * Áp dụng giao diện trạng thái kết thúc (confirmed / cancelled) cho đơn đặt chỗ
     */
    function applyStatusUpdateOnUi(container, data, reservationId) {
        // Hỗ trợ linh hoạt cả 2 chữ ký:
        // 1. applyStatusUpdateOnUi(container, data, reservationId)
        // 2. applyStatusUpdateOnUi(reservationId, statusString, displayName)
        if (typeof container === 'number' || (typeof container === 'string' && !document.getElementById(container))) {
            const passedId = container;
            const passedStatus = typeof data === 'string' ? data : (data && data.status ? data.status : 'confirmed');
            const passedDisplayName = reservationId || (data && data.statusDisplayName ? data.statusDisplayName : (passedStatus === 'confirmed' ? 'Đã xác nhận' : passedStatus));
            reservationId = passedId;
            data = {
                status: passedStatus,
                statusDisplayName: passedDisplayName,
                canCancel: false,
                isHoldActive: false
            };
            container = document.querySelector('[data-reservation-id="' + reservationId + '"]') ||
                        document.querySelector('[data-reservation-countdown][data-reservation-id="' + reservationId + '"]');
        } else if (typeof data === 'string') {
            data = {
                status: data,
                statusDisplayName: data === 'confirmed' ? 'Đã xác nhận' : (data === 'cancelled' ? 'Đã hủy' : data),
                canCancel: false,
                isHoldActive: false
            };
        }

        // A. Cập nhật theo dòng cụ thể trên Index hoặc MyReservations
        const row = (container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]');
        if (row) {
            const rowBadge = row.querySelector('[data-status-badge="' + reservationId + '"]') || row.querySelector('[data-status-badge]') || row.querySelector('td[data-status-cell] .badge');
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

            const countdownCell = row.querySelector('[data-countdown-cell="' + reservationId + '"]') || row.querySelector('[data-countdown-cell]') || (container ? container.parentElement : null);
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
                const cancelAction = row.querySelector('[data-cancel-action="' + reservationId + '"]') || row.querySelector('[data-cancel-action]') || row.querySelector('a[href*="/Reservations/Cancel"]');
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

        // Tuyệt đối không hiển thị cảnh báo hết hạn hoặc cảnh báo xác thực cho đơn đã confirmed hoặc cancelled
        const expiredAlert = document.getElementById('reservationExpiredAlert');
        if (expiredAlert) {
            expiredAlert.classList.add('d-none');
        }

        const authAlert = document.getElementById('reservationAuthAlert');
        if (authAlert) {
            authAlert.classList.add('d-none');
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

    /**
     * Finding 1: Áp dụng giao diện khi gặp lỗi xác thực hoặc phân quyền (HTTP 401 / 403)
     * Dừng đồng bộ, hướng dẫn người dùng đăng nhập lại với returnUrl nội bộ, không đổi sang expired giả.
     */
    function applyAuthErrorOnUi(container, statusCode, reservationId) {
        const is401 = statusCode === 401;
        const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
        const loginUrl = '/Account/Login?returnUrl=' + returnUrl;

        // A. Cập nhật theo dòng cụ thể trên Index hoặc MyReservations
        const row = (container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]');
        if (row) {
            const rowBadge = row.querySelector('[data-status-badge="' + reservationId + '"]') || row.querySelector('[data-status-badge]') || row.querySelector('td[data-status-cell] .badge');
            if (rowBadge) {
                if (is401) {
                    rowBadge.className = 'badge bg-warning text-dark px-2 py-1';
                    rowBadge.textContent = 'Hết phiên đăng nhập';
                } else {
                    rowBadge.className = 'badge bg-danger px-2 py-1';
                    rowBadge.textContent = 'Từ chối quyền';
                }
            }

            const countdownCell = row.querySelector('[data-countdown-cell="' + reservationId + '"]') || row.querySelector('[data-countdown-cell]') || (container ? container.parentElement : null);
            if (countdownCell) {
                if (is401) {
                    countdownCell.innerHTML = '<a href="' + loginUrl + '" class="btn btn-sm btn-outline-primary py-0 px-2 small">Đăng nhập lại</a>';
                } else {
                    countdownCell.innerHTML = '<span class="text-muted small">Từ chối truy cập</span>';
                }
            }

            const cancelAction = row.querySelector('[data-cancel-action="' + reservationId + '"]') || row.querySelector('[data-cancel-action]') || row.querySelector('a[href*="/Reservations/Cancel"]');
            if (cancelAction) {
                cancelAction.remove();
            }
        }

        // B. Cập nhật trên trang Chi Tiết (Details)
        const statusBadge = document.getElementById('reservationStatusBadge');
        if (statusBadge) {
            if (is401) {
                statusBadge.className = 'badge bg-warning text-dark';
                statusBadge.textContent = 'Hết phiên đăng nhập';
            } else {
                statusBadge.className = 'badge bg-danger';
                statusBadge.textContent = 'Từ chối quyền';
            }
        }

        const countdownCard = document.getElementById('reservationCountdownPanel');
        if (countdownCard) {
            const desc = countdownCard.querySelector('[data-countdown-desc]');
            if (desc) {
                if (is401) {
                    desc.innerHTML = 'Phiên đăng nhập đã hết hạn. Vui lòng <a href="' + loginUrl + '" class="fw-bold">đăng nhập lại</a> để tiếp tục đồng bộ trạng thái đơn giữ chỗ.';
                    desc.className = 'text-warning-emphasis fw-semibold';
                } else {
                    desc.textContent = 'Bạn không có quyền truy cập hoặc thao tác trên đơn giữ chỗ này.';
                    desc.className = 'text-danger fw-semibold';
                }
            }
        }

        // 1. TUYỆT ĐỐI ẨN HOẶC GIỮ ẨN KHỐI HẾT HẠN (reservationExpiredAlert)
        // Lỗi 401/403 chỉ phản ánh phiên/quyền, không chứng minh đơn đã expired hay giải phóng sức chứa
        const expiredAlert = document.getElementById('reservationExpiredAlert');
        if (expiredAlert) {
            expiredAlert.classList.add('d-none');
        }

        // 2. HIỂN THỊ KHỐI THÔNG BÁO XÁC THỰC / PHÂN QUYỀN RIÊNG BIỆT (reservationAuthAlert)
        const authAlert = document.getElementById('reservationAuthAlert');
        if (authAlert) {
            const heading = document.getElementById('reservationAuthAlertHeading');
            const body = document.getElementById('reservationAuthAlertBody');
            const icon = document.getElementById('reservationAuthAlertIcon');

            if (is401) {
                authAlert.className = 'alert alert-warning shadow-sm mb-4';
                if (icon) {
                    icon.innerHTML = '&#9888;';
                    icon.className = 'fs-2 text-warning';
                }
                if (heading) {
                    heading.textContent = 'Phiên đăng nhập đã hết hạn';
                }
                if (body) {
                    body.innerHTML = 'Phiên làm việc của bạn đã hết hạn sau 5 phút không hoạt động. Đơn đặt chỗ vẫn được bảo lưu thời hạn trên hệ thống. ' +
                        'Vui lòng <a href="' + loginUrl + '" class="alert-link fw-semibold" id="reservationAuthAlertLink">đăng nhập lại</a> để tiếp tục đồng bộ và thanh toán.';
                }
            } else {
                authAlert.className = 'alert alert-danger shadow-sm mb-4';
                if (icon) {
                    icon.innerHTML = '&#10008;';
                    icon.className = 'fs-2 text-danger';
                }
                if (heading) {
                    heading.textContent = 'Từ chối quyền truy cập';
                }
                if (body) {
                    body.textContent = 'Tài khoản hiện tại không có quyền truy cập hoặc thực hiện thao tác trên đơn đặt chỗ này.';
                }
            }
            authAlert.classList.remove('d-none');
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
        }
    }

    // T15 R3 Fix (R2-F04): Lắng nghe phục hồi kết nối SignalR để refetch canonical state ngay lập tức
    function resyncAllActiveReservations() {
        const candidateContainers = document.querySelectorAll('[data-reservation-countdown], [data-reservation-id], #reservationCountdownPanel');
        const processedIds = new Set();

        candidateContainers.forEach(function (cont) {
            const resId = cont.getAttribute('data-reservation-id');
            if (!resId || processedIds.has(resId)) return;
            processedIds.add(resId);

            const statusUrl = '/Reservations/Details?id=' + encodeURIComponent(resId) + '&handler=Status';
            fetch(statusUrl, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                }
            })
            .then(function (response) {
                if (response.status === 401) {
                    applyAuthErrorOnUi(cont, 401, resId);
                    return null;
                }
                if (response.status === 403) {
                    applyAuthErrorOnUi(cont, 403, resId);
                    return null;
                }
                if (!response.ok) return null;
                return response.json();
            })
            .then(function (resData) {
                if (!resData || !resData.success) return;

                if (resData.status === 'expired') {
                    applyExpiredStateOnUi(cont, resData, resId);
                } else if (resData.status === 'confirmed' || resData.status === 'cancelled' ||
                           resData.status === 'checked_in' || resData.status === 'completed' || resData.status === 'converted') {
                    applyStatusUpdateOnUi(cont, resData, resId);
                }
            })
            .catch(function (err) {
            });
        });
    }

    document.addEventListener('app:realtime-reconnected', resyncAllActiveReservations);

    // Expose helpers for testing and programmatic invocation
    window.ReservationCountdown = {
        init: initReservationCountdown,
        applyExpiredStateOnUi: applyExpiredStateOnUi,
        applyStatusUpdateOnUi: applyStatusUpdateOnUi,
        applyAuthErrorOnUi: applyAuthErrorOnUi,
        resyncAllActiveReservations: resyncAllActiveReservations
    };
    window.initReservationCountdown = initReservationCountdown;

    // Khởi chạy khi DOM đã sẵn sàng
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            initReservationCountdown();
        });
    } else {
        initReservationCountdown();
    }
})();
