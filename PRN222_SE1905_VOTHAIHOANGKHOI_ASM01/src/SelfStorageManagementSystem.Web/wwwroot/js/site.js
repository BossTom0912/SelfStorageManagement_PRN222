/**
 * SelfStorageManagementSystem.Web - JavaScript Dùng Chung
 * Chứa các hàm tiện ích chuẩn hóa tiếng Việt, tiền tệ VND, ngày tháng, trạng thái tải và bảo vệ form.
 * 
 * Lưu ý quan trọng: Layout KHÔNG tự ý khởi tạo kết nối SignalR toàn cục.
 * Từng trang có nhu cầu realtime sẽ khởi tạo kết nối riêng trong khối @section Scripts của trang đó.
 */

(function (window, document) {
    'use strict';

    // Khởi tạo đối tượng không gian tên SelfStorage
    const SelfStorage = window.SelfStorage || {};

    /**
     * Chuẩn hóa hiển thị tiền tệ Việt Nam Đồng (VND)
     * @param {number|string} amount Số tiền
     * @returns {string} Chuỗi định dạng ví dụ: "1.500.000 ₫"
     */
    SelfStorage.formatVnd = function (amount) {
        if (amount === null || amount === undefined || amount === '' || isNaN(Number(amount))) {
            return '0 ₫';
        }
        const num = Number(amount);
        return new Intl.NumberFormat('vi-VN', {
            style: 'currency',
            currency: 'VND',
            maximumFractionDigits: 0
        }).format(num);
    };

    /**
     * Chuẩn hóa hiển thị ngày tháng: dd/MM/yyyy (múi giờ Asia/Ho_Chi_Minh)
     * @param {string|Date} dateInput Chuỗi ngày tháng hoặc đối tượng Date
     * @returns {string} Chuỗi định dạng ví dụ: "15/10/2026"
     */
    SelfStorage.formatDate = function (dateInput) {
        if (!dateInput) return '—';
        const d = new Date(dateInput);
        if (isNaN(d.getTime())) return '—';
        try {
            const formatter = new Intl.DateTimeFormat('en-GB', {
                timeZone: 'Asia/Ho_Chi_Minh',
                day: '2-digit',
                month: '2-digit',
                year: 'numeric'
            });
            const parts = formatter.formatToParts(d);
            const p = {};
            parts.forEach(function (part) { p[part.type] = part.value; });
            return `${p.day}/${p.month}/${p.year}`;
        } catch (e) {
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const year = d.getFullYear();
            return `${day}/${month}/${year}`;
        }
    };

    /**
     * Chuẩn hóa hiển thị thời điểm ngày giờ: dd/MM/yyyy HH:mm (múi giờ Asia/Ho_Chi_Minh)
     * @param {string|Date} dateInput Chuỗi ngày giờ hoặc đối tượng Date
     * @returns {string} Chuỗi định dạng ví dụ: "15/10/2026 14:30"
     */
    SelfStorage.formatDateTime = function (dateInput) {
        if (!dateInput) return '—';
        const d = new Date(dateInput);
        if (isNaN(d.getTime())) return '—';
        try {
            const formatter = new Intl.DateTimeFormat('en-GB', {
                timeZone: 'Asia/Ho_Chi_Minh',
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit',
                hour12: false
            });
            const parts = formatter.formatToParts(d);
            const p = {};
            parts.forEach(function (part) { p[part.type] = part.value; });
            return `${p.day}/${p.month}/${p.year} ${p.hour}:${p.minute}`;
        } catch (e) {
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const year = d.getFullYear();
            const hours = String(d.getHours()).padStart(2, '0');
            const minutes = String(d.getMinutes()).padStart(2, '0');
            return `${day}/${month}/${year} ${hours}:${minutes}`;
        }
    };

    /**
     * Hiển thị lớp phủ tải dữ liệu toàn cục (Global Loading Overlay)
     * @param {string} [message="Đang xử lý, vui lòng đợi..."] Thông điệp hiển thị
     */
    SelfStorage.showLoading = function (message) {
        const overlay = document.getElementById('globalLoadingOverlay');
        if (overlay) {
            const textElem = overlay.querySelector('.global-loading-text');
            if (textElem) {
                textElem.textContent = message || 'Đang xử lý, vui lòng đợi...';
            }
            overlay.style.display = 'flex';
        }
    };

    /**
     * Ẩn lớp phủ tải dữ liệu toàn cục
     */
    SelfStorage.hideLoading = function () {
        const overlay = document.getElementById('globalLoadingOverlay');
        if (overlay) {
            overlay.style.display = 'none';
        }
    };

    /**
     * Đặt trạng thái đang xử lý cho nút bấm (Vô hiệu hóa + hiển thị spinner)
     * Chống bấm lặp lại (double-submit protection)
     * @param {HTMLButtonElement|string} buttonSelector Element hoặc selector của nút
     * @param {string} [loadingText="Đang xử lý..."] Chữ hiển thị khi đang tải
     */
    SelfStorage.setButtonLoading = function (buttonSelector, loadingText) {
        const btn = typeof buttonSelector === 'string' ? document.querySelector(buttonSelector) : buttonSelector;
        if (!btn) return;

        btn.dataset.originalHtml = btn.innerHTML;
        btn.disabled = true;
        btn.classList.add('btn-loading');

        const text = loadingText || 'Đang xử lý...';
        btn.innerHTML = `<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>${text}`;
    };

    /**
     * Khôi phục trạng thái ban đầu của nút bấm
     * @param {HTMLButtonElement|string} buttonSelector Element hoặc selector của nút
     */
    SelfStorage.resetButtonLoading = function (buttonSelector) {
        const btn = typeof buttonSelector === 'string' ? document.querySelector(buttonSelector) : buttonSelector;
        if (!btn) return;

        if (btn.dataset.originalHtml) {
            btn.innerHTML = btn.dataset.originalHtml;
            delete btn.dataset.originalHtml;
        }
        btn.disabled = false;
        btn.classList.remove('btn-loading');
    };

    // Gắn vào window để sử dụng toàn cục
    window.SelfStorage = SelfStorage;

    // Thiết lập sự kiện khi tài liệu DOM sẵn sàng
    document.addEventListener('DOMContentLoaded', function () {
        // 1. Tự động bảo vệ các form chống gửi lặp (Double Submit)
        const protectedForms = document.querySelectorAll('form[data-prevent-double-submit="true"], form.js-prevent-double-submit');
        protectedForms.forEach(function (form) {
            form.addEventListener('submit', function (e) {
                // Chỉ áp dụng nếu form hợp lệ (khi có unobtrusive validation)
                if (window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid()) {
                    return;
                }
                const submitBtn = form.querySelector('button[type="submit"], input[type="submit"]');
                if (submitBtn) {
                    SelfStorage.setButtonLoading(submitBtn);
                }
            });
        });

        // 2. Tự động đóng thông báo flash (Alert auto-dismiss sau 6 giây nếu có class alert-auto-dismiss)
        const autoDismissAlerts = document.querySelectorAll('.alert-auto-dismiss');
        autoDismissAlerts.forEach(function (alertElem) {
            setTimeout(function () {
                if (window.bootstrap && window.bootstrap.Alert) {
                    const bsAlert = window.bootstrap.Alert.getOrCreateInstance(alertElem);
                    if (bsAlert) bsAlert.close();
                } else {
                    alertElem.style.display = 'none';
                }
            }, 6000);
        });

        // 3. Đánh dấu menu active dựa trên đường dẫn hiện tại
        const currentPath = window.location.pathname.toLowerCase();
        const navLinks = document.querySelectorAll('.navbar-nav .nav-link');
        navLinks.forEach(function (link) {
            const href = link.getAttribute('href');
            if (href) {
                const lowerHref = href.toLowerCase();
                if (lowerHref === currentPath || (lowerHref !== '/' && currentPath.startsWith(lowerHref))) {
                    link.classList.add('active');
                }
            }
        });
    });

})(window, document);
