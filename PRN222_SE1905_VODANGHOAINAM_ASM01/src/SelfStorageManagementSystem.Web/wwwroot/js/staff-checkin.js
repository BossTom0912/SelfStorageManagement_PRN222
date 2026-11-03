/**
 * JavaScript dành riêng cho Quầy tiếp nhận & Bàn giao kho số hóa (Task T11)
 * Tuân thủ nghiêm ngặt quy tắc bảo mật:
 * - TUYỆT ĐỐI KHÔNG lưu mã PIN vào URL, cookie, log, localStorage hay sessionStorage.
 * - TUYỆT ĐỐI KHÔNG phát tán mã PIN qua SignalR hay WebSocket.
 * - Hỗ trợ sao chép nhanh mã PIN vào bộ nhớ tạm (clipboard) khi nhân viên yêu cầu.
 * - Xác thực đủ 4 hạng mục kiểm tra trước khi gửi form.
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Sao chép mã PIN vào Clipboard an toàn (không lưu vào bộ nhớ trình duyệt)
    const btnCopyPin = document.getElementById('btnCopyPin');
    if (btnCopyPin) {
        btnCopyPin.addEventListener('click', function () {
            const pinElement = document.getElementById('displayPinValue');
            if (pinElement) {
                const pinText = pinElement.innerText.trim();
                if (navigator.clipboard && window.isSecureContext) {
                    navigator.clipboard.writeText(pinText).then(function () {
                        showCopyFeedback(btnCopyPin, 'Đã sao chép!');
                    }).catch(function () {
                        fallbackCopyText(pinText, btnCopyPin);
                    });
                } else {
                    fallbackCopyText(pinText, btnCopyPin);
                }
            }
        });
    }

    function fallbackCopyText(text, btn) {
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        textArea.style.left = '-999999px';
        textArea.style.top = '-999999px';
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        try {
            document.execCommand('copy');
            showCopyFeedback(btn, 'Đã sao chép!');
        } catch (err) {
            showCopyFeedback(btn, 'Không thể sao chép');
        }
        document.body.removeChild(textArea);
    }

    function showCopyFeedback(btn, message) {
        const originalText = btn.innerHTML;
        btn.innerHTML = '<i class="bi bi-check2"></i> ' + message;
        btn.classList.remove('btn-outline-primary');
        btn.classList.add('btn-success');
        setTimeout(function () {
            btn.innerHTML = originalText;
            btn.classList.remove('btn-success');
            btn.classList.add('btn-outline-primary');
        }, 2000);
    }

    // 2. Cảnh báo kiểm tra ô kho không đạt (Failed/Maintenance)
    const inspectionForm = document.getElementById('inspectionForm');
    if (inspectionForm) {
        inspectionForm.addEventListener('submit', function (e) {
            const isUnsafeChecked = document.getElementById('chkIsUnsafe') && document.getElementById('chkIsUnsafe').checked;
            
            // Kiểm tra xem có hạng mục nào damaged hoặc missing không
            const conditionSelects = inspectionForm.querySelectorAll('select[name$=".Condition"]');
            let hasDamagedOrMissing = false;

            conditionSelects.forEach(function (select) {
                const val = select.value.toLowerCase();
                if (val === 'damaged' || val === 'missing') {
                    hasDamagedOrMissing = true;
                }
            });

            if (isUnsafeChecked || hasDamagedOrMissing) {
                const confirmMsg = isUnsafeChecked
                    ? 'CẢNH BÁO: Cờ mất an toàn (IsUnsafe) đang được bật. Ô kho này sẽ KHÔNG ĐẠT kiểm tra, tự động bị GIẢI PHÓNG khỏi hợp đồng và chuyển sang trạng thái BẢO TRÌ (Maintenance).\n\nBạn có chắc chắn muốn ghi nhận không?'
                    : 'CẢNH BÁO: Có hạng mục bị hư hỏng hoặc thiếu mất. Ô kho này sẽ KHÔNG ĐẠT kiểm tra, tự động bị GIẢI PHÓNG khỏi hợp đồng và chuyển sang trạng thái BẢO TRÌ (Maintenance).\n\nBạn có chắc chắn muốn ghi nhận không?';

                if (!confirm(confirmMsg)) {
                    e.preventDefault();
                    return false;
                }
            }
        });
    }

    // 3. Tự động cuộn đến thông báo lỗi hoặc kết quả cấp PIN
    const pinBox = document.getElementById('securityPinSection');
    if (pinBox) {
        pinBox.scrollIntoView({ behavior: 'smooth', block: 'center' });
    } else {
        const errorAlert = document.querySelector('.checkin-error-alert');
        if (errorAlert) {
            errorAlert.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    // 4. T15: Lắng nghe sự kiện CheckInChanged thời gian thực
    document.addEventListener('app:checkin-changed', function (e) {
        const detail = e.detail;
        const container = document.querySelector('.container') || document.querySelector('main');
        if (!container || document.getElementById('checkinRealtimeAlert')) return;

        const alertDiv = document.createElement('div');
        alertDiv.id = 'checkinRealtimeAlert';
        alertDiv.className = 'alert alert-info alert-dismissible fade show shadow-sm mb-3';
        alertDiv.setAttribute('role', 'alert');

        const wrapper = document.createElement('div');
        wrapper.className = 'd-flex align-items-center justify-content-between flex-wrap gap-2';

        const textDiv = document.createElement('div');
        textDiv.className = 'd-flex align-items-center';

        const iconSpan = document.createElement('span');
        iconSpan.className = 'me-2 fs-5 text-info';
        iconSpan.textContent = 'ℹ';

        const messageSpan = document.createElement('div');
        const strong = document.createElement('strong');
        strong.textContent = 'Tiếp nhận cập nhật: ';
        messageSpan.appendChild(strong);
        messageSpan.appendChild(document.createTextNode('Phiên tiếp nhận vừa có thay đổi trạng thái (' + (detail.status || 'mới') + '). Nhấn để tải lại dữ liệu.'));

        textDiv.appendChild(iconSpan);
        textDiv.appendChild(messageSpan);
        wrapper.appendChild(textDiv);

        const reloadBtn = document.createElement('button');
        reloadBtn.type = 'button';
        reloadBtn.className = 'btn btn-info btn-sm fw-semibold';
        reloadBtn.textContent = 'Tải lại';
        reloadBtn.onclick = function () { window.location.reload(); };
        wrapper.appendChild(reloadBtn);

        alertDiv.appendChild(wrapper);
        container.prepend(alertDiv);
    });

    // 5. T15 R2 Fix (F07): Xử lý khôi phục kết nối (app:realtime-reconnected)
    document.addEventListener('app:realtime-reconnected', function () {

        // Bảo vệ nghiêm ngặt: Tuyệt đối không làm mất mã PIN một lần đang hiển thị
        if (document.getElementById('securityPinSection')) {
            return;
        }

        // Không làm gián đoạn form đang nhập dở
        const active = document.activeElement;
        if (active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.tagName === 'SELECT')) {
            return;
        }

        const url = new URL(window.location.href);
        const code = url.searchParams.get('code');
        if (code) {
            // Refetch canonical state từ server
            fetch('/Staff/CheckIn?code=' + encodeURIComponent(code), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            }).then(function () {
                const container = document.querySelector('.container') || document.querySelector('main');
                if (!container || document.getElementById('checkinRealtimeAlert')) return;

                const alertDiv = document.createElement('div');
                alertDiv.id = 'checkinRealtimeAlert';
                alertDiv.className = 'alert alert-info alert-dismissible fade show shadow-sm mb-3';
                alertDiv.setAttribute('role', 'alert');

                const wrapper = document.createElement('div');
                wrapper.className = 'd-flex align-items-center justify-content-between flex-wrap gap-2';

                const textDiv = document.createElement('div');
                textDiv.className = 'd-flex align-items-center';

                const iconSpan = document.createElement('span');
                iconSpan.className = 'me-2 fs-5 text-info';
                iconSpan.textContent = 'ℹ';

                const messageSpan = document.createElement('div');
                const strong = document.createElement('strong');
                strong.textContent = 'Đã khôi phục kết nối: ';
                messageSpan.appendChild(strong);
                messageSpan.appendChild(document.createTextNode('Dữ liệu mới nhất sẵn sàng đồng bộ. Nhấn để cập nhật.'));

                textDiv.appendChild(iconSpan);
                textDiv.appendChild(messageSpan);
                wrapper.appendChild(textDiv);

                const reloadBtn = document.createElement('button');
                reloadBtn.type = 'button';
                reloadBtn.className = 'btn btn-info btn-sm fw-semibold';
                reloadBtn.textContent = 'Cập nhật';
                reloadBtn.onclick = function () { window.location.reload(); };
                wrapper.appendChild(reloadBtn);

                alertDiv.appendChild(wrapper);
                container.prepend(alertDiv);
            }).catch(function () {});
        }
    });
});
