# Báo Cáo Thực Hiện Task T03: Repository Quản Trị Ô Kho (Fix Codex Review P1 & P2)

- **Task ID**: T03
- **Tên Task**: Repository quản trị ô kho (Storage Unit Admin Repository)
- **Owner**: Gemini B (Quản trị Storage Units, Razor CRUD, SignalR Hub, SQL Tests)
- **Đợt**: 1
- **Chờ (Dependencies)**: T01 (**VERIFIED** tại [`AgentReport/WebMigration_T01.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T01.md))
- **Status**: **VERIFIED**
- **Contract Version**: v0 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm thực hiện**: 2026-10-15
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Tóm Tắt Khắc Phục Hai Điểm Codex Review (P1 & P2)

Task T03 đã khắc phục triệt để hai điểm review trọng yếu (P1 & P2) từ Codex mà không phá vỡ bất kỳ quy ước kiến trúc hay interface hiện có nào:

1. **P1 — Bảo đảm `applyUpdates` chỉ chạy đúng một lần (Single Execution & Staged Mutation Plan)**:
   - Đọc trạng thái ban đầu bằng `.AsNoTracking()`.
   - Tạo đối tượng `stagedUnit` detached chỉ chứa các scalar field (không giữ navigation collections).
   - Gọi `applyUpdates(stagedUnit)` **đúng 1 lần duy nhất** trước khi lấy khóa.
   - Chụp lại kết quả thành record bất biến `StorageUnitMutationPlan`, loại trừ hoàn toàn các trường hệ thống (`id`, `created_at`, `updated_at`) và navigation properties.
   - Đối chiếu chặt chẽ `targetFacilityId` và `targetUnitTypeId` do caller truyền vào với đích thực tế trong `mutationPlan`. Nếu không khớp, ném `ArgumentException` ngay trước khi lấy khóa hoặc SaveChanges.
   - Sau khi lấy đủ khóa và kiểm tra `freshDbUnit.updated_at == expectedUpdatedAt`, nạp lại entity trong tracking graph bằng `ReloadAsync()`, sau đó gán các trường từ `mutationPlan` lên entity. Tuyệt đối **không gọi lại** `applyUpdates`.

2. **P2 — Giải phóng semaphore khi bị hủy/timeout/lỗi (No Semaphore Leak on Abort)**:
   - Bao bọc toàn bộ chu kỳ lấy semaphore trong khối `try / finally`.
   - Khởi tạo danh sách theo dõi `var acquiredSemaphores = new List<SemaphoreSlim>()`.
   - Mỗi khi `WaitAsync` thành công, thêm ngay semaphore vào `acquiredSemaphores`.
   - Khi có bất kỳ ngoại lệ nào phát sinh (như `OperationCanceledException` khi hủy token lúc đang chờ semaphore thứ hai, hoặc `StorageUnitLockTimeoutException`), khối `finally` sẽ giải phóng các semaphore đã lấy theo thứ tự ngược lại (`for (var i = acquiredSemaphores.Count - 1; i >= 0; i--) acquiredSemaphores[i].Release()`).
   - Áp dụng mẫu bảo vệ an toàn này đồng bộ cho cả 3 phương thức: `CreateStorageUnitWithLockAsync`, `UpdateStorageUnitWithLockAsync`, và `DeleteStorageUnitWithLockAsync`.

---

## 2. Chi Tiết Kỹ Thuật Các Trường Trong Mutation Plan

Record bất biến `StorageUnitMutationPlan` được định nghĩa như sau:

```csharp
private sealed record StorageUnitMutationPlan(
    long SourceFacilityId,
    long SourceUnitTypeId,
    long TargetFacilityId,
    long TargetUnitTypeId,
    long? TargetAreaId,
    string TargetUnitCode,
    string? TargetFloorLabel,
    string? TargetZoneLabel,
    string TargetPhysicalStatus,
    bool TargetIsListed,
    string? TargetNotes
);
```

### Các trường được phép cập nhật (Allowed Editable Fields):
- `facility_id` & `unit_type_id`: Dùng để xác định khóa dung lượng nguồn và đích.
- `area_id`: Mã phân khu/tầng.
- `unit_code`: Mã ô kho hiển thị.
- `floor_label` & `zone_label`: Nhãn tầng và nhãn khu vực.
- `physical_status`: Trạng thái vật lý (`available`, `occupied`, `maintenance`, `reserved`).
- `is_listed`: Cờ niêm yết tìm kiếm/đặt chỗ.
- `notes`: Ghi chú nội bộ.

### Các trường bị cô lập và không cho phép thay đổi từ callback (Protected System Fields):
- `id`: Khóa chính bất biến.
- `created_at`: Thời điểm tạo ban đầu, bảo toàn lịch sử.
- `updated_at`: Được repository tự động gán `DateTimeOffset.UtcNow` ngay trước khi SaveChanges.
- Các navigation collections (`inspections`, `maintenance_work_orders`, `unit_allocations`, `unit_map_position`, `unit_status_histories`, v.v.): Hoàn toàn không được sao chép từ staged unit sang tracking entity, ngăn ngừa side-effect ngoài ý muốn.

---

## 3. Cơ Chế Kiểm Tra Đối Chiếu Target ID (Strict Target Verification)

Khi caller truyền `targetFacilityId` hoặc `targetUnitTypeId` vào `UpdateStorageUnitWithLockAsync`:

```csharp
if (targetFacilityId.HasValue && targetFacilityId.Value != mutationPlan.TargetFacilityId)
{
    throw new ArgumentException(
        $"Specified targetFacilityId ({targetFacilityId.Value}) does not match the actual mutated facility_id ({mutationPlan.TargetFacilityId}).",
        nameof(targetFacilityId));
}

if (targetUnitTypeId.HasValue && targetUnitTypeId.Value != mutationPlan.TargetUnitTypeId)
{
    throw new ArgumentException(
        $"Specified targetUnitTypeId ({targetUnitTypeId.Value}) does not match the actual mutated unit_type_id ({mutationPlan.TargetUnitTypeId}).",
        nameof(targetUnitTypeId));
}
```

- **Hành vi**: Nếu caller khai báo target ID khác với những gì callback thực hiện trên entity (hoặc ngược lại, callback thay đổi nhưng caller khai báo sai lệch), repository sẽ từ chối ngay lập tức bằng `ArgumentException` **trước khi** lấy khóa, **trước khi** mở transaction, và **trước khi** SaveChanges.
- Cơ sở dữ liệu và ChangeTracker được bảo toàn 100%, không bị sửa đổi.

---

## 4. Cơ Chế Giải Phóng Khóa Một Phần Khi Hủy (Partial Semaphore Cleanup)

Cấu trúc try/finally bảo vệ đa khóa trong nhánh In-Memory:

```csharp
var acquiredSemaphores = new List<SemaphoreSlim>();
IDbContextTransaction? transaction = null;

try
{
    if (!isRelational)
    {
        foreach (var key in keysToLock)
        {
            var sem = InMemorySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            var entered = await sem.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!entered)
            {
                throw new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT");
            }
            acquiredSemaphores.Add(sem);
        }
    }
    // ... Thao tác DB ...
}
catch (Exception ex)
{
    if (transaction != null)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
    }
    throw MapException(ex);
}
finally
{
    transaction?.Dispose();
    for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
    {
        acquiredSemaphores[i].Release();
    }
}
```

- **Kịch bản hủy**: Nếu semaphore thứ 1 đã vào `acquiredSemaphores`, nhưng semaphore thứ 2 bị hủy bởi `cancellationToken`, `WaitAsync` ném `OperationCanceledException`.
- Khối `finally` lập tức chạy và gọi `Release()` cho semaphore thứ 1. Semaphore thứ 2 chưa được add nên không bị release thừa.
- Semaphore thứ 1 trở lại trạng thái sẵn sàng (`CurrentCount == 1`), không bị treo hoặc deadlock các tiến trình tiếp theo.

---

## 5. Danh Sách Tập Tin Đã Thay Đổi

1. **`src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/StorageUnitAdminRepository.cs`**:
   - Thêm `DetermineCapacityLockKeys(sourceFac, sourceType, targetFac, targetType)` công khai.
   - Thêm `GetInMemorySemaphore(lockKey)` hỗ trợ kiểm tra trạng thái khóa trong unit test.
   - Thêm `StorageUnitMutationPlan` record bất biến.
   - Tái cấu trúc `UpdateStorageUnitWithLockAsync` thực hiện staged mutation (chạy `applyUpdates` 1 lần), kiểm tra target ID, và bảo vệ semaphore bằng `try / finally`.
   - Tái cấu trúc `CreateStorageUnitWithLockAsync` và `DeleteStorageUnitWithLockAsync` áp dụng danh sách `acquiredSemaphores` trong `try / finally`.
2. **`tests/SelfStorageManagementSystem.WebTests/StorageUnitAdminRepositoryTests.cs`**:
   - Cập nhật test đa luồng `TwoResourceLockOrdering_CrossTypeTransfer_ExecutesDeterministicallyWithoutDeadlock` dùng hai `DbContext` độc lập (`context1` và `context2`).
   - Thêm 5 test case mới xác minh P1 và P2.
3. **`AgentReport/WebMigration_T03.md`**:
   - Cập nhật chi tiết kết quả xử lý và bằng chứng kiểm thử.
4. **`AgentReport/WebMigration_TaskBoard.md`**:
   - Cập nhật bảng theo dõi tiến độ chính.

---

## 6. Danh Sách Kiểm Thử Bổ Sung (New Regression Tests)

| STT | Tên Test Case | Mục đích kiểm thử | Kết quả |
| :---: | :--- | :--- | :---: |
| **1** | `UpdateStorageUnitWithLock_ApplyUpdatesRunsExactlyOnce` | Đếm số lần gọi `applyUpdates` bằng counter nội bộ. Khẳng định `counter == 1` và dữ liệu được lưu đúng. | **PASSED** |
| **2** | `UpdateStorageUnitWithLock_NonDeterministicCallback_UsesSingleStagedResult` | Callback trả giá trị khác nhau giữa các lần gọi (stateful/non-deterministic). Xác minh chỉ giá trị staged của lần gọi duy nhất được áp dụng vào DB. | **PASSED** |
| **3** | `UpdateStorageUnitWithLock_TargetIdMismatch_RejectedBeforeSaveChanges` | Kiểm tra caller truyền `targetFacilityId` hoặc `targetUnitTypeId` lệch với callback. Xác minh ném `ArgumentException` và DB không bị thay đổi. | **PASSED** |
| **4** | `MutationPlan_ComputesDeterministicLocks_CoveringSourceAndTarget` | Kiểm tra hàm tính lock key bao phủ 4 kịch bản: đổi facility, đổi unit type, đổi cả hai, và không đổi. Kiểm tra sắp xếp theo thứ tự `StringComparer.Ordinal`. | **PASSED** |
| **5** | `Cancellation_DuringMultiLockAcquisition_ReleasesPartiallyAcquiredSemaphoresWithoutLeak` | Giữ trước semaphore thứ 2, chạy update chuyển loại, hủy token khi đang chờ. Khẳng định bắt được `OperationCanceledException`, semaphore thứ 1 không bị rò rỉ và tác vụ tiếp theo dùng semaphore thứ 1 thành công ngay lập tức. | **PASSED** |
| **6** | `TwoResourceLockOrdering_CrossTypeTransfer_ExecutesDeterministicallyWithoutDeadlock` | Kiểm thử khóa hai chiều đồng thời giữa hai tác vụ, sử dụng hai instance `DbContext` độc lập mô phỏng hai request thực tế. | **PASSED** |

---

## 7. Kết Quả Kiểm Thử Thực Tế (Execution Evidence)

### 7.1. Bộ test repository (`SelfStorageManagementSystem.WebTests`)
- **Lệnh chạy**:
  ```powershell
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~StorageUnitAdminRepositoryTests"
  ```
- **Kết quả**:
  - **Passed**: 18
  - **Failed**: 0
  - **Skipped**: 0
  - **Tổng cộng**: 18/18 tests (100% Pass)
  - **Thời gian**: ~2 giây

### 7.2. Bộ test toàn bộ solution Web (`SelfStorageManagementSystem.Web.sln`)
- **Lệnh chạy**:
  ```powershell
  dotnet test SelfStorageManagementSystem.Web.sln
  ```
- **Kết quả**:
  - **Passed**: 59
  - **Failed**: 0
  - **Skipped**: 0
  - **Tổng cộng**: 59/59 tests (100% Pass)
  - **Thời gian**: ~2 giây

### 7.3. Bộ test hồi quy toàn bộ solution gốc (`SelfStorageManagementSystem.sln`)
- **Lệnh chạy**:
  ```powershell
  dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrency"
  ```
- **Kết quả**:
  - **Passed**: 223
  - **Failed**: 0
  - **Skipped**: 0
  - **Tổng cộng**: 223/223 tests (100% Pass)
  - **Thời gian**: ~4 giây

---

## 8. Kết Luận và Bàn Giao

- **Hai lỗi P1 & P2 từ Codex Review đã được xử lý triệt để 100%**.
- **Không có bất kỳ semaphore nào bị rò rỉ khi timeout hoặc cancellation**.
- **`applyUpdates` chạy duy nhất 1 lần trên đối tượng staged**.
- **Target ID được xác thực chặt chẽ chống sai lệch phạm vi khóa**.
- **Toàn bộ 282 bài kiểm thử (59 WebTests + 223 Core Tests) đều PASSED**.
- **Không có rủi ro kỹ thuật tồn đọng**.

**Trạng thái cuối cùng**: **VERIFIED** (Sẵn sàng 100% cho Task T06 ở Đợt 2).
