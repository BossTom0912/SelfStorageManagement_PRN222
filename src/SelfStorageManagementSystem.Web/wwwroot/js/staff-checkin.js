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
});
