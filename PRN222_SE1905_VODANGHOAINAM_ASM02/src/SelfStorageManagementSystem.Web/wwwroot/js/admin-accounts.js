/**
 * admin-accounts.js
 * JavaScript cho mô-đun Quản trị tài khoản & Phân quyền (Task T14)
 * Hỗ trợ giao diện quản trị, cảnh báo an toàn và hook realtime SignalR T15.
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Ẩn/Hiện trường Cơ sở ban đầu theo vai trò tại trang Create
    const selectRoleCode = document.getElementById('selectRoleCode');
    const initialFacilityGroup = document.getElementById('initialFacilityGroup');

    if (selectRoleCode && initialFacilityGroup) {
        function toggleFacilityVisibility() {
            const role = selectRoleCode.value;
            if (role === 'business_operations_manager') {
                initialFacilityGroup.style.display = 'none';
            } else {
                initialFacilityGroup.style.display = 'block';
            }
        }

        selectRoleCode.addEventListener('change', toggleFacilityVisibility);
        toggleFacilityVisibility();
    }

    // 2. Xác nhận an toàn khi đổi trạng thái tài khoản
    const formUpdateStatus = document.getElementById('formUpdateStatus');
    const selectStatus = document.getElementById('selectStatus');

    if (formUpdateStatus && selectStatus) {
        formUpdateStatus.addEventListener('submit', function (e) {
            const chosen = selectStatus.value;
            if (chosen === 'locked' || chosen === 'disabled') {
                const msg = chosen === 'disabled'
                    ? 'CẢNH BÁO: Bạn đang chọn VÔ HIỆU HÓA vĩnh viễn tài khoản này. Người dùng sẽ không thể đăng nhập vào hệ thống.\n\nBạn có chắc chắn muốn thực hiện?'
                    : 'CẢNH BÁO: Bạn đang chọn KHÓA tài khoản này. Người dùng sẽ bị tạm khóa quyền đăng nhập.\n\nBạn có chắc chắn muốn thực hiện?';

                if (!confirm(msg)) {
                    e.preventDefault();
                    return false;
                }
            }
        });
    }

    // 3. Xác nhận an toàn khi thay đổi vai trò (đặc biệt là tước quyền admin)
    const formManageRoles = document.getElementById('formManageRoles');
    const roleAdminCheckbox = document.getElementById('role_admin');

    if (formManageRoles && roleAdminCheckbox) {
        const originallyHadAdmin = roleAdminCheckbox.checked;

        formManageRoles.addEventListener('submit', function (e) {
            if (originallyHadAdmin && !roleAdminCheckbox.checked) {
                if (!confirm('CẢNH BÁO QUAN TRỌNG: Bạn đang tước vai trò Quản trị viên (system_administrator) khỏi tài khoản này.\n\nHệ thống sẽ từ chối nếu đây là quản trị viên hoạt động duy nhất.\n\nBạn có chắc chắn muốn tiếp tục không?')) {
                    e.preventDefault();
                    return false;
                }
            }
        });
    }

    // 4. Hook chuẩn bị đón nhận sự kiện Invalidation Realtime từ SignalR (T15)
    function setupRealtimeListener() {
        const msgArea = document.getElementById('adminAccountsMessageArea');
        if (!msgArea) return;

        // Nếu đã có kết nối SignalR toàn cục hoặc thư viện SignalR có mặt
        if (typeof signalR !== 'undefined' && window.appSignalRConnection) {
            window.appSignalRConnection.on('AccountChanged', function (data) {
                showInvalidationNotice(msgArea, 'Dữ liệu tài khoản vừa được cập nhật trên hệ thống. Bấm vào đây để tải lại trang.');
            });
        }
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
